using System;
using MopedSim.Core;

namespace MopedSim.Garage
{
    /// <summary>
    /// Shared handle to the bike currently mounted on the garage work stand.
    /// Every garage station dispatches wrenching commands into this BikeSimulation (MopedSim.Core owns all rules)
    /// and raises AssemblyChanged so visuals/HUDs refresh event-driven instead of polling every frame.
    /// </summary>
    public sealed class GarageBikeContext
    {
        private readonly BikeSimulation _bike;
        private readonly ISimRandom _random;

        public GarageBikeContext(BikeSimulation bike, ISimRandom random)
        {
            if (bike == null)
            {
                throw new ArgumentNullException(nameof(bike));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            _bike = bike;
            _random = random;
        }

        /// <summary>
        /// Raised after any successful or attempted mutation of the bike's assembly graph, fasteners, carb, or fuel.
        /// </summary>
        public event Action AssemblyChanged;

        /// <summary>
        /// Raised after every wrench/install/swap command with Core's structured result.
        /// </summary>
        public event Action<WrenchActionResult> WrenchActionCompleted;

        public BikeSimulation Bike
        {
            get { return _bike; }
        }

        public ISimRandom Random
        {
            get { return _random; }
        }

        public void NotifyAssemblyChanged()
        {
            Action handler = AssemblyChanged;
            if (handler != null)
            {
                handler.Invoke();
            }
        }

        /// <summary>
        /// Publishes a Core wrench result and signals an assembly refresh.
        /// </summary>
        public WrenchActionResult Publish(WrenchActionResult result)
        {
            Action<WrenchActionResult> handler = WrenchActionCompleted;
            if (handler != null)
            {
                handler.Invoke(result);
            }

            NotifyAssemblyChanged();
            return result;
        }
    }
}
