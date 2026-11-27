using System.Globalization;
using MopedSim.Core;
using MopedSim.Garage;
using MopedSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MopedSim.App
{
    /// <summary>
    /// Hosts the runtime-built Garage station dock and the Roadside repair panel, plus the "broken down" ride prompt. Presentation and
    /// dispatch only: every repair, tow, dyno pull, plug read, refuel and jet swap is executed by <see cref="AppCompositionRoot"/> /
    /// the Garage station controllers / MopedSim.Core; results are only formatted into feedback text here. Event-driven; the
    /// per-frame <see cref="Tick"/> is allocation-free while the panels are idle (views dirty-check their own state).
    /// </summary>
    public sealed class StationUiController : MonoBehaviour
    {
        private const float DynoPullSeconds = 5f;
        private const float DynoStepSeconds = 0.02f;
        private const float WattsPerHorsepower = 745.7f;
        private const float JerrycanLiters = 5f;
        private const float DockClearance = 150f;
        private const float DynoKickStartSeconds = 1.5f;
        private const float PromptHoldSeconds = 4f;
        private const float StallPromptDelaySeconds = 2f;
        private static readonly float[] MixRatios = { 50f, 40f, 32f, 0f };

        private AppCompositionRoot _app;
        private RectTransform _canvasRoot;
        private GameObject _dockHost;
        private GameObject _roadsideHost;
        private GarageStationDockView _dock;
        private RoadsideRepairPanelView _roadside;
        private Text _promptText;
        private GameObject _promptHost;

        private bool _dockVisible;
        private bool _roadsideVisible;
        private bool _promptVisible;
        private int _ratioIndex;
        private int _lastJetSize = int.MinValue;
        private string _jetLabel = string.Empty;
        private string _feedback = string.Empty;
        private float _lastDynoHp;
        private float _lastPeakRpm;
        private Key _promptKey = Key.None;
        private float _promptHoldUntil;
        private float _stalledSince = -1f;

        public GarageStationDockView Dock { get { return _dock; } }
        public RoadsideRepairPanelView RoadsidePanel { get { return _roadside; } }
        public bool IsDockVisible { get { return _dockVisible; } }
        public bool IsRoadsidePanelVisible { get { return _roadsideVisible; } }
        public bool IsBreakdownPromptVisible { get { return _promptVisible; } }
        public string BreakdownPromptText { get { return _promptText != null ? _promptText.text : string.Empty; } }
        public string Feedback { get { return _feedback; } }
        public float LastDynoHorsepower { get { return _lastDynoHp; } }

        /// <summary>Builds the overlay canvas and both views (hidden), wires their events and the UI pointer guards.</summary>
        public void Initialize(AppCompositionRoot app)
        {
            if (_app != null)
            {
                return;
            }

            _app = app;
            Font font = HudStyle.Font;

            GameObject canvasGo = new GameObject("StationUi_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60; // Above the scene HUD, below pause (190), title (200) and settings (300).
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = HudStyle.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRoot = (RectTransform)canvasGo.transform;

            _dockHost = CreateStretchedHost("DockHost");
            _dock = _dockHost.AddComponent<GarageStationDockView>();
            _dock.BuildDefaultLayout((RectTransform)_dockHost.transform);
            _dockHost.SetActive(false);

            _roadsideHost = CreateStretchedHost("RoadsideHost");
            _roadside = _roadsideHost.AddComponent<RoadsideRepairPanelView>();
            _roadside.BuildDefaultLayout((RectTransform)_roadsideHost.transform);
            _roadsideHost.SetActive(false);

            _promptHost = new GameObject("BreakdownPrompt", typeof(RectTransform), typeof(Image));
            _promptHost.transform.SetParent(_canvasRoot, false);
            RectTransform prt = (RectTransform)_promptHost.transform;
            prt.anchorMin = new Vector2(0.5f, 0f);
            prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 120f);
            prt.sizeDelta = new Vector2(820f, 64f);
            Image pbg = _promptHost.GetComponent<Image>();
            pbg.color = new Color(0.45f, 0.10f, 0.08f, 0.88f);
            pbg.raycastTarget = false;
            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_promptHost.transform, false);
            RectTransform trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            _promptText = textGo.AddComponent<Text>();
            _promptText.font = font;
            _promptText.fontSize = 24;
            _promptText.alignment = TextAnchor.MiddleCenter;
            _promptText.color = Color.white;
            _promptText.raycastTarget = false;
            _promptHost.SetActive(false);

            WireDock();
            WireRoadside();

            // The scaffolded torque-wrench HUD sits in the bottom-right corner, exactly where the dock goes: lift it above the dock.
            RectTransform wrenchRt = _app.WrenchHud != null ? _app.WrenchHud.transform as RectTransform : null;
            if (wrenchRt != null)
            {
                wrenchRt.anchoredPosition += new Vector2(0f, DockClearance);
            }

            System.Func<bool> blocker = IsPointerOverUi;
            if (_app.Workstand != null) { _app.Workstand.PointerBlocker = blocker; }
            if (_app.Wrench != null) { _app.Wrench.PointerBlocker = blocker; }
        }

        private static bool IsPointerOverUi()
        {
            EventSystem es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        private GameObject CreateStretchedHost(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_canvasRoot, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        // ------------------------------------------------------------------ Session / frame hooks

        /// <summary>Shows the dock in the Garage, the repair panel while stranded, and hides everything otherwise.</summary>
        public void OnSessionState(GameSessionState state)
        {
            if (_app == null)
            {
                return;
            }

            bool garage = state == GameSessionState.Garage;
            bool roadside = state == GameSessionState.RoadsideRepair;
            if (garage != _dockVisible)
            {
                _dockVisible = garage;
                _dockHost.SetActive(garage);
                if (garage)
                {
                    RefreshStationStates();
                }
            }

            if (roadside != _roadsideVisible)
            {
                _roadsideVisible = roadside;
                _roadsideHost.SetActive(roadside);
                if (roadside)
                {
                    _roadside.SetMessage(string.Empty);
                    _roadside.SetFooterInteractable(true, true, true);
                    _roadside.RenderFaults(_app.ActiveBike, _app.World, _app.Catalog);
                    _roadside.FocusDefault();
                }
            }

            if (state != GameSessionState.Ride)
            {
                SetPrompt(false);
            }
        }

        /// <summary>Per-frame: refreshes whichever panel is visible and handles the Interact "pull over" press. Idle cost: zero allocations.</summary>
        public void Tick(GameSessionState state, in FrameControlInputs controls)
        {
            if (_app == null || _app.World == null)
            {
                return;
            }

            if (_dockVisible)
            {
                RefreshJetLabel();
                float capacity = _app.ActiveBike.Fuel.TankCapacityLiters;
                float fuelPct = capacity > 0.01f ? _app.ActiveBike.Fuel.FuelLevelLiters / capacity * 100f : 0f;
                _dock.RenderStatus(_app.World.CashUsd, _app.World.Reputation, _jetLabel, fuelPct, _lastDynoHp, _lastPeakRpm, _feedback);
            }

            if (_roadsideVisible)
            {
                _roadside.RenderFaults(_app.ActiveBike, _app.World, _app.Catalog);
            }

            if (state == GameSessionState.Ride)
            {
                // An active fault plus a dead / stalling / sputtering engine is a breakdown. A fouled plug stalls intermittently, so the
                // prompt latches for a few seconds. A stall without any fault (dry tank, flooded) prompts after a few seconds too, so the
                // rider can never be stranded without a way to the roadside panel. Interact pulls over whenever a fault or stall exists.
                EngineTelemetrySnapshot t = _app.FrameTelemetry;
                bool hasFault = _app.ActiveBike.GetActiveFaults().Count > 0;
                float now = Time.unscaledTime;
                if (t.IsStalled)
                {
                    if (_stalledSince < 0f) { _stalledSince = now; }
                }
                else
                {
                    _stalledSince = -1f;
                }

                bool stalledLong = _stalledSince >= 0f && now - _stalledSince >= StallPromptDelaySeconds;
                if ((hasFault && (t.IsStalled || !t.IsRunning || t.IsSputtering || t.IsHardSeized)) || stalledLong)
                {
                    _promptHoldUntil = now + PromptHoldSeconds;
                }

                SetPrompt(now < _promptHoldUntil);
                if ((hasFault || t.IsStalled) && controls.InteractPressed)
                {
                    _app.PullOverForRoadsideRepair();
                }
            }
        }

        /// <summary>Esc handler: while stranded at the roadside it resumes the ride (the panel's Resume button). Returns whether it consumed Esc.</summary>
        public bool CloseTopPanel()
        {
            if (_roadsideVisible)
            {
                ResumeRide();
                return true;
            }

            return false;
        }

        private void SetPrompt(bool visible)
        {
            if (visible)
            {
                Key key = _app.Input != null ? _app.Input.InteractKey : Key.E;
                if (key != _promptKey)
                {
                    _promptKey = key;
                    _promptText.text = "ENGINE TROUBLE - press [" + key + "] to pull over and fix it";
                }
            }

            if (visible != _promptVisible)
            {
                _promptVisible = visible;
                _promptHost.SetActive(visible);
            }
        }

        // ------------------------------------------------------------------ Roadside

        private void WireRoadside()
        {
            _roadside.RepairFaultRequested += OnRepairFault;
            _roadside.BuddyTowRequested += OnCallTow;
            _roadside.PedalHomeRequested += OnPedalHome;
            _roadside.ResumeRideRequested += ResumeRide;
        }

        private void OnRepairFault(string faultId)
        {
            RoadsideRepairResult r = _app.AttemptRoadsideRepair(faultId);
            if (r.Succeeded)
            {
                _feedback = "Roadside fix done: " + r.Message;
                _roadside.SetMessage("Fixed: " + r.Message);
            }
            else
            {
                _roadside.SetMessage("Repair failed: " + r.Message);
            }

            _roadside.FocusDefault();
        }

        private void OnCallTow()
        {
            RecoveryResult r = _app.RecoverWithBuddyTow();
            if (r.Succeeded)
            {
                _feedback = "Towed home for $" + r.CashCostUsd.ToString("F2", CultureInfo.InvariantCulture) + ". The fault is still there - fix it at the workstand.";
            }
            else
            {
                _roadside.SetMessage("Tow failed: " + r.Message);
            }
        }

        private void OnPedalHome()
        {
            RecoveryResult r = _app.RecoverWithPedalOfShame();
            if (r.Succeeded)
            {
                _feedback = "Pedalled home (" + r.DistanceMiles.ToString("0.0", CultureInfo.InvariantCulture) + " mi). Fix the fault at the workstand.";
            }
            else
            {
                _roadside.SetMessage("Cannot pedal home: " + r.Message);
            }
        }

        private void ResumeRide()
        {
            if (_app.Session != null)
            {
                _app.Session.TryTransitionTo(GameSessionState.Ride);
            }
        }

        // ------------------------------------------------------------------ Garage dock

        private void WireDock()
        {
            _dock.RideRequested += delegate { _app.TryStartRide(); };
            _dock.OpenShopRequested += delegate { _app.ToggleShop(); };
            _dock.OpenSettingsRequested += delegate { _app.OpenSettings(); };
            _dock.CyclePaintRequested += OnPaint;
            _dock.DynoPullRequested += OnDynoPull;
            _dock.PlugChopRequested += OnPlugChop;
            _dock.CycleMixRatioRequested += OnCycleMixRatio;
            _dock.RefuelRequested += OnRefuel;
            _dock.SwapJetRequested += OnSwapJet;
            _dock.SaveRequested += OnSave;
            // Swap Meet, Jobs and installing owned parts have no App-level API yet: RefreshStationStates disables them.
        }

        /// <summary>Enables / disables station buttons according to what the App and Garage controllers can actually do.</summary>
        public void RefreshStationStates()
        {
            const string soon = "Coming soon: ";
            _dock.SetButtonState("Btn_SwapMeet", false, soon + "the swap meet is not reachable from the garage yet.");
            _dock.SetButtonState("Btn_Jobs", false, soon + "pick up courier jobs from the garage.");
            _dock.SetButtonState("Btn_Inventory", false, soon + "install owned parts from here. For now swap parts at the workstand.");
            _dock.SetButtonState("Btn_DynoPull", _app.Dyno != null, null);
            _dock.SetButtonState("Btn_PlugChop", _app.PlugInspector != null, null);
            _dock.SetButtonState("Btn_Refuel", _app.Premix != null, null);
            _dock.SetButtonState("Btn_MixRatio", _app.Premix != null, null);
            _dock.SetButtonState("Btn_SwapJet", _app.CarbBench != null, null);
            UpdateMixLabel();
        }

        private void RefreshJetLabel()
        {
            int size = _app.CarbBench != null ? _app.CarbBench.CurrentMainJetSize : 0;
            if (size != _lastJetSize)
            {
                _lastJetSize = size;
                _jetLabel = size > 0 ? "#" + size.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }
        }

        private void OnPaint()
        {
            string id = _app.CyclePaintSwatch();
            _feedback = id != null ? "Paint: " + id : "No paint booth.";
        }

        private void OnPlugChop()
        {
            if (_app.PlugInspector == null)
            {
                _feedback = "No plug station.";
                return;
            }

            SparkPlugReading reading = _app.PlugInspector.Inspect();
            _feedback = reading.IsInstalled ? "Plug read: " + PlugChopChartView.GetZoneLabel(reading.ColorZone) : "No plug installed.";
        }

        private void OnDynoPull()
        {
            if (_app.Dyno == null)
            {
                _feedback = "No dyno.";
                return;
            }

            _app.Dyno.PedalStartSeconds = DynoKickStartSeconds;
            EngineTelemetrySnapshot t = _app.Dyno.RunFor(DynoPullSeconds, 1f, DynoStepSeconds);
            _lastDynoHp = _app.Dyno.PeakPowerWatts / WattsPerHorsepower;
            _lastPeakRpm = _app.Dyno.PeakRpm;
            if (t.IsSeized || t.IsHardSeized)
            {
                _feedback = "Dyno pull: ENGINE SEIZED at " + _lastPeakRpm.ToString("0", CultureInfo.InvariantCulture) + " rpm.";
            }
            else if (_app.Dyno.MaxAfr <= 0f)
            {
                _feedback = "Dyno pull: engine never fired - check fuel, spark plug and active faults.";
            }
            else
            {
                float minAfr = _app.Dyno.MinAfr > 1000f ? 0f : _app.Dyno.MinAfr;
                _feedback = "Dyno " + DynoPullSeconds.ToString("0", CultureInfo.InvariantCulture) + " s: "
                    + _lastDynoHp.ToString("0.0", CultureInfo.InvariantCulture) + " hp, AFR "
                    + minAfr.ToString("0.0", CultureInfo.InvariantCulture) + "-" + _app.Dyno.MaxAfr.ToString("0.0", CultureInfo.InvariantCulture)
                    + ", CHT " + _app.Dyno.PeakChtCelsius.ToString("0", CultureInfo.InvariantCulture) + " C";
            }
        }

        private void OnCycleMixRatio()
        {
            _ratioIndex = (_ratioIndex + 1) % MixRatios.Length;
            UpdateMixLabel();
            float ratio = MixRatios[_ratioIndex];
            _feedback = ratio <= 0f ? "Straight gas selected: NO OIL, the engine will seize." : "Premix set to " + ratio.ToString("0", CultureInfo.InvariantCulture) + ":1.";
        }

        private void UpdateMixLabel()
        {
            float ratio = MixRatios[_ratioIndex];
            string text = ratio <= 0f ? "Straight gas!" : "Mix " + ratio.ToString("0", CultureInfo.InvariantCulture) + ":1";
            _dock.SetButtonLabel("Btn_MixRatio", "PREMIX", text);
        }

        private void OnRefuel()
        {
            if (_app.Premix == null || _app.ActiveBike == null)
            {
                _feedback = "No premix station.";
                return;
            }

            float ratio = MixRatios[_ratioIndex];
            float before = _app.ActiveBike.Fuel.FuelLevelLiters;
            _app.Premix.MixBatch(JerrycanLiters, ratio);
            _app.Premix.RefuelTank(JerrycanLiters);
            float added = _app.ActiveBike.Fuel.FuelLevelLiters - before;
            string mix = ratio <= 0f ? "straight gas" : ratio.ToString("0", CultureInfo.InvariantCulture) + ":1 premix";
            _feedback = added > 0.01f
                ? "Poured " + added.ToString("0.0", CultureInfo.InvariantCulture) + " L of " + mix + " into the tank."
                : "Tank is already full.";
        }

        private void OnSwapJet()
        {
            CarbWorkbenchController bench = _app.CarbBench;
            if (bench == null || bench.AvailableMainJets.Count == 0)
            {
                _feedback = "No main jets in the parts crate.";
                return;
            }

            int current = bench.CurrentMainJetSize;
            PartDefinition next = null;
            PartDefinition smallest = null;
            for (int i = 0; i < bench.AvailableMainJets.Count; i++)
            {
                PartDefinition jet = bench.AvailableMainJets[i];
                int size = jet.Stats.MainJetSize;
                if (smallest == null || size < smallest.Stats.MainJetSize) { smallest = jet; }
                if (size > current && (next == null || size < next.Stats.MainJetSize)) { next = jet; }
            }

            WrenchActionResult r = bench.SwapMainJet(next != null ? next : smallest);
            _feedback = r.Message;
        }

        private void OnSave()
        {
            _feedback = _app.SaveGame() ? "Game saved." : "Save failed (see log).";
        }
    }
}
