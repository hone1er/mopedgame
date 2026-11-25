using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Computes aerodynamic drag area (CdA) scaling and rider posture interpolation
    /// when holding aero tuck (PROJECT_GUIDE.md section 2.4).
    /// </summary>
    public sealed class AeroTuckController : MonoBehaviour
    {
        [SerializeField] private float _baseCda = 0.68f;
        [SerializeField] private float _tuckReductionFraction = 0.25f;
        [SerializeField] private float _tuckTransitionSpeed = 6.0f;
        [SerializeField] private float _handlebarTuckEfficiency = 1.0f;
        [SerializeField] private Transform _riderVisualPivot;
        [SerializeField] private float _uprightPitchDeg = 0f;
        [SerializeField] private float _tuckedPitchDeg = 32f;

        private float _tuckBlend;

        public float TuckBlend
        {
            get { return _tuckBlend; }
        }

        public float HandlebarTuckEfficiency
        {
            get { return _handlebarTuckEfficiency; }
            set { _handlebarTuckEfficiency = Mathf.Clamp(value, 0.25f, 1.15f); }
        }

        public float CurrentCda
        {
            get
            {
                float effectiveReduction = _tuckReductionFraction * _handlebarTuckEfficiency;
                return _baseCda * (1f - (_tuckBlend * Mathf.Clamp(effectiveReduction, 0f, 0.40f)));
            }
        }

        /// <summary>
        /// Advances tuck blend deterministically inside FixedUpdate with zero GC allocation.
        /// </summary>
        public float Step(bool tuckInput, float dt)
        {
            float target = tuckInput ? 1f : 0f;
            _tuckBlend = Mathf.MoveTowards(_tuckBlend, target, _tuckTransitionSpeed * dt);

            if (_riderVisualPivot != null)
            {
                float pitch = Mathf.Lerp(_uprightPitchDeg, _tuckedPitchDeg, _tuckBlend);
                _riderVisualPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            return CurrentCda;
        }
    }
}
