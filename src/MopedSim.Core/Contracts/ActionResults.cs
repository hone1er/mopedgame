#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Structured result returned by BikeAssemblyGraph and BikeSimulation wrenching operations.
    /// </summary>
    public readonly struct WrenchActionResult
    {
        private readonly string? _targetId;
        private readonly string? _message;
        private readonly IReadOnlyList<string>? _blockingNodes;

        public readonly bool Succeeded;
        public readonly WrenchActionStatus Status;

        public string TargetId
        {
            get { return _targetId ?? string.Empty; }
        }

        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        public IReadOnlyList<string> BlockingNodes
        {
            get { return _blockingNodes ?? Array.Empty<string>(); }
        }

        public WrenchActionResult(
            bool succeeded,
            WrenchActionStatus status,
            string targetId,
            string message,
            IReadOnlyList<string>? blockingNodes = null)
        {
            Succeeded = succeeded;
            Status = status;
            _targetId = targetId ?? string.Empty;
            _message = message ?? string.Empty;
            _blockingNodes = blockingNodes ?? Array.Empty<string>();
        }

        public static WrenchActionResult Ok(string targetId, string message)
        {
            return new WrenchActionResult(true, WrenchActionStatus.Success, targetId, message);
        }

        public static WrenchActionResult Blocked(string targetId, IReadOnlyList<string> blockingNodes, string message)
        {
            return new WrenchActionResult(false, WrenchActionStatus.Blocked, targetId, message, blockingNodes);
        }

        public static WrenchActionResult Fail(WrenchActionStatus status, string targetId, string message)
        {
            return new WrenchActionResult(false, status, targetId, message);
        }
    }

    /// <summary>
    /// Frozen spark plug chop diagnostic snapshot.
    /// </summary>
    public readonly struct SparkPlugReading
    {
        private readonly string? _diagnosticSummary;

        public readonly float ElectrodeGapMm;
        public readonly float FoulingLevel;
        public readonly float InsulatorColorIndex; // 0.0 ChalkWhite -> 0.5 ChocolateBrown -> 1.0 WetBlack
        public readonly PlugColorZone ColorZone;
        public readonly int HeatRange;
        public readonly bool IsInstalled;

        public string DiagnosticSummary
        {
            get { return _diagnosticSummary ?? string.Empty; }
        }

        public SparkPlugReading(
            float electrodeGapMm,
            float foulingLevel,
            float insulatorColorIndex,
            PlugColorZone colorZone,
            int heatRange,
            bool isInstalled,
            string diagnosticSummary)
        {
            ElectrodeGapMm = electrodeGapMm;
            FoulingLevel = foulingLevel;
            InsulatorColorIndex = insulatorColorIndex;
            ColorZone = colorZone;
            HeatRange = heatRange;
            IsInstalled = isInstalled;
            _diagnosticSummary = diagnosticSummary ?? string.Empty;
        }
    }

    /// <summary>
    /// Result of attempting a roadside repair against an active fault using the packed Tool Roll.
    /// </summary>
    public readonly struct RoadsideRepairResult
    {
        private readonly string? _faultId;
        private readonly string? _message;
        private readonly IReadOnlyList<string>? _missingToolsOrSpares;

        public readonly bool Succeeded;
        public readonly FaultType FaultType;
        public readonly bool RequiresGarageRebuild;
        public readonly bool ConsumedSpare;

        public string FaultId
        {
            get { return _faultId ?? string.Empty; }
        }

        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        public IReadOnlyList<string> MissingToolsOrSpares
        {
            get { return _missingToolsOrSpares ?? Array.Empty<string>(); }
        }

        public RoadsideRepairResult(
            bool succeeded,
            string faultId,
            FaultType faultType,
            string message,
            IReadOnlyList<string>? missingToolsOrSpares = null,
            bool requiresGarageRebuild = false,
            bool consumedSpare = false)
        {
            Succeeded = succeeded;
            _faultId = faultId ?? string.Empty;
            FaultType = faultType;
            _message = message ?? string.Empty;
            _missingToolsOrSpares = missingToolsOrSpares ?? Array.Empty<string>();
            RequiresGarageRebuild = requiresGarageRebuild;
            ConsumedSpare = consumedSpare;
        }
    }

    /// <summary>
    /// Recovery outcome when executing Pedal of Shame or calling the Buddy Tow truck.
    /// </summary>
    public readonly struct RecoveryResult
    {
        private readonly string? _mode;
        private readonly string? _message;

        public readonly bool Succeeded;
        public readonly float DistanceMiles;
        public readonly float StaminaUsed;
        public readonly decimal CashCostUsd;

        public string Mode
        {
            get { return _mode ?? string.Empty; }
        }

        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        public RecoveryResult(
            bool succeeded,
            string mode,
            float distanceMiles,
            float staminaUsed,
            decimal cashCostUsd,
            string message)
        {
            Succeeded = succeeded;
            _mode = mode ?? string.Empty;
            DistanceMiles = distanceMiles;
            StaminaUsed = staminaUsed;
            CashCostUsd = cashCostUsd;
            _message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Outcome of buying a catalog part with garage cash through SimulationWorld.TryPurchasePart.
    /// </summary>
    public readonly struct PurchaseResult
    {
        private readonly string? _partId;
        private readonly string? _message;

        public readonly bool Succeeded;
        public readonly decimal PriceUsd;
        public readonly decimal RemainingCashUsd;

        public string PartId
        {
            get { return _partId ?? string.Empty; }
        }

        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        public PurchaseResult(bool succeeded, string partId, decimal priceUsd, decimal remainingCashUsd, string message)
        {
            Succeeded = succeeded;
            _partId = partId ?? string.Empty;
            PriceUsd = priceUsd;
            RemainingCashUsd = remainingCashUsd;
            _message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// UI-ready interactable mount slot or fastener entry returned by BikeAssemblyGraph.GetInteractableNodes().
    /// </summary>
    public readonly struct InteractableNode
    {
        private readonly string? _nodeId;
        private readonly string? _parentSlotId;
        private readonly IReadOnlyList<string>? _blockingNodeIds;

        public readonly bool IsFastener;
        public readonly bool IsInstalled;
        public readonly bool CanRemove;
        public readonly bool CanInstallOrTorque;
        public readonly bool IsStripped;
        public readonly bool IsSeized;
        public readonly bool IsUnderTorqued;
        public readonly float CurrentTorqueNm;
        public readonly float TargetTorqueNm;

        public string NodeId
        {
            get { return _nodeId ?? string.Empty; }
        }

        public string ParentSlotId
        {
            get { return _parentSlotId ?? string.Empty; }
        }

        public IReadOnlyList<string> BlockingNodeIds
        {
            get { return _blockingNodeIds ?? Array.Empty<string>(); }
        }

        public InteractableNode(
            string nodeId,
            string parentSlotId,
            bool isFastener,
            bool isInstalled,
            bool canRemove,
            bool canInstallOrTorque,
            bool isStripped,
            bool isSeized,
            bool isUnderTorqued,
            float currentTorqueNm,
            float targetTorqueNm,
            IReadOnlyList<string>? blockingNodeIds = null)
        {
            _nodeId = nodeId ?? string.Empty;
            _parentSlotId = parentSlotId ?? string.Empty;
            IsFastener = isFastener;
            IsInstalled = isInstalled;
            CanRemove = canRemove;
            CanInstallOrTorque = canInstallOrTorque;
            IsStripped = isStripped;
            IsSeized = isSeized;
            IsUnderTorqued = isUnderTorqued;
            CurrentTorqueNm = currentTorqueNm;
            TargetTorqueNm = targetTorqueNm;
            _blockingNodeIds = blockingNodeIds ?? Array.Empty<string>();
        }
    }
}
