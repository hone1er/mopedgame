#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Represents a currently active mechanical fault on a moped.
    /// </summary>
    public sealed class FaultState
    {
        public string FaultId { get; }
        public FaultType FaultType { get; }
        public string Description { get; }
        public bool IsFieldRepairable { get; }

        public FaultState(string faultId, FaultType faultType, string description, bool isFieldRepairable)
        {
            FaultId = faultId ?? string.Empty;
            FaultType = faultType;
            Description = description ?? string.Empty;
            IsFieldRepairable = isFieldRepairable;
        }
    }

    /// <summary>
    /// Evaluates the 10 §2.3 fault conditions and executes Tool Roll roadside repairs, Pedal of Shame, and Buddy Tow.
    /// </summary>
    public sealed class FaultEngine
    {
        private readonly List<FaultState> _activeFaults = new List<FaultState>();
        private readonly HashSet<string> _activeFaultIds = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<FaultState> ActiveFaults
        {
            get { return _activeFaults; }
        }

        public bool HasFault(string faultId)
        {
            return faultId != null && faultId.Length > 0 && _activeFaultIds.Contains(faultId);
        }

        /// <summary>
        /// Rebuilds active fault list deterministically from live subsystem state.
        /// </summary>
        public void Evaluate(
            SparkPlugState sparkPlug,
            FuelSystem fuel,
            IgnitionSystem ignition,
            EngineSimulation engine,
            DrivetrainModel drivetrain,
            ExhaustSystem exhaust,
            ThermalModel thermal)
        {
            _activeFaults.Clear();
            _activeFaultIds.Clear();

            if (sparkPlug.FoulingLevel >= 0.85f)
            {
                AddFault(FaultIds.FouledPlug, FaultType.FouledPlug, "Spark plug fouled with carbon/oil.", true);
            }
            if (fuel.IsMainJetClogged)
            {
                AddFault(FaultIds.CloggedJet, FaultType.CloggedJet, "Main jet clogged by tank rust debris.", true);
            }
            if (ignition.IsCondenserHeatSoaked)
            {
                AddFault(FaultIds.CondenserHeatSoak, FaultType.CondenserHeatSoak, "6V condenser heat-soaked above 95C crankcase.", true);
            }
            if (ignition.Type == IgnitionType.Points6V && ignition.IsPointsOutOfSpec)
            {
                AddFault(FaultIds.PointsDrift, FaultType.PointsDrift, "6V points gap drifted outside [0.30, 0.45] mm.", true);
            }
            if (engine.AirLeakSeverity >= 0.25f)
            {
                AddFault(FaultIds.AirLeak, FaultType.AirLeak, "Intake/gasket/crankseal air leak causing lean hang.", true);
            }
            if (drivetrain.IsChainThrown)
            {
                AddFault(FaultIds.ThrownChain, FaultType.ThrownChain, "Drive chain thrown off rear sprocket.", true);
            }
            if (drivetrain.Type == DrivetrainType.VariatedBelt && drivetrain.IsBeltSnapped)
            {
                AddFault(FaultIds.SnappedBelt, FaultType.SnappedBelt, "Variator V-belt snapped.", true);
            }
            if (exhaust.ExhaustLeakFactor >= 0.35f)
            {
                AddFault(FaultIds.ExhaustLoose, FaultType.ExhaustLoose, "Exhaust flange bolts loosened; unmuffled leak.", true);
            }
            if (thermal.IsSoftSeized)
            {
                AddFault(FaultIds.SoftSeize, FaultType.SoftSeize, "4-corner thermal soft seize (CHT >= 230C).", true);
            }
            if (thermal.IsHardSeized)
            {
                AddFault(FaultIds.HardSeize, FaultType.HardSeize, "Hard seizure — piston/cylinder scored; garage rebuild required.", false);
            }
        }

        /// <summary>
        /// Attempts a roadside repair for the requested fault ID using items packed in the Tool Roll.
        /// </summary>
        public RoadsideRepairResult TryRoadsideRepair(
            string faultId,
            IReadOnlyCollection<string> toolRollItemIds,
            SparkPlugState sparkPlug,
            FuelSystem fuel,
            IgnitionSystem ignition,
            EngineSimulation engine,
            DrivetrainModel drivetrain,
            BikeAssemblyGraph assembly,
            ThermalModel thermal)
        {
            if (faultId == null || faultId.Length == 0 || !HasFault(faultId))
            {
                return new RoadsideRepairResult(false, faultId ?? string.Empty, FaultType.FouledPlug, "Requested fault is not active.");
            }

            var normalizedTools = NormalizeToolSet(toolRollItemIds);

            switch (faultId)
            {
                case FaultIds.FouledPlug:
                {
                    bool hasWrench = HasTool(normalizedTools, ToolIds.SparkPlugWrench, "sparkplugwrench");
                    bool hasSparePlug = HasTool(normalizedTools, ToolIds.SparePlug, "spareplug");
                    bool hasBrush = HasTool(normalizedTools, ToolIds.WireBrush, "wirebrush");
                    var missing = new List<string>();
                    if (!hasWrench)
                    {
                        missing.Add(ToolIds.SparkPlugWrench);
                    }
                    if (!hasSparePlug && !hasBrush)
                    {
                        missing.Add(ToolIds.SparePlug);
                    }
                    if (missing.Count > 0)
                    {
                        return new RoadsideRepairResult(false, faultId, FaultType.FouledPlug, "Missing spark plug wrench or spare/brush.", missing);
                    }
                    if (hasSparePlug)
                    {
                        sparkPlug.ReplacePlug(sparkPlug.HeatRange, 0.60f);
                        RemoveFault(faultId);
                        return new RoadsideRepairResult(true, faultId, FaultType.FouledPlug, "Swapped in fresh spare spark plug roadside.", consumedSpare: true);
                    }
                    sparkPlug.CleanPlug(0.60f);
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.FouledPlug, "Brushed fouled electrode clean roadside.");
                }

                case FaultIds.CloggedJet:
                {
                    bool hasFlathead = HasTool(normalizedTools, ToolIds.Flathead, "flathead");
                    if (!hasFlathead)
                    {
                        return new RoadsideRepairResult(
                            false,
                            faultId,
                            FaultType.CloggedJet,
                            "Need flathead screwdriver to drop float bowl and clear jet.",
                            new[] { ToolIds.Flathead });
                    }
                    fuel.ClearMainJetClog();
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.CloggedJet, "Cleared clogged main jet roadside.");
                }

                case FaultIds.CondenserHeatSoak:
                {
                    bool hasPuller = HasTool(normalizedTools, ToolIds.FlywheelPuller, "flywheelpuller");
                    bool hasCondenser = HasTool(normalizedTools, ToolIds.SpareCondenser, "sparecondenser");
                    if (hasPuller && hasCondenser)
                    {
                        ignition.ReplaceCondenser();
                        RemoveFault(faultId);
                        return new RoadsideRepairResult(true, faultId, FaultType.CondenserHeatSoak, "Replaced heat-soaked condenser roadside.", consumedSpare: true);
                    }
                    if (thermal.CrankcaseTempCelsius < ignition.CondenserHeatThresholdC - 10f)
                    {
                        ignition.Step(0f, 0f, thermal.CrankcaseTempCelsius, sparkPlug.FoulingLevel, sparkPlug.ElectrodeGapMm, true, true, 0f, new SeededSimRandom(1));
                        RemoveFault(faultId);
                        return new RoadsideRepairResult(true, faultId, FaultType.CondenserHeatSoak, "Crankcase cooled below 85C; 6V condenser recovered.");
                    }
                    return new RoadsideRepairResult(
                        false,
                        faultId,
                        FaultType.CondenserHeatSoak,
                        "Condenser still heat-soaked (>85C); wait to cool or pack flywheel puller + spare condenser.",
                        new[] { ToolIds.FlywheelPuller, ToolIds.SpareCondenser });
                }

                case FaultIds.PointsDrift:
                {
                    bool hasFlathead = HasTool(normalizedTools, ToolIds.Flathead, "flathead");
                    bool hasFeeler = HasTool(normalizedTools, ToolIds.FeelerGauge, "feelergauge");
                    var missing = new List<string>();
                    if (!hasFlathead)
                    {
                        missing.Add(ToolIds.Flathead);
                    }
                    if (!hasFeeler)
                    {
                        missing.Add(ToolIds.FeelerGauge);
                    }
                    if (missing.Count > 0)
                    {
                        return new RoadsideRepairResult(false, faultId, FaultType.PointsDrift, "Need flathead + feeler gauge to reset 6V points gap to 0.35 mm.", missing);
                    }
                    ignition.SetPointsGap(0.35f);
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.PointsDrift, "Reset 6V points gap to 0.35 mm roadside.");
                }

                case FaultIds.AirLeak:
                {
                    bool has10mm = HasTool(normalizedTools, ToolIds.Wrench10mm, "wrench10mm");
                    bool hasFlathead = HasTool(normalizedTools, ToolIds.Flathead, "flathead");
                    if (!has10mm && !hasFlathead)
                    {
                        return new RoadsideRepairResult(
                            false,
                            faultId,
                            FaultType.AirLeak,
                            "Need 10mm wrench or flathead to tighten intake/carb clamp.",
                            new[] { ToolIds.Wrench10mm });
                    }
                    TightenBoltIfInstalled(assembly, FastenerIds.CarbClamp);
                    TightenBoltIfInstalled(assembly, FastenerIds.Intake1);
                    TightenBoltIfInstalled(assembly, FastenerIds.Intake2);
                    TightenBoltIfInstalled(assembly, FastenerIds.HeadNut1);
                    TightenBoltIfInstalled(assembly, FastenerIds.HeadNut2);
                    TightenBoltIfInstalled(assembly, FastenerIds.HeadNut3);
                    TightenBoltIfInstalled(assembly, FastenerIds.HeadNut4);

                    if (engine.CrankSealLeakSeverity >= 0.25f)
                    {
                        return new RoadsideRepairResult(
                            false,
                            faultId,
                            FaultType.AirLeak,
                            "Tightened clamps, but worn crank seal requires Garage rebuild.",
                            requiresGarageRebuild: true);
                    }
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.AirLeak, "Tightened loose intake/carb fasteners roadside.");
                }

                case FaultIds.ThrownChain:
                {
                    bool hasLink = HasTool(normalizedTools, ToolIds.SpareMasterLink, "sparemasterlink", "masterlink");
                    bool hasPliers = HasTool(normalizedTools, ToolIds.NeedleNose, "needlenose");
                    bool has15mm = HasTool(normalizedTools, ToolIds.Wrench15mm, "wrench15mm");
                    var missing = new List<string>();
                    if (!hasLink)
                    {
                        missing.Add(ToolIds.SpareMasterLink);
                    }
                    if (!hasPliers)
                    {
                        missing.Add(ToolIds.NeedleNose);
                    }
                    if (!has15mm)
                    {
                        missing.Add(ToolIds.Wrench15mm);
                    }
                    if (missing.Count > 0)
                    {
                        return new RoadsideRepairResult(false, faultId, FaultType.ThrownChain, "Missing master link, needle-nose pliers, or 15mm axle wrench.", missing);
                    }
                    drivetrain.RepairChain(20f);
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.ThrownChain, "Re-mounted drive chain with master link and set tension.", consumedSpare: true);
                }

                case FaultIds.SnappedBelt:
                {
                    bool hasBelt = HasTool(normalizedTools, ToolIds.SpareBelt, "sparebelt");
                    bool hasStrap = HasTool(normalizedTools, ToolIds.VariatorStrap, "variatorstrap", "variatortool")
                        || normalizedTools.Contains("variatorstraptool")
                        || HasTool(normalizedTools, ToolIds.Wrench10mm, "wrench10mm");
                    var missing = new List<string>();
                    if (!hasBelt)
                    {
                        missing.Add(ToolIds.SpareBelt);
                    }
                    if (!hasStrap)
                    {
                        missing.Add(ToolIds.VariatorStrap);
                    }
                    if (missing.Count > 0)
                    {
                        return new RoadsideRepairResult(false, faultId, FaultType.SnappedBelt, "Missing spare V-belt or variator tool.", missing);
                    }
                    drivetrain.ReplaceBelt();
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.SnappedBelt, "Installed spare variator V-belt roadside.", consumedSpare: true);
                }

                case FaultIds.ExhaustLoose:
                {
                    bool has10mm = HasTool(normalizedTools, ToolIds.Wrench10mm, "wrench10mm");
                    if (!has10mm)
                    {
                        return new RoadsideRepairResult(
                            false,
                            faultId,
                            FaultType.ExhaustLoose,
                            "Need 10mm wrench to tighten exhaust flange nuts.",
                            new[] { ToolIds.Wrench10mm });
                    }
                    bool hasSpareNut = HasTool(normalizedTools, ToolIds.SpareFlangeNut, "spareflangenut");
                    bool flangeMissing = IsBoltMissing(assembly, FastenerIds.ExhaustFlange1) || IsBoltMissing(assembly, FastenerIds.ExhaustFlange2);
                    if (flangeMissing && !hasSpareNut)
                    {
                        return new RoadsideRepairResult(
                            false,
                            faultId,
                            FaultType.ExhaustLoose,
                            "Flange nut fell off on road — need spare flange nut.",
                            new[] { ToolIds.SpareFlangeNut });
                    }
                    TightenBoltOrInstall(assembly, FastenerIds.ExhaustFlange1);
                    TightenBoltOrInstall(assembly, FastenerIds.ExhaustFlange2);
                    TightenBoltIfInstalled(assembly, FastenerIds.ExhaustFrameBracket);
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.ExhaustLoose, "Secured exhaust flange hardware roadside.", consumedSpare: flangeMissing);
                }

                case FaultIds.SoftSeize:
                {
                    if (!thermal.TryClearSoftSeize())
                    {
                        return new RoadsideRepairResult(
                            false,
                            faultId,
                            FaultType.SoftSeize,
                            "Cylinder still too hot (" + thermal.ChtCelsius.ToString("0") + "C); cool down below 140C on curb first.");
                    }
                    RemoveFault(faultId);
                    return new RoadsideRepairResult(true, faultId, FaultType.SoftSeize, "Engine cooled after soft seize; ready to gently ride home.");
                }

                case FaultIds.HardSeize:
                default:
                    return new RoadsideRepairResult(
                        false,
                        FaultIds.HardSeize,
                        FaultType.HardSeize,
                        "Hard seize is not field-repairable — use Pedal of Shame or call Buddy Tow to West Oakland Garage.",
                        requiresGarageRebuild: true);
            }
        }

        private void AddFault(string faultId, FaultType type, string description, bool isFieldRepairable)
        {
            if (_activeFaultIds.Add(faultId))
            {
                _activeFaults.Add(new FaultState(faultId, type, description, isFieldRepairable));
            }
        }

        private void RemoveFault(string faultId)
        {
            if (_activeFaultIds.Remove(faultId))
            {
                for (int i = _activeFaults.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_activeFaults[i].FaultId, faultId, StringComparison.Ordinal))
                    {
                        _activeFaults.RemoveAt(i);
                    }
                }
            }
        }

        private static HashSet<string> NormalizeToolSet(IReadOnlyCollection<string> items)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return set;
            }
            foreach (string raw in items)
            {
                if (raw == null || raw.Length == 0)
                {
                    continue;
                }
                set.Add(raw);
                set.Add(raw.Replace("_", string.Empty));
            }
            return set;
        }

        private static bool HasTool(HashSet<string> normalized, string canonicalId, string alias1, string alias2 = "")
        {
            if (normalized.Contains(canonicalId) || normalized.Contains(canonicalId.Replace("_", string.Empty)))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(alias1) && normalized.Contains(alias1))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(alias2) && normalized.Contains(alias2))
            {
                return true;
            }
            return false;
        }

        private static bool IsBoltMissing(BikeAssemblyGraph assembly, string fastenerId)
        {
            FastenerState? f = assembly.GetFastener(fastenerId);
            return f != null && !f.IsInstalled;
        }

        private static void TightenBoltIfInstalled(BikeAssemblyGraph assembly, string fastenerId)
        {
            FastenerState? f = assembly.GetFastener(fastenerId);
            if (f != null && f.IsInstalled && !f.IsStripped)
            {
                assembly.TryTorqueFastener(fastenerId, f.TargetTorqueNm);
            }
        }

        private static void TightenBoltOrInstall(BikeAssemblyGraph assembly, string fastenerId)
        {
            FastenerState? f = assembly.GetFastener(fastenerId);
            if (f != null && !f.IsStripped)
            {
                assembly.TryTorqueFastener(fastenerId, f.TargetTorqueNm);
            }
        }
    }
}
