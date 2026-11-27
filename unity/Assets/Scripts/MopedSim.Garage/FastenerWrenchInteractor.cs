using System;
using MopedSim.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MopedSim.Garage
{
    /// <summary>
    /// Hold-to-torque wrench bridge. Holding the wrench ramps a pending torque value; releasing commits it to
    /// BikeSimulation.TryTorqueFastener. Loosening, penetrating oil, and thread repair dispatch the matching Core commands.
    /// Strip thresholds, tolerances, seized/stripped rules, and leak effects are owned by MopedSim.Core; this component
    /// only exposes Core's FastenerState for HUD display. Update is allocation-free.
    /// </summary>
    public sealed class FastenerWrenchInteractor : MonoBehaviour
    {
        [SerializeField] private float _torqueRampNmPerSecond = 6f;
        [SerializeField] private float _maxWrenchTorqueNm = 60f;
        [SerializeField] private float _minHoldSecondsToCommit = 0.2f;
        [SerializeField] private bool _readMouseInput;

        private GarageBikeContext _context;
        private string _activeFastenerId = string.Empty;
        private float _pendingTorqueNm;
        private bool _wrenchHeld;
        private float _heldSeconds;
        private int _selectedFrame = -1;

        /// <summary>
        /// Raised with each Core wrench result (torque, loosen, oil, thread repair).
        /// </summary>
        public event Action<WrenchActionResult> WrenchResult;

        /// <summary>
        /// Raised when the active fastener changes so HUDs can bind to its FastenerState.
        /// </summary>
        public event Action<FastenerState> ActiveFastenerChanged;

        public string ActiveFastenerId
        {
            get { return _activeFastenerId; }
        }

        public float PendingTorqueNm
        {
            get { return _pendingTorqueNm; }
        }

        public bool IsWrenchHeld
        {
            get { return _wrenchHeld; }
        }

        /// <summary>Optional hook (set by the App) that returns true while the pointer is over UI, so UI clicks never start a torque pull.</summary>
        public Func<bool> PointerBlocker { get; set; }

        public bool ReadMouseInput
        {
            get { return _readMouseInput; }
            set { _readMouseInput = value; }
        }

        public float TorqueRampNmPerSecond
        {
            get { return _torqueRampNmPerSecond; }
            set { _torqueRampNmPerSecond = Mathf.Max(0.1f, value); }
        }

        /// <summary>
        /// Core fastener state for the active target (null when nothing is selected or the archetype lacks it).
        /// </summary>
        public FastenerState ActiveFastener
        {
            get
            {
                if (_context == null || _activeFastenerId.Length == 0)
                {
                    return null;
                }

                return _context.Bike.Assembly.GetFastener(_activeFastenerId);
            }
        }

        public void Bind(GarageBikeContext context)
        {
            _context = context;
            ClearTarget();
        }

        public bool SelectFastener(string fastenerId)
        {
            if (_context == null || string.IsNullOrEmpty(fastenerId))
            {
                return false;
            }

            FastenerState state = _context.Bike.Assembly.GetFastener(fastenerId);
            if (state == null)
            {
                return false;
            }

            _activeFastenerId = fastenerId;
            _pendingTorqueNm = 0f;
            _wrenchHeld = false;
            _heldSeconds = 0f;
            _selectedFrame = Time.frameCount;
            Action<FastenerState> handler = ActiveFastenerChanged;
            if (handler != null)
            {
                handler.Invoke(state);
            }

            return true;
        }

        public void ClearTarget()
        {
            _activeFastenerId = string.Empty;
            _pendingTorqueNm = 0f;
            _wrenchHeld = false;
        }

        public void BeginTorque()
        {
            if (_activeFastenerId.Length == 0)
            {
                return;
            }

            _wrenchHeld = true;
            _heldSeconds = 0f;
            _pendingTorqueNm = 0f;
        }

        /// <summary>
        /// Ramps pending torque while held. Zero allocations; safe in Update.
        /// </summary>
        public void TickTorque(float dt)
        {
            if (!_wrenchHeld || dt <= 0f)
            {
                return;
            }

            _heldSeconds += dt;
            _pendingTorqueNm = Mathf.Min(_maxWrenchTorqueNm, _pendingTorqueNm + _torqueRampNmPerSecond * dt);
        }

        /// <summary>
        /// Releases the wrench and commits the pending torque to Core.
        /// </summary>
        public WrenchActionResult ReleaseAndCommit()
        {
            _wrenchHeld = false;
            return ApplyTorque(_pendingTorqueNm);
        }

        public void CancelTorque()
        {
            _wrenchHeld = false;
            _pendingTorqueNm = 0f;
        }

        /// <summary>
        /// Commits an explicit torque value (torque-wrench click setting) to the active fastener.
        /// </summary>
        public WrenchActionResult ApplyTorque(float torqueNm)
        {
            if (_context == null || _activeFastenerId.Length == 0)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, _activeFastenerId, "No fastener selected.");
            }

            _pendingTorqueNm = torqueNm;
            return Publish(_context.Bike.TryTorqueFastener(_activeFastenerId, torqueNm));
        }

        public WrenchActionResult Loosen()
        {
            if (_context == null || _activeFastenerId.Length == 0)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, _activeFastenerId, "No fastener selected.");
            }

            _pendingTorqueNm = 0f;
            return Publish(_context.Bike.TryRemoveFastener(_activeFastenerId));
        }

        public WrenchActionResult ApplyPenetratingOil()
        {
            if (_context == null || _activeFastenerId.Length == 0)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, _activeFastenerId, "No fastener selected.");
            }

            return Publish(_context.Bike.ApplyPenetratingOil(_activeFastenerId));
        }

        public WrenchActionResult RepairStrippedThreads()
        {
            if (_context == null || _activeFastenerId.Length == 0)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, _activeFastenerId, "No fastener selected.");
            }

            _pendingTorqueNm = 0f;
            return Publish(_context.Bike.RepairStrippedFastener(_activeFastenerId));
        }

        private WrenchActionResult Publish(WrenchActionResult result)
        {
            Action<WrenchActionResult> handler = WrenchResult;
            if (handler != null)
            {
                handler.Invoke(result);
            }

            return _context.Publish(result);
        }

        private void Update()
        {
            if (_readMouseInput)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null && _activeFastenerId.Length > 0)
                {
                    // The press that selected the fastener on the work stand never starts a pull.
                    if (mouse.leftButton.wasPressedThisFrame && Time.frameCount != _selectedFrame && (PointerBlocker == null || !PointerBlocker()))
                    {
                        BeginTorque();
                    }
                    else if (mouse.leftButton.wasReleasedThisFrame && _wrenchHeld)
                    {
                        // A quick tap is not a wrench pull: cancel instead of committing a near-zero torque.
                        if (_heldSeconds >= _minHoldSecondsToCommit)
                        {
                            ReleaseAndCommit();
                        }
                        else
                        {
                            CancelTorque();
                        }
                    }
                }
            }

            TickTorque(Time.deltaTime);
        }
    }
}
