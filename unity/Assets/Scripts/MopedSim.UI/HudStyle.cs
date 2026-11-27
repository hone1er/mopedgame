using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Shared HUD look and layout constants (font scale, translucent panel backgrounds, padding, safe-area margins) plus small
    /// uGUI factory helpers. All values are for the 1920x1080 CanvasScaler reference resolution. Display-only: no simulation logic.
    /// </summary>
    public static class HudStyle
    {
        /// <summary>Canvas reference resolution the pixel values below are authored against.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>Distance from every screen edge to HUD panels.</summary>
        public const float Margin = 24f;

        /// <summary>Height of the controls banner pinned to the top of the screen.</summary>
        public const float BannerHeight = 40f;

        /// <summary>Gap between the banner and the first HUD element beneath it.</summary>
        public const float BannerGap = 12f;

        /// <summary>Top inset (from the screen top) for anything that must sit below the controls banner.</summary>
        public const float SafeTop = Margin + BannerHeight + BannerGap;

        /// <summary>Inner padding of translucent panels.</summary>
        public const float Padding = 14f;

        public const int FontCaption = 15;
        public const int FontBody = 20;
        public const int FontValue = 26;
        public const int FontTitle = 22;
        public const int FontHero = 76;

        public static readonly Color PanelBackground = new Color(0.04f, 0.05f, 0.07f, 0.66f);
        public static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color TextPrimary = new Color(0.96f, 0.97f, 0.98f, 1f);
        public static readonly Color TextDim = new Color(0.70f, 0.76f, 0.82f, 1f);
        public static readonly Color Good = new Color(0.30f, 0.86f, 0.42f, 1f);
        public static readonly Color Warn = new Color(0.98f, 0.76f, 0.20f, 1f);
        public static readonly Color Bad = new Color(0.96f, 0.28f, 0.24f, 1f);
        public static readonly Color Cool = new Color(0.45f, 0.72f, 0.98f, 1f);

        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private static Font _font;
        private static Sprite _fillSprite;

        /// <summary>Built-in UI font (cached).</summary>
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                }

                return _font;
            }
        }

        /// <summary>
        /// Filled uGUI Images without a sprite render as a full rectangle (fillAmount is ignored). Assigns a cached 1x1 white sprite
        /// so bars actually fill. Idempotent and safe on null.
        /// </summary>
        public static void PrepareFill(Image image)
        {
            if (image == null || image.sprite != null)
            {
                return;
            }

            if (_fillSprite == null)
            {
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply(false, true);
                tex.hideFlags = HideFlags.HideAndDontSave;
                _fillSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
                _fillSprite.hideFlags = HideFlags.HideAndDontSave;
            }

            image.sprite = _fillSprite;
        }

        /// <summary>Translucent background panel anchored to a screen corner/edge. Pivot equals the anchor.</summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, bool background)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            if (background)
            {
                Image img = go.AddComponent<Image>();
                img.color = PanelBackground;
                img.raycastTarget = false;
            }

            return rt;
        }

        /// <summary>
        /// Text positioned top-left inside its parent (x right, y down, both in reference pixels). Shadowed for readability over
        /// bright scenery.
        /// </summary>
        public static Text CreateLabel(Transform parent, string name, float x, float y, float width, float height, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
            Text text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = string.Empty;
            Shadow shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        /// <summary>Horizontal bar: a dim track plus a left-to-right filled Image (returned). Positioned top-left like labels.</summary>
        public static Image CreateBar(Transform parent, string name, float x, float y, float width, float height, Color fillColor)
        {
            GameObject track = new GameObject(name + "Track", typeof(RectTransform));
            track.transform.SetParent(parent, false);
            RectTransform trt = (RectTransform)track.transform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(x, -y);
            trt.sizeDelta = new Vector2(width, height);
            Image trackImg = track.AddComponent<Image>();
            trackImg.color = BarTrack;
            trackImg.raycastTarget = false;

            GameObject fill = new GameObject(name, typeof(RectTransform));
            fill.transform.SetParent(track.transform, false);
            RectTransform frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            Image img = fill.AddComponent<Image>();
            img.color = fillColor;
            img.raycastTarget = false;
            PrepareFill(img);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 0f;
            return img;
        }

        /// <summary>Thin vertical marker (tick) over a bar, positioned by normalized x (0..1) of the bar's width.</summary>
        public static RectTransform CreateBarMarker(Image bar, string name, float normalizedX, Color color)
        {
            Transform track = bar.transform.parent;
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(track, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(normalizedX, 0f);
            rt.anchorMax = new Vector2(normalizedX, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(3f, 6f);
            rt.anchoredPosition = Vector2.zero;
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>Sets <paramref name="target"/>'s colour only when it changed (avoids needless canvas rebuilds).</summary>
        public static void SetColor(Graphic target, Color color)
        {
            if (target != null && target.color != color)
            {
                target.color = color;
            }
        }
    }
}
