#nullable enable

namespace MopedSim.Core
{
    /// <summary>
    /// Simulates fuel tank capacity, reserve threshold, petcock Off/On/Reserve, float bowl buffer, and rust jet clogging.
    /// </summary>
    public sealed class FuelSystem
    {
        public float TankCapacityLiters { get; set; } = 3.2f;
        public float FuelLevelLiters { get; set; } = 3.0f;
        public float ReserveThresholdLiters { get; set; } = 0.6f;
        public float FloatBowlCapacityLiters { get; set; } = 0.04f;
        public float FloatBowlLiters { get; set; } = 0.04f;
        public float PremixRatio { get; set; } = 0.025f; // 40:1 standard; <0.008 is oil starvation
        public PetcockMode PetcockMode { get; set; } = PetcockMode.On;
        public float RustContamination { get; set; }
        public bool HasInlineFilter { get; set; } = true;
        public bool IsMainJetClogged { get; set; }
        public bool IsFloatNeedleStuck { get; set; }

        public bool IsReserveActive { get; private set; }
        public bool IsSputtering { get; private set; }
        public bool HasFuelForCombustion { get; private set; } = true;

        /// <summary>
        /// Steps fuel flow from tank through petcock into the float bowl and checks rust jet fouling.
        /// </summary>
        public void Step(
            float dt,
            float rpm,
            float throttle,
            bool isRunning,
            bool isTankInstalled,
            bool isCarbInstalled,
            ISimRandom rng)
        {
            if (!isTankInstalled || !isCarbInstalled)
            {
                IsReserveActive = FuelLevelLiters <= ReserveThresholdLiters;
                IsSputtering = false;
                HasFuelForCombustion = false;
                return;
            }

            IsReserveActive = FuelLevelLiters <= ReserveThresholdLiters;

            if (dt > 0f && isRunning && rpm > 400f)
            {
                // ~0.00045 L/s at 7500 RPM WOT (~1.6 L/hr realistic moped fuel burn)
                float burnRate = 0.00010f + 0.00040f * SimMath.Clamp(rpm / 7500f, 0.1f, 1.5f) * (0.25f + 0.75f * throttle);
                if (IsFloatNeedleStuck)
                {
                    burnRate += 0.00020f; // dripping overflow
                }
                float burned = burnRate * dt;
                FloatBowlLiters = SimMath.Max(0f, FloatBowlLiters - burned);
            }

            // Tank -> float bowl feed depending on PetcockMode
            bool tankCanFeed = false;
            if (PetcockMode == PetcockMode.On)
            {
                tankCanFeed = FuelLevelLiters > ReserveThresholdLiters;
            }
            else if (PetcockMode == PetcockMode.Reserve)
            {
                tankCanFeed = FuelLevelLiters > 0f;
            }

            if (tankCanFeed)
            {
                float needed = SimMath.Max(0f, FloatBowlCapacityLiters - FloatBowlLiters);
                float available = PetcockMode == PetcockMode.On
                    ? SimMath.Max(0f, FuelLevelLiters - ReserveThresholdLiters)
                    : FuelLevelLiters;

                float transfer = SimMath.Min(needed, available);
                if (transfer > 0f)
                {
                    FuelLevelLiters -= transfer;
                    FloatBowlLiters += transfer;
                }
            }

            // Stochastic rust flake clog when running rusty tank without inline filter
            if (dt > 0f && isRunning && !HasInlineFilter && !IsMainJetClogged && RustContamination > 0.20f)
            {
                double clogProb = RustContamination * 0.005 * dt;
                if (rng.NextDouble() < clogProb)
                {
                    IsMainJetClogged = true;
                }
            }

            IsSputtering = FloatBowlLiters > 0.0002f && FloatBowlLiters <= FloatBowlCapacityLiters * 0.30f;
            HasFuelForCombustion = FloatBowlLiters > 0.0002f;
        }

        /// <summary>
        /// Refuels tank with specified liters and premix oil ratio.
        /// </summary>
        public void Refuel(float liters, float premixRatio, float rustContamination = -1f)
        {
            if (liters > 0f)
            {
                FuelLevelLiters = SimMath.Clamp(FuelLevelLiters + liters, 0f, TankCapacityLiters);
            }
            PremixRatio = SimMath.Max(0f, premixRatio);
            if (rustContamination >= 0f)
            {
                RustContamination = SimMath.Clamp01(rustContamination);
            }
            if (FuelLevelLiters > 0f && PetcockMode != PetcockMode.Off)
            {
                bool canFill = PetcockMode == PetcockMode.Reserve || FuelLevelLiters > ReserveThresholdLiters;
                if (canFill)
                {
                    FloatBowlLiters = FloatBowlCapacityLiters;
                }
            }
        }

        /// <summary>
        /// Clears main jet clog during roadside blow-out or carb workbench clean.
        /// </summary>
        public void ClearMainJetClog()
        {
            IsMainJetClogged = false;
            IsFloatNeedleStuck = false;
        }
    }
}
