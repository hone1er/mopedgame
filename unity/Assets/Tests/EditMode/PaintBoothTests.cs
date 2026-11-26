using System;
using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Garage;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// Verifies PaintBoothController curated swatches, hex parsing/normalization, patina &amp; metallic clamping,
    /// allocation-free MaterialPropertyBlock updates without material instantiation, BikeVisualAssembler binding,
    /// BikeSaveState persistence, and PaintChanged events.
    /// </summary>
    public sealed class PaintBoothTests
    {
        private GameObject _root;
        private PaintBoothController _booth;
        private Material _sharedMaterial;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PaintBoothTestRoot");
            _booth = _root.AddComponent<PaintBoothController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }

            if (_sharedMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_sharedMaterial);
                _sharedMaterial = null;
            }
        }

        [Test]
        public void CuratedSwatches_ContainExpectedVintagePresets_AndLookupWorks()
        {
            IReadOnlyList<PaintSwatch> swatches = PaintBoothController.Swatches;
            Assert.AreEqual(7, swatches.Count);

            AssertSwatch("little_yellow", "Little Yellow", "#E8B923");
            AssertSwatch("magnum_red", "Magnum Red", "#C42828");
            AssertSwatch("classic_silver", "Classic Silver", "#C0C4C8");
            AssertSwatch("gloss_black", "Gloss Black", "#1E2024");
            AssertSwatch("vintage_grey", "Vintage Grey", "#7A8088");
            AssertSwatch("matte_petg_charcoal", "Matte PETG Charcoal", "#2B2E33");
            AssertSwatch("bay_teal", "Bay Teal", "#1F7A8C");

            PaintSwatch caseInsensitive;
            Assert.IsTrue(PaintBoothController.TryGetSwatch("LITTLE_YELLOW", out caseInsensitive));
            Assert.AreEqual("#E8B923", caseInsensitive.Hex);

            PaintSwatch missing;
            Assert.IsFalse(PaintBoothController.TryGetSwatch("does_not_exist", out missing));
            Assert.IsFalse(PaintBoothController.TryGetSwatch(string.Empty, out missing));
            Assert.IsFalse(PaintBoothController.TryGetSwatch(null, out missing));
        }

        [Test]
        public void ApplySwatch_UpdatesFinishAndRejectsUnknownId()
        {
            PaintSwatch silver;
            Assert.IsTrue(PaintBoothController.TryGetSwatch("classic_silver", out silver));

            Assert.IsTrue(_booth.ApplySwatch("classic_silver"));
            Assert.AreEqual("#C0C4C8", _booth.CurrentPaintHex);
            Assert.AreEqual(silver.DefaultMetallic, _booth.MetallicFlake, 1e-5f);
            Assert.AreEqual(silver.DefaultSmoothness, _booth.BaseSmoothness, 1e-5f);

            PaintSwatch matte;
            Assert.IsTrue(PaintBoothController.TryGetSwatch("matte_petg_charcoal", out matte));
            Assert.IsTrue(_booth.ApplySwatch("matte_petg_charcoal"));
            Assert.AreEqual("#2B2E33", _booth.CurrentPaintHex);
            Assert.Less(_booth.BaseSmoothness, silver.DefaultSmoothness);
            Assert.AreEqual(matte.DefaultSmoothness, _booth.BaseSmoothness, 1e-5f);

            int eventCalls = 0;
            _booth.PaintChanged += delegate { eventCalls++; };

            Assert.IsFalse(_booth.ApplySwatch("nonexistent_swatch"));
            Assert.AreEqual("#2B2E33", _booth.CurrentPaintHex);
            Assert.AreEqual(0, eventCalls);
        }

        [Test]
        public void TryParsePaintHex_NormalizesValidInput_AndRejectsInvalidInput()
        {
            string normalized;
            Color color;

            Assert.IsTrue(PaintBoothController.TryParsePaintHex("e8b923", out normalized, out color));
            Assert.AreEqual("#E8B923", normalized);
            Assert.AreEqual(0xE8 / 255f, color.r, 1e-4f);
            Assert.AreEqual(0xB9 / 255f, color.g, 1e-4f);
            Assert.AreEqual(0x23 / 255f, color.b, 1e-4f);
            Assert.AreEqual(1f, color.a, 1e-5f);

            Assert.IsTrue(PaintBoothController.TryParsePaintHex("  #1f7a8c ", out normalized, out color));
            Assert.AreEqual("#1F7A8C", normalized);
            Assert.AreEqual(0x1F / 255f, color.r, 1e-4f);
            Assert.AreEqual(0x7A / 255f, color.g, 1e-4f);
            Assert.AreEqual(0x8C / 255f, color.b, 1e-4f);

            Assert.IsFalse(PaintBoothController.TryParsePaintHex(null, out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex(string.Empty, out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("   ", out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("#12345", out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("#1234567", out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("#12345678", out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("##E8B923", out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("#GGGGGG", out normalized, out color));
            Assert.IsFalse(PaintBoothController.TryParsePaintHex("red", out normalized, out color));
        }

        [Test]
        public void PatinaAndMetallic_AreClamped_AndHighPatinaAttenuatesSmoothness()
        {
            Assert.IsTrue(_booth.ApplySwatch("little_yellow"));
            float baseSmoothness = _booth.BaseSmoothness;
            Assert.Greater(baseSmoothness, 0.5f);

            Assert.IsTrue(_booth.SetPaint("e8b923", -0.5f, -2f));
            Assert.AreEqual(0f, _booth.PatinaLevel, 1e-6f);
            Assert.AreEqual(0f, _booth.MetallicFlake, 1e-6f);
            float cleanSmoothness = _booth.EffectiveSmoothness;
            Assert.AreEqual(baseSmoothness, cleanSmoothness, 1e-5f);

            Assert.IsTrue(_booth.SetPaint("#E8B923", 2.5f, 4.2f));
            Assert.AreEqual(1f, _booth.PatinaLevel, 1e-6f);
            Assert.AreEqual(1f, _booth.MetallicFlake, 1e-6f);
            float weatheredSmoothness = _booth.EffectiveSmoothness;
            Assert.Less(weatheredSmoothness, cleanSmoothness);

            Assert.IsTrue(_booth.SetPaint("#E8B923", float.NaN, float.PositiveInfinity));
            Assert.AreEqual(0f, _booth.PatinaLevel, 1e-6f);
            Assert.AreEqual(1f, _booth.MetallicFlake, 1e-6f);
        }

        [Test]
        public void MaterialPropertyBlock_UpdatesRendererWithoutMutatingSharedMaterial()
        {
            MeshRenderer renderer = CreateTempPaintedRenderer("PaintedFrame");
            Color originalMatBaseColor = _sharedMaterial.HasProperty(PaintBoothController.BaseColorPropertyId)
                ? _sharedMaterial.GetColor(PaintBoothController.BaseColorPropertyId)
                : Color.white;
            float originalMatMetallic = _sharedMaterial.HasProperty(PaintBoothController.MetallicPropertyId)
                ? _sharedMaterial.GetFloat(PaintBoothController.MetallicPropertyId)
                : 0f;

            _booth.SetRendererRoot(renderer.transform);
            Assert.IsTrue(_booth.ApplySwatch("little_yellow"));
            Assert.IsTrue(_booth.SetPaint("e8b923", 0f, 0.42f));

            MaterialPropertyBlock readBack = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(readBack);

            Color expectedClean = PaintBoothController.ComputeEffectiveColor(_booth.CurrentBaseColor, 0f);
            Color appliedBase = readBack.GetColor(PaintBoothController.BaseColorPropertyId);
            Color appliedColor = readBack.GetColor(PaintBoothController.ColorPropertyId);
            float appliedMetallic = readBack.GetFloat(PaintBoothController.MetallicPropertyId);
            float appliedCleanSmoothness = readBack.GetFloat(PaintBoothController.SmoothnessPropertyId);

            AssertColorApprox(expectedClean, appliedBase);
            AssertColorApprox(expectedClean, appliedColor);
            Assert.AreEqual(0.42f, appliedMetallic, 1e-5f);
            Assert.AreEqual(_booth.BaseSmoothness, appliedCleanSmoothness, 1e-5f);

            // Increasing patina attenuates smoothness and weathers base color toward rust-oxide tint.
            Assert.IsTrue(_booth.SetPaint("#E8B923", 0.85f, 0.42f));
            renderer.GetPropertyBlock(readBack);
            Color appliedWeathered = readBack.GetColor(PaintBoothController.BaseColorPropertyId);
            float appliedWeatheredSmoothness = readBack.GetFloat(PaintBoothController.SmoothnessPropertyId);

            AssertColorApprox(_booth.EffectiveColor, appliedWeathered);
            Assert.Less(appliedWeatheredSmoothness, appliedCleanSmoothness);
            Assert.Greater(Vector4.Distance(ToVector4(appliedBase), ToVector4(appliedWeathered)), 0.05f);

            // Verify sharedMaterial reference was never cloned/replaced or mutated.
            Assert.AreSame(_sharedMaterial, renderer.sharedMaterial);
            if (_sharedMaterial.HasProperty(PaintBoothController.BaseColorPropertyId))
            {
                AssertColorApprox(originalMatBaseColor, _sharedMaterial.GetColor(PaintBoothController.BaseColorPropertyId));
            }

            if (_sharedMaterial.HasProperty(PaintBoothController.MetallicPropertyId))
            {
                Assert.AreEqual(originalMatMetallic, _sharedMaterial.GetFloat(PaintBoothController.MetallicPropertyId), 1e-5f);
            }
        }

        [Test]
        public void BindAssembler_PaintsExistingAndNewlyMountedParts()
        {
            GameObject assemblerGo = new GameObject("Assembler");
            assemblerGo.transform.SetParent(_root.transform, false);
            BikeVisualAssembler assembler = assemblerGo.AddComponent<BikeVisualAssembler>();

            MeshRenderer frameRenderer = CreateTempPaintedRenderer("FrameVisual");
            frameRenderer.transform.SetParent(null, false);
            assembler.MountPartImmediate(SlotIds.Frame, "frame_maxer_e50", string.Empty, frameRenderer.gameObject);

            _booth.Bind(assembler);
            Assert.IsTrue(_booth.ApplySwatch("bay_teal"));

            MaterialPropertyBlock readBack = new MaterialPropertyBlock();
            frameRenderer.GetPropertyBlock(readBack);
            AssertColorApprox(_booth.EffectiveColor, readBack.GetColor(PaintBoothController.BaseColorPropertyId));

            // Mounting a new part after binding must automatically receive the active paint finish.
            MeshRenderer tankRenderer = CreateTempPaintedRenderer("TankVisual");
            tankRenderer.transform.SetParent(null, false);
            assembler.MountPartImmediate(SlotIds.FuelTank, "tank_stock", SlotIds.Frame, tankRenderer.gameObject);

            tankRenderer.GetPropertyBlock(readBack);
            AssertColorApprox(_booth.EffectiveColor, readBack.GetColor(PaintBoothController.BaseColorPropertyId));
            Assert.AreSame(_sharedMaterial, tankRenderer.sharedMaterial);
        }

        [Test]
        public void IsPaintableBodySlot_MatchesOnlyFrameFuelTankAndFrontFender()
        {
            Assert.IsTrue(PaintBoothController.IsPaintableBodySlot(SlotIds.Frame));
            Assert.IsTrue(PaintBoothController.IsPaintableBodySlot(SlotIds.FuelTank));
            Assert.IsTrue(PaintBoothController.IsPaintableBodySlot(SlotIds.FrontFender));

            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(null));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(string.Empty));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(SlotIds.RearWheel));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(SlotIds.FrontWheel));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(SlotIds.Exhaust));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(SlotIds.CylinderHead));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(SlotIds.Carburetor));
            Assert.IsFalse(PaintBoothController.IsPaintableBodySlot(SlotIds.Handlebars));
        }

        [Test]
        public void BindAssembler_PaintsOnlyBodySlots_AndLeavesRearWheelAndExhaustUntinted()
        {
            GameObject assemblerGo = new GameObject("AssemblerBodyFilter");
            assemblerGo.transform.SetParent(_root.transform, false);
            BikeVisualAssembler assembler = assemblerGo.AddComponent<BikeVisualAssembler>();

            MeshRenderer frameRenderer = CreateTempPaintedRenderer("FrameVisual");
            frameRenderer.transform.SetParent(null, false);
            MeshRenderer tankRenderer = CreateTempPaintedRenderer("TankVisual");
            tankRenderer.transform.SetParent(null, false);
            MeshRenderer fenderRenderer = CreateTempPaintedRenderer("FrontFenderVisual");
            fenderRenderer.transform.SetParent(null, false);
            MeshRenderer rearWheelRenderer = CreateTempPaintedRenderer("RearWheelVisual");
            rearWheelRenderer.transform.SetParent(null, false);
            MeshRenderer exhaustRenderer = CreateTempPaintedRenderer("ExhaustVisual");
            exhaustRenderer.transform.SetParent(null, false);

            assembler.MountPartImmediate(SlotIds.Frame, "frame_maxer_e50", string.Empty, frameRenderer.gameObject);
            assembler.MountPartImmediate(SlotIds.FuelTank, "tank_stock", SlotIds.Frame, tankRenderer.gameObject);
            assembler.MountPartImmediate(SlotIds.FrontFender, "fender_front_stock", SlotIds.Frame, fenderRenderer.gameObject);
            assembler.MountPartImmediate(SlotIds.RearWheel, "wheel_rear_16", SlotIds.Frame, rearWheelRenderer.gameObject);
            assembler.MountPartImmediate(SlotIds.Exhaust, "exhaust_stock", SlotIds.Frame, exhaustRenderer.gameObject);

            _booth.Bind(assembler);
            Assert.IsTrue(_booth.ApplySwatch("bay_teal"));

            MaterialPropertyBlock readBack = new MaterialPropertyBlock();
            frameRenderer.GetPropertyBlock(readBack);
            AssertColorApprox(_booth.EffectiveColor, readBack.GetColor(PaintBoothController.BaseColorPropertyId));

            tankRenderer.GetPropertyBlock(readBack);
            AssertColorApprox(_booth.EffectiveColor, readBack.GetColor(PaintBoothController.BaseColorPropertyId));

            fenderRenderer.GetPropertyBlock(readBack);
            AssertColorApprox(_booth.EffectiveColor, readBack.GetColor(PaintBoothController.BaseColorPropertyId));

            Assert.IsFalse(rearWheelRenderer.HasPropertyBlock(), "RearWheel must not receive frame paint tint.");
            Assert.IsFalse(exhaustRenderer.HasPropertyBlock(), "Exhaust must not receive frame paint tint.");

            // Newly mounted non-body parts after binding must also remain untinted.
            MeshRenderer barsRenderer = CreateTempPaintedRenderer("HandlebarsVisual");
            barsRenderer.transform.SetParent(null, false);
            assembler.MountPartImmediate(SlotIds.Handlebars, "bars_stock", SlotIds.Frame, barsRenderer.gameObject);
            Assert.IsFalse(barsRenderer.HasPropertyBlock(), "Handlebars mounted after bind must not receive frame paint tint.");
        }

        [Test]
        public void ApplyToSave_AndPaintChangedEvent_ReportNormalizedValues()
        {
            string lastHex = string.Empty;
            float lastPatina = -1f;
            float lastMetallic = -1f;
            int eventCount = 0;

            _booth.PaintChanged += delegate(string hex, float patina, float metallic)
            {
                lastHex = hex;
                lastPatina = patina;
                lastMetallic = metallic;
                eventCount++;
            };

            Assert.IsTrue(_booth.SetPaint("1f7a8c", 0.35f, 0.25f));
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual("#1F7A8C", lastHex);
            Assert.AreEqual(0.35f, lastPatina, 1e-5f);
            Assert.AreEqual(0.25f, lastMetallic, 1e-5f);

            // Invalid hex must not mutate state or fire PaintChanged.
            Assert.IsFalse(_booth.SetPaint("not-a-hex", 0.9f, 0.9f));
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual("#1F7A8C", _booth.CurrentPaintHex);

            BikeSaveState save = new BikeSaveState();
            _booth.ApplyToSave(save);
            Assert.AreEqual("#1F7A8C", save.PaintHex);
            Assert.AreEqual(0.35f, save.PatinaLevel, 1e-5f);

            Assert.Throws<ArgumentNullException>(delegate { _booth.ApplyToSave(null); });
        }

        private MeshRenderer CreateTempPaintedRenderer(string name)
        {
            if (_sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                Assert.IsNotNull(shader, "Expected at least one built-in or URP shader to be available in EditMode.");
                _sharedMaterial = new Material(shader);
                if (_sharedMaterial.HasProperty(PaintBoothController.BaseColorPropertyId))
                {
                    _sharedMaterial.SetColor(PaintBoothController.BaseColorPropertyId, Color.white);
                }
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(_root.transform, false);
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _sharedMaterial;
            return renderer;
        }

        private static void AssertSwatch(string id, string expectedName, string expectedHex)
        {
            PaintSwatch swatch;
            Assert.IsTrue(PaintBoothController.TryGetSwatch(id, out swatch), "Missing curated swatch: " + id);
            Assert.AreEqual(expectedName, swatch.DisplayName);
            Assert.AreEqual(expectedHex, swatch.Hex);
            Assert.GreaterOrEqual(swatch.DefaultMetallic, 0f);
            Assert.LessOrEqual(swatch.DefaultMetallic, 1f);
            Assert.GreaterOrEqual(swatch.DefaultSmoothness, 0f);
            Assert.LessOrEqual(swatch.DefaultSmoothness, 1f);

            string normalized;
            Color parsed;
            Assert.IsTrue(PaintBoothController.TryParsePaintHex(swatch.Hex, out normalized, out parsed));
            Assert.AreEqual(swatch.Hex, normalized);
        }

        private static void AssertColorApprox(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f, "Red mismatch");
            Assert.AreEqual(expected.g, actual.g, 1e-4f, "Green mismatch");
            Assert.AreEqual(expected.b, actual.b, 1e-4f, "Blue mismatch");
            Assert.AreEqual(expected.a, actual.a, 1e-4f, "Alpha mismatch");
        }

        private static Vector4 ToVector4(Color c)
        {
            return new Vector4(c.r, c.g, c.b, c.a);
        }
    }
}
