using System;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Workbench plug-chop station: pulls the plug reading from BikeSimulation.InspectSparkPlug() (Core decides color zone)
    /// and swaps plugs via the assembly graph. Event-driven only.
    /// </summary>
    public sealed class PlugChopInspector : MonoBehaviour
    {
        private GarageBikeContext _context;
        private SparkPlugReading _lastReading;
        private bool _hasReading;

        public event Action<SparkPlugReading> PlugInspected;

        public bool HasReading
        {
            get { return _hasReading; }
        }

        public SparkPlugReading LastReading
        {
            get { return _lastReading; }
        }

        public PlugColorZone LastColorZone
        {
            get { return _lastReading.ColorZone; }
        }

        public void Bind(GarageBikeContext context)
        {
            _context = context;
            _hasReading = false;
            _lastReading = default(SparkPlugReading);
        }

        public SparkPlugReading Inspect()
        {
            if (_context == null)
            {
                return default(SparkPlugReading);
            }

            _lastReading = _context.Bike.InspectSparkPlug();
            _hasReading = true;
            Action<SparkPlugReading> handler = PlugInspected;
            if (handler != null)
            {
                handler.Invoke(_lastReading);
            }

            return _lastReading;
        }

        /// <summary>
        /// Swaps the spark plug (e.g., hotter/colder heat range) through Core's assembly graph rules.
        /// </summary>
        public WrenchActionResult SwapPlug(PartDefinition plug)
        {
            if (_context == null || plug == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, SlotIds.SparkPlug, "No bike or plug selected.");
            }

            return _context.Publish(_context.Bike.TrySwapPart(SlotIds.SparkPlug, new PartInstance(plug)));
        }
    }
}
