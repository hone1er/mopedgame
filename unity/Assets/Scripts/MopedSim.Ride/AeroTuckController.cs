using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Blends rider posture into aero tuck and reports the rigid-body drag area (CdA) for the ride controller
    /// (PROJECT_GUIDE.md section 2.4). The tuck cut is Core's BikeSimulation.AeroTuckCdAMultiplier (0.70 = 30% less CdA)
    /// divided by the mounted handlebar's TuckDragBonusMultiplier, exactly matching Core's headless longitudinal model.
    /// Upright CdA comes from BikeSimulation.Archetype.DragAreaCdA x handlebar drag via MopedBikeController.Bind.
    /// </summary>
    public sealed class AeroTuckController : MonoBehaviour
    {
        [SerializeField] private float _baseCda = 0.56f;
        [SerializeField] private float _tuckTransitionSpeed = 6.0f;
        [SerializeField] private Transform _riderVisualPivot;
        [SerializeField] private float _uprightPitchDeg = 0f;
        [SerializeField] private float _tuckedPitchDeg = 32f;

        private float _handlebarTuckBonus = 1.0f;
        private float _tuckBlend;

        /// <summary>
        /// Fractional CdA reduction at full tuck behind stock bars (1 - BikeSimulation.AeroTuckCdAMultiplier = 0.30).
        /// </summary>
        public static float TuckReductionFraction
        {
            get { return 1f - BikeSimulation.AeroTuckCdAMultiplier; }
        }

        public float TuckBlend
        {
            get { return _tuckBlend; }
        }

        public float BaseCdA
        {
            get { return _baseCda; }
        }

        /// <summary>
        /// Mounted handlebar TuckDragBonusMultiplier (clip-ons &gt; 1 tuck better, tall BMX risers &lt; 1 tuck worse).
        /// </summary>
        public float HandlebarTuckEfficiency
        {
            get { return _handlebarTuckBonus; }
            set { _handlebarTuckBonus = Mathf.Max(0.5f, value); }
        }

        /// <summary>
        /// CdA multiplier at full tuck, identical to Core: AeroTuckCdAMultiplier / max(0.5, bar tuck bonus).
        /// </summary>
        public float FullTuckCdAMultiplier
        {
            get { return BikeSimulation.AeroTuckCdAMultiplier / Mathf.Max(0.5f, _handlebarTuckBonus); }
        }

        public float CurrentCda
        {
            get { return _baseCda * Mathf.Lerp(1f, FullTuckCdAMultiplier, _tuckBlend); }
        }

        /// <summary>
        /// Sets the upright drag area (m^2) read from BikeSimulation.Archetype.DragAreaCdA x handlebar drag multiplier.
        /// </summary>
        public void SetBaseCdA(float cda)
        {
            _baseCda = Mathf.Max(0.05f, cda);
        }

        /// <summary>
        /// Advances tuck blend deterministically inside FixedUpdate with zero GC allocation and returns the current CdA.
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
