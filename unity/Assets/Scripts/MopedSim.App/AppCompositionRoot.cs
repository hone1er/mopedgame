using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MopedSim.Audio;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Garage;
using MopedSim.Ride;
using MopedSim.UI;
using MopedSim.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MopedSim.App
{
    /// <summary>
    /// Composition root: loads catalogs (MopedSim.Data), builds the deterministic SimulationWorld (MopedSim.Core), and wires
    /// Ride / Garage / World / UI / Audio together in code (no UnityEvent inspector wiring). Owns the per-frame telemetry fan-out
    /// and guarantees exactly one stepper advances the active bike per frame: the ride controller in Ride, the center-stand dyno
    /// in Garage, nothing in RoadsideRepair / Tow. Update performs zero GC allocations.
    /// </summary>
    public sealed class AppCompositionRoot : MonoBehaviour
    {
        public const string DefaultBikeInstanceId = "bike_primary";

        [Header("Bootstrap")]
        [SerializeField] private bool _bootstrapOnStart = true;
        [SerializeField] private uint _seed = 1u;
        [SerializeField] private string _startingArchetypeId = ArchetypeIds.MaxerE50;
        [SerializeField] private string _catalogDirectoryOverride = string.Empty;
        [SerializeField] private string[] _additiveScenes = new string[] { "Garage", "UI_Overlay" };
        [SerializeField] private string _initialZoneScene = "Zone_WestOakland";

        [Header("App")]
        [SerializeField] private InputAdapter _input;
        [SerializeField] private GameSessionStateMachine _session;

        [Header("Ride")]
        [SerializeField] private MopedBikeController _ride;
        [SerializeField] private AeroTuckController _aero;
        [SerializeField] private MopedCameraRig _cameraRig;

        [Header("Garage")]
        [SerializeField] private BikeVisualAssembler _assembler;
        [SerializeField] private GarageWorkstandController _workstand;
        [SerializeField] private FastenerWrenchInteractor _wrench;
        [SerializeField] private CarbWorkbenchController _carbBench;
        [SerializeField] private PlugChopInspector _plugInspector;
        [SerializeField] private PremixStationController _premix;
        [SerializeField] private CenterStandDynoController _dyno;

        [Header("World")]
        [SerializeField] private ZoneStreamManager _zones;
        [SerializeField] private BayAreaWeatherController _weather;

        [Header("UI")]
        [SerializeField] private RideHudView _rideHud;
        [SerializeField] private ToolRollOverlayView _toolRollView;
        [SerializeField] private GarageWrenchHudView _wrenchHud;
        [SerializeField] private PlugChopChartView _plugChart;
        [SerializeField] private PartsCatalogShopView _shop;
        [SerializeField] private DiagnosticHintsPresenter _hints;

        [Header("Audio")]
        [SerializeField] private TwoStrokeAudioDriver _audio;

        private readonly List<string> _inventory = new List<string>(32);
        private readonly List<string> _toolRollChoices = new List<string>(24);
        private SimulationWorld _world;
        private BikeSimulation _bike;
        private GarageBikeContext _garageContext;
        private CatalogDatabase _catalog;
        private IReadOnlyList<PartDefinition> _shopCatalog;
        private bool _bootstrapped;
        private EngineTelemetrySnapshot _frameTelemetry;
        private SaveState _lastLoadedSave;

        public event Action<RoadsideRepairResult> RoadsideRepairAttempted;
        public event Action<RecoveryResult> RecoveryCompleted;

        public bool IsBootstrapped
        {
            get { return _bootstrapped; }
        }

        public SimulationWorld World
        {
            get { return _world; }
        }

        public BikeSimulation ActiveBike
        {
            get { return _bike; }
        }

        public GarageBikeContext GarageContext
        {
            get { return _garageContext; }
        }

        /// <summary>
        /// Loaded catalog database (null when bootstrapped from an injected world).
        /// </summary>
        public CatalogDatabase Catalog
        {
            get { return _catalog; }
        }

        public GameSessionStateMachine Session
        {
            get { return _session; }
        }

        /// <summary>
        /// Parts offered by the shop terminal / carb jet crate (catalog PartList, or the injected list).
        /// </summary>
        public IReadOnlyList<PartDefinition> ShopCatalog
        {
            get { return _shopCatalog; }
        }

        public IReadOnlyList<string> Inventory
        {
            get { return _inventory; }
        }

        public EngineTelemetrySnapshot FrameTelemetry
        {
            get { return _frameTelemetry; }
        }

        public uint Seed
        {
            get { return _seed; }
        }

        public bool BootstrapOnStart
        {
            get { return _bootstrapOnStart; }
            set { _bootstrapOnStart = value; }
        }

        /// <summary>
        /// Assigns scene components in code (SceneScaffoldBuilder, tests). Any argument may be null.
        /// </summary>
        public void AssignCoreComponents(InputAdapter input, GameSessionStateMachine session, MopedBikeController ride, CenterStandDynoController dyno)
        {
            _input = input;
            _session = session;
            _ride = ride;
            _dyno = dyno;
        }

        public void AssignGarageComponents(
            BikeVisualAssembler assembler,
            GarageWorkstandController workstand,
            FastenerWrenchInteractor wrench,
            CarbWorkbenchController carbBench,
            PlugChopInspector plugInspector,
            PremixStationController premix)
        {
            _assembler = assembler;
            _workstand = workstand;
            _wrench = wrench;
            _carbBench = carbBench;
            _plugInspector = plugInspector;
            _premix = premix;
        }

        public void AssignPresentationComponents(
            RideHudView rideHud,
            ToolRollOverlayView toolRollView,
            GarageWrenchHudView wrenchHud,
            PlugChopChartView plugChart,
            PartsCatalogShopView shop,
            DiagnosticHintsPresenter hints,
            TwoStrokeAudioDriver audioDriver)
        {
            _rideHud = rideHud;
            _toolRollView = toolRollView;
            _wrenchHud = wrenchHud;
            _plugChart = plugChart;
            _shop = shop;
            _hints = hints;
            _audio = audioDriver;
        }

        public void AssignWorldComponents(ZoneStreamManager zones, BayAreaWeatherController weather, AeroTuckController aero, MopedCameraRig cameraRig)
        {
            _zones = zones;
            _weather = weather;
            _aero = aero;
            _cameraRig = cameraRig;
        }

        private IEnumerator Start()
        {
            if (!_bootstrapOnStart || _bootstrapped)
            {
                yield break;
            }

            // Garage.unity and UI_Overlay.unity are separate additive scenes (ADR-004); load them, then resolve their
            // components once (never per frame) because cross-scene serialized references are not possible.
            for (int i = 0; i < _additiveScenes.Length; i++)
            {
                string sceneName = _additiveScenes[i];
                if (string.IsNullOrEmpty(sceneName) || SceneManager.GetSceneByName(sceneName).isLoaded
                    || !Application.CanStreamedLevelBeLoaded(sceneName))
                {
                    continue;
                }

                AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                while (op != null && !op.isDone)
                {
                    yield return null;
                }
            }

            ResolveMissingComponents();
            if (_bootstrapped)
            {
                yield break;
            }

            if (BootstrapFromCatalogs() && _zones != null && !string.IsNullOrEmpty(_initialZoneScene))
            {
                LoadInitialZone();
            }
        }

        private async void LoadInitialZone()
        {
            try
            {
                await _zones.TransitionToZoneAsync(_initialZoneScene, false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Fills unassigned references from loaded scenes (one-time bootstrap lookup).
        /// </summary>
        public void ResolveMissingComponents()
        {
            if (_input == null) { _input = Find<InputAdapter>(); }
            if (_session == null) { _session = Find<GameSessionStateMachine>(); }
            if (_ride == null) { _ride = Find<MopedBikeController>(); }
            if (_aero == null) { _aero = Find<AeroTuckController>(); }
            if (_cameraRig == null) { _cameraRig = Find<MopedCameraRig>(); }
            if (_assembler == null) { _assembler = Find<BikeVisualAssembler>(); }
            if (_workstand == null) { _workstand = Find<GarageWorkstandController>(); }
            if (_wrench == null) { _wrench = Find<FastenerWrenchInteractor>(); }
            if (_carbBench == null) { _carbBench = Find<CarbWorkbenchController>(); }
            if (_plugInspector == null) { _plugInspector = Find<PlugChopInspector>(); }
            if (_premix == null) { _premix = Find<PremixStationController>(); }
            if (_dyno == null) { _dyno = Find<CenterStandDynoController>(); }
            if (_zones == null) { _zones = Find<ZoneStreamManager>(); }
            if (_weather == null) { _weather = Find<BayAreaWeatherController>(); }
            if (_rideHud == null) { _rideHud = Find<RideHudView>(); }
            if (_toolRollView == null) { _toolRollView = Find<ToolRollOverlayView>(); }
            if (_wrenchHud == null) { _wrenchHud = Find<GarageWrenchHudView>(); }
            if (_plugChart == null) { _plugChart = Find<PlugChopChartView>(); }
            if (_shop == null) { _shop = Find<PartsCatalogShopView>(); }
            if (_hints == null) { _hints = Find<DiagnosticHintsPresenter>(); }
            if (_audio == null) { _audio = Find<TwoStrokeAudioDriver>(); }
        }

        private static T Find<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        }

        /// <summary>
        /// Default catalog location: repo data/catalogs next to the Unity project in the Editor, StreamingAssets/Catalogs in players.
        /// </summary>
        public string ResolveCatalogDirectory()
        {
            if (!string.IsNullOrEmpty(_catalogDirectoryOverride))
            {
                return _catalogDirectoryOverride;
            }

            if (Application.isEditor)
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs"));
            }

            // Staged by MopedSim.Editor.CliBuild before every player build (see CliBuild.StagedCatalogFolder).
            return Path.Combine(Application.streamingAssetsPath, "Catalogs");
        }

        /// <summary>
        /// Loads + validates catalogs through MopedSim.Data and composes the session. Returns false (with logged errors) on failure.
        /// </summary>
        public bool BootstrapFromCatalogs()
        {
            string dir = ResolveCatalogDirectory();
            CatalogDatabase db;
            try
            {
                db = CatalogLoader.LoadFromDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AppCompositionRoot] Catalog load failed from '" + dir + "': " + ex.Message);
                return false;
            }

            CatalogValidationReport report = CatalogLoader.Validate(db);
            if (!report.IsValid)
            {
                for (int i = 0; i < report.Errors.Count; i++)
                {
                    Debug.LogError("[AppCompositionRoot] Catalog validation: " + report.Errors[i]);
                }

                return false;
            }

            ZoneEnvironmentSpec startZone;
            if (!db.Zones.TryGetValue("Zone_WestOakland", out startZone))
            {
                startZone = null;
            }

            SimulationWorld world = new SimulationWorld(new SeededSimRandom(_seed), startZone);
            BikeSimulation bike = new BikeSimulation(db.GetArchetype(_startingArchetypeId), db.Parts);
            world.RegisterBike(DefaultBikeInstanceId, bike, true);

            _catalog = db;
            if (_assembler != null)
            {
                _assembler.SetSocketBindings(BuildSocketBindings(db, _startingArchetypeId));
            }

            BootstrapWithWorld(world, db.PartList);
            return true;
        }

        /// <summary>
        /// Builds slot-to-socket bindings from the archetype catalog's per-slot "attachSocket" metadata (falls back to defaults).
        /// </summary>
        public static SlotSocketBindings BuildSocketBindings(CatalogDatabase db, string archetypeId)
        {
            SlotSocketBindings bindings = SlotSocketBindings.CreateDefault();
            ArchetypeMetadata meta;
            if (db != null && db.ArchetypeMeta.TryGetValue(archetypeId, out meta) && meta != null)
            {
                for (int i = 0; i < meta.Slots.Count; i++)
                {
                    SlotMetadata slot = meta.Slots[i];
                    if (slot != null && !string.IsNullOrEmpty(slot.AttachSocket))
                    {
                        bindings.Set(slot.SlotId, slot.AttachSocket);
                    }
                }
            }

            return bindings;
        }

        /// <summary>
        /// Composes the session around an existing world (used by BootstrapFromCatalogs and by deterministic PlayMode scenarios).
        /// </summary>
        public void BootstrapWithWorld(SimulationWorld world, IReadOnlyList<PartDefinition> shopCatalog)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (world.ActiveBike == null)
            {
                throw new InvalidOperationException("SimulationWorld has no active bike registered.");
            }

            Unwire();
            _world = world;
            _bike = world.ActiveBike;
            _shopCatalog = shopCatalog;
            _garageContext = new GarageBikeContext(_bike, world.Random);
            _frameTelemetry = _bike.LatestTelemetry;

            if (_ride != null)
            {
                _ride.SetAero(_aero);
                _ride.Bind(_bike, world.Random);
                _ride.SetSurfaceResolver(WorldSurfaceBridge.TryResolve);
                if (_weather != null)
                {
                    // Karl the Fog trigger volumes only react to the ride moped's rigidbody.
                    _weather.SetTrackedRider(_ride.GetComponent<Rigidbody>());
                }
            }

            if (_workstand != null)
            {
                _workstand.Assembler = _assembler;
                _workstand.Bind(_garageContext);
                _workstand.NodeSelected += OnWorkstandNodeSelected;
            }

            if (_wrench != null)
            {
                _wrench.Bind(_garageContext);
                _wrench.ActiveFastenerChanged += OnActiveFastenerChanged;
                _wrench.WrenchResult += OnWrenchResult;
            }

            if (_carbBench != null)
            {
                _carbBench.Bind(_garageContext);
                _carbBench.SetJetCatalog(shopCatalog);
            }

            if (_plugInspector != null)
            {
                _plugInspector.Bind(_garageContext);
                _plugInspector.PlugInspected += OnPlugInspected;
            }

            if (_premix != null)
            {
                _premix.Bind(_garageContext);
            }

            if (_dyno != null)
            {
                _dyno.Bind(_garageContext);
            }

            if (_toolRollView != null)
            {
                BuildToolRollChoices(shopCatalog);
                _toolRollView.SetAvailableItems(_toolRollChoices);
                _toolRollView.PackRequested += OnPackRequested;
                _toolRollView.UnpackRequested += OnUnpackRequested;
            }

            if (_shop != null)
            {
                if (shopCatalog != null)
                {
                    _shop.SetCatalog(shopCatalog);
                }

                _shop.SetCash(world.CashUsd);
                _shop.PurchaseRequested += OnPurchaseRequested;
            }

            if (_zones != null)
            {
                _zones.ActiveZoneChanged += OnActiveZoneChanged;
            }

            if (_session != null)
            {
                _session.StateChanged += OnSessionStateChanged;
            }

            _garageContext.AssemblyChanged += OnAssemblyChanged;
            _bootstrapped = true;

            ApplyZone(world.CurrentZone);
            RefreshToolRollView();
            SyncVisuals();
            ApplySessionState(_session != null ? _session.CurrentState : GameSessionState.Garage);
        }

        private void OnDestroy()
        {
            Unwire();
        }

        private void Unwire()
        {
            if (!_bootstrapped)
            {
                return;
            }

            if (_workstand != null)
            {
                _workstand.NodeSelected -= OnWorkstandNodeSelected;
            }

            if (_wrench != null)
            {
                _wrench.ActiveFastenerChanged -= OnActiveFastenerChanged;
                _wrench.WrenchResult -= OnWrenchResult;
            }

            if (_plugInspector != null)
            {
                _plugInspector.PlugInspected -= OnPlugInspected;
            }

            if (_toolRollView != null)
            {
                _toolRollView.PackRequested -= OnPackRequested;
                _toolRollView.UnpackRequested -= OnUnpackRequested;
            }

            if (_shop != null)
            {
                _shop.PurchaseRequested -= OnPurchaseRequested;
            }

            if (_zones != null)
            {
                _zones.ActiveZoneChanged -= OnActiveZoneChanged;
            }

            if (_session != null)
            {
                _session.StateChanged -= OnSessionStateChanged;
            }

            if (_garageContext != null)
            {
                _garageContext.AssemblyChanged -= OnAssemblyChanged;
            }

            _bootstrapped = false;
        }

        private void Update()
        {
            if (!_bootstrapped)
            {
                return;
            }

            GameSessionState state = _session != null ? _session.CurrentState : GameSessionState.Garage;
            FrameControlInputs controls = _input != null ? _input.SampleInputs() : default(FrameControlInputs);

            if (_weather != null && _ride != null)
            {
                _ride.SetEnvironment(_weather.CurrentAmbientAirDensity, _weather.CurrentSurfaceGripMultiplier);
            }

            if (state == GameSessionState.Ride && _ride != null)
            {
                _ride.SetControls(
                    controls.Throttle,
                    controls.FrontBrake,
                    controls.RearBrake,
                    controls.Steer,
                    controls.AeroTuck,
                    controls.PedalAssist,
                    controls.Choke,
                    controls.Decompressor);
                _frameTelemetry = _ride.LatestTelemetry;
                if (_cameraRig != null)
                {
                    if (controls.ToggleCameraPressed)
                    {
                        _cameraRig.ToggleMode();
                    }

                    _cameraRig.UpdateRideTelemetry(_ride.SpeedMps, _aero != null ? _aero.TuckBlend : 0f);
                }
            }
            else if (state == GameSessionState.Garage && _dyno != null && _dyno.IsRunning)
            {
                _frameTelemetry = _dyno.Latest;
            }
            else
            {
                _frameTelemetry = _bike.LatestTelemetry;
            }

            if (state == GameSessionState.Garage && _wrenchHud != null && _wrench != null)
            {
                _wrenchHud.SetAppliedTorque(_wrench.PendingTorqueNm);
            }

            if (_rideHud != null)
            {
                _rideHud.Render(in _frameTelemetry);
            }

            if (_audio != null)
            {
                _audio.UpdateTelemetry(in _frameTelemetry);
            }

            if (_hints != null)
            {
                _hints.Present(in _frameTelemetry, _bike.GetActiveFaults());
            }
        }

        // ---------------------------------------------------------------- Session flow

        public bool TryStartRide()
        {
            return _session != null && _session.TryTransitionTo(GameSessionState.Ride);
        }

        public bool ReturnToGarage()
        {
            return _session != null && _session.TryTransitionTo(GameSessionState.Garage);
        }

        public bool PullOverForRoadsideRepair()
        {
            return _session != null && _session.TryTransitionTo(GameSessionState.RoadsideRepair);
        }

        /// <summary>
        /// Attempts a roadside repair with the packed Tool Roll (Core decides). Resumes riding once no faults remain.
        /// </summary>
        public RoadsideRepairResult AttemptRoadsideRepair(string faultId)
        {
            if (_world == null)
            {
                return new RoadsideRepairResult(false, faultId, FaultType.FouledPlug, "Session not bootstrapped.");
            }

            RoadsideRepairResult result = _world.TryRoadsideRepairActiveBike(faultId);
            RefreshToolRollView();
            Action<RoadsideRepairResult> handler = RoadsideRepairAttempted;
            if (handler != null)
            {
                handler.Invoke(result);
            }

            if (result.Succeeded && _bike.GetActiveFaults().Count == 0 && _session != null
                && _session.CurrentState == GameSessionState.RoadsideRepair)
            {
                _session.TryTransitionTo(GameSessionState.Ride);
            }

            _garageContext.NotifyAssemblyChanged();
            return result;
        }

        /// <summary>
        /// Calls the Buddy Truck (Core computes the fee); on success the bike lands back in the Garage.
        /// </summary>
        public RecoveryResult RecoverWithBuddyTow()
        {
            if (_world == null)
            {
                return new RecoveryResult(false, "BuddyTow", 0f, 0f, 0m, "Session not bootstrapped.");
            }

            if (_session != null && !_session.TryTransitionTo(GameSessionState.Tow))
            {
                return new RecoveryResult(false, "BuddyTow", 0f, 0f, 0m, "Cannot call a tow from " + _session.CurrentState + ".");
            }

            RecoveryResult result = _world.RecoverActiveBikeWithBuddyTow();
            if (result.Succeeded && _session != null)
            {
                _session.TryTransitionTo(GameSessionState.Garage);
            }

            FinishRecovery(result);
            return result;
        }

        /// <summary>
        /// Pedals the dead bike home (Core computes stamina cost) and returns to the Garage.
        /// </summary>
        public RecoveryResult RecoverWithPedalOfShame()
        {
            if (_world == null)
            {
                return new RecoveryResult(false, "PedalOfShame", 0f, 0f, 0m, "Session not bootstrapped.");
            }

            RecoveryResult result = _bike.PedalOfShame(_world.CurrentZone.DistanceFromGarageMiles);
            if (result.Succeeded && _session != null)
            {
                if (_session.CurrentState == GameSessionState.Tow || !_session.TryTransitionTo(GameSessionState.Garage))
                {
                    _session.ForceState(GameSessionState.Garage);
                }
            }

            FinishRecovery(result);
            return result;
        }

        private void FinishRecovery(RecoveryResult result)
        {
            if (_shop != null)
            {
                _shop.SetCash(_world.CashUsd);
            }

            Action<RecoveryResult> handler = RecoveryCompleted;
            if (handler != null)
            {
                handler.Invoke(result);
            }

            _garageContext.NotifyAssemblyChanged();
        }

        // ---------------------------------------------------------------- Tool roll & shop

        public bool TryPackToolRollItem(string itemId)
        {
            bool packed = _world != null && _world.TryPackToolRollItem(itemId);
            RefreshToolRollView();
            return packed;
        }

        public bool UnpackToolRollItem(string itemId)
        {
            bool removed = _world != null && _world.RemoveToolRollItem(itemId);
            RefreshToolRollView();
            return removed;
        }

        /// <summary>
        /// Buys a catalog part into the milk-crate inventory. Cash rules live in Core (SimulationWorld.TryPurchasePart).
        /// </summary>
        public bool TryPurchasePart(PartDefinition part)
        {
            if (_world == null || part == null)
            {
                return false;
            }

            PurchaseResult purchase = _world.TryPurchasePart(part);
            if (!purchase.Succeeded)
            {
                return false;
            }

            _inventory.Add(part.Id);
            if (_shop != null)
            {
                _shop.SetCash(_world.CashUsd);
            }

            return true;
        }

        // ---------------------------------------------------------------- Save / load

        public SaveState CaptureSave()
        {
            bool hints = _hints == null || _hints.HintsEnabled;
            return SaveLoadBridge.Capture(_world, DefaultBikeInstanceId, _inventory, _seed, hints, _lastLoadedSave);
        }

        public void SaveToPath(string path)
        {
            SaveLoadBridge.SaveToPath(CaptureSave(), path);
        }

        /// <summary>
        /// Restores world + active bike state from a save file (migrated by MopedSim.Data). Returns entries Core rejected.
        /// </summary>
        public int LoadFromPath(string path)
        {
            SaveState state = SaveLoadBridge.LoadFromPath(path);
            _lastLoadedSave = state;
            SaveLoadBridge.ApplyWorldState(state, _world);
            _inventory.Clear();
            for (int i = 0; i < state.Inventory.Count; i++)
            {
                _inventory.Add(state.Inventory[i].PartId);
            }

            if (_hints != null)
            {
                _hints.HintsEnabled = state.DiagnosticHintsEnabled;
            }

            int rejected = 0;
            for (int i = 0; i < state.Bikes.Count; i++)
            {
                if (string.Equals(state.Bikes[i].InstanceId, state.ActiveBikeInstanceId, StringComparison.Ordinal))
                {
                    rejected += SaveLoadBridge.ApplyBikeState(state.Bikes[i], _bike, _catalog != null ? _catalog.Parts : null);
                }
            }

            RefreshToolRollView();
            if (_shop != null)
            {
                _shop.SetCash(_world.CashUsd);
            }

            _garageContext.NotifyAssemblyChanged();
            return rejected;
        }

        // ---------------------------------------------------------------- Event handlers (event-driven, may allocate)

        private void OnSessionStateChanged(GameSessionState previous, GameSessionState current)
        {
            ApplySessionState(current);
        }

        private void ApplySessionState(GameSessionState state)
        {
            bool riding = state == GameSessionState.Ride;
            if (_ride != null)
            {
                _ride.SimulationEnabled = riding;
                if (!riding)
                {
                    _ride.SetControls(0f, 0f, 0f, 0f, false, false, false, false);
                }
            }

            if (_dyno != null && state != GameSessionState.Garage)
            {
                _dyno.StopRun();
            }

            bool garage = state == GameSessionState.Garage;
            if (_workstand != null)
            {
                _workstand.SetInspectionActive(garage);
                if (garage)
                {
                    _workstand.RefreshInteractables();
                }
            }

            if (_wrench != null)
            {
                _wrench.ReadMouseInput = garage;
                if (!garage)
                {
                    _wrench.CancelTorque();
                }
            }
        }

        private void OnAssemblyChanged()
        {
            if (_ride != null)
            {
                _ride.RefreshChassisFromSimulation();
            }

            SyncVisuals();
        }

        private async void SyncVisuals()
        {
            if (_assembler == null || _bike == null)
            {
                return;
            }

            try
            {
                await _assembler.SyncWithSimulationAsync(_bike);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void OnWorkstandNodeSelected(InteractableNode node)
        {
            if (node.IsFastener && _wrench != null)
            {
                _wrench.SelectFastener(node.NodeId);
            }
        }

        private void OnActiveFastenerChanged(FastenerState fastener)
        {
            if (_wrenchHud != null)
            {
                _wrenchHud.ShowFastener(fastener);
            }
        }

        private void OnWrenchResult(WrenchActionResult result)
        {
            if (_wrenchHud != null)
            {
                _wrenchHud.ShowResult(in result);
            }
        }

        private void OnPlugInspected(SparkPlugReading reading)
        {
            if (_plugChart != null)
            {
                _plugChart.Render(in reading);
            }
        }

        private void OnPackRequested(string itemId)
        {
            TryPackToolRollItem(itemId);
        }

        private void OnUnpackRequested(string itemId)
        {
            UnpackToolRollItem(itemId);
        }

        private void OnPurchaseRequested(PartDefinition part)
        {
            TryPurchasePart(part);
        }

        private void OnActiveZoneChanged(string zoneId)
        {
            ZoneEnvironmentSpec zone;
            if (_catalog != null && _catalog.Zones.TryGetValue(zoneId, out zone) && zone != null)
            {
                ApplyZone(zone);
            }
        }

        private void ApplyZone(ZoneEnvironmentSpec zone)
        {
            if (zone == null || _world == null)
            {
                return;
            }

            _world.SetZone(zone);
            if (_weather != null)
            {
                _weather.ApplyZoneEnvironment(zone);
                if (_ride != null)
                {
                    _ride.SetEnvironment(_weather.CurrentAmbientAirDensity, _weather.CurrentSurfaceGripMultiplier);
                }
            }
            else if (_ride != null)
            {
                _ride.SetEnvironment(zone.AmbientAirDensity, zone.BaseSurfaceGrip);
            }
        }

        private void RefreshToolRollView()
        {
            if (_toolRollView != null && _world != null)
            {
                _toolRollView.Render(_world.ActiveToolRoll, _world.ToolRollCapacity);
            }
        }

        private void BuildToolRollChoices(IReadOnlyList<PartDefinition> catalog)
        {
            _toolRollChoices.Clear();
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    PartDefinition p = catalog[i];
                    if (p != null && (p.Stats.IsTool || p.Stats.IsSpare || p.Category == PartCategory.ToolRollItem))
                    {
                        _toolRollChoices.Add(p.Id);
                    }
                }
            }

            if (_toolRollChoices.Count == 0)
            {
                _toolRollChoices.Add(ToolIds.SparkPlugWrench);
                _toolRollChoices.Add(ToolIds.SparePlug);
                _toolRollChoices.Add(ToolIds.Flathead);
                _toolRollChoices.Add(ToolIds.NeedleNose);
                _toolRollChoices.Add(ToolIds.FeelerGauge);
                _toolRollChoices.Add(ToolIds.Wrench10mm);
                _toolRollChoices.Add(ToolIds.Wrench15mm);
                _toolRollChoices.Add(ToolIds.PenetratingOil);
                _toolRollChoices.Add(ToolIds.WireBrush);
                _toolRollChoices.Add(ToolIds.SpareMasterLink);
                _toolRollChoices.Add(ToolIds.SpareFlangeNut);
                _toolRollChoices.Add(ToolIds.SpareCondenser);
            }
        }
    }
}
