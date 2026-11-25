using System;
using System.Collections.Generic;
using System.IO;
using MopedSim.Core;
using MopedSim.Garage;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// Socket binding contract (PROJECT_GUIDE.md section 5.2): every catalog slot's attachSocket must exist on its parent's
    /// default part, and BikeVisualAssembler must parent each mounted part under exactly that parent's SOCKET_* transform.
    /// Part visuals are synthesized from catalog requiredSockets (the same names validate_assets.py verifies inside each .glb),
    /// so the check runs without glTF IO.
    /// </summary>
    public sealed class BikeVisualAssemblerSocketBindingTests
    {
        [Serializable]
        private sealed class PartEntry
        {
            public string id = string.Empty;
            public string[] requiredSockets = new string[0];
        }

        [Serializable]
        private sealed class PartsFile
        {
            public PartEntry[] parts = new PartEntry[0];
        }

        [Serializable]
        private sealed class SlotEntry
        {
            public string slotId = string.Empty;
            public string parentSlotId = string.Empty;
            public string attachSocket = string.Empty;
            public string defaultPartId = string.Empty;
        }

        [Serializable]
        private sealed class ArchetypeEntry
        {
            public string id = string.Empty;
            public SlotEntry[] mountSlots = new SlotEntry[0];
        }

        [Serializable]
        private sealed class ArchetypesFile
        {
            public ArchetypeEntry[] archetypes = new ArchetypeEntry[0];
        }

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        private static string CatalogPath(string file)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs", file));
        }

        private static Dictionary<string, PartEntry> LoadParts()
        {
            PartsFile file = JsonUtility.FromJson<PartsFile>(File.ReadAllText(CatalogPath("parts.json")));
            Dictionary<string, PartEntry> parts = new Dictionary<string, PartEntry>(StringComparer.Ordinal);
            for (int i = 0; i < file.parts.Length; i++)
            {
                parts[file.parts[i].id] = file.parts[i];
            }

            return parts;
        }

        private static ArchetypeEntry[] LoadArchetypes()
        {
            return JsonUtility.FromJson<ArchetypesFile>(File.ReadAllText(CatalogPath("archetypes.json"))).archetypes;
        }

        private static GameObject BuildFakePartVisual(PartEntry part)
        {
            GameObject visual = new GameObject(part != null ? part.id : "data_only_part");
            if (part == null)
            {
                return visual;
            }

            for (int i = 0; i < part.requiredSockets.Length; i++)
            {
                GameObject socket = new GameObject(part.requiredSockets[i]);
                socket.transform.SetParent(visual.transform, false);
                socket.transform.localPosition = new Vector3(0.01f * (i + 1), 0.02f * (i + 1), 0.03f * (i + 1));
            }

            return visual;
        }

        [Test]
        public void CatalogAttachSockets_ExistOnParentDefaultParts()
        {
            Dictionary<string, PartEntry> parts = LoadParts();
            ArchetypeEntry[] archetypes = LoadArchetypes();
            Assert.AreEqual(3, archetypes.Length);

            List<string> problems = new List<string>();
            for (int a = 0; a < archetypes.Length; a++)
            {
                Dictionary<string, SlotEntry> slots = new Dictionary<string, SlotEntry>(StringComparer.Ordinal);
                for (int s = 0; s < archetypes[a].mountSlots.Length; s++)
                {
                    slots[archetypes[a].mountSlots[s].slotId] = archetypes[a].mountSlots[s];
                }

                for (int s = 0; s < archetypes[a].mountSlots.Length; s++)
                {
                    SlotEntry slot = archetypes[a].mountSlots[s];
                    if (string.IsNullOrEmpty(slot.attachSocket))
                    {
                        continue;
                    }

                    SlotEntry parent;
                    PartEntry parentPart;
                    if (!slots.TryGetValue(slot.parentSlotId, out parent)
                        || !parts.TryGetValue(parent.defaultPartId, out parentPart)
                        || Array.IndexOf(parentPart.requiredSockets, slot.attachSocket) < 0)
                    {
                        problems.Add(archetypes[a].id + ":" + slot.slotId + " -> " + slot.attachSocket);
                    }
                }
            }

            Assert.IsEmpty(problems, "Attach sockets missing on parent parts:\n" + string.Join("\n", problems));
        }

        [Test]
        public void DefaultBindings_MatchMaxerE50CatalogAttachSockets()
        {
            SlotSocketBindings bindings = SlotSocketBindings.CreateDefault();
            ArchetypeEntry[] archetypes = LoadArchetypes();
            ArchetypeEntry e50 = null;
            for (int i = 0; i < archetypes.Length; i++)
            {
                if (archetypes[i].id == ArchetypeIds.MaxerE50)
                {
                    e50 = archetypes[i];
                }
            }

            Assert.IsNotNull(e50);
            for (int s = 0; s < e50.mountSlots.Length; s++)
            {
                SlotEntry slot = e50.mountSlots[s];
                if (string.IsNullOrEmpty(slot.attachSocket))
                {
                    continue;
                }

                string bound;
                Assert.IsTrue(bindings.TryGetAttachSocket(slot.slotId, out bound), "No default binding for " + slot.slotId);
                Assert.AreEqual(slot.attachSocket, bound, slot.slotId);
            }
        }

        [Test]
        public void Assembler_ParentsEveryCatalogSlotUnderItsParentsSocket()
        {
            Dictionary<string, PartEntry> parts = LoadParts();
            ArchetypeEntry[] archetypes = LoadArchetypes();
            for (int a = 0; a < archetypes.Length; a++)
            {
                ArchetypeEntry archetype = archetypes[a];
                _root = new GameObject("AssemblerRoot_" + archetype.id);
                BikeVisualAssembler assembler = _root.AddComponent<BikeVisualAssembler>();
                SlotSocketBindings bindings = SlotSocketBindings.CreateDefault();
                for (int s = 0; s < archetype.mountSlots.Length; s++)
                {
                    bindings.Set(archetype.mountSlots[s].slotId, archetype.mountSlots[s].attachSocket);
                }

                assembler.SetSocketBindings(bindings);

                for (int s = 0; s < archetype.mountSlots.Length; s++)
                {
                    SlotEntry slot = archetype.mountSlots[s];
                    PartEntry part;
                    parts.TryGetValue(slot.defaultPartId ?? string.Empty, out part);
                    assembler.MountPartImmediate(slot.slotId, slot.defaultPartId, slot.parentSlotId, BuildFakePartVisual(part));
                }

                for (int s = 0; s < archetype.mountSlots.Length; s++)
                {
                    SlotEntry slot = archetype.mountSlots[s];
                    GameObject mounted;
                    Assert.IsTrue(assembler.TryGetMountedPart(slot.slotId, out mounted), archetype.id + ":" + slot.slotId + " not mounted");
                    if (string.IsNullOrEmpty(slot.attachSocket))
                    {
                        Assert.AreSame(assembler.AssemblyRoot, mounted.transform.parent, slot.slotId + " root part should sit on the assembly root");
                        continue;
                    }

                    Transform expectedSocket;
                    Assert.IsTrue(assembler.TryGetSocket(slot.parentSlotId, slot.attachSocket, out expectedSocket),
                        archetype.id + ":" + slot.parentSlotId + " lacks " + slot.attachSocket);
                    Assert.IsTrue(assembler.IsBoundToSocket(slot.slotId), archetype.id + ":" + slot.slotId + " fell back off-socket");
                    Assert.AreSame(expectedSocket, mounted.transform.parent,
                        archetype.id + ":" + slot.slotId + " should hang off " + slot.parentSlotId + "/" + slot.attachSocket);
                }

                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        [Test]
        public void DuplicateSocketNames_OnSiblingWheels_DoNotCollide()
        {
            _root = new GameObject("AssemblerRoot_Wheels");
            BikeVisualAssembler assembler = _root.AddComponent<BikeVisualAssembler>();
            PartEntry frame = new PartEntry { id = "frame", requiredSockets = new[] { "SOCKET_FORK_STEERER", "SOCKET_REAR_AXLE" } };
            PartEntry fork = new PartEntry { id = "fork", requiredSockets = new[] { "SOCKET_FRONT_AXLE" } };
            PartEntry wheel = new PartEntry { id = "wheel", requiredSockets = new[] { "SOCKET_WHEEL_HUB", "SOCKET_SPROCKET_MOUNT", "BOLT_AXLE_L", "BOLT_AXLE_R" } };

            assembler.MountPartImmediate("SLOT_FRAME", "frame", string.Empty, BuildFakePartVisual(frame));
            assembler.MountPartImmediate(SlotIds.ForkAssembly, "fork", "SLOT_FRAME", BuildFakePartVisual(fork));
            assembler.MountPartImmediate(SlotIds.FrontWheel, "wheel", SlotIds.ForkAssembly, BuildFakePartVisual(wheel));
            assembler.MountPartImmediate(SlotIds.RearWheel, "wheel", "SLOT_FRAME", BuildFakePartVisual(wheel));
            assembler.MountPartImmediate("SLOT_FRONT_BRAKE", "brake", SlotIds.FrontWheel, null);
            assembler.MountPartImmediate(SlotIds.RearBrake, "brake", SlotIds.RearWheel, null);

            Transform frontHub;
            Transform rearHub;
            Assert.IsTrue(assembler.TryGetSocket(SlotIds.FrontWheel, "SOCKET_WHEEL_HUB", out frontHub));
            Assert.IsTrue(assembler.TryGetSocket(SlotIds.RearWheel, "SOCKET_WHEEL_HUB", out rearHub));
            Assert.AreNotSame(frontHub, rearHub);

            GameObject frontBrake;
            GameObject rearBrake;
            Assert.IsTrue(assembler.TryGetMountedPart("SLOT_FRONT_BRAKE", out frontBrake));
            Assert.IsTrue(assembler.TryGetMountedPart(SlotIds.RearBrake, out rearBrake));
            Assert.AreSame(frontHub, frontBrake.transform.parent);
            Assert.AreSame(rearHub, rearBrake.transform.parent);

            Transform leftAxleBolt;
            Assert.IsTrue(assembler.TryGetSocket(SlotIds.RearWheel, "BOLT_AXLE_L", out leftAxleBolt));
            Assert.IsTrue(leftAxleBolt.IsChildOf(rearBrake.transform.parent.parent));

            List<string> missing = new List<string>();
            Assert.AreEqual(0, assembler.CollectMissingSockets(SlotIds.FrontWheel, wheel.requiredSockets, missing));
            Assert.AreEqual(1, assembler.CollectMissingSockets(SlotIds.FrontWheel, new[] { "SOCKET_NOT_THERE" }, missing));
        }

        [Test]
        public void UnmountingParent_CascadesToChildSlots()
        {
            _root = new GameObject("AssemblerRoot_Cascade");
            BikeVisualAssembler assembler = _root.AddComponent<BikeVisualAssembler>();
            PartEntry cylinder = new PartEntry { id = "cyl", requiredSockets = new[] { "SOCKET_HEAD_BASE", "SOCKET_EXHAUST_PORT" } };
            PartEntry head = new PartEntry { id = "head", requiredSockets = new[] { "SOCKET_SPARK_PLUG", "BOLT_HEAD_NUT_1" } };

            assembler.MountPartImmediate(SlotIds.Cylinder, "cyl", string.Empty, BuildFakePartVisual(cylinder));
            assembler.MountPartImmediate(SlotIds.CylinderHead, "head", SlotIds.Cylinder, BuildFakePartVisual(head));
            assembler.MountPartImmediate(SlotIds.SparkPlug, "plug", SlotIds.CylinderHead, null);
            assembler.MountPartImmediate(SlotIds.Exhaust, "pipe", SlotIds.Cylinder, null);
            Assert.AreEqual(4, assembler.MountedPartCount);
            Assert.IsTrue(assembler.IsBoundToSocket(SlotIds.SparkPlug));

            Assert.IsTrue(assembler.UnmountPart(SlotIds.CylinderHead));
            Assert.AreEqual(2, assembler.MountedPartCount, "Removing the head must also unmount the spark plug visual.");
            GameObject plug;
            Assert.IsFalse(assembler.TryGetMountedPart(SlotIds.SparkPlug, out plug));

            assembler.ClearAllMountedParts();
            Assert.AreEqual(0, assembler.MountedPartCount);
        }
    }
}
