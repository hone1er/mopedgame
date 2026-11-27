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
        [SerializeField] private float _maxLeanDeg = 35f;
        [Tooltip("Roll stiffness as a multiple of the rider's gravity toppling stiffness (m*g*h). Must exceed 1 or the bike is an unstable inverted pendulum.")]
        [SerializeField] private float _leanStiffnessScale = 3f;
        [SerializeField] private float _leanDampingRatio = 0.9f;
        [SerializeField] private float _leanRateDegPerSec = 140f;
        [SerializeField] private float _lowSpeedSteerYawRateDegPerSec = 55f;
        [SerializeField] private float _yawResponse = 5f;
        [SerializeField, Range(0f, 1f)] private float _lateralGripResponse = 0.7f;

        [Header("Tip-over Recovery (presentation-layer safety net)")]
        [SerializeField] private float _tipOverLeanDeg = 70f;
        [SerializeField] private float _tipOverRecoverDelaySec = 1.5f;
        [SerializeField] private float _recoverRideHeightM = 0.42f;

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
        private float _leanTargetDeg;
        private float _tippedTimerSec;
        private readonly RaycastHit[] _recoverHits = new RaycastHit[8];

        public EngineTelemetrySnapshot LatestTelemetry { get { return _latestTelemetry; } }
        public float SpeedMps { get; private set; }

        /// <summary>
        /// Signed speed along the bike's flattened heading (negative when rolling backwards).
        /// </summary>
        public float ForwardSpeedMps { get; private set; }
        public float SpeedMph { get { return SpeedMps * 2.2369363f; } }
        public float LeanAngleDeg { get { return _lastLeanDeg; } }
        public float LastRailCrossingAngleDeg { get { return _lastRailCrossingAngleDeg; } }
        public float LastRailLateralAccel { get { return _lastRailLateralAccel; } }
        /// <summary>True once the tip-over safety net or PickUpBike() has stood the bike back up (latched).</summary>
        public bool WasRecoveredFromTipOver { get; private set; }
        public int TipOverRecoveryCount { get; private set; }
        public bool IsTippedOver { get { return _tippedTimerSec > 0f; } }
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

        /// <summary>
        /// Places the moped upright at <paramref name="worldPosition"/> facing <paramref name="yawDeg"/>, zeroing rigid-body
        /// velocities and clearing residual lean, steer, and wheel contact state. Zero GC allocations.
        /// </summary>
        public void ResetUprightAt(Vector3 worldPosition, float yawDeg)
        {
            EnsureInitialized();
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            transform.SetPositionAndRotation(worldPosition, rot);
            if (_rb != null)
            {
                _rb.position = worldPosition;
                _rb.rotation = rot;
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
            }

            _steerInput = 0f;
            _leanTargetDeg = 0f;
            _lastLeanDeg = 0f;
            _lastRailCrossingAngleDeg = 90f;
            _lastRailLateralAccel = 0f;
            SpeedMps = 0f;
            ForwardSpeedMps = 0f;
            _front = default(WheelState);
            _rear = default(WheelState);
        }

        /// <summary>
        /// Stands the bike back upright on the road beneath its current position, facing its current heading, with zero velocity.
        /// Wired to the R / gamepad West "pick up bike" input and to the automatic tip-over recovery. Zero GC allocations.
        /// </summary>
        public void PickUpBike()
        {
            EnsureInitialized();
            Vector3 pos = _tf.position;
            Vector3 heading = Vector3.ProjectOnPlane(_tf.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.05f)
            {
                // Lying nose-up/down: fall back on the roof direction projected flat.
                heading = Vector3.ProjectOnPlane(_tf.up, Vector3.up);
            }

            float yawDeg = heading.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(heading.normalized, Vector3.up).eulerAngles.y : _tf.eulerAngles.y;

            // Find the road below the bike (nearest hit that is not the bike itself).
            Vector3 origin = pos + Vector3.up * 2f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _recoverHits, 8f, _groundMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            float groundY = pos.y;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _recoverHits[i];
                if (h.collider == null || h.collider.attachedRigidbody == _rb || h.distance >= bestDist)
                {
                    continue;
                }

                bestDist = h.distance;
                groundY = h.point.y;
                found = true;
            }

            float mountY = _rearWheelMount != null ? _tf.InverseTransformPoint(_rearWheelMount.position).y : 0f;
            float originHeight = _recoverRideHeightM - mountY + 0.05f;
            Vector3 target = found ? new Vector3(pos.x, groundY + originHeight, pos.z) : pos + Vector3.up * 0.5f;
            ResetUprightAt(target, yawDeg);
            _tippedTimerSec = 0f;
            WasRecoveredFromTipOver = true;
            TipOverRecoveryCount++;
        }

        private void UpdateTipOverRecovery(float dt)
        {
            float tiltDeg = Vector3.Angle(_tf.up, Vector3.up);
            if (tiltDeg > _tipOverLeanDeg)
            {
                _tippedTimerSec += dt;
                if (_tippedTimerSec >= _tipOverRecoverDelaySec)
                {
                    PickUpBike();
                }
            }
            else
            {
                _tippedTimerSec = 0f;
            }
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

            if (!_rb.isKinematic)
            {
                UpdateTipOverRecovery(dt);
            }

            Vector3 velocity = _rb.linearVelocity;
            Vector3 forward = _tf.forward;
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
            if (flatForward.sqrMagnitude > 0.0001f)
            {
                flatForward.Normalize();
            }

            float forwardSpeed = Vector3.Dot(velocity, flatForward);
            ForwardSpeedMps = forwardSpeed;
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

                // Core's drivetrain models forward wheel rotation only; rolling backwards reads as a stopped wheel.
                _latestTelemetry = _sim.Step(dt, in input, Mathf.Max(0f, forwardSpeed), _rng);
                stepped = true;
            }

            if (stepped)
            {
                ApplyDrive(in _latestTelemetry, forwardSpeed);
            }
            ApplyBrakes(forwardSpeed, flatForward, dt);
            ApplyAero(velocity, cda);
            ApplyHazards(flatForward, forwardSpeed, dt);
            ApplyLateralGrip(velocity, flatForward, dt);
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

            bool wasGrounded = wheel.Grounded;
            wheel.Grounded = true;
            wheel.ContactPoint = hit.point;
            wheel.ContactNormal = hit.normal;
            wheel.Surface = _surfaceDetector.ResolveColliderSurface(hit.collider);

            float rayDist = hit.distance - _suspensionTravelM;
            float springLen = rayDist - _wheelRadiusM;
            float compression = Mathf.Clamp(_suspensionRestLengthM - springLen, 0f, _suspensionRestLengthM + _suspensionTravelM);
            wheel.Compression = compression;
            if (!wasGrounded)
            {
                // First contact: no damper spike from a stale zero-compression history.
                wheel.PrevCompression = compression;
            }

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

        /// <summary>
        /// Rear-tyre side force: cancels sideways velocity (relative to the heading) so the bike carves turns instead of
        /// sliding. Limited by the surface/weather grip budget; suppressed while a rail crossing is slipping the tyre.
        /// </summary>
        private void ApplyLateralGrip(Vector3 velocity, Vector3 flatForward, float dt)
        {
            if (!_rear.Grounded || Mathf.Abs(_lastRailLateralAccel) > 0.0001f || flatForward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);
            float lateralVel = Vector3.Dot(velocity, flatRight);
            float grip = Mathf.Max(0.05f, _rear.Surface.GripMultiplier * _weatherGripMultiplier);
            float maxForce = grip * _rb.mass * 9.81f;
            float force = Mathf.Clamp(-lateralVel * _lateralGripResponse * _rb.mass / dt, -maxForce, maxForce);
            _rb.AddForce(flatRight * force, ForceMode.Force);
        }

        /// <summary>
        /// Moped-style lean: the commanded turn is a yaw rate (capped so the implied lean stays under the max lean), the lean
        /// target is the steady-state bicycle lean atan(v*yawRate/g) (so lean ~ v^2/r), and a roll PD with gravity feed-forward
        /// holds it. Stiffness is expressed relative to the toppling stiffness m*g*h, so the bike can never become an
        /// unstable inverted pendulum; releasing steer drives the target (and lean) back to zero.
        /// </summary>
        private void ApplyLeanAndSteer(float forwardSpeed, float dt)
        {
            if (!IsGrounded)
            {
                return;
            }

            const float gravity = 9.81f;
            Vector3 worldUp = Vector3.up;
            Vector3 leanAxis = _tf.forward;
            float currentLeanDeg = Vector3.SignedAngle(worldUp, _tf.up, leanAxis);
            _lastLeanDeg = currentLeanDeg;

            float speed = SpeedMps;
            float lowSpeedYawRate = _lowSpeedSteerYawRateDegPerSec * Mathf.Deg2Rad;
            float maxLeanRad = _maxLeanDeg * Mathf.Deg2Rad;
            float yawRateCap = Mathf.Min(lowSpeedYawRate, gravity * Mathf.Tan(maxLeanRad) / Mathf.Max(speed, 0.1f));
            // A stationary moped cannot pivot on the spot; fade steering authority in over the first metre per second.
            float commandedYawRate = _steerInput * yawRateCap * Mathf.Clamp01(speed / 1f);

            float desiredLeanDeg = 0f;
            if (forwardSpeed > 0.5f)
            {
                desiredLeanDeg = -Mathf.Atan(speed * commandedYawRate / gravity) * Mathf.Rad2Deg;
                desiredLeanDeg = Mathf.Clamp(desiredLeanDeg, -_maxLeanDeg, _maxLeanDeg);
            }

            _leanTargetDeg = Mathf.MoveTowards(_leanTargetDeg, desiredLeanDeg, _leanRateDegPerSec * dt);

            float h = Mathf.Max(0.3f, _rb.centerOfMass.y);
            float toppleStiffness = _rb.mass * gravity * h;
            float stiffness = _leanStiffnessScale * toppleStiffness;
            // Use the body's real roll inertia (collider-less rigs report ~1 kg*m^2) and keep both gains inside what an
            // explicit fixed-step integrator can resolve; otherwise the damping term over-corrects each step and the lean
            // oscillates and grows. Gravity is cancelled by the feed-forward below, so clamping k cannot topple the bike.
            float rollInertia = Mathf.Max(0.05f, _rb.inertiaTensor.z);
            float stepDt = Mathf.Max(dt, 1e-4f);
            float stiffnessLimit = 0.35f * rollInertia / (stepDt * stepDt);
            float gainScale = 1f;
            if (stiffness + toppleStiffness > stiffnessLimit)
            {
                // The feed-forward behaves as extra stiffness whenever gravity has no ground pivot to topple about.
                gainScale = stiffnessLimit / (stiffness + toppleStiffness);
                stiffness *= gainScale;
            }

            float damping = 2f * _leanDampingRatio * Mathf.Sqrt(stiffness * rollInertia);
            damping = Mathf.Min(damping, 0.9f * rollInertia / stepDt);

            float leanError = Mathf.DeltaAngle(currentLeanDeg, _leanTargetDeg) * Mathf.Deg2Rad;
            float rollRate = Vector3.Dot(_rb.angularVelocity, leanAxis);
            float gravityFeedForward = -gainScale * toppleStiffness * Mathf.Sin(currentLeanDeg * Mathf.Deg2Rad);
            float rollTorque = (leanError * stiffness) - (rollRate * damping) + gravityFeedForward;
            _rb.AddTorque(leanAxis * rollTorque, ForceMode.Force);

            float desiredYawRate = forwardSpeed < 0f ? -commandedYawRate : commandedYawRate;
            float yawRate = Vector3.Dot(_rb.angularVelocity, worldUp);
            float yawTorque = (desiredYawRate - yawRate) * _yawResponse * _rb.mass * 0.4f;
            _rb.AddTorque(worldUp * yawTorque, ForceMode.Force);
        }
    }
}
