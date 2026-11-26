#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Hidden defects a used swap-meet part may carry (PROJECT_GUIDE.md §2.5 Parts Economy).
    /// </summary>
    public enum SwapMeetDefect
    {
        None = 0,
        RustyTank = 1,
        WeakCondenser = 2,
        StrippedThread = 3,
        WornRings = 4,
        WornBelt = 5
    }

    /// <summary>
    /// Tunables for the rotating Parts Alley swap meet (injectable).
    /// </summary>
    public sealed class SwapMeetConfig
    {
        public int OffersPerRotation { get; set; } = 6;

        /// <summary>Asking price discount off catalog list price, drawn uniformly in [Min, Max).</summary>
        public float MinDiscount { get; set; } = 0.35f;
        public float MaxDiscount { get; set; } = 0.65f;

        /// <summary>Chance that an offer whose category can carry a defect actually hides one.</summary>
        public float DefectChance { get; set; } = 0.45f;

        /// <summary>Visible (cosmetic) condition range of used parts.</summary>
        public float MinCondition { get; set; } = 0.6f;
        public float MaxCondition { get; set; } = 0.92f;
    }

    /// <summary>
    /// One used part for sale. <see cref="RevealedDefect"/> stays <see cref="SwapMeetDefect.None"/> until inspected.
    /// </summary>
    public sealed class SwapMeetOffer
    {
        internal SwapMeetDefect HiddenDefect { get; }
        internal float DefectSeverity { get; }

        public string OfferId { get; }
        public PartDefinition Part { get; }
        public decimal ListPriceUsd { get; }
        public decimal AskingPriceUsd { get; }

        /// <summary>Visible condition 0..1 as advertised by the seller.</summary>
        public float Condition { get; }

        public bool IsInspected { get; internal set; }
        public bool IsSold { get; internal set; }

        public string PartId
        {
            get { return Part.Id; }
        }

        public SwapMeetDefect RevealedDefect
        {
            get { return IsInspected ? HiddenDefect : SwapMeetDefect.None; }
        }

        internal SwapMeetOffer(string offerId, PartDefinition part, decimal askingPriceUsd, float condition, SwapMeetDefect hiddenDefect, float defectSeverity)
        {
            OfferId = offerId;
            Part = part;
            ListPriceUsd = part.PriceUsd;
            AskingPriceUsd = askingPriceUsd;
            Condition = condition;
            HiddenDefect = hiddenDefect;
            DefectSeverity = defectSeverity;
        }
    }

    /// <summary>
    /// Result of <see cref="SwapMeetMarket.InspectOffer"/>.
    /// </summary>
    public readonly struct SwapMeetInspection
    {
        private readonly string? _offerId;
        private readonly string? _description;

        public readonly bool Succeeded;
        public readonly SwapMeetDefect Defect;

        public string OfferId
        {
            get { return _offerId ?? string.Empty; }
        }

        public string Description
        {
            get { return _description ?? string.Empty; }
        }

        public SwapMeetInspection(bool succeeded, string offerId, SwapMeetDefect defect, string description)
        {
            Succeeded = succeeded;
            _offerId = offerId ?? string.Empty;
            Defect = defect;
            _description = description ?? string.Empty;
        }
    }

    /// <summary>
    /// Result of <see cref="SwapMeetMarket.TryBuyOffer"/>. <see cref="Defect"/> is the true defect (now known to the buyer either way).
    /// </summary>
    public readonly struct SwapMeetPurchaseResult
    {
        private readonly string? _offerId;
        private readonly string? _message;

        public readonly bool Succeeded;
        public readonly PartInstance? Part;
        public readonly SwapMeetDefect Defect;
        public readonly bool DefectWasRevealedBeforePurchase;
        public readonly decimal PriceUsd;
        public readonly decimal RemainingCashUsd;

        public string OfferId
        {
            get { return _offerId ?? string.Empty; }
        }

        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        public SwapMeetPurchaseResult(
            bool succeeded,
            string offerId,
            PartInstance? part,
            SwapMeetDefect defect,
            bool defectWasRevealedBeforePurchase,
            decimal priceUsd,
            decimal remainingCashUsd,
            string message)
        {
            Succeeded = succeeded;
            _offerId = offerId ?? string.Empty;
            Part = part;
            Defect = defect;
            DefectWasRevealedBeforePurchase = defectWasRevealedBeforePurchase;
            PriceUsd = priceUsd;
            RemainingCashUsd = remainingCashUsd;
            _message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Deterministic, seeded, rotating used-parts market (Parts Alley swap meet). Each rotation index re-rolls the stock from
    /// <c>seed</c> combined with the index, so the same seed + rotation always shows the same offers. Offers are discounted and
    /// may hide a defect that <see cref="InspectOffer"/> reveals; <see cref="TryBuyOffer"/> spends world cash and puts the
    /// (defect-applied) <see cref="PartInstance"/> into <see cref="SimulationWorld.Inventory"/>.
    /// </summary>
    public sealed class SwapMeetMarket
    {
        private readonly List<PartDefinition> _eligible = new List<PartDefinition>();
        private readonly List<SwapMeetOffer> _offers = new List<SwapMeetOffer>();
        private readonly List<SwapMeetDefect> _defectScratch = new List<SwapMeetDefect>(4);

        public uint Seed { get; }
        public SwapMeetConfig Config { get; }
        public int RotationIndex { get; private set; }

        public IReadOnlyList<SwapMeetOffer> Offers
        {
            get { return _offers; }
        }

        /// <summary>
        /// Builds the market over <paramref name="catalogParts"/> in the given (catalog file) order and rolls rotation 0.
        /// Tools, spares, garage stations, consumables, premix oil, gaskets, and free parts are never offered.
        /// </summary>
        public SwapMeetMarket(IEnumerable<PartDefinition> catalogParts, uint seed, SwapMeetConfig? config = null)
        {
            if (catalogParts == null)
            {
                throw new ArgumentNullException(nameof(catalogParts));
            }
            Seed = seed;
            Config = config ?? new SwapMeetConfig();
            foreach (PartDefinition part in catalogParts)
            {
                if (part != null && IsSellable(part))
                {
                    _eligible.Add(part);
                }
            }
            Rotate(0);
        }

        /// <summary>
        /// Replaces the stock with the deterministic offers of <paramref name="rotationIndex"/> (e.g. the in-game weekend number).
        /// </summary>
        public void Rotate(int rotationIndex)
        {
            RotationIndex = rotationIndex;
            _offers.Clear();
            if (_eligible.Count == 0)
            {
                return;
            }

            var rng = new SeededSimRandom(StableHash.Combine(Seed, unchecked((uint)rotationIndex)));
            int[] order = new int[_eligible.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                int tmp = order[i];
                order[i] = order[j];
                order[j] = tmp;
            }

            int count = Math.Min(Math.Max(0, Config.OffersPerRotation), order.Length);
            float minDiscount = SimMath.Clamp01(Config.MinDiscount);
            float maxDiscount = SimMath.Max(minDiscount, SimMath.Clamp01(Config.MaxDiscount));
            float minCond = SimMath.Clamp01(Config.MinCondition);
            float maxCond = SimMath.Max(minCond, SimMath.Clamp01(Config.MaxCondition));
            for (int k = 0; k < count; k++)
            {
                PartDefinition part = _eligible[order[k]];

                // Fixed draw count per offer keeps the stream stable regardless of defect eligibility.
                double discountRoll = rng.NextDouble();
                double conditionRoll = rng.NextDouble();
                double defectRoll = rng.NextDouble();
                double defectPickRoll = rng.NextDouble();
                double severityRoll = rng.NextDouble();

                float discount = minDiscount + (maxDiscount - minDiscount) * (float)discountRoll;
                decimal asking = Math.Round(part.PriceUsd * (1m - (decimal)discount), 2, MidpointRounding.AwayFromZero);
                if (asking < 1m)
                {
                    asking = 1m;
                }
                float condition = minCond + (maxCond - minCond) * (float)conditionRoll;

                SwapMeetDefect defect = SwapMeetDefect.None;
                CollectPossibleDefects(part, _defectScratch);
                if (_defectScratch.Count > 0 && defectRoll < SimMath.Clamp01(Config.DefectChance))
                {
                    int pick = Math.Min(_defectScratch.Count - 1, (int)(defectPickRoll * _defectScratch.Count));
                    defect = _defectScratch[pick];
                }

                string offerId = "swap_r" + rotationIndex.ToString("000") + "_" + (k + 1).ToString("00") + "_" + part.Id;
                _offers.Add(new SwapMeetOffer(offerId, part, asking, condition, defect, 0.5f + 0.5f * (float)severityRoll));
            }
        }

        public void AdvanceRotation()
        {
            Rotate(RotationIndex + 1);
        }

        public SwapMeetOffer? GetOffer(string offerId)
        {
            for (int i = 0; i < _offers.Count; i++)
            {
                if (string.Equals(_offers[i].OfferId, offerId, StringComparison.Ordinal))
                {
                    return _offers[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Reveals the offer's hidden defect (or confirms it is clean).
        /// </summary>
        public SwapMeetInspection InspectOffer(string offerId)
        {
            SwapMeetOffer? offer = GetOffer(offerId);
            if (offer == null || offer.IsSold)
            {
                return new SwapMeetInspection(false, offerId, SwapMeetDefect.None, "No such offer on the table.");
            }
            offer.IsInspected = true;
            return new SwapMeetInspection(true, offer.OfferId, offer.HiddenDefect, DescribeDefect(offer.HiddenDefect, offer.Part));
        }

        /// <summary>
        /// Buys an offer with world cash. On success the part (with its defect applied) is added to <see cref="SimulationWorld.Inventory"/>.
        /// </summary>
        public SwapMeetPurchaseResult TryBuyOffer(string offerId, SimulationWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            SwapMeetOffer? offer = GetOffer(offerId);
            if (offer == null || offer.IsSold)
            {
                return new SwapMeetPurchaseResult(false, offerId, null, SwapMeetDefect.None, false, 0m, world.CashUsd, "No such offer on the table.");
            }
            if (world.CashUsd < offer.AskingPriceUsd)
            {
                return new SwapMeetPurchaseResult(false, offerId, null, SwapMeetDefect.None, offer.IsInspected, offer.AskingPriceUsd, world.CashUsd,
                    "Insufficient cash for used " + offer.Part.DisplayName + " ($" + offer.AskingPriceUsd.ToString("0.00") + ").");
            }

            world.CashUsd -= offer.AskingPriceUsd;
            offer.IsSold = true;

            var instance = new PartInstance(offer.Part, world.Inventory.NextInstanceId("used_" + offer.Part.Id))
            {
                Condition = offer.Condition,
                Wear = SimMath.Clamp01(1f - offer.Condition),
            };
            ApplyDefect(instance, offer.HiddenDefect, offer.DefectSeverity);
            world.Inventory.Add(instance);

            bool known = offer.IsInspected;
            string msg = "Bought used " + offer.Part.DisplayName + " for $" + offer.AskingPriceUsd.ToString("0.00") + "."
                + (offer.HiddenDefect != SwapMeetDefect.None && !known ? " Uh oh: " + DescribeDefect(offer.HiddenDefect, offer.Part) : string.Empty);
            return new SwapMeetPurchaseResult(true, offer.OfferId, instance, offer.HiddenDefect, known, offer.AskingPriceUsd, world.CashUsd, msg);
        }

        /// <summary>
        /// Category-driven defect eligibility: fuel tanks rust, points ignitions have condensers, pistons/cylinders wear rings,
        /// belts wear, and bolted engine/exhaust parts can have stripped threads.
        /// </summary>
        public static IReadOnlyList<SwapMeetDefect> GetPossibleDefects(PartDefinition part)
        {
            var list = new List<SwapMeetDefect>(2);
            CollectPossibleDefects(part, list);
            return list;
        }

        private static void CollectPossibleDefects(PartDefinition part, List<SwapMeetDefect> into)
        {
            into.Clear();
            if (part == null)
            {
                return;
            }
            switch (part.Category)
            {
                case PartCategory.FuelTank:
                    into.Add(SwapMeetDefect.RustyTank);
                    break;
                case PartCategory.Ignition:
                    if (part.Stats.IgnitionType == IgnitionType.Points6V)
                    {
                        into.Add(SwapMeetDefect.WeakCondenser);
                    }
                    break;
                case PartCategory.Piston:
                    into.Add(SwapMeetDefect.WornRings);
                    break;
                case PartCategory.Cylinder:
                    into.Add(SwapMeetDefect.WornRings);
                    into.Add(SwapMeetDefect.StrippedThread);
                    break;
                case PartCategory.DriveBelt:
                    into.Add(SwapMeetDefect.WornBelt);
                    break;
                case PartCategory.CylinderHead:
                case PartCategory.Exhaust:
                case PartCategory.IntakeManifold:
                case PartCategory.EngineCase:
                case PartCategory.Carburetor:
                    into.Add(SwapMeetDefect.StrippedThread);
                    break;
            }
        }

        private static void ApplyDefect(PartInstance instance, SwapMeetDefect defect, float severity)
        {
            float s = SimMath.Clamp01(severity);
            switch (defect)
            {
                case SwapMeetDefect.RustyTank:
                    instance.RustContamination = SimMath.Clamp01(0.45f + 0.45f * s);
                    break;
                case SwapMeetDefect.WeakCondenser:
                    instance.Condition = SimMath.Min(instance.Condition, 0.4f - 0.2f * s);
                    break;
                case SwapMeetDefect.StrippedThread:
                    instance.Condition = SimMath.Min(instance.Condition, 0.55f - 0.15f * s);
                    break;
                case SwapMeetDefect.WornRings:
                    float maxGap = instance.Definition.Stats.OptimalRingGapMaxMm > 0f ? instance.Definition.Stats.OptimalRingGapMaxMm : 0.45f;
                    instance.RingGapMm = maxGap + 0.1f + 0.2f * s;
                    instance.Wear = SimMath.Max(instance.Wear, 0.7f + 0.25f * s);
                    break;
                case SwapMeetDefect.WornBelt:
                    instance.Wear = SimMath.Max(instance.Wear, 0.75f + 0.2f * s);
                    instance.Condition = SimMath.Min(instance.Condition, 0.35f);
                    break;
            }
            instance.Wear = SimMath.Clamp01(instance.Wear);
            instance.Condition = SimMath.Clamp01(instance.Condition);
        }

        private static string DescribeDefect(SwapMeetDefect defect, PartDefinition part)
        {
            switch (defect)
            {
                case SwapMeetDefect.RustyTank:
                    return "Flashlight down the filler: flaky rust scale inside the " + part.DisplayName + ".";
                case SwapMeetDefect.WeakCondenser:
                    return "Meter check: the condenser on the " + part.DisplayName + " is weak and will heat-soak.";
                case SwapMeetDefect.StrippedThread:
                    return "Thread gauge snags: a stripped stud thread on the " + part.DisplayName + ".";
                case SwapMeetDefect.WornRings:
                    return "Feeler gauge: ring end-gap on the " + part.DisplayName + " is way out of spec.";
                case SwapMeetDefect.WornBelt:
                    return "Glazed and cracked: the " + part.DisplayName + " is near the end of its life.";
                default:
                    return "Looks honest: no hidden defects on the " + part.DisplayName + ".";
            }
        }

        private static bool IsSellable(PartDefinition part)
        {
            if (part.PriceUsd <= 0m || part.Stats.IsTool || part.Stats.IsSpare)
            {
                return false;
            }
            switch (part.Category)
            {
                case PartCategory.ToolRollItem:
                case PartCategory.GarageStation:
                case PartCategory.Consumable:
                case PartCategory.PremixOil:
                case PartCategory.Gasket:
                    return false;
                default:
                    return true;
            }
        }
    }
}
