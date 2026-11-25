using UnityEngine;
using UnityEngine.InputSystem;

namespace MopedSim.Ride
{
    /// <summary>
    /// Pure input handler for pedal-start / pedal-assist, decompressor, and choke controls.
    /// Contains NO stamina or torque math: rider stamina and pedal torque are owned by MopedSim.Core
    /// (BikeSimulation reads RideInputState.PedalAssist / Decompressor / Choke and reports RiderStamina).
    /// This component only resolves raw device state and drives a cosmetic pedal-crank animation angle.
    /// </summary>
    public sealed class PedalInputHandler : MonoBehaviour
    {
        [SerializeField] private bool _useDirectKeyboardFallback = true;
        [SerializeField] private float _pedalCrankDegreesPerSecond = 540f;
        [SerializeField] private Transform _crankVisual;

        private bool _pedalHeldExternal;
        private bool _decompressorHeldExternal;
        private bool _chokeHeldExternal;
        private bool _hasExternalControl;
        private float _crankAngleDeg;

        public bool PedalAssistHeld { get; private set; }
        public bool DecompressorHeld { get; private set; }
        public bool ChokeHeld { get; private set; }

        public void SetExternalInputs(bool pedalAssist, bool decompressor, bool choke)
        {
            _hasExternalControl = true;
            _pedalHeldExternal = pedalAssist;
            _decompressorHeldExternal = decompressor;
            _chokeHeldExternal = choke;
        }

        public void ClearExternalOverride()
        {
            _hasExternalControl = false;
        }

        /// <summary>
        /// Resolves current pedal/decompressor/choke state. Zero allocations.
        /// </summary>
        public void Poll(float dt)
        {
            if (_hasExternalControl)
            {
                PedalAssistHeld = _pedalHeldExternal;
                DecompressorHeld = _decompressorHeldExternal;
                ChokeHeld = _chokeHeldExternal;
            }
            else
            {
                Keyboard kb = _useDirectKeyboardFallback ? Keyboard.current : null;
                if (kb != null)
                {
                    PedalAssistHeld = kb.spaceKey.isPressed;
                    DecompressorHeld = kb.qKey.isPressed;
                    ChokeHeld = kb.cKey.isPressed;
                }
                else
                {
                    PedalAssistHeld = false;
                    DecompressorHeld = false;
                    ChokeHeld = false;
                }
            }

            if (PedalAssistHeld)
            {
                _crankAngleDeg += _pedalCrankDegreesPerSecond * dt;
                if (_crankAngleDeg > 360f)
                {
                    _crankAngleDeg -= 360f;
                }
            }

            if (_crankVisual != null)
            {
                _crankVisual.localRotation = Quaternion.Euler(_crankAngleDeg, 0f, 0f);
            }
        }
    }
}
