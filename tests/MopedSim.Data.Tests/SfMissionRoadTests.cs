#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MopedSim.Core;
using Xunit;

namespace MopedSim.Data.Tests
{
    /// <summary>
    /// Pins Zone_SF_Mission job checkpoints to the road blockout in SceneScaffoldBuilder.PopulateSfMission
    /// (zone root at world Z 2500; Valencia_Corridor 14 m wide x 300 m long, TwinPeaks_Grade22 10 m wide, 22% grade over local Z 300..400).
    /// </summary>
    public sealed class SfMissionRoadTests
    {
        private const float ZoneRootZ = 2500f;

        private static string ScaffoldSource()
        {
            string path = Path.Combine(TestCatalog.RepoRoot, "unity", "Assets", "Scripts", "MopedSim.Editor", "SceneScaffoldBuilder.cs");
            return File.ReadAllText(path);
        }

        private static string SfMissionBody()
        {
            string src = ScaffoldSource();
            int start = src.IndexOf("private static void PopulateSfMission", StringComparison.Ordinal);
            Assert.True(start >= 0, "PopulateSfMission not found in SceneScaffoldBuilder.cs");
            int end = src.IndexOf("CreateCheckpointBeacons(scene, \"Zone_SF_Mission\")", start, StringComparison.Ordinal);
            Assert.True(end > start);
            return src.Substring(start, end - start);
        }

        private static Match RoadMatch(string body, string roadName)
        {
            Match m = Regex.Match(
                body,
                "CreateRoad\\(root\\.transform, \"" + roadName + "\"\\s*,\\s*new Vector3\\(([-\\d.]+)f,\\s*([-\\d.]+)f,\\s*([-\\d.]+)f\\)\\s*,\\s*new Vector3\\(([-\\d.]+)f,\\s*([-\\d.]+)f,\\s*([-\\d.]+)f\\)",
                RegexOptions.CultureInvariant);
            Assert.True(m.Success, roadName + " road blockout not found in PopulateSfMission");
            return m;
        }

        private static float F(Match m, int g)
        {
            return float.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        [Fact]
        public void SfMissionCheckpoints_LieOnScaffoldValenciaOrTwinPeaksRoad()
        {
            string body = SfMissionBody();
            Match valencia = RoadMatch(body, "Valencia_Corridor");
            Match peaks = RoadMatch(body, "TwinPeaks_Grade22");

            float vHalfW = F(valencia, 4) / 2f;
            float vMin = ZoneRootZ + F(valencia, 3) - F(valencia, 6) / 2f;
            float vMax = ZoneRootZ + F(valencia, 3) + F(valencia, 6) / 2f;
            float pHalfW = F(peaks, 4) / 2f;
            float pMin = ZoneRootZ + F(peaks, 3) - F(peaks, 6) / 2f;
            float pMax = ZoneRootZ + F(peaks, 3) + F(peaks, 6) / 2f;

            int seen = 0;
            foreach (JobDefinition job in TestCatalog.Real.JobList)
            {
                foreach (JobCheckpointSpec cp in job.Checkpoints.Where(c => c.ZoneId == "Zone_SF_Mission"))
                {
                    seen++;
                    string tag = job.Id + "/" + cp.Id;
                    bool onValencia = cp.Z >= vMin && cp.Z <= vMax && Math.Abs(cp.X) <= vHalfW;
                    bool onPeaks = cp.Z >= pMin && cp.Z <= pMax && Math.Abs(cp.X) <= pHalfW;
                    Assert.True(onValencia || onPeaks, tag + " at (" + cp.X + "," + cp.Z + ") is off the SF Mission road blockout.");
                    if (onValencia && !onPeaks)
                    {
                        Assert.Equal(0f, cp.Y, 3);
                    }
                }
            }
            Assert.True(seen >= 8, "Expected the SF Mission checkpoints to be exercised, saw " + seen);
        }
    }
}
