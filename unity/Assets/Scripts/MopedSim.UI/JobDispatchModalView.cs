using System;
using System.Collections.Generic;
using System.Text;
using MopedSim.Core;
using MopedSim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Interactive Bay Area job dispatch modal displaying Courier gigs, Bay Swarm rallies,
    /// Hill Climbs, and Speed Traps with cargo fit indicators and Accept/Abandon/Collect/Close actions.
    /// Emits Action events only; App executes Core job acceptance and payout collection.
    /// </summary>
    public sealed class JobDispatchModalView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private sealed class JobRowEntry
        {
            public GameObject Root;
            public Text InfoText;
            public Button AcceptButton;
            public Text ButtonLabel;
            public string BoundJobId = string.Empty;
        }

        [SerializeField] private Text _headerText;
        [SerializeField] private Text _feedbackText;
        [SerializeField] private RectTransform _jobListRoot;
        [SerializeField] private Button _abandonButton;
        [SerializeField] private Button _collectButton;
        [SerializeField] private Button _closeButton;

        private readonly List<JobRowEntry> _rowPool = new List<JobRowEntry>(12);
        private readonly List<string> _cachedJobIds = new List<string>(12);
        private readonly StringBuilder _sb = new StringBuilder(220);

        private GameObject _modalRoot;
        private Transform _builtParent;
        private Font _font;

        private bool _hasRendered;
        private int _displayedJobCount;
        private string _displayedFeedback = string.Empty;
        private string _lastActiveJobId = string.Empty;
        private JobSessionStatus _lastStatus = JobSessionStatus.Idle;
        private bool _lastCanCollect;
        private int _lastCargoSlots = int.MinValue;
        private int _lastCargoKgTenths = int.MinValue;
        private string _lastFeedback = string.Empty;

        public event Action<string> AcceptJobRequested;
        public event Action AbandonJobRequested;
        public event Action CollectRewardRequested;
        public event Action CloseRequested;

        public int DisplayedJobCount
        {
            get { return _displayedJobCount; }
        }

        public string DisplayedFeedback
        {
            get { return _displayedFeedback; }
        }

        public void TriggerAcceptJob(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return;
            }

            Action<string> handler = AcceptJobRequested;
            if (handler != null)
            {
                handler(jobId);
            }
        }

        public void TriggerAbandonJob()
        {
            Action handler = AbandonJobRequested;
            if (handler != null)
            {
                handler();
            }
        }

        public void TriggerCollectReward()
        {
            Action handler = CollectRewardRequested;
            if (handler != null)
            {
                handler();
            }
        }

        public void TriggerClose()
        {
            Action handler = CloseRequested;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Builds the centered dark job dispatch modal under <paramref name="root"/>.
        /// Idempotent for the same parent RectTransform.
        /// </summary>
        public void BuildDefaultLayout(RectTransform root)
        {
            RectTransform parent = root != null ? root : transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (_modalRoot != null && _builtParent == parent)
            {
                return;
            }

            if (_modalRoot != null)
            {
                DestroyObjectSafe(_modalRoot);
                _modalRoot = null;
                _rowPool.Clear();
            }

            _builtParent = parent;
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _modalRoot = new GameObject("JobDispatch_Modal", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _modalRoot.transform.SetParent(parent, false);
            RectTransform modalRt = (RectTransform)_modalRoot.transform;
            modalRt.anchorMin = new Vector2(0.5f, 0.5f);
            modalRt.anchorMax = new Vector2(0.5f, 0.5f);
            modalRt.pivot = new Vector2(0.5f, 0.5f);
            modalRt.anchoredPosition = Vector2.zero;
            modalRt.sizeDelta = new Vector2(820f, 540f);

            Image bg = _modalRoot.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

            VerticalLayoutGroup vLayout = _modalRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(16, 16, 14, 14);
            vLayout.spacing = 8f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            _headerText = CreateLabel(_modalRoot.transform, "Header", "BAY AREA JOB DISPATCH", 22, TextAnchor.MiddleLeft, Color.white, 30f, _font);
            _feedbackText = CreateLabel(_modalRoot.transform, "FeedbackBanner", _displayedFeedback, 15, TextAnchor.MiddleLeft, new Color(0.95f, 0.85f, 0.45f, 1f), 26f, _font);

            GameObject listGo = new GameObject("JobList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_modalRoot.transform, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 320f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 6f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _jobListRoot = (RectTransform)listGo.transform;

            GameObject footerGo = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_modalRoot.transform, false);
            LayoutElement footerLe = footerGo.GetComponent<LayoutElement>();
            footerLe.preferredHeight = 44f;
            HorizontalLayoutGroup footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;

            _abandonButton = CreateButton(footerGo.transform, "Btn_AbandonActive", "Abandon Active", new Color(0.58f, 0.22f, 0.20f, 1f), _font, TriggerAbandonJob);
            _collectButton = CreateButton(footerGo.transform, "Btn_CollectReward", "Collect Reward", new Color(0.20f, 0.56f, 0.28f, 1f), _font, TriggerCollectReward);
            _closeButton = CreateButton(footerGo.transform, "Btn_Close", "Close", new Color(0.25f, 0.27f, 0.32f, 1f), _font, TriggerClose);

            _hasRendered = false;
        }

        /// <summary>
        /// Renders the catalog job list, active job status, bike cargo fit, and footer button states.
        /// Dirty-checked so steady-state repeated calls allocate zero GC memory.
        /// </summary>
        public void RenderCatalog(
            CatalogDatabase catalog,
            SimulationWorld world,
            BikeSimulation bike,
            JobDefinition activeJob,
            JobSessionStatus status,
            bool canCollectReward,
            string feedback)
        {
            IReadOnlyList<JobDefinition> jobs = catalog != null ? catalog.JobList : null;
            CargoCapacity cargo = bike != null ? CargoCapacityCalculator.Compute(bike) : CargoCapacity.None;
            string activeId = activeJob != null ? activeJob.Id : string.Empty;
            string safeFeedback = feedback ?? string.Empty;
            int cargoKgTenths = Mathf.RoundToInt(cargo.MaxKg * 10f);

            if (_hasRendered
                && status == _lastStatus
                && canCollectReward == _lastCanCollect
                && cargo.Slots == _lastCargoSlots
                && cargoKgTenths == _lastCargoKgTenths
                && string.Equals(activeId, _lastActiveJobId, StringComparison.Ordinal)
                && string.Equals(safeFeedback, _lastFeedback, StringComparison.Ordinal)
                && AreJobIdsUnchanged(jobs))
            {
                return;
            }

            _hasRendered = true;
            _lastStatus = status;
            _lastCanCollect = canCollectReward;
            _lastCargoSlots = cargo.Slots;
            _lastCargoKgTenths = cargoKgTenths;
            _lastActiveJobId = activeId;
            _lastFeedback = safeFeedback;

            _cachedJobIds.Clear();
            int jobCount = jobs != null ? jobs.Count : 0;
            for (int i = 0; i < jobCount; i++)
            {
                _cachedJobIds.Add(jobs[i].Id);
            }

            _displayedJobCount = jobCount;

            _sb.Length = 0;
            if (activeJob != null && status == JobSessionStatus.Active)
            {
                _sb.Append("ACTIVE: ").Append(activeJob.DisplayName);
            }
            else if (canCollectReward)
            {
                _sb.Append("JOB COMPLETE — READY TO COLLECT REWARD");
            }
            else
            {
                _sb.Append("Cargo Rack: ").Append(cargo.Slots).Append(" slots / ").Append(cargo.MaxKg.ToString("0.0")).Append(" kg");
            }

            if (world != null)
            {
                _sb.Append("  |  $").Append(world.CashUsd.ToString("F2")).Append("  |  Rep ").Append(world.Reputation);
            }

            if (safeFeedback.Length > 0)
            {
                _sb.Append("  |  ").Append(safeFeedback);
            }

            _displayedFeedback = _sb.ToString();
            if (_feedbackText != null)
            {
                _feedbackText.text = _displayedFeedback;
            }

            if (_jobListRoot != null)
            {
                bool hasActive = status == JobSessionStatus.Active;
                for (int i = 0; i < jobCount; i++)
                {
                    JobDefinition job = jobs[i];
                    JobRowEntry entry = GetOrCreateRow(i);
                    entry.BoundJobId = job.Id;
                    entry.Root.name = "JobRow_" + job.Id;
                    entry.Root.SetActive(true);

                    bool fitsCargo = bike == null
                        ? (job.RequiredCargoSlots <= 0 && job.RequiredCargoKg <= 0f)
                        : CargoCapacityCalculator.CanCarry(cargo, job);
                    bool isThisActive = hasActive && string.Equals(activeId, job.Id, StringComparison.Ordinal);

                    entry.InfoText.text = FormatJobRow(job, fitsCargo, isThisActive);
                    entry.AcceptButton.interactable = !hasActive && fitsCargo;
                    if (isThisActive)
                    {
                        entry.ButtonLabel.text = "Active";
                    }
                    else if (hasActive)
                    {
                        entry.ButtonLabel.text = "Locked";
                    }
                    else if (!fitsCargo)
                    {
                        entry.ButtonLabel.text = "Need Rack";
                    }
                    else
                    {
                        entry.ButtonLabel.text = "Accept";
                    }
                }

                for (int i = jobCount; i < _rowPool.Count; i++)
                {
                    if (_rowPool[i].Root != null && _rowPool[i].Root.activeSelf)
                    {
                        _rowPool[i].Root.SetActive(false);
                    }
                }
            }

            if (_abandonButton != null)
            {
                _abandonButton.interactable = status == JobSessionStatus.Active;
            }

            if (_collectButton != null)
            {
                _collectButton.interactable = canCollectReward;
            }

            if (_closeButton != null)
            {
                _closeButton.interactable = true;
            }
        }

        private bool AreJobIdsUnchanged(IReadOnlyList<JobDefinition> jobs)
        {
            int count = jobs != null ? jobs.Count : 0;
            if (count != _cachedJobIds.Count)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(jobs[i].Id, _cachedJobIds[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private JobRowEntry GetOrCreateRow(int index)
        {
            while (_rowPool.Count <= index)
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                }

                JobRowEntry entry = new JobRowEntry();
                GameObject rowGo = new GameObject("JobRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_jobListRoot, false);
                rowGo.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 0.95f);

                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 48f;
                le.minHeight = 42f;

                HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                hLayout.padding = new RectOffset(10, 10, 4, 4);
                hLayout.spacing = 10f;
                hLayout.childControlWidth = true;
                hLayout.childControlHeight = true;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = true;

                GameObject infoGo = new GameObject("Info", typeof(RectTransform), typeof(LayoutElement));
                infoGo.transform.SetParent(rowGo.transform, false);
                LayoutElement infoLe = infoGo.GetComponent<LayoutElement>();
                infoLe.flexibleWidth = 1f;
                Text infoText = infoGo.AddComponent<Text>();
                infoText.font = _font;
                infoText.fontSize = 14;
                infoText.alignment = TextAnchor.MiddleLeft;
                infoText.color = Color.white;
                infoText.raycastTarget = false;

                Button acceptBtn = CreateButton(rowGo.transform, "Btn_Accept", "Accept", new Color(0.20f, 0.48f, 0.68f, 1f), _font, null);
                LayoutElement btnLe = acceptBtn.GetComponent<LayoutElement>();
                btnLe.preferredWidth = 112f;
                btnLe.flexibleWidth = 0f;

                JobRowEntry captured = entry;
                acceptBtn.onClick.AddListener(delegate { TriggerAcceptJob(captured.BoundJobId); });

                entry.Root = rowGo;
                entry.InfoText = infoText;
                entry.AcceptButton = acceptBtn;
                entry.ButtonLabel = acceptBtn.GetComponentInChildren<Text>();
                _rowPool.Add(entry);
            }

            return _rowPool[index];
        }

        private string FormatJobRow(JobDefinition job, bool fitsCargo, bool isThisActive)
        {
            _sb.Length = 0;
            _sb.Append(GetJobTypeBadge(job.JobType)).Append(' ').Append(job.DisplayName);
            _sb.Append("  |  ").Append(job.ZoneId);
            if (!string.IsNullOrEmpty(job.TargetZoneId) && !string.Equals(job.ZoneId, job.TargetZoneId, StringComparison.Ordinal))
            {
                _sb.Append(" -> ").Append(job.TargetZoneId);
            }

            if (job.RequiredCargoSlots > 0 || job.RequiredCargoKg > 0f)
            {
                _sb.Append("  |  Cargo: ").Append(job.RequiredCargoSlots).Append("sl/").Append(job.RequiredCargoKg.ToString("0.#")).Append("kg");
                _sb.Append(fitsCargo ? " [FIT]" : " [LOCKED]");
            }

            _sb.Append("  |  $").Append(job.RewardUsd.ToString("0"));
            if (job.BonusRewardUsd > 0m)
            {
                _sb.Append(" (+$").Append(job.BonusRewardUsd.ToString("0")).Append(')');
            }

            _sb.Append(" +").Append(job.ReputationReward).Append(" rep");
            if (isThisActive)
            {
                _sb.Append("  [ACTIVE]");
            }

            return _sb.ToString();
        }

        private static string GetJobTypeBadge(JobType type)
        {
            switch (type)
            {
                case JobType.BaySwarmRally: return "[Rally]";
                case JobType.HillClimb: return "[Climb]";
                case JobType.SpeedTrap: return "[SpeedTrap]";
                default: return "[Courier]";
            }
        }

        private static Text CreateLabel(
            Transform parent,
            string name,
            string text,
            int fontSize,
            TextAnchor alignment,
            Color color,
            float height,
            Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            Text label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.text = text ?? string.Empty;
            return label;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Color color,
            Font font,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            Image bg = go.GetComponent<Image>();
            bg.color = color;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            Text text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;
            return btn;
        }

        private static void DestroyObjectSafe(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
