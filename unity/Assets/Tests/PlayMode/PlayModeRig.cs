using MopedSim.Core;
using MopedSim.Ride;
using UnityEngine;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// Builds deterministic, scripted-physics ride rigs: a collider-free rigid body driven by MopedBikeController.SimulateStep
    /// and advanced with Physics.Simulate, so scenarios never depend on frame timing.
    /// </summary>
    internal static class PlayModeRig
    {
        public const float Dt = 1f / 60f;
        public const float MpsToMph = 2.23693629f;

        // Suspension equilibrium for the stock 127 kg rig: wheel mounts sit ~0.42 m above the road surface.
        public const float RideHeightM = 0.42f;

        public static GameObject CreateGround(string name, Vector3 center, Quaternion rotation, Vector3 size)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = name;
            ground.transform.SetPositionAndRotation(center, rotation);
            ground.transform.localScale = size;
            return ground;
        }

        /// <summary>
        /// Creates a ride rig whose wheel mounts rest on the surface whose top passes through surfacePoint with the given up/forward.
        /// Rotation is frozen so scenarios isolate longitudinal / lateral dynamics from lean balance.
        /// </summary>
        public static MopedBikeController CreateRideRig(string name, BikeSimulation sim, uint seed, Vector3 surfacePoint, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            Vector3 up = rotation * Vector3.up;
            go.transform.SetPositionAndRotation(surfacePoint + up * RideHeightM, rotation);

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.inertiaTensor = new Vector3(18f, 16f, 5f);
            rb.inertiaTensorRotation = Quaternion.identity;

            MopedBikeController ride = go.AddComponent<MopedBikeController>();
            ride.AutoStepInFixedUpdate = false;
            AeroTuckController aero = go.AddComponent<AeroTuckController>();

            GameObject front = new GameObject("FrontWheelMount");
            front.transform.SetParent(go.transform, false);
            front.transform.localPosition = new Vector3(0f, 0f, 0.58f);
            GameObject rear = new GameObject("RearWheelMount");
            rear.transform.SetParent(go.transform, false);
            rear.transform.localPosition = new Vector3(0f, 0f, -0.58f);

            ride.SetMounts(front.transform, rear.transform);
            ride.SetAero(aero);
            ride.Bind(sim, new SeededSimRandom(seed));
            // Controller init enables interpolation for rendering; scripted Physics.Simulate loops need transforms in sync.
            rb.interpolation = RigidbodyInterpolation.None;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            return ride;
        }

        /// <summary>
        /// Advances every rig one tick, then integrates physics once.
        /// </summary>
        public static void Step(MopedBikeController a, MopedBikeController b)
        {
            if (a != null)
            {
                a.SimulateStep(Dt);
            }

            if (b != null)
            {
                b.SimulateStep(Dt);
            }

            Physics.Simulate(Dt);
        }
    }
}
