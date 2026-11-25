using System;
using System.Collections.Generic;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Workbench carb tuning: main jet swaps (catalog MainJet parts via BikeSimulation.TrySwapPart on SLOT_MAIN_JET),
    /// pilot jet swaps, needle clip, and idle screw adjustments written onto the installed carburetor PartInstance overrides.
    /// AFR/combustion effects are computed solely by MopedSim.Core on the next Step.
    /// </summary>
    public sealed class CarbWorkbenchController : MonoBehaviour
    {
        private readonly List<PartDefinition> _mainJets = new List<PartDefinition>(24);
        private readonly List<PartDefinition> _pilotJets = new List<PartDefinition>(8);
        private GarageBikeContext _context;

        public event Action<WrenchActionResult> CarbAdjusted;

        public IReadOnlyList<PartDefinition> AvailableMainJets
        {
            get { return _mainJets; }
        }

        public IReadOnlyList<PartDefinition> AvailablePilotJets
        {
            get { return _pilotJets; }
        }

        public void Bind(GarageBikeContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Supplies the jet inventory (catalog parts with PartCategory.MainJet / PilotJet), typically from MopedSim.Data.
        /// </summary>
        public void SetJetCatalog(IReadOnlyList<PartDefinition> parts)
        {
            _mainJets.Clear();
            _pilotJets.Clear();
            if (parts == null)
            {
                return;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                PartDefinition p = parts[i];
                if (p == null)
                {
                    continue;
                }

                if (p.Category == PartCategory.MainJet)
                {
                    _mainJets.Add(p);
                }
                else if (p.Category == PartCategory.PilotJet)
                {
                    _pilotJets.Add(p);
                }
            }
        }

        public PartDefinition FindMainJetBySize(int mainJetSize)
        {
            for (int i = 0; i < _mainJets.Count; i++)
            {
                if (_mainJets[i].Stats.MainJetSize == mainJetSize)
                {
                    return _mainJets[i];
                }
            }

            return null;
        }

        public PartDefinition FindPilotJetBySize(int pilotJetSize)
        {
            for (int i = 0; i < _pilotJets.Count; i++)
            {
                if (_pilotJets[i].Stats.PilotJetSize == pilotJetSize)
                {
                    return _pilotJets[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Installed main jet size as reported by the mounted jet part, else the carb override/default (display only).
        /// </summary>
        public int CurrentMainJetSize
        {
            get
            {
                if (_context == null)
                {
                    return 0;
                }

                PartInstance jet = _context.Bike.Assembly.GetInstalledPart(SlotIds.MainJet);
                if (jet != null && jet.Definition.Stats.MainJetSize > 0)
                {
                    return jet.Definition.Stats.MainJetSize;
                }

                PartInstance carb = _context.Bike.Assembly.GetInstalledPart(SlotIds.Carburetor);
                if (carb == null)
                {
                    return 0;
                }

                return carb.MainJetOverride > 0 ? carb.MainJetOverride : carb.Definition.Stats.MainJetSize;
            }
        }

        public WrenchActionResult SwapMainJet(int mainJetSize)
        {
            PartDefinition jet = FindMainJetBySize(mainJetSize);
            if (jet == null)
            {
                return Publish(WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, SlotIds.MainJet, "No #" + mainJetSize + " main jet in the parts crate."));
            }

            return SwapMainJet(jet);
        }

        /// <summary>
        /// Swaps the main jet through Core's assembly graph. Archetypes without a SLOT_MAIN_JET fall back to the carb override.
        /// </summary>
        public WrenchActionResult SwapMainJet(PartDefinition mainJet)
        {
            if (_context == null || mainJet == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, SlotIds.MainJet, "No bike or jet selected.");
            }

            BikeSimulation bike = _context.Bike;
            if (bike.Assembly.HasSlot(SlotIds.MainJet))
            {
                return Publish(bike.TrySwapPart(SlotIds.MainJet, new PartInstance(mainJet)));
            }

            PartInstance carb = bike.Assembly.GetInstalledPart(SlotIds.Carburetor);
            if (carb == null)
            {
                return Publish(WrenchActionResult.Fail(WrenchActionStatus.SlotEmpty, SlotIds.Carburetor, "Install a carburetor before jetting."));
            }

            carb.MainJetOverride = mainJet.Stats.MainJetSize;
            return Publish(WrenchActionResult.Ok(SlotIds.Carburetor, "Carb main jet set to #" + mainJet.Stats.MainJetSize + "."));
        }

        public WrenchActionResult SwapPilotJet(PartDefinition pilotJet)
        {
            if (pilotJet == null || pilotJet.Stats.PilotJetSize <= 0)
            {
                return Publish(WrenchActionResult.Fail(WrenchActionStatus.InvalidSpec, SlotIds.Carburetor, "Select a pilot jet."));
            }

            PartInstance carb;
            WrenchActionResult missing;
            if (!TryGetCarb(out carb, out missing))
            {
                return missing;
            }

            carb.PilotJetOverride = pilotJet.Stats.PilotJetSize;
            return Publish(WrenchActionResult.Ok(SlotIds.Carburetor, "Pilot jet set to #" + pilotJet.Stats.PilotJetSize + "."));
        }

        public WrenchActionResult SetNeedleClip(int clipPosition)
        {
            if (clipPosition <= 0)
            {
                return Publish(WrenchActionResult.Fail(WrenchActionStatus.InvalidSpec, SlotIds.Carburetor, "Needle clip position must be 1 or higher."));
            }

            PartInstance carb;
            WrenchActionResult missing;
            if (!TryGetCarb(out carb, out missing))
            {
                return missing;
            }

            carb.NeedleClipOverride = clipPosition;
            return Publish(WrenchActionResult.Ok(SlotIds.Carburetor, "Needle clip moved to position " + clipPosition + "."));
        }

        public WrenchActionResult SetIdleScrewTurns(float turns)
        {
            if (turns <= 0f)
            {
                return Publish(WrenchActionResult.Fail(WrenchActionStatus.InvalidSpec, SlotIds.Carburetor, "Idle screw must be open at least a fraction of a turn."));
            }

            PartInstance carb;
            WrenchActionResult missing;
            if (!TryGetCarb(out carb, out missing))
            {
                return missing;
            }

            carb.IdleScrewTurnsOverride = turns;
            return Publish(WrenchActionResult.Ok(SlotIds.Carburetor, "Idle screw set to " + turns.ToString("0.00") + " turns out."));
        }

        private bool TryGetCarb(out PartInstance carb, out WrenchActionResult failure)
        {
            carb = _context != null ? _context.Bike.Assembly.GetInstalledPart(SlotIds.Carburetor) : null;
            if (carb == null)
            {
                failure = Publish(WrenchActionResult.Fail(WrenchActionStatus.SlotEmpty, SlotIds.Carburetor, "Install a carburetor first."));
                return false;
            }

            failure = default(WrenchActionResult);
            return true;
        }

        private WrenchActionResult Publish(WrenchActionResult result)
        {
            Action<WrenchActionResult> handler = CarbAdjusted;
            if (handler != null)
            {
                handler.Invoke(result);
            }

            return _context != null ? _context.Publish(result) : result;
        }
    }
}
