using MopedSim.App;
using MopedSim.Core;
using MopedSim.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// M0 exit check (PROJECT_GUIDE.md section 7): MopedSim.Core is callable from MopedSim.App inside Unity.
    /// </summary>
    public sealed class CoreCallableFromAppTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void CompositionRoot_BootstrapsWorld_AndStepsCoreSimulation()
        {
            _root = new GameObject("AppRoot_M0");
            GameSessionStateMachine session = _root.AddComponent<GameSessionStateMachine>();
            AppCompositionRoot app = _root.AddComponent<AppCompositionRoot>();
            app.BootstrapOnStart = false;
            app.AssignCoreComponents(null, session, null, null);

            SimulationWorld world = new SimulationWorld(new SeededSimRandom(42u));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50));
            app.BootstrapWithWorld(world, null);

            Assert.IsTrue(app.IsBootstrapped);
            Assert.AreSame(world.ActiveBike, app.ActiveBike);
            Assert.IsNotNull(app.GarageContext);

            RideInputState input = new RideInputState(0.6f);
            EngineTelemetrySnapshot telemetry = default(EngineTelemetrySnapshot);
            for (int i = 0; i < 120; i++)
            {
                telemetry = app.World.Step(1f / 60f, in input);
            }

            Assert.IsTrue(telemetry.IsRunning, "Engine should run after 2 simulated seconds at 60% throttle.");
            Assert.Greater(telemetry.Rpm, 1000f);
            Assert.Greater(telemetry.EffectiveAfr, 0f);
            Assert.AreEqual(2f, app.World.TotalElapsedSeconds, 0.001f);
        }

        [Test]
        public void SameSeedAndInputs_ProduceBitIdenticalTelemetry_InsideUnity()
        {
            BikeSimulation a = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation b = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            SeededSimRandom rngA = new SeededSimRandom(9001u);
            SeededSimRandom rngB = new SeededSimRandom(9001u);
            RideInputState input = new RideInputState(1f);

            for (int i = 0; i < 1200; i++)
            {
                a.Step(1f / 60f, in input, rngA);
                b.Step(1f / 60f, in input, rngB);
            }

            Assert.AreEqual(a.LatestTelemetry.Rpm, b.LatestTelemetry.Rpm);
            Assert.AreEqual(a.LatestTelemetry.ChtCelsius, b.LatestTelemetry.ChtCelsius);
            Assert.AreEqual(a.LatestTelemetry.VehicleSpeedMps, b.LatestTelemetry.VehicleSpeedMps);
            Assert.AreEqual(a.LatestTelemetry.EffectiveAfr, b.LatestTelemetry.EffectiveAfr);
        }
    }
}
