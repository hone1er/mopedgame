using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Tracks independent front/rear cable drum brake temperatures, braking fade coefficient,
    /// and optional visual heat tinting on brake hub renderers.
    /// </summary>
    public sealed class BrakeFadeController : MonoBehaviour
    {
        [Header("Brake Capacity")]
        [SerializeField] private float _maxFrontBrakeTorqueNm = 260f;
        [SerializeField] private float _maxRearBrakeTorqueNm = 210f;
        [SerializeField] private float _ambientTempCelsius = 22f;
        [SerializeField] private float _fadeOnsetTempCelsius = 175f;
        [SerializeField] private float _fadeSevereTempCelsius = 310f;
        [SerializeField] private float _heatGainPerNmSec = 0.012f;
        [SerializeField] private float _coolingPerSecBase = 0.045f;
        [SerializeField] private float _airflowCoolingFactor = 0.010f;

        [Header("Optional Visualizers")]
        [SerializeField] private Renderer _frontDrumRenderer;
        [SerializeField] private Renderer _rearDrumRenderer;
        [SerializeField] private Color _coolColor = new Color(0.28f, 0.28f, 0.30f, 1f);
        [SerializeField] private Color _hotFadeColor = new Color(0.85f, 0.28f, 0.12f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _frontPropertyBlock;
        private MaterialPropertyBlock _rearPropertyBlock;
        private float _frontTempC = 22f;
        private float _rearTempC = 22f;

        public float FrontBrakeTempC { get { return _frontTempC; } }
        public float RearBrakeTempC { get { return _rearTempC; } }
        public float FrontFadeCoefficient { get { return ComputeEfficiency(_frontTempC); } }
        public float RearFadeCoefficient { get { return ComputeEfficiency(_rearTempC); } }

        private void Awake()
        {
            _frontPropertyBlock = new MaterialPropertyBlock();
            _rearPropertyBlock = new MaterialPropertyBlock();
            _frontTempC = _ambientTempCelsius;
            _rearTempC = _ambientTempCelsius;
        }

        public void SetMaxBrakeTorques(float frontMaxNm, float rearMaxNm)
        {
            _maxFrontBrakeTorqueNm = Mathf.Max(50f, frontMaxNm);
            _maxRearBrakeTorqueNm = Mathf.Max(50f, rearMaxNm);
        }

        /// <summary>
        /// Steps brake heating/cooling and returns effective front & rear braking torques (Nm).
        /// Zero GC allocations per call.
        /// </summary>
        public void StepBrakes(
            float frontInput,
            float rearInput,
            float wheelSpeedMps,
            float dt,
            out float effectiveFrontTorqueNm,
            out float effectiveRearTorqueNm)
        {
            float clampedFront = Mathf.Clamp01(frontInput);
            float clampedRear = Mathf.Clamp01(rearInput);
            float absSpeed = Mathf.Abs(wheelSpeedMps);

            float frontEff = ComputeEfficiency(_frontTempC);
            float rearEff = ComputeEfficiency(_rearTempC);

            effectiveFrontTorqueNm = clampedFront * _maxFrontBrakeTorqueNm * frontEff;
            effectiveRearTorqueNm = clampedRear * _maxRearBrakeTorqueNm * rearEff;

            float coolingRate = _coolingPerSecBase + (absSpeed * _airflowCoolingFactor);
            _frontTempC = UpdateDrumTemperature(_frontTempC, effectiveFrontTorqueNm, absSpeed, coolingRate, dt);
            _rearTempC = UpdateDrumTemperature(_rearTempC, effectiveRearTorqueNm, absSpeed, coolingRate, dt);

            UpdateDrumVisual(_frontDrumRenderer, _frontPropertyBlock, _frontTempC);
            UpdateDrumVisual(_rearDrumRenderer, _rearPropertyBlock, _rearTempC);
        }

        private float UpdateDrumTemperature(float currentTemp, float torqueNm, float absSpeed, float coolingRate, float dt)
        {
            float heatInput = torqueNm * Mathf.Min(absSpeed, 25f) * _heatGainPerNmSec;
            float coolDelta = (currentTemp - _ambientTempCelsius) * coolingRate;
            float next = currentTemp + (heatInput - coolDelta) * dt;
            return Mathf.Clamp(next, _ambientTempCelsius, 420f);
        }

        private float ComputeEfficiency(float tempC)
        {
            if (tempC <= _fadeOnsetTempCelsius)
            {
                return 1f;
            }

            float t = Mathf.Clamp01((tempC - _fadeOnsetTempCelsius) / Mathf.Max(1f, _fadeSevereTempCelsius - _fadeOnsetTempCelsius));
            return Mathf.Lerp(1f, 0.35f, t);
        }

        private void UpdateDrumVisual(Renderer targetRenderer, MaterialPropertyBlock block, float tempC)
        {
            if (targetRenderer == null || block == null)
            {
                return;
            }

            float heatNorm = Mathf.Clamp01((tempC - _fadeOnsetTempCelsius) / Mathf.Max(1f, _fadeSevereTempCelsius - _fadeOnsetTempCelsius));
            Color tint = Color.Lerp(_coolColor, _hotFadeColor, heatNorm);
            Color emission = _hotFadeColor * (heatNorm * 0.65f);

            targetRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(EmissionColorId, emission);
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
