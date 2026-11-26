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
        [SerializeField] private PaintBoothController _paintBooth;

        [Header("World")]
        [SerializeField] private ZoneStreamManager _zones;
        [SerializeField] private BayAreaWeatherController _weather;
        [SerializeField] private CourierGigController _courier;
        [SerializeField] private RallyPackController _rally;

        [Header("UI")]
        [SerializeField] private RideHudView _rideHud;
        [SerializeField] private ToolRollOverlayView _toolRollView;
        [SerializeField] private GarageWrenchHudView _wrenchHud;
        [SerializeField] private PlugChopChartView _plugChart;
        [SerializeField] private PartsCatalogShopView _shop;
        [SerializeField] private DiagnosticHintsPresenter _hints;
        [SerializeField] private SettingsMenuView _settingsView;
        [SerializeField] private SwapMeetView _swapMeet;
        [SerializeField] private JobBoardHudView _jobBoard;

        [Header("Settings")]
        [SerializeField] private string _settingsFileName = "settings.json";

        [Header("Audio")]
        [SerializeField] private TwoStrokeAudioDriver _audio;

        [Header("Ride / Garage Presentation")]
        [Tooltip("Local pose of the assembled .glb bike under RideMoped (wheel mounts ride ~0.42 m above the road, 0.3 m above the body origin).")]
        [SerializeField] private Vector3 _rideVisualLocalOffset = new Vector3(0f, -0.12f, 0f);
        [Tooltip("Keeps the ride rigidbody kinematic outside Ride / RoadsideRepair so it never falls before a zone streams in.")]
        [SerializeField] private bool _holdRideBodyKinematicOffRoad = true;

        private readonly List<string> _inventory = new List<string>(32);
        private readonly List<string> _unresolvedInventory = new List<string>(4);
        private readonly List<string> _toolRollChoices = new List<string>(24);
        private SimulationWorld _world;
        private BikeSimulation _bike;
        private GarageBikeContext _garageContext;
        private CatalogDatabase _catalog;
        private IReadOnlyList<PartDefinition> _shopCatalog;
        private bool _bootstrapped;
        private EngineTelemetrySnapshot _frameTelemetry;
        private SaveState _lastLoadedSave;
        private BikeCustomizationState _customization = new BikeCustomizationState();
        private GameSettingsData _settings;
        private bool _persistSettings;
        private SwapMeetMarket _swapMarket;
        private AudioSource _engineAudioSource;
        private float _lastAppliedAirDensity = RideInputState.StandardAirDensity;
        private float _lastAppliedGrip = 1f;
        private Rigidbody _rideBody;
        private Transform _bikeVisualHome;
        private Vector3 _bikeVisualHomePosition;
        private Quaternion _bikeVisualHomeRotation = Quaternion.identity;
        private Vector3 _bikeVisualHomeScale = Vector3.one;
        private bool _bikeVisualHomeCaptured;

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

        /// <summary>
        /// Part ids on the garage shelf: Core <c>World.Inventory</c> (the source of truth) plus saved ids no catalog could resolve.
        /// Rebuilt on access (UI / save events only, never per frame).
        /// </summary>
        public IReadOnlyList<string> Inventory
        {
            get
            {
                RefreshInventoryMirror();
                return _inventory;
            }
        }

        private void RefreshInventoryMirror()
        {
            _inventory.Clear();
            if (_world != null)
            {
                IReadOnlyList<PartInstance> items = _world.Inventory.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    _inventory.Add(items[i].PartId);
                }
            }

            for (int i = 0; i < _unresolvedInventory.Count; i++)
            {
                _inventory.Add(_unresolvedInventory[i]);
            }
        }

        public EngineTelemetrySnapshot FrameTelemetry
        {
            get { return _frameTelemetry; }
        }

        /// <summary>Paint booth customization of the active bike (persisted as paintHex / patinaLevel / decalStyle).</summary>
        public BikeCustomizationState Customization
        {
            get { return _customization; }
        }

        /// <summary>Applied settings (keybindings, audio mix, hints, graphics); null before bootstrap.</summary>
        public GameSettingsData Settings
        {
            get { return _settings; }
        }

        /// <summary>Parts Alley swap meet stock (null when bootstrapped without a shop catalog).</summary>
        public SwapMeetMarket SwapMarket
        {
            get { return _swapMarket; }
        }

        public CourierGigController Courier
        {
            get { return _courier; }
        }

        public RallyPackController Rally
        {
            get { return _rally; }
        }

        public ZoneStreamManager Zones
        {
            get { return _zones; }
        }

        public BayAreaWeatherController Weather
        {
            get { return _weather; }
        }

        public PaintBoothController PaintBooth
        {
            get { return _paintBooth; }
        }

        public InputAdapter Input
        {
            get { return _input; }
        }

        /// <summary>Ambient air density last pushed into the ride controller (zone baseline blended with Karl the Fog).</summary>
        public float LastAppliedAmbientAirDensity
        {
            get { return _lastAppliedAirDensity; }
        }

        /// <summary>Weather surface-grip multiplier last pushed into the ride controller.</summary>
        public float LastAppliedSurfaceGripMultiplier
        {
            get { return _lastAppliedGrip; }
        }

        public uint Seed
        {
            get { return _seed; }
        }

        /// <summary>
        /// The assembled .glb bike hierarchy (BikeVisualAssembler.AssemblyRoot) when it is a dedicated child that can travel between
        /// the Garage workstand and RideMoped; null otherwise.
        /// </summary>
        public Transform BikeVisualRoot
        {
            get { return _bikeVisualHomeCaptured && _assembler != null ? _assembler.AssemblyRoot : null; }
        }

        /// <summary>True while the assembled bike visual is parented under the ride controller (Ride / RoadsideRepair).</summary>
        public bool IsBikeVisualOnRideMoped
        {
            get
            {
                Transform visual = BikeVisualRoot;
                return visual != null && _ride != null && visual.parent == _ride.transform;
            }
        }

        public bool IsSettingsMenuOpen
        {
            get { return _settingsView != null && _settingsView.gameObject.activeSelf; }
        }

        public bool IsShopOpen
        {
            get { return _shop != null && _shop.gameObject.activeSelf; }
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

        /// <summary>
        /// Assigns M6 economy / customization / settings components in code (SceneScaffoldBuilder, tests). Any argument may be null.
        /// </summary>
        public void AssignEconomyComponents(
            PaintBoothController paintBooth,
            CourierGigController courier,
            RallyPackController rally,
            SwapMeetView swapMeet,
            JobBoardHudView jobBoard,
            SettingsMenuView settingsView)
        {
            _paintBooth = paintBooth;
            _courier = courier;
            _rally = rally;
            _swapMeet = swapMeet;
            _jobBoard = jobBoard;
            _settingsView = settingsView;
        }

        /// <summary>
        /// Uses <paramref name="settings"/> for the next bootstrap (or applies them immediately when already bootstrapped).
        /// </summary>
        public void ApplySettings(GameSettingsData settings)
        {
            _settings = SettingsPreferencesStore.Sanitize(settings != null ? settings : SettingsPreferencesStore.CreateDefaults());
            if (!_bootstrapped)
            {
                return;
            }

            if (_settingsView != null && !ReferenceEquals(_settingsView.CurrentSettings, _settings))
            {
                _settingsView.Bind(_settings, _hints);
                _settings = _settingsView.CurrentSettings;
            }

            ApplySettingsToSystems(_settings);
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
            if (_paintBooth == null) { _paintBooth = Find<PaintBoothController>(); }
            if (_courier == null) { _courier = Find<CourierGigController>(); }
            if (_rally == null) { _rally = Find<RallyPackController>(); }
            if (_settingsView == null) { _settingsView = Find<SettingsMenuView>(); }
            if (_swapMeet == null) { _swapMeet = Find<SwapMeetView>(); }
            if (_jobBoard == null) { _jobBoard = Find<JobBoardHudView>(); }
        }

        /// <summary>
        /// Settings file location (Application.persistentDataPath/settings.json by default).
        /// </summary>
        public string ResolveSettingsPath()
        {
            return Path.Combine(Application.persistentDataPath, string.IsNullOrEmpty(_settingsFileName) ? "settings.json" : _settingsFileName);
        }

        private static T Find<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
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
            _settings = SettingsPreferencesStore.LoadFromPath(ResolveSettingsPath());
            _persistSettings = true;
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
        /// Composes the session around an existing world plus a loaded catalog (job board, zone environments, save restore).
        /// </summary>
        public void BootstrapWithWorld(SimulationWorld world, IReadOnlyList<PartDefinition> shopCatalog, CatalogDatabase catalog)
        {
            _catalog = catalog;
            BootstrapWithWorld(world, shopCatalog);
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

            if (_paintBooth != null)
            {
                _paintBooth.Bind(_assembler);
                _paintBooth.SetPaint(_customization.PaintHex, _customization.PatinaLevel, _paintBooth.MetallicFlake);
                _paintBooth.PaintChanged += OnPaintChanged;
            }

            if (_courier != null)
            {
                _courier.Bind(world, _bike, null);
                if (_courier.Rider == null && _ride != null)
                {
                    _courier.SetRider(_ride.transform);
                }

                _courier.RewardCollected += OnJobRewardCollected;
            }

            if (_rally != null && _courier != null)
            {
                _rally.Bind(_courier);
            }

            _swapMarket = shopCatalog != null ? new SwapMeetMarket(shopCatalog, _seed) : null;
            if (_swapMeet != null)
            {
                _swapMeet.SetMarket(_swapMarket);
                _swapMeet.PurchaseRequested += OnSwapMeetPurchaseRequested;
            }

            _engineAudioSource = _audio != null ? _audio.GetComponent<AudioSource>() : null;
            if (_settings == null)
            {
                _settings = SettingsPreferencesStore.CreateDefaults();
            }

            if (_settingsView != null)
            {
                _settingsView.Bind(_settings, _hints);
                _settings = _settingsView.CurrentSettings;
                _settingsView.SettingsApplied += OnSettingsApplied;
                _settingsView.DiagnosticHintsToggled += OnDiagnosticHintsToggled;
                _settingsView.AudioMixChanged += OnAudioMixChanged;
                _settingsView.KeyBindingsChanged += OnKeyBindingsChanged;
                _settingsView.GraphicsPresetChanged += OnGraphicsPresetChanged;
            }

            ApplySettingsToSystems(_settings);

            _garageContext.AssemblyChanged += OnAssemblyChanged;
            _rideBody = _ride != null ? _ride.GetComponent<Rigidbody>() : null;
            CaptureBikeVisualHome();
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

            if (_paintBooth != null)
            {
                _paintBooth.PaintChanged -= OnPaintChanged;
            }

            if (_courier != null)
            {
                _courier.RewardCollected -= OnJobRewardCollected;
            }

            if (_swapMeet != null)
            {
                _swapMeet.PurchaseRequested -= OnSwapMeetPurchaseRequested;
            }

            if (_settingsView != null)
            {
                _settingsView.SettingsApplied -= OnSettingsApplied;
                _settingsView.DiagnosticHintsToggled -= OnDiagnosticHintsToggled;
                _settingsView.AudioMixChanged -= OnAudioMixChanged;
                _settingsView.KeyBindingsChanged -= OnKeyBindingsChanged;
                _settingsView.GraphicsPresetChanged -= OnGraphicsPresetChanged;
            }

            _bootstrapped = false;
        }

        private void Update()
        {
            if (!_bootstrapped)
            {
                return;
            }

            FrameControlInputs controls = _input != null ? _input.SampleInputs() : default(FrameControlInputs);
            if (controls.ToggleGarageRidePressed || controls.ToggleSettingsPressed || controls.ToggleShopPressed || controls.CyclePaintPressed)
            {
                HandleSessionHotkeys(in controls);
            }

            GameSessionState state = _session != null ? _session.CurrentState : GameSessionState.Garage;

            if (_weather != null && _ride != null)
            {
                PushRideEnvironment(_weather.CurrentAmbientAirDensity, _weather.CurrentSurfaceGripMultiplier);
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

            if (_courier != null)
            {
                // The job clock runs on the road (ride, roadside repair, tow) and pauses on the garage workstand.
                if (state != GameSessionState.Garage && _courier.HasActiveJob)
                {
                    float roughness = _ride != null && _ride.IsGrounded ? _ride.RearSurface.Roughness : 0f;
                    _courier.StepJob(Time.deltaTime, in _frameTelemetry, roughness);
                }

                if (_jobBoard != null)
                {
                    JobSessionSnapshot snapshot = _courier.GetSnapshot();
                    _jobBoard.Render(in snapshot, _courier.ActiveJobTitle);
                }
            }
        }

        private void PushRideEnvironment(float airDensity, float grip)
        {
            _lastAppliedAirDensity = airDensity;
            _lastAppliedGrip = grip;
            if (_ride != null)
            {
                _ride.SetEnvironment(airDensity, grip);
            }
        }

        // ---------------------------------------------------------------- Jobs, swap meet & paint booth

        /// <summary>
        /// Accepts a catalog job by id through the courier controller (Core checks cargo rack, seize state, active job).
        /// </summary>
        public JobAcceptResult AcceptJob(string jobId)
        {
            JobDefinition job;
            if (_catalog == null || string.IsNullOrEmpty(jobId) || !_catalog.Jobs.TryGetValue(jobId, out job) || job == null)
            {
                return new JobAcceptResult(false, JobRejectReason.InvalidJob, jobId ?? string.Empty, 0, 0f, CargoCapacity.None,
                    "Unknown job '" + (jobId ?? string.Empty) + "'.");
            }

            return AcceptJob(job);
        }

        public JobAcceptResult AcceptJob(JobDefinition job)
        {
            if (_courier == null)
            {
                return new JobAcceptResult(false, JobRejectReason.NoBike, job != null ? job.Id : string.Empty, 0, 0f, CargoCapacity.None,
                    "No courier dispatch in the scene.");
            }

            return _courier.AcceptJob(job);
        }

        public bool AbandonJob()
        {
            return _courier != null && _courier.AbandonJob();
        }

        /// <summary>Collects every pending job payout into world cash / reputation (exactly once; Core rule).</summary>
        public bool CollectJobReward()
        {
            return _courier != null && _courier.CollectReward();
        }

        /// <summary>Buys a swap meet offer (Core prices, defects, and cash) into the garage inventory.</summary>
        public SwapMeetPurchaseResult TryBuySwapMeetOffer(string offerId)
        {
            if (_swapMarket == null || _world == null)
            {
                return new SwapMeetPurchaseResult(false, offerId ?? string.Empty, null, SwapMeetDefect.None, false, 0m,
                    _world != null ? _world.CashUsd : 0m, "The swap meet is closed.");
            }

            SwapMeetPurchaseResult result = _swapMarket.TryBuyOffer(offerId, _world);
            if (_shop != null)
            {
                _shop.SetCash(_world.CashUsd);
            }

            if (_swapMeet != null)
            {
                _swapMeet.ShowPurchaseResult(in result);
            }

            return result;
        }

        /// <summary>Applies a paint booth swatch to the active bike (customization is persisted on save).</summary>
        public bool ApplyPaintSwatch(string swatchId)
        {
            return _paintBooth != null && _paintBooth.ApplySwatch(swatchId);
        }

        /// <summary>Sets the active bike's decal style (Core validates the length).</summary>
        public bool SetDecalStyle(string decalStyle)
        {
            return _customization.TrySetDecalStyle(decalStyle);
        }

        private void OnPaintChanged(string hex, float patina, float metallic)
        {
            _customization.TrySetPaintHex(hex);
            _customization.SetPatinaLevel(patina);
        }

        private void OnJobRewardCollected(decimal cashUsd)
        {
            if (_shop != null)
            {
                _shop.SetCash(cashUsd);
            }
        }

        private void OnSwapMeetPurchaseRequested(string offerId)
        {
            TryBuySwapMeetOffer(offerId);
        }

        // ---------------------------------------------------------------- Settings

        private void ApplySettingsToSystems(GameSettingsData settings)
        {
            if (settings == null)
            {
                return;
            }

            if (_input != null)
            {
                _input.ApplyKeyBindings(settings);
            }

            ApplyAudioMix(settings.MasterVolume, settings.EngineVolume);
            ApplyDiagnosticHints(settings.DiagnosticHintsEnabled);
        }

        private void ApplyAudioMix(float master, float engine)
        {
            AudioListener.volume = Mathf.Clamp01(master);
            if (_engineAudioSource != null)
            {
                _engineAudioSource.volume = Mathf.Clamp01(engine);
            }
        }

        private void ApplyDiagnosticHints(bool enabled)
        {
            if (_hints != null)
            {
                _hints.HintsEnabled = enabled;
            }

            if (_audio != null)
            {
                _audio.SetDiagnosticHints(enabled);
            }
        }

        private void OnSettingsApplied(GameSettingsData settings)
        {
            _settings = settings;
            ApplySettingsToSystems(settings);
            if (_persistSettings)
            {
                try
                {
                    SettingsPreferencesStore.SaveToPath(ResolveSettingsPath(), settings);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AppCompositionRoot] Could not save settings: " + ex.Message);
                }
            }
        }

        private void OnDiagnosticHintsToggled(bool enabled)
        {
            ApplyDiagnosticHints(enabled);
        }

        private void OnAudioMixChanged(AudioMixSettings mix)
        {
            ApplyAudioMix(mix.MasterVolume, mix.EngineVolume);
        }

        private void OnKeyBindingsChanged(GameSettingsData settings)
        {
            if (_input != null)
            {
                _input.ApplyKeyBindings(settings);
            }
        }

        private void OnGraphicsPresetChanged(GraphicsQualitySettings graphics)
        {
            Application.targetFrameRate = graphics.TargetFrameRate;
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

        /// <summary>
        /// F1 / Tab: Garage -> Ride or Ride -> Garage (RoadsideRepair / Tow must be resolved through repair or recovery first).
        /// </summary>
        public bool ToggleGarageRide()
        {
            if (_session == null)
            {
                return false;
            }

            switch (_session.CurrentState)
            {
                case GameSessionState.Garage:
                    return TryStartRide();
                case GameSessionState.Ride:
                    return ReturnToGarage();
                default:
                    return false;
            }
        }

        /// <summary>F2 / Escape: shows or hides the settings menu; returns whether it is open afterwards.</summary>
        public bool ToggleSettingsMenu()
        {
            if (_settingsView == null)
            {
                return false;
            }

            bool open = !_settingsView.gameObject.activeSelf;
            SetViewVisible(_settingsView, open);
            return IsSettingsMenuOpen;
        }

        /// <summary>F3: shows or hides the parts catalog shop (refreshing the cash readout); returns whether it is open afterwards.</summary>
        public bool ToggleShop()
        {
            if (_shop == null)
            {
                return false;
            }

            bool open = !_shop.gameObject.activeSelf;
            if (open && _world != null)
            {
                _shop.SetCash(_world.CashUsd);
            }

            SetViewVisible(_shop, open);
            return IsShopOpen;
        }

        /// <summary>
        /// F4: applies the next curated paint booth swatch (after the one matching the current paint) live on the bike. Returns the
        /// applied swatch id, or null without a paint booth.
        /// </summary>
        public string CyclePaintSwatch()
        {
            IReadOnlyList<PaintSwatch> swatches = PaintBoothController.Swatches;
            if (_paintBooth == null || swatches.Count == 0)
            {
                return null;
            }

            int current = -1;
            for (int i = 0; i < swatches.Count; i++)
            {
                if (string.Equals(swatches[i].Hex, _paintBooth.CurrentPaintHex, StringComparison.OrdinalIgnoreCase))
                {
                    current = i;
                    break;
                }
            }

            PaintSwatch next = swatches[(current + 1) % swatches.Count];
            return _paintBooth.ApplySwatch(next.Id) ? next.Id : null;
        }

        private void HandleSessionHotkeys(in FrameControlInputs controls)
        {
            if (controls.ToggleGarageRidePressed)
            {
                ToggleGarageRide();
            }

            if (controls.ToggleSettingsPressed)
            {
                ToggleSettingsMenu();
            }

            if (controls.ToggleShopPressed)
            {
                ToggleShop();
            }

            if (controls.CyclePaintPressed)
            {
                CyclePaintSwatch();
            }
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

            PartInstance bought;
            PurchaseResult purchase = _world.TryPurchasePartIntoInventory(part, out bought);
            if (!purchase.Succeeded)
            {
                return false;
            }

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
            return SaveLoadBridge.Capture(_world, DefaultBikeInstanceId, _unresolvedInventory, _seed, hints, _lastLoadedSave, _customization);
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
            IReadOnlyDictionary<string, PartDefinition> parts = ResolvePartLookup();
            SaveLoadBridge.ApplyWorldState(state, _world, parts);
            _unresolvedInventory.Clear();
            for (int i = 0; i < state.Inventory.Count; i++)
            {
                InventoryItemState item = state.Inventory[i];
                if (item != null && !string.IsNullOrEmpty(item.PartId) && !parts.ContainsKey(item.PartId))
                {
                    // Kept as an id so a save -> load -> save cycle never drops parts a stripped catalog cannot resolve.
                    _unresolvedInventory.Add(item.PartId);
                }
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
                    _customization = SaveLoadBridge.ReadCustomization(state.Bikes[i]);
                    if (_paintBooth != null)
                    {
                        _paintBooth.SetPaint(_customization.PaintHex, _customization.PatinaLevel, _paintBooth.MetallicFlake);
                    }
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

        private IReadOnlyDictionary<string, PartDefinition> ResolvePartLookup()
        {
            if (_catalog != null)
            {
                return _catalog.Parts;
            }

            Dictionary<string, PartDefinition> lookup = new Dictionary<string, PartDefinition>(StringComparer.Ordinal);
            if (_shopCatalog != null)
            {
                for (int i = 0; i < _shopCatalog.Count; i++)
                {
                    PartDefinition p = _shopCatalog[i];
                    if (p != null && !string.IsNullOrEmpty(p.Id))
                    {
                        lookup[p.Id] = p;
                    }
                }
            }

            return lookup;
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

            bool onRoad = state == GameSessionState.Ride || state == GameSessionState.RoadsideRepair;
            if (_rideBody != null && _holdRideBodyKinematicOffRoad && _rideBody.isKinematic == onRoad)
            {
                _rideBody.isKinematic = !onRoad;
            }

            PlaceBikeVisual(onRoad);
            ApplyCameraForState(garage);
            ApplyHudVisibility(state);
        }

        // ---------------------------------------------------------------- Ride <-> Garage presentation (event-driven)

        private void CaptureBikeVisualHome()
        {
            if (_bikeVisualHomeCaptured || _assembler == null)
            {
                return;
            }

            Transform visual = _assembler.AssemblyRoot;
            // AssemblyRoot falls back to the assembler's own transform: never drag the WorkStand (and its controllers) around.
            if (visual == null || visual == _assembler.transform || visual.parent == null || (_ride != null && visual.parent == _ride.transform))
            {
                return;
            }

            _bikeVisualHome = visual.parent;
            _bikeVisualHomePosition = visual.localPosition;
            _bikeVisualHomeRotation = visual.localRotation;
            _bikeVisualHomeScale = visual.localScale;
            _bikeVisualHomeCaptured = true;
        }

        /// <summary>
        /// Moves the assembled bike visual onto RideMoped on the road and back onto the workstand in the Garage, restoring its
        /// original local pose (the PaintBooth renderer root and workstand orbit pivot keep pointing at the same hierarchy).
        /// </summary>
        private void PlaceBikeVisual(bool onRoad)
        {
            Transform visual = BikeVisualRoot;
            if (visual == null)
            {
                return;
            }

            if (onRoad && _ride != null)
            {
                if (visual.parent != _ride.transform)
                {
                    visual.SetParent(_ride.transform, false);
                }

                visual.localPosition = _rideVisualLocalOffset;
                visual.localRotation = Quaternion.identity;
                visual.localScale = _bikeVisualHomeScale;
            }
            else if (!onRoad && _bikeVisualHome != null)
            {
                if (visual.parent != _bikeVisualHome)
                {
                    visual.SetParent(_bikeVisualHome, false);
                }

                visual.localPosition = _bikeVisualHomePosition;
                visual.localRotation = _bikeVisualHomeRotation;
                visual.localScale = _bikeVisualHomeScale;
            }
        }

        private void ApplyCameraForState(bool garage)
        {
            Camera rideCamera = _cameraRig != null ? _cameraRig.AttachedCamera : null;
            if (rideCamera == null)
            {
                return;
            }

            // The workstand toggles the inspection camera; the chase camera renders everywhere else (or always without one).
            Camera inspection = _workstand != null ? _workstand.InspectionCamera : null;
            rideCamera.enabled = !garage || inspection == null;
        }

        /// <summary>
        /// Mode-aware HUD panels: Garage shows the wrench HUD / plug chart / tool roll; Ride shows the ride HUD / job board / hints;
        /// RoadsideRepair shows the tool roll / wrench HUD / hints; Tow shows hints only.
        /// </summary>
        private void ApplyHudVisibility(GameSessionState state)
        {
            bool garage = state == GameSessionState.Garage;
            bool ride = state == GameSessionState.Ride;
            bool roadside = state == GameSessionState.RoadsideRepair;

            SetViewVisible(_rideHud, ride);
            SetViewVisible(_jobBoard, ride || roadside);
            SetViewVisible(_hints, true);
            SetViewVisible(_wrenchHud, garage || roadside);
            SetViewVisible(_toolRollView, garage || roadside);
            SetViewVisible(_plugChart, garage);
            if (!garage)
            {
                SetViewVisible(_shop, false);
                SetViewVisible(_swapMeet, false);
            }
        }

        private void SetViewVisible(Component view, bool visible)
        {
            if (view == null)
            {
                return;
            }

            GameObject go = view.gameObject;
            // Never deactivate the composition root itself (views may share its GameObject in tests and minimal rigs).
            if (go == gameObject || transform.IsChildOf(go.transform))
            {
                return;
            }

            if (go.activeSelf != visible)
            {
                go.SetActive(visible);
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
                PushRideEnvironment(_weather.CurrentAmbientAirDensity, _weather.CurrentSurfaceGripMultiplier);
            }
            else
            {
                PushRideEnvironment(zone.AmbientAirDensity, zone.BaseSurfaceGrip);
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
