using System.Collections.Generic;
using MopedSim.Core;

namespace MopedSim.Data
{
    /// <summary>
    /// The slot and fastener IDs that MopedSim.Core runtime code (BikeSimulation, BikeAssemblyGraph leak model, FaultEngine)
    /// reads by constant. Catalog IDs are used verbatim; <see cref="CatalogLoader.Validate"/> fails if an archetype is missing
    /// any of these or mounts the wrong Core category in them, so every validated archetype can drive BikeSimulation fully.
    /// <para>
    /// Every SlotIds/FastenerIds constant uses the canonical catalog + Blender string (SLOT_FRAME, SLOT_VARIATOR_BELT,
    /// SLOT_FLYWHEEL, BOLT_REAR_AXLE_L/R, ...) and is mounted by at least one catalog archetype; constants that only some
    /// archetypes declare (e.g. SlotIds.VariatorBelt) are not required here. MopedSim.Data.Tests enforces both directions.
    /// </para>
    /// </summary>
    public static class CoreIdContract
    {
        private static readonly KeyValuePair<string, PartCategory>[] RequiredSlotCategories =
        {
            new KeyValuePair<string, PartCategory>(SlotIds.SparkPlug, PartCategory.SparkPlug),
            new KeyValuePair<string, PartCategory>(SlotIds.Ignition, PartCategory.Ignition),
            new KeyValuePair<string, PartCategory>(SlotIds.FuelTank, PartCategory.FuelTank),
            new KeyValuePair<string, PartCategory>(SlotIds.Cylinder, PartCategory.Cylinder),
            new KeyValuePair<string, PartCategory>(SlotIds.Piston, PartCategory.Piston),
            new KeyValuePair<string, PartCategory>(SlotIds.CylinderHead, PartCategory.CylinderHead),
            new KeyValuePair<string, PartCategory>(SlotIds.Carburetor, PartCategory.Carburetor),
            new KeyValuePair<string, PartCategory>(SlotIds.IntakeManifold, PartCategory.IntakeManifold),
            new KeyValuePair<string, PartCategory>(SlotIds.AirFilter, PartCategory.AirFilter),
            new KeyValuePair<string, PartCategory>(SlotIds.MainJet, PartCategory.MainJet),
            new KeyValuePair<string, PartCategory>(SlotIds.Exhaust, PartCategory.Exhaust),
            new KeyValuePair<string, PartCategory>(SlotIds.Handlebars, PartCategory.Handlebars),
            new KeyValuePair<string, PartCategory>(SlotIds.BaseGasket, PartCategory.Gasket),
            new KeyValuePair<string, PartCategory>(SlotIds.IntakeGasket, PartCategory.Gasket),
            new KeyValuePair<string, PartCategory>(SlotIds.HeadGasket, PartCategory.Gasket),
            new KeyValuePair<string, PartCategory>(SlotIds.ExhaustGasket, PartCategory.Gasket),
        };

        private static readonly KeyValuePair<string, PartCategory>[] ChainDriveSlotCategories =
        {
            new KeyValuePair<string, PartCategory>(SlotIds.FrontSprocket, PartCategory.SprocketFront),
            new KeyValuePair<string, PartCategory>(SlotIds.RearSprocket, PartCategory.SprocketRear),
        };

        private static readonly string[] RequiredFasteners =
        {
            FastenerIds.HeadNut1,
            FastenerIds.HeadNut2,
            FastenerIds.HeadNut3,
            FastenerIds.HeadNut4,
            FastenerIds.Intake1,
            FastenerIds.Intake2,
            FastenerIds.CarbClamp,
            FastenerIds.ExhaustFlange1,
            FastenerIds.ExhaustFlange2,
            FastenerIds.ExhaustFrameBracket,
        };

        /// <summary>Slots every archetype must declare, with the Core category Core expects mounted there.</summary>
        public static IReadOnlyList<KeyValuePair<string, PartCategory>> RequiredSlots
        {
            get { return RequiredSlotCategories; }
        }

        /// <summary>Extra slots required for chain-final-drive archetypes (anything but <see cref="DrivetrainType.VariatedBelt"/>).</summary>
        public static IReadOnlyList<KeyValuePair<string, PartCategory>> ChainDriveSlots
        {
            get { return ChainDriveSlotCategories; }
        }

        /// <summary>Fasteners the Core air/exhaust leak model and FaultEngine repairs address by constant.</summary>
        public static IReadOnlyList<string> RequiredFastenerIds
        {
            get { return RequiredFasteners; }
        }

        /// <summary>Core category the drivetrain-side slot must accept for a given archetype drivetrain.</summary>
        public static PartCategory DrivetrainSideCategory(DrivetrainType drivetrain)
        {
            switch (drivetrain)
            {
                case DrivetrainType.VariatedBelt:
                    return PartCategory.Variator;
                case DrivetrainType.TwoSpeedAuto:
                    return PartCategory.Gearbox;
                default:
                    return PartCategory.Clutch;
            }
        }
    }
}
