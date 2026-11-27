using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Runtime uGUI builders shared by <see cref="TitleMenuView"/> and <see cref="PauseMenuView"/> (same visual style as
    /// <see cref="SettingsMenuView"/>: LegacyRuntime.ttf, dark cards, flat colored buttons). Build-time only; never called per frame.
    /// </summary>
    internal static class MenuWidgets
    {
        internal const string BuiltinFontName = "LegacyRuntime.ttf";

        internal static Font LoadFont()
        {
            return Resources.GetBuiltinResource<Font>(BuiltinFontName);
        }

        /// <summary>Creates a full-screen overlay canvas (own sort order) with a backdrop image and returns its rect.</summary>
        internal static RectTransform CreateOverlayCanvas(Transform parent, string name, int sortingOrder, Color backdropColor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
            go.transform.SetParent(parent, false);
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.GetComponent<Image>().color = backdropColor;
            return (RectTransform)go.transform;
        }

        internal static RectTransform CreateCard(Transform parent, string name, Vector2 size, Color color, int padding, float spacing)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            VerticalLayoutGroup v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.spacing = spacing;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return rt;
        }

        internal static Text CreateLabel(Transform parent, string name, string text, int fontSize, TextAnchor anchor, Color color, float height, Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            Text label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = true;
            label.text = text ?? string.Empty;
            return label;
        }

        /// <summary>Pins a free-standing (non layout-driven) rect to a centered anchor.</summary>
        internal static void PinCentered(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>Flat colored button whose selected/highlighted tint is clearly distinct (keyboard + gamepad focus visibility).</summary>
        internal static Button CreateButton(Transform parent, string name, string label, Color color, int fontSize, float height, Font font, UnityEngine.Events.UnityAction onClick, out Text labelText)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            Image bg = go.GetComponent<Image>();
            bg.color = color;
            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.60f, 0.60f, 0.60f, 1f);
            cb.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.55f);
            cb.colorMultiplier = 1f;
            btn.colors = cb;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 6f);
            textRt.offsetMax = new Vector2(-12f, -6f);
            Text text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.text = label;
            labelText = text;
            return btn;
        }

        /// <summary>Gives keyboard/gamepad focus to <paramref name="target"/> through the scene EventSystem (no-op without one).</summary>
        internal static void Focus(Selectable target)
        {
            EventSystem es = EventSystem.current;
            if (es != null && target != null && target.gameObject.activeInHierarchy && target.IsInteractable())
            {
                es.SetSelectedGameObject(target.gameObject);
            }
        }
    }
}
