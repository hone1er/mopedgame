using System;
using System.Collections.Generic;
using MopedSim.Core;

namespace MopedSim.Garage
{
    /// <summary>
    /// Maps BikeAssemblyGraph mount slot IDs to the parent part's SOCKET_* empty they attach to
    /// (PROJECT_GUIDE.md section 5.2 and data/catalogs/archetypes.json "attachSocket").
    /// The parent slot itself is always taken from Core's MountSlotSpec.ParentSlotId.
    /// MopedSim.App may override entries from catalog metadata; the defaults mirror the shipped catalog.
    /// </summary>
    public sealed class SlotSocketBindings
    {
        private readonly Dictionary<string, string> _attachSockets = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count
        {
            get { return _attachSockets.Count; }
        }

        /// <summary>
        /// Creates the canonical binding table for Core SlotIds (identical across the Maxer E50, Bramble PA50, and Targa A35 catalogs).
        /// </summary>
        public static SlotSocketBindings CreateDefault()
        {
            SlotSocketBindings b = new SlotSocketBindings();
            b.Set(SlotIds.ForkAssembly, "SOCKET_FORK_STEERER");
            b.Set(SlotIds.FrontWheel, "SOCKET_FRONT_AXLE");
            b.Set(SlotIds.FrontBrakeCable, "SOCKET_FRONT_AXLE");
            b.Set(SlotIds.Handlebars, "SOCKET_HANDLEBAR_CLAMP");
            b.Set(SlotIds.ThrottleCable, "SOCKET_GRIP_R");
            b.Set(SlotIds.Headlight, "SOCKET_HEADLIGHT_MOUNT");
            b.Set(SlotIds.FrontFender, "SOCKET_FENDER_FRONT");
            b.Set(SlotIds.RearSuspension, "SOCKET_SHOCK_TOP_L");
            b.Set(SlotIds.RearWheel, "SOCKET_REAR_AXLE");
            b.Set(SlotIds.Chain, "SOCKET_SPROCKET_MOUNT");
            b.Set(SlotIds.RearSprocket, "SOCKET_SPROCKET_MOUNT");
            b.Set(SlotIds.RearBrake, "SOCKET_WHEEL_HUB");
            b.Set(SlotIds.Seat, "SOCKET_SEAT_POST");
            b.Set(SlotIds.CargoRack, "SOCKET_CARGO_RACK");
            b.Set(SlotIds.FuelTank, "SOCKET_FUEL_TANK");
            b.Set(SlotIds.Petcock, "SOCKET_PETCOCK");
            b.Set(SlotIds.FuelLine, "SOCKET_PETCOCK");
            b.Set(SlotIds.InlineFilter, "SOCKET_PETCOCK");
            b.Set(SlotIds.EngineCradle, "SOCKET_ENGINE_MOUNT");
            b.Set(SlotIds.Ignition, "SOCKET_IGNITION_STATOR");
            b.Set(SlotIds.StatorPlate, "SOCKET_IGNITION_STATOR");
            b.Set(SlotIds.DrivetrainSide, "SOCKET_CLUTCH_OUTPUT");
            b.Set(SlotIds.DriveBelt, "SOCKET_DRIVE_OUTPUT");
            b.Set(SlotIds.FrontSprocket, "SOCKET_CLUTCH_OUTPUT");
            b.Set(SlotIds.BaseGasket, "SOCKET_CYLINDER_BASE");
            b.Set(SlotIds.Piston, "SOCKET_CYLINDER_BASE");
            b.Set(SlotIds.Cylinder, "SOCKET_CYLINDER_BASE");
            b.Set(SlotIds.HeadGasket, "SOCKET_HEAD_BASE");
            b.Set(SlotIds.CylinderHead, "SOCKET_HEAD_BASE");
            b.Set(SlotIds.SparkPlug, "SOCKET_SPARK_PLUG");
            b.Set(SlotIds.ExhaustGasket, "SOCKET_EXHAUST_PORT");
            b.Set(SlotIds.Exhaust, "SOCKET_EXHAUST_PORT");
            b.Set(SlotIds.IntakeGasket, "SOCKET_INTAKE_PORT");
            b.Set(SlotIds.IntakeManifold, "SOCKET_INTAKE_PORT");
            b.Set(SlotIds.Carburetor, "SOCKET_CARB_MOUNT");
            b.Set(SlotIds.MainJet, "SOCKET_AIR_FILTER");
            b.Set(SlotIds.AirFilter, "SOCKET_AIR_FILTER");

            // Catalog-only slot IDs (data/catalogs/archetypes.json) that have no Core SlotIds constant.
            b.Set("SLOT_FRONT_BRAKE", "SOCKET_WHEEL_HUB");
            b.Set("SLOT_REAR_BRAKE_CABLE", "SOCKET_REAR_AXLE");
            b.Set("SLOT_FLYWHEEL", "SOCKET_IGNITION_STATOR");
            b.Set("SLOT_PLUG_BOOT", "SOCKET_PLUG_BOOT");
            b.Set("SLOT_PILOT_JET", "SOCKET_AIR_FILTER");
            b.Set("SLOT_VARIATOR_ROLLERS", "SOCKET_DRIVE_OUTPUT");
            b.Set("SLOT_VARIATOR_BELT", "SOCKET_DRIVE_OUTPUT");
            return b;
        }

        public void Set(string slotId, string attachSocketName)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return;
            }

            if (string.IsNullOrEmpty(attachSocketName))
            {
                _attachSockets.Remove(slotId);
                return;
            }

            _attachSockets[slotId] = attachSocketName;
        }

        public bool TryGetAttachSocket(string slotId, out string attachSocketName)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                attachSocketName = string.Empty;
                return false;
            }

            return _attachSockets.TryGetValue(slotId, out attachSocketName);
        }
    }
}
