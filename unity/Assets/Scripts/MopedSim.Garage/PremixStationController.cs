using System;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Mixed jerrycan batch: fuel liters plus the fuel:oil ratio label (50 = 50:1, 0 = straight gas).
    /// </summary>
    public readonly struct PremixBatch
    {
        public readonly float FuelLiters;
        public readonly float FuelPartsPerOil;
        public readonly float OilFraction;

        public PremixBatch(float fuelLiters, float fuelPartsPerOil, float oilFraction)
        {
            FuelLiters = fuelLiters;
            FuelPartsPerOil = fuelPartsPerOil;
            OilFraction = oilFraction;
        }

        public bool IsStraightGas
        {
            get { return OilFraction <= 0f; }
        }
    }

    /// <summary>
    /// Premix jerrycan station (PROJECT_GUIDE.md section 2.5): mixes 32:1 / 40:1 / 50:1 or accidental straight-gas batches
    /// and refuels the tank through BikeSimulation.Refuel. Ratio-to-oil conversion uses Core's ThermalModel.PremixFromRatio;
    /// lubrication and seizure consequences are computed by MopedSim.Core.
    /// </summary>
    public sealed class PremixStationController : MonoBehaviour
    {
        public static readonly float[] StandardRatios = new float[] { 32f, 40f, 50f, 0f };

        [SerializeField] private float _jerrycanCapacityLiters = 5f;

        private GarageBikeContext _context;
        private PremixBatch _batch;
        private bool _hasBatch;

        public event Action<PremixBatch> BatchMixed;
        public event Action<float> TankRefueled;

        public bool HasBatch
        {
            get { return _hasBatch; }
        }

        public PremixBatch CurrentBatch
        {
            get { return _batch; }
        }

        public void Bind(GarageBikeContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Mixes a jerrycan batch. fuelPartsPerOil = 0 represents forgetting the oil entirely (0:1 straight gas).
        /// </summary>
        public PremixBatch MixBatch(float fuelLiters, float fuelPartsPerOil)
        {
            float liters = Mathf.Clamp(fuelLiters, 0f, _jerrycanCapacityLiters);
            float ratio = Mathf.Max(0f, fuelPartsPerOil);
            _batch = new PremixBatch(liters, ratio, ThermalModel.PremixFromRatio(ratio));
            _hasBatch = liters > 0f;
            Action<PremixBatch> handler = BatchMixed;
            if (handler != null)
            {
                handler.Invoke(_batch);
            }

            return _batch;
        }

        /// <summary>
        /// Pours up to the requested liters from the current batch into the bike tank. Returns liters poured.
        /// </summary>
        public float RefuelTank(float liters)
        {
            if (_context == null || !_hasBatch || liters <= 0f)
            {
                return 0f;
            }

            float poured = Mathf.Min(liters, _batch.FuelLiters);
            _context.Bike.Refuel(poured, _batch.OilFraction);
            float remaining = _batch.FuelLiters - poured;
            _batch = new PremixBatch(remaining, _batch.FuelPartsPerOil, _batch.OilFraction);
            _hasBatch = remaining > 0f;

            Action<float> handler = TankRefueled;
            if (handler != null)
            {
                handler.Invoke(poured);
            }

            _context.NotifyAssemblyChanged();
            return poured;
        }

        public void SetPetcock(PetcockMode mode)
        {
            if (_context == null)
            {
                return;
            }

            _context.Bike.SetPetcockMode(mode);
            _context.NotifyAssemblyChanged();
        }
    }
}
