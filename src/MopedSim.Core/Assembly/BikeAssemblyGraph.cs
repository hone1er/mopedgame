using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Live mount slot node in BikeAssemblyGraph holding an optional installed PartInstance.
    /// </summary>
    public sealed class MountSlotNode
    {
        public MountSlotSpec Spec { get; }
        public PartInstance? InstalledPart { get; internal set; }

        public bool IsOccupied
        {
            get { return InstalledPart != null; }
        }

        public MountSlotNode(MountSlotSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }
    }

    /// <summary>
    /// Live fastener state tracking torque window, stripping, seizing, Locktite, and vibration loosening.
    /// </summary>
    public sealed class FastenerState
    {
        public FastenerSpec Spec { get; }
        public float CurrentTorqueNm { get; internal set; }
        public bool IsInstalled { get; internal set; }
        public bool IsStripped { get; internal set; }
        public bool IsSeized { get; internal set; }
        public bool PenetratingOilApplied { get; internal set; }
        public bool HasLocktite { get; set; }

        public string FastenerId
        {
            get { return Spec.FastenerId; }
        }

        public string OwnerSlotId
        {
            get { return Spec.OwnerSlotId; }
        }

        public float TargetTorqueNm
        {
            get { return Spec.TargetTorqueNm; }
        }

        public float ToleranceNm
        {
            get { return Spec.ToleranceNm; }
        }

        public float StripThresholdNm
        {
            get { return Spec.StripThresholdNm; }
        }

        public float VibrationLoosenRate
        {
            get { return Spec.VibrationLoosenRate; }
        }

        public bool IsProperlyTorqued
        {
            get
            {
                return IsInstalled
                    && !IsStripped
                    && CurrentTorqueNm >= Spec.TargetTorqueNm - Spec.ToleranceNm
                    && CurrentTorqueNm <= Spec.TargetTorqueNm + Spec.ToleranceNm;
            }
        }

        public bool IsUnderTorqued
        {
            get
            {
                return IsInstalled
                    && !IsStripped
                    && CurrentTorqueNm < Spec.TargetTorqueNm - Spec.ToleranceNm;
            }
        }

        public float UnderTorqueSeverity
        {
            get
            {
                if (!IsInstalled || IsStripped)
                {
                    return 1.0f;
                }
                float minSpec = Spec.TargetTorqueNm - Spec.ToleranceNm;
                if (CurrentTorqueNm >= minSpec)
                {
                    return 0f;
                }
                return SimMath.Clamp01((minSpec - CurrentTorqueNm) / SimMath.Max(minSpec, 0.5f));
            }
        }

        public FastenerState(FastenerSpec spec, bool installed = true)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            IsInstalled = installed;
            CurrentTorqueNm = installed ? spec.TargetTorqueNm : 0f;
            IsSeized = installed && spec.DefaultSeized;
        }
    }

    /// <summary>
    /// Enforces mount slot hierarchy, DAG removal preconditions, torque windows, seized studs, and piston checks.
    /// </summary>
    public sealed class BikeAssemblyGraph
    {
        /// <summary>Vibration loosening multiplier for fasteners still inside their torque window (preload friction holds them).</summary>
        public const float InSpecLoosenMultiplier = 0.05f;

        /// <summary>Vibration loosening multiplier for under-torqued fasteners (lost preload lets them unwind quickly).</summary>
        public const float UnderTorquedLoosenMultiplier = 2.5f;

        private readonly List<MountSlotNode> _slotsOrdered = new List<MountSlotNode>();
        private readonly List<FastenerState> _fastenersOrdered = new List<FastenerState>();
        private readonly Dictionary<string, MountSlotNode> _slotsById = new Dictionary<string, MountSlotNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, FastenerState> _fastenersById = new Dictionary<string, FastenerState>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FastenerState>> _fastenersByOwner = new Dictionary<string, List<FastenerState>>(StringComparer.Ordinal);

        public IReadOnlyList<MountSlotNode> Slots
        {
            get { return _slotsOrdered; }
        }

        public IReadOnlyList<FastenerState> Fasteners
        {
            get { return _fastenersOrdered; }
        }

        public BikeAssemblyGraph(BikeArchetypeSpec archetype, IReadOnlyDictionary<string, PartDefinition>? catalog = null)
        {
            if (archetype == null)
            {
                throw new ArgumentNullException(nameof(archetype));
            }

            for (int i = 0; i < archetype.MountSlots.Count; i++)
            {
                MountSlotSpec slotSpec = archetype.MountSlots[i];
                var node = new MountSlotNode(slotSpec);
                _slotsOrdered.Add(node);
                _slotsById[slotSpec.SlotId] = node;
                _fastenersByOwner[slotSpec.SlotId] = new List<FastenerState>();
            }

            for (int i = 0; i < archetype.Fasteners.Count; i++)
            {
                FastenerSpec fSpec = archetype.Fasteners[i];
                var fState = new FastenerState(fSpec, installed: false);
                _fastenersOrdered.Add(fState);
                _fastenersById[fSpec.FastenerId] = fState;
                if (_fastenersByOwner.TryGetValue(fSpec.OwnerSlotId, out var ownerList) && ownerList != null)
                {
                    ownerList.Add(fState);
                }
            }

            if (catalog != null && archetype.DefaultPartIds.Count > 0)
            {
                // Install in slot declaration order (parent-before-child) for deterministic initialization.
                for (int i = 0; i < _slotsOrdered.Count; i++)
                {
                    string slotId = _slotsOrdered[i].Spec.SlotId;
                    if (archetype.DefaultPartIds.TryGetValue(slotId, out var partId)
                        && partId != null
                        && partId.Length > 0
                        && catalog.TryGetValue(partId, out var def)
                        && def != null)
                    {
                        _slotsOrdered[i].InstalledPart = new PartInstance(def);
                        if (_fastenersByOwner.TryGetValue(slotId, out var owned) && owned != null)
                        {
                            for (int j = 0; j < owned.Count; j++)
                            {
                                owned[j].IsInstalled = true;
                                owned[j].CurrentTorqueNm = owned[j].Spec.TargetTorqueNm;
                                owned[j].IsSeized = owned[j].Spec.DefaultSeized;
                            }
                        }
                    }
                }
            }
        }

        public MountSlotNode? GetSlot(string slotId)
        {
            if (slotId == null || slotId.Length == 0)
            {
                return null;
            }
            return _slotsById.TryGetValue(slotId, out var node) ? node : null;
        }

        public PartInstance? GetInstalledPart(string slotId)
        {
            MountSlotNode? node = GetSlot(slotId);
            return node != null ? node.InstalledPart : null;
        }

        public FastenerState? GetFastener(string fastenerId)
        {
            if (fastenerId == null || fastenerId.Length == 0)
            {
                return null;
            }
            return _fastenersById.TryGetValue(fastenerId, out var state) ? state : null;
        }

        public bool IsSlotOccupied(string slotId)
        {
            MountSlotNode? node = GetSlot(slotId);
            return node != null && node.IsOccupied;
        }

        public bool HasSlot(string slotId)
        {
            return slotId != null && slotId.Length > 0 && _slotsById.ContainsKey(slotId);
        }

        /// <summary>
        /// Attempts to remove a part from a mount slot after verifying all DAG preconditions.
        /// </summary>
        public WrenchActionResult TryRemovePart(string slotId)
        {
            MountSlotNode? node = GetSlot(slotId);
            if (node == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, slotId, "Unknown mount slot.");
            }
            if (!node.IsOccupied)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.SlotEmpty, slotId, "Mount slot is already empty.");
            }

            List<string> blocking = CollectRemovalBlockers(node);
            if (blocking.Count > 0)
            {
                return WrenchActionResult.Blocked(
                    slotId,
                    blocking,
                    "Cannot remove " + slotId + "; blocked by: " + string.Join(", ", blocking));
            }

            node.InstalledPart = null;
            return WrenchActionResult.Ok(slotId, "Removed part from " + slotId + ".");
        }

        /// <summary>
        /// Attempts to install a part into a mount slot, validating parent mount, compatibility, piston arrow, and ring gap.
        /// </summary>
        public WrenchActionResult TryInstallPart(string slotId, PartInstance part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            MountSlotNode? node = GetSlot(slotId);
            if (node == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, slotId, "Unknown mount slot.");
            }
            if (node.IsOccupied)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.SlotAlreadyOccupied, slotId, "Slot already contains an installed part.");
            }

            string? parentSlotId = node.Spec.ParentSlotId;
            if (parentSlotId != null && parentSlotId.Length > 0)
            {
                MountSlotNode? parent = GetSlot(parentSlotId);
                if (parent != null && !parent.IsOccupied)
                {
                    return WrenchActionResult.Blocked(
                        slotId,
                        new[] { parentSlotId },
                        "Install parent slot " + parentSlotId + " first.");
                }
            }

            if (node.Spec.RequiresEmptySlotIds.Length > 0)
            {
                var blockers = new List<string>();
                for (int i = 0; i < node.Spec.RequiresEmptySlotIds.Length; i++)
                {
                    string reqEmpty = node.Spec.RequiresEmptySlotIds[i];
                    if (IsSlotOccupied(reqEmpty))
                    {
                        blockers.Add(reqEmpty);
                    }
                }
                if (blockers.Count > 0)
                {
                    return WrenchActionResult.Blocked(
                        slotId,
                        blockers,
                        "Cannot install " + slotId + " while " + string.Join(", ", blockers) + " is mounted.");
                }
            }

            if (part.Definition.Category != node.Spec.AcceptedCategory)
            {
                return WrenchActionResult.Fail(
                    WrenchActionStatus.IncompatiblePart,
                    slotId,
                    "Part category " + part.Definition.Category + " does not fit " + node.Spec.AcceptedCategory + ".");
            }

            if (node.Spec.RequiredInterfaces.Length > 0 && !HasMatchingInterface(node.Spec.RequiredInterfaces, part.Definition.CompatibleInterfaces))
            {
                return WrenchActionResult.Fail(
                    WrenchActionStatus.IncompatiblePart,
                    slotId,
                    "Part interface is incompatible with " + slotId + ".");
            }

            if (node.Spec.AcceptedCategory == PartCategory.Piston || string.Equals(slotId, SlotIds.Piston, StringComparison.Ordinal))
            {
                if (!part.ArrowTowardExhaust)
                {
                    return WrenchActionResult.Fail(
                        WrenchActionStatus.InvalidOrientation,
                        slotId,
                        "Piston orientation error: arrow must point toward the exhaust port.");
                }

                float minGap = part.Definition.Stats.OptimalRingGapMinMm;
                float maxGap = part.Definition.Stats.OptimalRingGapMaxMm;
                if (maxGap > 0f && (part.RingGapMm < minGap || part.RingGapMm > maxGap))
                {
                    return WrenchActionResult.Fail(
                        WrenchActionStatus.InvalidSpec,
                        slotId,
                        "Piston ring end-gap out of spec (" + part.RingGapMm.ToString("0.00") + " mm; expected " + minGap.ToString("0.00") + "-" + maxGap.ToString("0.00") + " mm).");
                }
            }

            node.InstalledPart = part;
            return WrenchActionResult.Ok(slotId, "Installed " + part.Definition.DisplayName + " into " + slotId + ".");
        }

        /// <summary>
        /// Swaps a part in-place (for example, swapping main jet or spark plug) when unblocked.
        /// </summary>
        /// <remarks>
        /// A swap re-hangs the replacement part on the slot's own fasteners (e.g. exhaust flange studs), so blockers that are
        /// healthy fasteners owned by this slot do not prevent it; their torque state carries over unchanged (a swap is never a
        /// free re-torque). Any foreign blocker (another slot's fastener, an occupied blocking slot) or a seized/stripped owned
        /// fastener still blocks, exactly like <see cref="TryRemovePart"/>. On install failure the original part is restored.
        /// </remarks>
        public WrenchActionResult TrySwapPart(string slotId, PartInstance newPart)
        {
            if (newPart == null)
            {
                throw new ArgumentNullException(nameof(newPart));
            }

            MountSlotNode? node = GetSlot(slotId);
            if (node == null || !node.IsOccupied)
            {
                return TryInstallPart(slotId, newPart);
            }

            List<string> blocking = CollectRemovalBlockers(node);
            if (blocking.Count > 0 && !AreAllSwapTransferableFasteners(node, blocking))
            {
                return WrenchActionResult.Blocked(
                    slotId,
                    blocking,
                    "Cannot swap " + slotId + "; blocked by: " + string.Join(", ", blocking));
            }

            PartInstance? previous = node.InstalledPart;
            node.InstalledPart = null;
            WrenchActionResult install = TryInstallPart(slotId, newPart);
            if (!install.Succeeded)
            {
                node.InstalledPart = previous;
            }
            return install;
        }

        private bool AreAllSwapTransferableFasteners(MountSlotNode node, List<string> blocking)
        {
            for (int i = 0; i < blocking.Count; i++)
            {
                FastenerState? f = GetFastener(blocking[i]);
                if (f == null
                    || !string.Equals(f.OwnerSlotId, node.Spec.SlotId, StringComparison.Ordinal)
                    || f.IsSeized
                    || f.IsStripped)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Loosens and removes an installed fastener; forcing a seized stud without penetrating oil snaps/strips it.
        /// </summary>
        public WrenchActionResult TryRemoveFastener(string fastenerId)
        {
            FastenerState? f = GetFastener(fastenerId);
            if (f == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, fastenerId, "Unknown fastener.");
            }
            if (!f.IsInstalled)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.SlotEmpty, fastenerId, "Fastener is already removed.");
            }
            if (f.IsStripped)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerStripped, fastenerId, "Fastener is stripped; requires tap/helicoil repair.");
            }
            if (f.IsSeized && !f.PenetratingOilApplied)
            {
                f.IsStripped = true;
                return WrenchActionResult.Fail(
                    WrenchActionStatus.FastenerSeized,
                    fastenerId,
                    "Fastener " + fastenerId + " is seized and snapped/stripped! Apply penetrating oil first.");
            }

            f.IsInstalled = false;
            f.CurrentTorqueNm = 0f;
            f.IsSeized = false;
            f.PenetratingOilApplied = false;
            return WrenchActionResult.Ok(fastenerId, "Removed fastener " + fastenerId + ".");
        }

        /// <summary>
        /// Applies penetrating oil to break rust seizure on a fastener.
        /// </summary>
        public WrenchActionResult ApplyPenetratingOil(string fastenerId)
        {
            FastenerState? f = GetFastener(fastenerId);
            if (f == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, fastenerId, "Unknown fastener.");
            }
            if (f.IsStripped)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerStripped, fastenerId, "Fastener is already stripped; requires tap/helicoil repair.");
            }
            f.PenetratingOilApplied = true;
            return WrenchActionResult.Ok(fastenerId, "Applied penetrating oil to " + fastenerId + ".");
        }

        /// <summary>
        /// Repairs a stripped thread/stud via tap or helicoil insert.
        /// </summary>
        public WrenchActionResult RepairStrippedFastener(string fastenerId)
        {
            FastenerState? f = GetFastener(fastenerId);
            if (f == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, fastenerId, "Unknown fastener.");
            }
            f.IsStripped = false;
            f.IsSeized = false;
            f.PenetratingOilApplied = false;
            f.IsInstalled = false;
            f.CurrentTorqueNm = 0f;
            return WrenchActionResult.Ok(fastenerId, "Repaired threads for " + fastenerId + ".");
        }

        /// <summary>
        /// Marks a fastener as seized by rust/heat cycling (for test setup or aging events).
        /// </summary>
        public void SetFastenerSeized(string fastenerId, bool isSeized)
        {
            FastenerState? f = GetFastener(fastenerId);
            if (f != null)
            {
                f.IsSeized = isSeized;
                if (!isSeized)
                {
                    f.PenetratingOilApplied = false;
                }
            }
        }

        /// <summary>
        /// Installs and torques a fastener; exceeding StripThresholdNm strips threads.
        /// </summary>
        public WrenchActionResult TryTorqueFastener(string fastenerId, float torqueNm)
        {
            FastenerState? f = GetFastener(fastenerId);
            if (f == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerNotFound, fastenerId, "Unknown fastener.");
            }
            if (torqueNm < 0f)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.InvalidSpec, fastenerId, "Torque cannot be negative.");
            }
            if (!IsSlotOccupied(f.OwnerSlotId))
            {
                return WrenchActionResult.Blocked(
                    fastenerId,
                    new[] { f.OwnerSlotId },
                    "Install " + f.OwnerSlotId + " before torquing " + fastenerId + ".");
            }
            if (f.IsStripped)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerStripped, fastenerId, "Fastener is stripped; repair threads first.");
            }
            if (f.IsSeized && !f.PenetratingOilApplied)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.FastenerSeized, fastenerId, "Fastener is seized; apply penetrating oil first.");
            }

            f.IsInstalled = true;
            f.IsSeized = false;
            f.CurrentTorqueNm = torqueNm;

            if (torqueNm > f.StripThresholdNm)
            {
                f.IsStripped = true;
                return WrenchActionResult.Fail(
                    WrenchActionStatus.FastenerStripped,
                    fastenerId,
                    "Over-torqued " + fastenerId + " (" + torqueNm.ToString("0.0") + " Nm > " + f.StripThresholdNm.ToString("0.0") + " Nm)! Threads stripped.");
            }

            if (torqueNm < f.TargetTorqueNm - f.ToleranceNm)
            {
                return WrenchActionResult.Ok(fastenerId, "Fastener under-torqued (" + torqueNm.ToString("0.0") + " Nm); leak and vibration loosening risk.");
            }

            return WrenchActionResult.Ok(fastenerId, "Torqued " + fastenerId + " to " + torqueNm.ToString("0.0") + " Nm.");
        }

        /// <summary>
        /// Applies chassis and engine vibration loosening across installed fasteners deterministically.
        /// </summary>
        public void ApplyVibration(float dt, float chassisVibration, float potholeImpulse)
        {
            if (dt <= 0f)
            {
                return;
            }
            float intensity = SimMath.Max(0f, chassisVibration) + SimMath.Max(0f, potholeImpulse) * 3.5f;
            if (intensity <= 0f)
            {
                return;
            }

            for (int i = 0; i < _fastenersOrdered.Count; i++)
            {
                FastenerState f = _fastenersOrdered[i];
                if (!f.IsInstalled || f.IsStripped || f.HasLocktite)
                {
                    continue;
                }

                // Clamp preload friction holds an in-spec fastener (only slow creep); once it backs off below its torque
                // window it loses that friction and the buzz unwinds it rapidly.
                float preloadFactor = f.IsUnderTorqued ? UnderTorquedLoosenMultiplier : InSpecLoosenMultiplier;
                float loss = f.VibrationLoosenRate * preloadFactor * intensity * dt;
                f.CurrentTorqueNm = SimMath.Max(0f, f.CurrentTorqueNm - loss);
            }
        }

        /// <summary>
        /// Computes downstream air leak severity (0..1) from loose head nuts, intake bolts, carb clamp, and damaged gaskets.
        /// </summary>
        public float ComputeAirLeakSeverity()
        {
            float leak = 0f;

            // Head nuts
            leak += GetFastenerLeakContribution(FastenerIds.HeadNut1, 0.14f);
            leak += GetFastenerLeakContribution(FastenerIds.HeadNut2, 0.14f);
            leak += GetFastenerLeakContribution(FastenerIds.HeadNut3, 0.14f);
            leak += GetFastenerLeakContribution(FastenerIds.HeadNut4, 0.14f);

            // Intake bolts & carb clamp
            leak += GetFastenerLeakContribution(FastenerIds.Intake1, 0.22f);
            leak += GetFastenerLeakContribution(FastenerIds.Intake2, 0.22f);
            leak += GetFastenerLeakContribution(FastenerIds.CarbClamp, 0.30f);

            // Gaskets (only if defined in archetype)
            leak += GetGasketLeakContribution(SlotIds.BaseGasket, 0.45f);
            leak += GetGasketLeakContribution(SlotIds.IntakeGasket, 0.38f);
            leak += GetGasketLeakContribution(SlotIds.HeadGasket, 0.35f);

            return SimMath.Clamp01(leak);
        }

        /// <summary>
        /// Computes exhaust flange leak severity (0..1) from loose flange bolts or missing exhaust/gasket.
        /// </summary>
        public float ComputeExhaustLeakSeverity()
        {
            if (!IsSlotOccupied(SlotIds.Exhaust))
            {
                return 1.0f;
            }

            float leak = 0f;
            leak += GetFastenerLeakContribution(FastenerIds.ExhaustFlange1, 0.42f);
            leak += GetFastenerLeakContribution(FastenerIds.ExhaustFlange2, 0.42f);
            leak += GetFastenerLeakContribution(FastenerIds.ExhaustFrameBracket, 0.16f);
            leak += GetGasketLeakContribution(SlotIds.ExhaustGasket, 0.32f);

            return SimMath.Clamp01(leak);
        }

        /// <summary>
        /// Sums total mounted part mass in kilograms.
        /// </summary>
        public float GetMountedPartsMassKg()
        {
            float mass = 0f;
            for (int i = 0; i < _slotsOrdered.Count; i++)
            {
                PartInstance? part = _slotsOrdered[i].InstalledPart;
                if (part != null)
                {
                    mass += part.Definition.MassKg;
                }
            }
            return mass;
        }

        /// <summary>
        /// Sums cargo slot capacity from mounted rack/crate accessories.
        /// </summary>
        public int GetCargoCapacitySlots()
        {
            int slots = 0;
            for (int i = 0; i < _slotsOrdered.Count; i++)
            {
                PartInstance? part = _slotsOrdered[i].InstalledPart;
                if (part != null)
                {
                    slots += part.Definition.Stats.CargoSlots;
                }
            }
            return slots;
        }

        /// <summary>
        /// Returns an ordered snapshot of all slots and fasteners with wrenchability status for Garage UI highlighting.
        /// </summary>
        public IReadOnlyList<InteractableNode> GetInteractableNodes()
        {
            var result = new List<InteractableNode>(_slotsOrdered.Count + _fastenersOrdered.Count);

            for (int i = 0; i < _slotsOrdered.Count; i++)
            {
                MountSlotNode node = _slotsOrdered[i];
                List<string> blockers = node.IsOccupied
                    ? CollectRemovalBlockers(node)
                    : CollectInstallBlockers(node);

                bool canRemove = node.IsOccupied && blockers.Count == 0;
                bool canInstall = !node.IsOccupied && blockers.Count == 0;

                result.Add(new InteractableNode(
                    node.Spec.SlotId,
                    node.Spec.ParentSlotId ?? string.Empty,
                    isFastener: false,
                    isInstalled: node.IsOccupied,
                    canRemove: canRemove,
                    canInstallOrTorque: canInstall,
                    isStripped: false,
                    isSeized: false,
                    isUnderTorqued: false,
                    currentTorqueNm: 0f,
                    targetTorqueNm: 0f,
                    blockingNodeIds: blockers));
            }

            for (int i = 0; i < _fastenersOrdered.Count; i++)
            {
                FastenerState f = _fastenersOrdered[i];
                bool ownerMounted = IsSlotOccupied(f.OwnerSlotId);
                bool canRemove = f.IsInstalled && !f.IsStripped;
                bool canTorque = ownerMounted && !f.IsStripped && (!f.IsSeized || f.PenetratingOilApplied);

                result.Add(new InteractableNode(
                    f.FastenerId,
                    f.OwnerSlotId,
                    isFastener: true,
                    isInstalled: f.IsInstalled,
                    canRemove: canRemove,
                    canInstallOrTorque: canTorque,
                    isStripped: f.IsStripped,
                    isSeized: f.IsSeized,
                    isUnderTorqued: f.IsUnderTorqued,
                    currentTorqueNm: f.CurrentTorqueNm,
                    targetTorqueNm: f.TargetTorqueNm,
                    blockingNodeIds: ownerMounted ? Array.Empty<string>() : new[] { f.OwnerSlotId }));
            }

            return result;
        }

        private float GetFastenerLeakContribution(string fastenerId, float weight)
        {
            FastenerState? f = GetFastener(fastenerId);
            if (f == null)
            {
                return 0f;
            }
            if (!IsSlotOccupied(f.OwnerSlotId))
            {
                return weight;
            }
            return f.UnderTorqueSeverity * weight;
        }

        private float GetGasketLeakContribution(string slotId, float weight)
        {
            MountSlotNode? node = GetSlot(slotId);
            if (node == null)
            {
                return 0f;
            }
            if (!node.IsOccupied || node.InstalledPart == null)
            {
                return weight;
            }
            return (1f - SimMath.Clamp01(node.InstalledPart.Condition)) * weight;
        }

        private List<string> CollectRemovalBlockers(MountSlotNode node)
        {
            var blocking = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Fasteners explicitly blocking removal
            for (int i = 0; i < node.Spec.BlockedByFastenerIds.Length; i++)
            {
                string fId = node.Spec.BlockedByFastenerIds[i];
                FastenerState? f = GetFastener(fId);
                if (f != null && f.IsInstalled && seen.Add(fId))
                {
                    blocking.Add(fId);
                }
            }

            // Fasteners owned directly by this slot
            if (_fastenersByOwner.TryGetValue(node.Spec.SlotId, out var owned) && owned != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    FastenerState f = owned[i];
                    if (f.IsInstalled && seen.Add(f.FastenerId))
                    {
                        blocking.Add(f.FastenerId);
                    }
                }
            }

            // Slots blocking removal (e.g., Head/Exhaust/Intake block Cylinder)
            for (int i = 0; i < node.Spec.BlockedBySlotIds.Length; i++)
            {
                string blockerId = node.Spec.BlockedBySlotIds[i];
                if (IsSlotOccupied(blockerId) && seen.Add(blockerId))
                {
                    blocking.Add(blockerId);
                }
            }

            // Slots that must be empty before removing this slot (e.g., Piston requires Cylinder empty)
            for (int i = 0; i < node.Spec.RequiresEmptySlotIds.Length; i++)
            {
                string reqEmpty = node.Spec.RequiresEmptySlotIds[i];
                if (IsSlotOccupied(reqEmpty) && seen.Add(reqEmpty))
                {
                    blocking.Add(reqEmpty);
                }
            }

            return blocking;
        }

        private List<string> CollectInstallBlockers(MountSlotNode node)
        {
            var blocking = new List<string>();
            string? parentSlotId = node.Spec.ParentSlotId;
            if (parentSlotId != null && parentSlotId.Length > 0)
            {
                MountSlotNode? parent = GetSlot(parentSlotId);
                if (parent != null && !parent.IsOccupied)
                {
                    blocking.Add(parentSlotId);
                }
            }
            for (int i = 0; i < node.Spec.RequiresEmptySlotIds.Length; i++)
            {
                string reqEmpty = node.Spec.RequiresEmptySlotIds[i];
                if (IsSlotOccupied(reqEmpty))
                {
                    blocking.Add(reqEmpty);
                }
            }
            return blocking;
        }

        private static bool HasMatchingInterface(string[] requiredInterfaces, string[] providedInterfaces)
        {
            for (int i = 0; i < requiredInterfaces.Length; i++)
            {
                for (int j = 0; j < providedInterfaces.Length; j++)
                {
                    if (string.Equals(requiredInterfaces[i], providedInterfaces[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
