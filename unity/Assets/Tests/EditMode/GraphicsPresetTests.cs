using System.IO;
using MopedSim.UI;
using MopedSim.World;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>Graphics presets map to real quality level, frame cap and fog quality, and persist.</summary>
    public sealed class GraphicsPresetTests
    {
        private int _origLevel;
        private int _origFps;
        private GameObject _root;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _origLevel = QualitySettings.GetQualityLevel();
            _origFps = Application.targetFrameRate;
            _tempDir = Path.Combine(Path.GetTempPath(), "gfx_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.SetQualityLevel(_origLevel, true);
            Application.targetFrameRate = _origFps;
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void Table_HasEveryPresetOnceWithValidValuesAndExistingQualityLevel()
        {
            Assert.AreEqual(4, GraphicsPresetTable.Count);
            for (int i = 0; i < GraphicsPresetTable.Count; i++)
            {
                GraphicsPresetEntry e = GraphicsPresetTable.GetByIndex(i);
                Assert.AreEqual((GraphicsPreset)i, e.Preset);
                Assert.GreaterOrEqual(e.FogQualityScale, SettingsPreferencesStore.MinFogQualityScale);
                Assert.LessOrEqual(e.FogQualityScale, SettingsPreferencesStore.MaxFogQualityScale);
                Assert.GreaterOrEqual(e.TargetFrameRate, SettingsPreferencesStore.MinTargetFrameRate);
                Assert.LessOrEqual(e.TargetFrameRate, SettingsPreferencesStore.MaxTargetFrameRate);
                Assert.GreaterOrEqual(GraphicsPresetTable.FindQualityLevel(e.QualityLevelName), 0, e.QualityLevelName);
            }
        }

        [Test]
        public void Cycle_VisitsLowMediumHighUltraAndUpdatesFpsAndFog()
        {
            _root = new GameObject("GfxRoot");
            SettingsMenuView view = _root.AddComponent<SettingsMenuView>();
            view.Bind(SettingsPreferencesStore.CreateDefaults(), null);
            GraphicsQualitySettings last = default(GraphicsQualitySettings);
            view.GraphicsPresetChanged += g => last = g;

            GraphicsPreset[] expected = { GraphicsPreset.High, GraphicsPreset.Ultra, GraphicsPreset.Low, GraphicsPreset.Medium };
            for (int i = 0; i < expected.Length; i++)
            {
                view.CycleGraphicsPreset();
                GraphicsPresetEntry e = GraphicsPresetTable.Get(expected[i]);
                Assert.AreEqual(expected[i], last.Preset);
                Assert.AreEqual(e.TargetFrameRate, last.TargetFrameRate);
                Assert.AreEqual(e.FogQualityScale, last.FogQualityScale, 1e-5f);
            }
        }

        [Test]
        public void Persistence_RoundTripsUltraPreset()
        {
            string path = Path.Combine(_tempDir, "settings.json");
            GameSettingsData data = SettingsPreferencesStore.CreateDefaults();
            GraphicsPresetEntry e = GraphicsPresetTable.Get(GraphicsPreset.Ultra);
            data.GraphicsPreset = GraphicsPreset.Ultra;
            data.TargetFrameRate = e.TargetFrameRate;
            data.FogQualityScale = e.FogQualityScale;
            SettingsPreferencesStore.SaveToPath(path, data);

            GameSettingsData reloaded = SettingsPreferencesStore.LoadFromPath(path);
            Assert.AreEqual(GraphicsPreset.Ultra, reloaded.GraphicsPreset);
            Assert.AreEqual(120, reloaded.TargetFrameRate);
            Assert.AreEqual(e.FogQualityScale, reloaded.FogQualityScale, 1e-5f);
        }

        [Test]
        public void Apply_SetsQualityLevelAndFrameRate()
        {
            GraphicsPreset[] presets = { GraphicsPreset.Low, GraphicsPreset.Ultra, GraphicsPreset.Medium };
            for (int i = 0; i < presets.Length; i++)
            {
                GraphicsPresetEntry e = GraphicsPresetTable.Get(presets[i]);
                int applied = GraphicsPresetTable.Apply(new GraphicsQualitySettings(presets[i], e.TargetFrameRate, e.FogQualityScale));
                Assert.AreEqual(GraphicsPresetTable.FindQualityLevel(e.QualityLevelName), applied);
                Assert.AreEqual(applied, QualitySettings.GetQualityLevel());
                Assert.AreEqual(e.TargetFrameRate, Application.targetFrameRate);
            }
        }

        [Test]
        public void WeatherController_ReceivesAndClampsFogQualityScale()
        {
            _root = new GameObject("GfxWeather");
            BayAreaWeatherController weather = _root.AddComponent<BayAreaWeatherController>();
            weather.SetFogQualityScale(0.5f);
            Assert.AreEqual(0.5f, weather.FogQualityScale, 1e-5f);
            weather.SetFogQualityScale(9f);
            Assert.AreEqual(1.5f, weather.FogQualityScale, 1e-5f);
            GameObject later = new GameObject("GfxWeatherLater");
            try
            {
                Assert.AreEqual(1.5f, later.AddComponent<BayAreaWeatherController>().FogQualityScale, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(later);
                weather.SetFogQualityScale(1f);
            }
        }
    }
}
