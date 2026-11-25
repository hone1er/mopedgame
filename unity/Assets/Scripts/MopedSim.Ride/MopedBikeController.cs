using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Custom two-wheel raycast suspension + countersteer lean controller.
    /// Longitudinal drive/brake forces come exclusively from BikeSimulation.Step(...) outputs
    /// (RearWheelTorqueNm, EngineBrakeTorqueNm). This class performs rigid-body kinematics only:
    /// NO combustion, AFR, CHT, wrenching, fault, or economy math lives here.
    /// FixedUpdate performs zero managed allocations (cached refs, structs, non-alloc raycasts).
    /// Chassis mass, wheel radius, rolling resistance and base drag area are read from the bound
    /// BikeSimulation (archetype + mounted parts) in Bind(), never duplicated as hand-tuned copies.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MopedBikeController : MonoBehaviour
    {
        [Header("Chassis (overwritten from BikeSimulation.Archetype in Bind)")]
        [SerializeField] private float _riderMassKg = 75f;
        [SerializeField] private Vector3 _centerOfMassLocal = new Vector3(0f, 0.45f, 0f);
        [SerializeField] private bool _autoStepInFixedUpdate = true;

        [Header("Raycast Suspension")]
        [SerializeField] private Transform _frontWheelMount;
        [SerializeField] private Transform _rearWheelMount;
        [SerializeField] private float _suspensionRestLengthM = 0.22f;
        [SerializeField] private float _suspensionTravelM = 0.12f;
        [SerializeField] private float _springStiffnessNPerM = 9000f;
        [SerializeField] private float _damperNsPerM = 650f;
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Lean & Steering")]
        [SerializeField] private float _maxLeanDeg = 38f;
        [SerializeField] private float _leanStiffness = 360f;
        [SerializeField] private float _leanDamping = 60f;
        [SerializeField] private float _lowSpeedSteerYawRateDegPerSec = 55f;
        [SerializeField] private float _yawResponse = 5f;
        [SerializeField] private float _countersteerSpeedMps = 4.5f;

        [Header("Aero / Drag (applied as rigid-body force only)")]
        [SerializeField] private AeroTuckController _aero;
        [SerializeField] private float _referenceAirDensityKgM3 = 1.225f;

        [Header("Optional Subsystems")]
        [SerializeField] private BrakeFadeController _brakes;
        [SerializeField] private PedalInputHandler _pedal;

        private struct WheelState
        {
            public bool Grounded;
            public Vector3 ContactPoint;
            public Vector3 ContactNormal;
            public float Compression;
            public float PrevCompression;
            public SurfaceHazardSample Surface;
        }

        private Rigidbody _rb;
        private Transform _tf;
        private WheelState _front;
        private WheelState _rear;
        private readonly SurfaceHazardDetector _surfaceDetector = new SurfaceHazardDetector();

        private BikeSimulation _sim;
        private ISimRandom _rng;
        private EngineTelemetrySnapshot _latestTelemetry;

        private float _bikeMassKg = 52f;
        private float _wheelRadiusM = 0.27f;
        private float _rollingResistanceCoeff = 0.015f;
        private float _baseCdA = 0.56f;

        private float _throttle;
        private float _frontBrakeInput;
        private float _rearBrakeInput;
        private float _steerInput;
        private bool _tuckInput;
        private bool _pedalAssistInput;
        private bool _chokeInput;
        private bool _decompressorInput;
        private float _ambientAirDensity = 1.0f;
        private float _weatherGripMultiplier = 1.0f;
        private float _lastRailCrossingAngleDeg = 90f;
        private float _lastRailLateralAccel;
        private float _lastLeanDeg;
        private bool _simulationEnabled = true;

        public EngineTelemetrySnapshot LatestTelemetry { get { return _latestTelemetry; } }
        public float SpeedMps { get; private set; }
        public float SpeedMph { get { return SpeedMps * 2.2369363f; } }
        public float LeanAngleDeg { get { return _lastLeanDeg; } }
        public float LastRailCrossingAngleDeg { get { return _lastRailCrossingAngleDeg; } }
        public float LastRailLateralAccel { get { return _lastRailLateralAccel; } }
        public bool IsGrounded { get { return _front.Grounded || _rear.Grounded; } }
        public SurfaceHazardSample RearSurface { get { return _rear.Surface; } }
        public AeroTuckController Aero { get { return _aero; } }
        public BrakeFadeController Brakes { get { return _brakes; } }
        public BikeSimulation Simulation { get { return _sim; } }
        public Rigidbody Body { get { return _rb; } }
        public float WheelRadiusM { get { return _wheelRadiusM; } }
        public float BaseDragAreaCdA { get { return _baseCdA; } }
        public float TotalMassKg { get { return _bikeMassKg + _riderMassKg; } }

        /// <summary>
        /// When true (default) FixedUpdate advances the ride. Tests and replay harnesses disable this and call SimulateStep(dt) explicitly.
        /// </summary>
        public bool AutoStepInFixedUpdate
        {
            get { return _autoStepInFixedUpdate; }
            set { _autoStepInFixedUpdate = value; }
        }

        public bool SimulationEnabled
        {
            get { return _simulationEnabled; }
            set { _simulationEnabled = value; }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_rb != null)
            {
                return;
            }

            _rb = GetComponent<Rigidbody>();
            _tf = transform;
            _rb.mass = _bikeMassKg + _riderMassKg;
            _rb.centerOfMass = _centerOfMassLocal;
            _rb.linearDamping = 0f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _surfaceDetector.ClearCache();
            _ambientAirDensity = RideInputState.StandardAirDensity;
        }

        /// <summary>
        /// Binds the headless MopedSim.Core simulation + deterministic RNG. Called by MopedSim.App.
        /// Reads chassis mass, wheel radius, rolling resistance, and handlebar drag/tuck multipliers from Core
        /// so the rigid body matches the archetype and currently mounted parts.
        /// </summary>
        public void Bind(BikeSimulation simulation, ISimRandom rng)
        {
            EnsureInitialized();
            _sim = simulation;
            _rng = rng;
            if (_sim != null)
            {
                _latestTelemetry = _sim.LatestTelemetry;
            }

            RefreshChassisFromSimulation();
        }

        /// <summary>
        /// Re-reads chassis parameters after garage part swaps (bars, rack, wheels). Event-driven, not per-frame.
        /// </summary>
        public void RefreshChassisFromSimulation()
        {
            if (_sim == null)
            {
                return;
            }

            EnsureInitialized();
            BikeArchetypeSpec archetype = _sim.Archetype;
            _bikeMassKg = Mathf.Max(20f, archetype.BaseDryMassKg + _sim.Assembly.GetMountedPartsMassKg());
            _wheelRadiusM = Mathf.Max(0.15f, archetype.WheelRadiusM);
            _rollingResistanceCoeff = Mathf.Max(0f, archetype.RollingResistanceCoeff);

            float barDrag = 1f;
            float barTuck = 1f;
            PartInstance bars = _sim.Assembly.GetInstalledPart(SlotIds.Handlebars);
            if (bars != null)
            {
                barDrag = bars.Definition.Stats.AeroDragMultiplier;
                barTuck = bars.Definition.Stats.TuckDragBonusMultiplier;
            }

            _baseCdA = Mathf.Max(0.05f, archetype.DragAreaCdA * barDrag);
            if (_aero != null)
            {
                _aero.SetBaseCdA(_baseCdA);
                _aero.HandlebarTuckEfficiency = barTuck;
            }

            _rb.mass = _bikeMassKg + _riderMassKg;
        }

        /// <summary>
        /// Injects a world-side surface resolver (e.g. MopedSim.App bridging MopedSim.World RoadHazardZone) without Ride referencing World.
        /// </summary>
        public void SetSurfaceResolver(SurfaceHazardDetector.ExternalSurfaceResolver resolver)
        {
            _surfaceDetector.SetExternalResolver(resolver);
        }

        public void SetMounts(Transform frontWheel, Transform rearWheel)
        {
            _frontWheelMount = frontWheel;
            _rearWheelMount = rearWheel;
        }

        public void SetAero(AeroTuckController aero)
        {
            _aero = aero;
            if (_aero != null)
            {
                _aero.SetBaseCdA(_baseCdA);
            }
        }

        public void SetBrakes(BrakeFadeController brakes)
        {
            _brakes = brakes;
        }

        public void SetPedalHandler(PedalInputHandler pedal)
        {
            _pedal = pedal;
        }

        public void SetEnvironment(float ambientAirDensity, float weatherGripMultiplier)
        {
            _ambientAirDensity = ambientAirDensity;
            _weatherGripMultiplier = weatherGripMultiplier;
        }

        public void SetControls(
            float throttle,
            float frontBrake,
            float rearBrake,
            float steer,
            bool aeroTuck,
            bool pedalAssist,
            bool choke,
            bool decompressor)
        {
            _throttle = Mathf.Clamp01(throttle);
            _frontBrakeInput = Mathf.Clamp01(frontBrake);
            _rearBrakeInput = Mathf.Clamp01(rearBrake);
            _steerInput = Mathf.Clamp(steer, -1f, 1f);
            _tuckInput = aeroTuck;
            _pedalAssistInput = pedalAssist;
            _chokeInput = choke;
            _decompressorInput = decompressor;
        }

        private void FixedUpdate()
        {
            if (_autoStepInFixedUpdate)
            {
                SimulateStep(Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// Advances one ride tick: wheel raycasts, Core BikeSimulation.Step, then rigid-body forces. Zero GC allocations.
        /// Physics integration itself is performed by Unity (FixedUpdate) or Physics.Simulate (tests).
        /// </summary>
        public void SimulateStep(float dt)
        {
            EnsureInitialized();
            if (dt <= 0f)
            {
                return;
            }

            Vector3 velocity = _rb.linearVelocity;
            Vector3 forward = _tf.forward;
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
            if (flatForward.sqrMagnitude > 0.0001f)
            {
                flatForward.Normalize();
            }

            float forwardSpeed = Vector3.Dot(velocity, flatForward);
            SpeedMps = Mathf.Abs(forwardSpeed);

            if (_pedal != null)
            {
                _pedal.Poll(dt);
            }

            bool pedalAssist = _pedalAssistInput || (_pedal != null && _pedal.PedalAssistHeld);
            bool decompressor = _decompressorInput || (_pedal != null && _pedal.DecompressorHeld);
            bool choke = _chokeInput || (_pedal != null && _pedal.ChokeHeld);

            float cda = _aero != null ? _aero.Step(_tuckInput, dt) : _baseCdA;

            float slopeRad = 0f;
            float roughness = 0f;
            float gripMultiplier = 1f;

            UpdateWheel(_frontWheelMount, ref _front, dt);
            UpdateWheel(_rearWheelMount, ref _rear, dt);

            if (_rear.Grounded)
            {
                roughness = _rear.Surface.Roughness;
                gripMultiplier = _rear.Surface.GripMultiplier * _weatherGripMultiplier;
                Vector3 slopeFwd = Vector3.ProjectOnPlane(forward, _rear.ContactNormal);
                if (slopeFwd.sqrMagnitude > 0.0001f)
                {
                    slopeRad = Mathf.Asin(Mathf.Clamp(slopeFwd.normalized.y, -1f, 1f));
                }
            }

            bool stepped = false;
            if (_sim != null && _rng != null && _simulationEnabled)
            {
                RideInputState input = new RideInputState(
                    _throttle,
                    _frontBrakeInput,
                    _rearBrakeInput,
                    _steerInput,
                    _tuckInput,
                    pedalAssist,
                    choke,
                    decompressor,
                    slopeRad,
                    gripMultiplier,
                    roughness,
                    _ambientAirDensity);

                _latestTelemetry = _sim.Step(dt, in input, SpeedMps, _rng);
                stepped = true;
            }

            if (stepped)
            {
                ApplyDrive(in _latestTelemetry, forwardSpeed);
            }
            ApplyBrakes(forwardSpeed, flatForward, dt);
            ApplyAero(velocity, cda);
            ApplyHazards(flatForward, forwardSpeed, dt);
            ApplyLeanAndSteer(forwardSpeed, dt);
        }

        private void UpdateWheel(Transform mount, ref WheelState wheel, float dt)
        {
            if (mount == null)
            {
                wheel.Grounded = false;
                return;
            }

            float maxLen = _suspensionRestLengthM + _suspensionTravelM + _wheelRadiusM;
            Vector3 origin = mount.position + _tf.up * _suspensionTravelM;
            RaycastHit hit;
            bool didHit = Physics.Raycast(origin, -_tf.up, out hit, maxLen + _suspensionTravelM, _groundMask, QueryTriggerInteraction.Ignore);

            wheel.PrevCompression = wheel.Compression;
            if (!didHit)
            {
                wheel.Grounded = false;
                wheel.Compression = 0f;
                return;
            }

            wheel.Grounded = true;
            wheel.ContactPoint = hit.point;
            wheel.ContactNormal = hit.normal;
            wheel.Surface = _surfaceDetector.ResolveColliderSurface(hit.collider);

            float rayDist = hit.distance - _suspensionTravelM;
            float springLen = rayDist - _wheelRadiusM;
            float compression = Mathf.Clamp(_suspensionRestLengthM - springLen, 0f, _suspensionRestLengthM + _suspensionTravelM);
            wheel.Compression = compression;

            float compressionVel = (compression - wheel.PrevCompression) / Mathf.Max(dt, 0.0001f);
            float springForce = (compression * _springStiffnessNPerM) + (compressionVel * _damperNsPerM);
            springForce = Mathf.Max(0f, springForce);

            _rb.AddForceAtPosition(hit.normal * springForce, hit.point, ForceMode.Force);
        }

        private void ApplyDrive(in EngineTelemetrySnapshot telemetry, float forwardSpeed)
        {
            if (!_rear.Grounded)
            {
                return;
            }

            // Engine braking is a wheel-side drag reported by Core's drivetrain; it only resists forward motion
            // and never pushes a stationary bike backwards.
            float engineBrakeWheelTorque = 0f;
            if (_sim != null && forwardSpeed > 0.5f)
            {
                engineBrakeWheelTorque = Mathf.Max(0f, _sim.Drivetrain.EngineBrakeWheelTorqueNm);
            }

            float netTorque = telemetry.RearWheelTorqueNm - engineBrakeWheelTorque;
            float driveForce = netTorque / _wheelRadiusM;
            float grip = Mathf.Max(0.05f, _rear.Surface.GripMultiplier * _weatherGripMultiplier);
            float maxTraction = grip * _rb.mass * 9.81f * 0.9f;
            driveForce = Mathf.Clamp(driveForce, -maxTraction, maxTraction);

            Vector3 driveDir = Vector3.ProjectOnPlane(_tf.forward, _rear.ContactNormal);
            if (driveDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _rb.AddForceAtPosition(driveDir.normalized * driveForce, _rear.ContactPoint, ForceMode.Force);

            float rollingDrag = _rear.Surface.RollingDragMultiplier * _rollingResistanceCoeff * _rb.mass * 9.81f;
            Vector3 velocity = _rb.linearVelocity;
            Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
            if (flatVel.sqrMagnitude > 0.0025f)
            {
                _rb.AddForce(-flatVel.normalized * rollingDrag, ForceMode.Force);
            }
        }

        private void ApplyBrakes(float forwardSpeed, Vector3 flatForward, float dt)
        {
            if (_brakes == null)
            {
                return;
            }

            float frontTorque;
            float rearTorque;
            _brakes.StepBrakes(_frontBrakeInput, _rearBrakeInput, forwardSpeed, dt, out frontTorque, out rearTorque);

            float sign = forwardSpeed >= 0f ? -1f : 1f;
            if (Mathf.Abs(forwardSpeed) < 0.05f)
            {
                return;
            }

            if (_front.Grounded)
            {
                float grip = Mathf.Max(0.05f, _front.Surface.GripMultiplier * _weatherGripMultiplier);
                float maxForce = grip * _rb.mass * 9.81f * 0.5f;
                float force = Mathf.Min(frontTorque / _wheelRadiusM, maxForce);
                _rb.AddForceAtPosition(flatForward * (sign * force), _front.ContactPoint, ForceMode.Force);
            }

            if (_rear.Grounded)
            {
                float grip = Mathf.Max(0.05f, _rear.Surface.GripMultiplier * _weatherGripMultiplier);
                float maxForce = grip * _rb.mass * 9.81f * 0.45f;
                float force = Mathf.Min(rearTorque / _wheelRadiusM, maxForce);
                _rb.AddForceAtPosition(flatForward * (sign * force), _rear.ContactPoint, ForceMode.Force);
            }
        }

        private void ApplyAero(Vector3 velocity, float cda)
        {
            if (cda <= 0f)
            {
                return;
            }

            float speed = velocity.magnitude;
            if (speed < 0.1f)
            {
                return;
            }

            float airDensityKgM3 = _referenceAirDensityKgM3 * Mathf.Max(0.5f, _ambientAirDensity);
            float dragForce = 0.5f * airDensityKgM3 * cda * speed * speed;
            _rb.AddForce(-velocity / speed * dragForce, ForceMode.Force);
        }

        private void ApplyHazards(Vector3 flatForward, float forwardSpeed, float dt)
        {
            _lastRailLateralAccel = 0f;
            if (!_rear.Grounded)
            {
                return;
            }

            SurfaceHazardSample surface = _rear.Surface;
            if (surface.PotholeImpulse > 0f && SpeedMps > 2f)
            {
                // Deterministic pseudo-bump keyed on distance travelled keeps this allocation- and RNG-free.
                float phase = Mathf.Abs(Mathf.Sin(_rear.ContactPoint.x * 7.3f + _rear.ContactPoint.z * 5.1f));
                if (phase > 0.82f)
                {
                    _rb.AddForce(Vector3.up * (surface.PotholeImpulse * _rb.mass * 6f), ForceMode.Force);
                }
            }

            if (surface.SurfaceType == RoadSurfaceType.SteelRailTrack && surface.HasRailAxis)
            {
                float crossingAngle;
                float lateral = SurfaceHazardDetector.ComputeRailLateralSlipAccel(
                    flatForward,
                    surface.RailAxisWorld,
                    SpeedMps,
                    surface.GripMultiplier,
                    out crossingAngle);

                _lastRailCrossingAngleDeg = crossingAngle;
                _lastRailLateralAccel = lateral;
                if (Mathf.Abs(lateral) > 0.0001f)
                {
                    Vector3 right = _tf.right;
                    _rb.AddForce(right * (lateral * _rb.mass), ForceMode.Force);
                }
            }
        }

        private void ApplyLeanAndSteer(float forwardSpeed, float dt)
        {
            if (!IsGrounded)
            {
                return;
            }

            float speedFactor = Mathf.Clamp01(SpeedMps / 12f);
            float leanTargetDeg = -_steerInput * _maxLeanDeg * Mathf.Lerp(0.25f, 1f, speedFactor);

            // Countersteer: at speed, a brief opposite lean impulse precedes the commanded lean.
            if (SpeedMps > _countersteerSpeedMps)
            {
                leanTargetDeg += _steerInput * 6f * (1f - speedFactor * 0.5f);
            }

            Vector3 up = _tf.up;
            Vector3 fwd = _tf.forward;
            Vector3 worldUp = Vector3.up;

            Vector3 leanAxis = fwd;
            float currentLeanDeg = Vector3.SignedAngle(worldUp, up, leanAxis);
            _lastLeanDeg = currentLeanDeg;

            float leanError = Mathf.DeltaAngle(currentLeanDeg, leanTargetDeg) * Mathf.Deg2Rad;
            float rollRate = Vector3.Dot(_rb.angularVelocity, leanAxis);
            float rollTorque = (leanError * _leanStiffness) - (rollRate * _leanDamping);
            _rb.AddTorque(leanAxis * rollTorque, ForceMode.Force);

            // Yaw rate from lean (bicycle model) blended with low-speed handlebar yaw.
            float leanRad = currentLeanDeg * Mathf.Deg2Rad;
            float leanYaw = SpeedMps > 0.5f ? (9.81f * Mathf.Tan(-leanRad) / Mathf.Max(SpeedMps, 1.5f)) : 0f;
            float lowSpeedYaw = -_steerInput * _lowSpeedSteerYawRateDegPerSec * Mathf.Deg2Rad * (1f - speedFactor);
            float desiredYawRate = Mathf.Clamp(leanYaw + lowSpeedYaw, -2.2f, 2.2f);
            if (forwardSpeed < 0f)
            {
                desiredYawRate = -desiredYawRate;
            }

            float yawRate = Vector3.Dot(_rb.angularVelocity, worldUp);
            float yawTorque = (desiredYawRate - yawRate) * _yawResponse * _rb.mass * 0.4f;
            _rb.AddTorque(worldUp * yawTorque, ForceMode.Force);
        }
    }
}
