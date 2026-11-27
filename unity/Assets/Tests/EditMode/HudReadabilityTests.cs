using MopedSim.Core;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.Tests.EditMode
{
    public sealed class HudReadabilityTests
    {
        [Test]
        public void ControlsBanner_TextFollowsMode_AndListsPickUpOnlyWhileRiding()
        {
            GameObject go = new GameObject("Banner", typeof(RectTransform));
            try
            {
                ControlsBannerView view = go.AddComponent<ControlsBannerView>();
                view.SetMode(ControlsBannerMode.Ride);
                Assert.AreEqual(ControlsBannerMode.Ride, view.Mode);
                StringAssert.Contains("[R] Pick up", ControlsBannerView.TextFor(ControlsBannerMode.Ride));
                StringAssert.DoesNotContain("[R]", ControlsBannerView.TextFor(ControlsBannerMode.Garage));
                StringAssert.Contains("torque", ControlsBannerView.TextFor(ControlsBannerMode.Garage));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HudStyle_PrepareFill_GivesFilledImageASprite_AndSafeTopClearsBanner()
        {
            GameObject go = new GameObject("Fill", typeof(RectTransform));
            try
            {
                Image img = go.AddComponent<Image>();
                Assert.IsNull(img.sprite);
                HudStyle.PrepareFill(img);
                Assert.IsNotNull(img.sprite);
                HudStyle.PrepareFill(null);
                Assert.GreaterOrEqual(HudStyle.SafeTop, HudStyle.Margin + HudStyle.BannerHeight);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RideHud_RenderWithoutBoundViews_DoesNotThrow()
        {
            GameObject go = new GameObject("Hud");
            try
            {
                RideHudView hud = go.AddComponent<RideHudView>();
                EngineTelemetrySnapshot t = default(EngineTelemetrySnapshot);
                hud.Render(in t);
                Assert.AreEqual(1, hud.RenderCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
