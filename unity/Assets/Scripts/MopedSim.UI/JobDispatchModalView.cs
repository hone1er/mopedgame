using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MopedSim.Core;
using MopedSim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Runtime-built Bay Area job dispatch modal (Settings / Title style): every catalog gig with its pickup and drop zones, cargo need,
    /// payout, time limit and an eligibility reason, plus Accept / Abandon / Collect / Close. It also owns the completion / fail toast.
    /// Display-only: it emits events and App executes the Core accept / abandon / collect calls. Rendering is event-driven
    /// (open, accept, status change), never per frame.
    /// </summary>
    public sealed class JobDispatchModalView : MonoBehaviour
    {
        public const string NeedsRackMessage = "Needs a cargo rack / milk crate";
        public const string ActiveGigMessage = "Finish or abandon the active gig first";
        public const string SeizedMessage = "Engine seized - fix the bike first";
        public const string NoBikeMessage = "No bike available";
        public const string EligibleMessage = "Ready to ride";

        private const float ToastSeconds = 3.5f;

        private sealed class JobRowEntry
        {
            public GameObject Root;
            public Text InfoText;
            public Button AcceptButton;
            public Text ButtonLabel;
            public string BoundJobId = string.Empty;
        }

        private readonly List<JobRowEntry> _rowPool = new List<JobRowEntry>(12);
        private readonly StringBuilder _sb = new StringBuilder(260);

        private RectTransform _backdrop;
        private RectTransform _card;
        private RectTransform _jobListRoot;
        private Text _headerText;
        private Text _statusText;
        private Button _abandonButton;
        private Button _collectButton;
        private Button _closeButton;
        private Font _font;

        private RectTransform _toastRoot;
        private Text _toastText;
        private float _toastRemaining;

        private int _displayedJobCount;
        private string _displayedFeedback = string.Empty;
        private int _eligibleCount;

        public event Action<string> AcceptJobRequested;
        public event Action AbandonJobRequested;
        public event Action CollectRewardRequested;
        public event Action CloseRequested;

        public bool IsBuilt { get { return _backdrop != null; } }
        public bool IsOpen { get { return _backdrop != null && _backdrop.gameObject.activeSelf; } }
        public int DisplayedJobCount { get { return _displayedJobCount; } }
        public int EligibleJobCount { get { return _eligibleCount; } }
        public string DisplayedFeedback { get { return _displayedFeedback; } }
        public bool IsToastVisible { get { return _toastRoot != null && _toastRoot.gameObject.activeSelf; } }
        public string ToastText { get { return _toastText != null ? _toastText.text : string.Empty; } }

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
            Fire(AbandonJobRequested);
        }

        public void TriggerCollectReward()
        {
            Fire(CollectRewardRequested);
        }

        public void TriggerClose()
        {
            Fire(CloseRequested);
        }

        /// <summary>Builds the dimmed full-screen modal and the toast under <paramref name="parent"/>. Idempotent.</summary>
        public void Build(Transform parent)
        {
            if (_backdrop != null || parent == null)
            {
                return;
            }

            _font = MenuWidgets.LoadFont();
            _backdrop = MenuWidgets.CreateOverlayCanvas(parent, "JobDispatch_Canvas", 185, new Color(0f, 0f, 0f, 0.62f));
            _card = MenuWidgets.CreateCard(_backdrop, "JobDispatchCard", new Vector2(1180f, 880f), new Color(0.08f, 0.09f, 0.12f, 0.97f), 20, 8f);
            _headerText = MenuWidgets.CreateLabel(_card, "Header", "BAY AREA GIG DISPATCH", 34, TextAnchor.MiddleLeft, new Color(0.95f, 0.62f, 0.18f, 1f), 46f, _font);
            _statusText = MenuWidgets.CreateLabel(_card, "Status", string.Empty, 20, TextAnchor.MiddleLeft, new Color(0.70f, 0.90f, 1f, 1f), 30f, _font);

            GameObject listGo = new GameObject("JobList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_card, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 560f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 6f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _jobListRoot = (RectTransform)listGo.transform;

            GameObject footerGo = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_card, false);
            footerGo.GetComponent<LayoutElement>().preferredHeight = 54f;
            HorizontalLayoutGroup footer = footerGo.GetComponent<HorizontalLayoutGroup>();
            footer.spacing = 12f;
            footer.childControlWidth = true;
            footer.childControlHeight = true;
            footer.childForceExpandWidth = true;
            footer.childForceExpandHeight = true;

            _abandonButton = MenuWidgets.CreateButton(footerGo.transform, "Btn_AbandonActive", "Abandon Active Gig", new Color(0.58f, 0.22f, 0.20f, 1f), 22, 54f, _font, TriggerAbandonJob, out Text _);
            _collectButton = MenuWidgets.CreateButton(footerGo.transform, "Btn_CollectReward", "Collect Reward", new Color(0.20f, 0.56f, 0.28f, 1f), 22, 54f, _font, TriggerCollectReward, out Text _);
            _closeButton = MenuWidgets.CreateButton(footerGo.transform, "Btn_Close", "Close  (J / Esc)", new Color(0.25f, 0.27f, 0.32f, 1f), 22, 54f, _font, TriggerClose, out Text _);
            _backdrop.gameObject.SetActive(false);

            BuildToast(parent);
        }

        private void BuildToast(Transform parent)
        {
            GameObject canvasGo = new GameObject("JobToast_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(parent, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = HudStyle.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            _toastRoot = HudStyle.CreatePanel(canvasGo.transform, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, -(HudStyle.SafeTop + 84f)), new Vector2(820f, 96f), true);
            _toastText = HudStyle.CreateLabel(_toastRoot, "ToastText", HudStyle.Padding, 8f, 820f - 2f * HudStyle.Padding, 80f, 26, HudStyle.TextPrimary, TextAnchor.MiddleCenter);
            _toastText.supportRichText = true;
            _toastRoot.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (_backdrop == null)
            {
                return;
            }

            _backdrop.gameObject.SetActive(true);
            _backdrop.SetAsLastSibling();
            FocusDefault();
        }

        public void Hide()
        {
            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        /// <summary>Shows the completion / fail toast for a few seconds (unscaled time).</summary>
        public void ShowToast(string text, Color color)
        {
            if (_toastRoot == null)
            {
                return;
            }

            _toastText.text = text ?? string.Empty;
            _toastText.color = color;
            _toastRemaining = ToastSeconds;
            _toastRoot.gameObject.SetActive(true);
        }

        public void HideToast()
        {
            _toastRemaining = 0f;
            if (_toastRoot != null)
            {
                _toastRoot.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (_toastRemaining <= 0f)
            {
                return;
            }

            _toastRemaining -= Time.unscaledDeltaTime;
            if (_toastRemaining <= 0f)
            {
                HideToast();
            }
        }

        /// <summary>
        /// Eligibility of <paramref name="job"/> for the bike's current cargo rack. Returns true when the gig can be accepted now;
        /// <paramref name="reason"/> is the player-facing explanation (a short "Ready to ride" when eligible).
        /// </summary>
        public static bool EvaluateEligibility(JobDefinition job, BikeSimulation bike, bool hasActiveGig, out string reason)
        {
            if (hasActiveGig)
            {
                reason = ActiveGigMessage;
                return false;
            }

            if (bike == null)
            {
                reason = NoBikeMessage;
                return false;
            }

            if (bike.Thermal.IsHardSeized)
            {
                reason = SeizedMessage;
                return false;
            }

            CargoCapacity cargo = CargoCapacityCalculator.Compute(bike);
            if (CargoCapacityCalculator.CanCarry(cargo, job))
            {
                reason = EligibleMessage;
                return true;
            }

            reason = NeedsRackText(job, in cargo);
            return false;
        }

        private static string NeedsRackText(JobDefinition job, in CargoCapacity cargo)
        {
            StringBuilder sb = new StringBuilder(96);
            sb.Append(NeedsRackMessage).Append(": ").Append(job.RequiredCargoSlots.ToString(CultureInfo.InvariantCulture)).Append(" slot")
                .Append(job.RequiredCargoSlots == 1 ? string.Empty : "s").Append(" / ").Append(job.RequiredCargoKg.ToString("0.#", CultureInfo.InvariantCulture))
                .Append(" kg (yours: ").Append(cargo.Slots.ToString(CultureInfo.InvariantCulture)).Append(" / ")
                .Append(cargo.MaxKg.ToString("0.#", CultureInfo.InvariantCulture)).Append(" kg)");
            return sb.ToString();
        }

        /// <summary>Renders the catalog gig list, header status and footer button states.</summary>
        public void RenderCatalog(
            CatalogDatabase catalog,
            SimulationWorld world,
            BikeSimulation bike,
            JobDefinition activeJob,
            JobSessionStatus status,
            bool canCollectReward,
            string feedback)
        {
            if (_jobListRoot == null)
            {
                return;
            }

            IReadOnlyList<JobDefinition> jobs = catalog != null ? catalog.JobList : null;
            int jobCount = jobs != null ? jobs.Count : 0;
            bool hasActive = status == JobSessionStatus.Active;
            string activeId = activeJob != null ? activeJob.Id : string.Empty;
            string safeFeedback = feedback ?? string.Empty;
            _displayedJobCount = jobCount;
            _eligibleCount = 0;

            _sb.Length = 0;
            if (hasActive && activeJob != null)
            {
                _sb.Append("ACTIVE GIG: ").Append(activeJob.DisplayName);
            }
            else if (canCollectReward)
            {
                _sb.Append("DELIVERED - collect your reward below");
            }
            else
            {
                CargoCapacity cargo = bike != null ? CargoCapacityCalculator.Compute(bike) : CargoCapacity.None;
                _sb.Append("Cargo rack: ").Append(cargo.Slots).Append(cargo.Slots == 1 ? " slot / " : " slots / ").Append(cargo.MaxKg.ToString("0.#", CultureInfo.InvariantCulture)).Append(" kg");
            }

            if (world != null)
            {
                _sb.Append("   |   $").Append(world.CashUsd.ToString("N0", CultureInfo.InvariantCulture)).Append("   |   Reputation ").Append(world.Reputation);
            }

            if (safeFeedback.Length > 0)
            {
                _sb.Append("   |   ").Append(safeFeedback);
            }

            _displayedFeedback = _sb.ToString();
            _statusText.text = _displayedFeedback;

            for (int i = 0; i < jobCount; i++)
            {
                JobDefinition job = jobs[i];
                JobRowEntry entry = GetOrCreateRow(i);
                entry.BoundJobId = job.Id;
                entry.Root.name = "JobRow_" + job.Id;
                entry.Root.SetActive(true);

                bool isThisActive = hasActive && string.Equals(activeId, job.Id, StringComparison.Ordinal);
                string reason;
                bool eligible = EvaluateEligibility(job, bike, hasActive, out reason);
                if (eligible)
                {
                    _eligibleCount++;
                }

                entry.InfoText.text = FormatJobRow(job, catalog, eligible, reason, isThisActive);
                entry.AcceptButton.interactable = eligible;
                entry.ButtonLabel.text = isThisActive ? "Active" : (eligible ? "Accept" : "Locked");
            }

            for (int i = jobCount; i < _rowPool.Count; i++)
            {
                if (_rowPool[i].Root != null && _rowPool[i].Root.activeSelf)
                {
                    _rowPool[i].Root.SetActive(false);
                }
            }

            _abandonButton.interactable = hasActive;
            _collectButton.gameObject.SetActive(canCollectReward);
            _closeButton.interactable = true;
            if (IsOpen)
            {
                FocusDefault();
            }
        }

        private void FocusDefault()
        {
            for (int i = 0; i < _rowPool.Count; i++)
            {
                JobRowEntry e = _rowPool[i];
                if (e.Root != null && e.Root.activeSelf && e.AcceptButton.IsInteractable())
                {
                    MenuWidgets.Focus(e.AcceptButton);
                    return;
                }
            }

            MenuWidgets.Focus(_closeButton);
        }

        private JobRowEntry GetOrCreateRow(int index)
        {
            while (_rowPool.Count <= index)
            {
                JobRowEntry entry = new JobRowEntry();
                GameObject rowGo = new GameObject("JobRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_jobListRoot, false);
                rowGo.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 0.95f);
                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 66f;
                le.minHeight = 66f;
                HorizontalLayoutGroup h = rowGo.GetComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(12, 12, 4, 4);
                h.spacing = 12f;
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = true;

                GameObject infoGo = new GameObject("Info", typeof(RectTransform), typeof(LayoutElement));
                infoGo.transform.SetParent(rowGo.transform, false);
                LayoutElement infoLe = infoGo.GetComponent<LayoutElement>();
                infoLe.flexibleWidth = 1f;
                infoLe.minWidth = 0f;
                infoLe.preferredWidth = 800f;
                Text infoText = infoGo.AddComponent<Text>();
                infoText.font = _font;
                infoText.fontSize = 16;
                infoText.alignment = TextAnchor.MiddleLeft;
                infoText.color = Color.white;
                infoText.raycastTarget = false;
                infoText.supportRichText = true;
                infoText.horizontalOverflow = HorizontalWrapMode.Wrap;

                JobRowEntry captured = entry;
                Text label;
                Button accept = MenuWidgets.CreateButton(rowGo.transform, "Btn_Accept", "Accept", new Color(0.20f, 0.48f, 0.68f, 1f), 22, 54f, _font,
                    delegate { TriggerAcceptJob(captured.BoundJobId); }, out label);
                LayoutElement btnLe = accept.GetComponent<LayoutElement>();
                btnLe.minWidth = 150f;
                btnLe.preferredWidth = 150f;
                btnLe.flexibleWidth = 0f;

                entry.Root = rowGo;
                entry.InfoText = infoText;
                entry.AcceptButton = accept;
                entry.ButtonLabel = label;
                _rowPool.Add(entry);
            }

            return _rowPool[index];
        }

        private string FormatJobRow(JobDefinition job, CatalogDatabase catalog, bool eligible, string reason, bool isThisActive)
        {
            _sb.Length = 0;
            _sb.Append("<b>").Append(job.DisplayName).Append("</b>   <color=#8FB4D8>").Append(GetJobTypeBadge(job.JobType)).Append("</color>");
            if (isThisActive)
            {
                _sb.Append("   <color=#F2C230>[ACTIVE]</color>");
            }

            bool sameZone = string.IsNullOrEmpty(job.TargetZoneId) || string.Equals(job.TargetZoneId, job.ZoneId, StringComparison.Ordinal);
            _sb.Append("\n").Append(ZoneName(catalog, job.ZoneId));
            if (sameZone)
            {
                _sb.Append(" (local run)");
            }
            else
            {
                _sb.Append(" -> ").Append(ZoneName(catalog, job.TargetZoneId));
            }
            if (job.RequiredCargoSlots > 0 || job.RequiredCargoKg > 0f)
            {
                _sb.Append("   |   Cargo ").Append(job.RequiredCargoSlots).Append(" slot").Append(job.RequiredCargoSlots == 1 ? string.Empty : "s")
                    .Append(" / ").Append(job.RequiredCargoKg.ToString("0.#", CultureInfo.InvariantCulture)).Append(" kg");
            }

            _sb.Append("   |   <color=#4DDB6B>$").Append(job.RewardUsd.ToString("0", CultureInfo.InvariantCulture)).Append("</color>");
            if (job.BonusRewardUsd > 0m)
            {
                _sb.Append(" (+$").Append(job.BonusRewardUsd.ToString("0", CultureInfo.InvariantCulture)).Append(" bonus)");
            }

            _sb.Append("   |   +").Append(job.ReputationReward).Append(" rep");
            if (job.TimeLimitSeconds > 0f)
            {
                _sb.Append("   |   ").Append(JobBoardHudView.FormatClock(Mathf.CeilToInt(job.TimeLimitSeconds))).Append(" limit");
            }

            _sb.Append("\n").Append(eligible ? "<color=#4DDB6B>" : "<color=#F2C230>").Append(reason).Append("</color>");
            return _sb.ToString();
        }

        private static string ZoneName(CatalogDatabase catalog, string zoneId)
        {
            ZoneEnvironmentSpec zone;
            if (catalog != null && !string.IsNullOrEmpty(zoneId) && catalog.Zones.TryGetValue(zoneId, out zone) && !string.IsNullOrEmpty(zone.DisplayName))
            {
                return zone.DisplayName;
            }

            return zoneId ?? string.Empty;
        }

        private static string GetJobTypeBadge(JobType type)
        {
            switch (type)
            {
                case JobType.BaySwarmRally: return "RALLY";
                case JobType.HillClimb: return "HILL CLIMB";
                case JobType.SpeedTrap: return "SPEED TRAP";
                default: return "COURIER";
            }
        }

        private static void Fire(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }
    }
}
