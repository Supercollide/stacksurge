using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// Shared look for the whole UI. Scene/prefab screens and code-built dialogs are made of plain rectangles,
    /// so instead of editing every prefab, <see cref="Apply"/> gives those rectangles rounded corners at runtime
    /// (radius scaled to their size) and drops the offset Outline/Shadow borders. Icons and art (custom sprites),
    /// masks, filled/tiled images (progress bars), hairlines and full-screen backdrops are left untouched.
    /// </summary>
    public static class UiSkin
    {
        // Palette (from the app icon): yellow, teal, blue bars and the orange bolt, on deep navy.
        public static readonly Color Board = new Color32(0x12, 0x1A, 0x2B, 0xFF);
        public static readonly Color Slot = new Color32(0x1B, 0x24, 0x36, 0xFF);
        public static readonly Color Surface = new Color32(0x18, 0x20, 0x33, 0xFF);
        public static readonly Color SurfaceRaised = new Color32(0x24, 0x2E, 0x46, 0xFF);
        public static readonly Color Primary = new Color32(0x3B, 0x82, 0xF6, 0xFF);
        public static readonly Color Accent = new Color32(0xF5, 0xC0, 0x4A, 0xFF);
        public static readonly Color Teal = new Color32(0x2B, 0xC4, 0xA7, 0xFF);
        public static readonly Color Bolt = new Color32(0xEF, 0x63, 0x3B, 0xFF);
        public static readonly Color Danger = new Color32(0xE5, 0x48, 0x4D, 0xFF);
        public static readonly Color TextMuted = new Color32(0xA3, 0xAE, 0xC6, 0xFF);
        public static readonly Color AppBackground = new Color32(0x0D, 0x13, 0x22, 0xFF);
        /// <summary>Hairline border that replaces the old saturated cyan/blue panel frames.</summary>
        public static readonly Color Frame = new Color(1f, 1f, 1f, 0.10f);

        const float MinRadius = 6f;
        const float MaxRadius = 32f;
        const float FrameMaxInset = 24f;     // a frame is a panel only this much bigger than the one drawn over it
        const float DarkPanelMaxChannel = 0.13f;
        const float RadiusPerSide = 0.22f;
        const float FallbackRadius = 14f;   // for rects a layout group hasn't sized yet
        const float HairlineSize = 12f;      // thinner than this stays square (dividers, bars)

        /// <param name="canvasSize">Size of the root canvas in canvas units; rects covering it are backdrops and stay square.</param>
        public static void Apply(Transform root, Vector2 canvasSize)
        {
            if (root == null) return;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                ApplyTo(image, canvasSize);
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
                PadLabels(selectable);
        }

        static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>
        /// Keeps a button's label clear of the button's edges: inner margins sized from the button's height and
        /// measured against the button itself (some prefab label boxes are bigger than their button). The label keeps
        /// its designed size and may only shrink to fit inside that padding, so it never grows and no layout changes.
        /// </summary>
        /// <param name="keepUpdated">Also re-pad whenever the button is resized (layout-driven buttons start at zero size).</param>
        public static void PadLabels(Selectable selectable, bool keepUpdated = true)
        {
            if (selectable == null || selectable is TMP_InputField || selectable is Slider || selectable is Scrollbar) return;
            var button = selectable.transform as RectTransform;
            if (button == null) return;
            if (keepUpdated && !selectable.TryGetComponent(out LabelPadder _)) selectable.gameObject.AddComponent<LabelPadder>();
            Rect bounds = button.rect;
            if (bounds.width <= 0f || bounds.height <= 0f) return;

            float padX = Mathf.Clamp(bounds.height * 0.2f, 10f, 24f);
            float padY = Mathf.Clamp(bounds.height * 0.08f, 2f, 8f);

            foreach (var text in selectable.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.GetComponentInParent<Selectable>(true) != selectable) continue; // belongs to a nested control

                // Text box corners in the button's space; positive inset = the box already sits inside the button.
                text.rectTransform.GetWorldCorners(Corners);
                Vector2 min = button.InverseTransformPoint(Corners[0]);
                Vector2 max = button.InverseTransformPoint(Corners[2]);
                float left = min.x - bounds.xMin, right = bounds.xMax - max.x;
                float bottom = min.y - bounds.yMin, top = bounds.yMax - max.y;
                text.margin = new Vector4(
                    Mathf.Max(0f, padX - left), Mathf.Max(0f, padY - top),
                    Mathf.Max(0f, padX - right), Mathf.Max(0f, padY - bottom));

                if (!text.enableAutoSizing)
                {
                    text.fontSizeMax = text.fontSize;
                    text.fontSizeMin = Mathf.Min(text.fontSize, Mathf.Max(8f, text.fontSize * 0.6f));
                    text.enableAutoSizing = true;
                }
                else
                {
                    // Only ever lowers the minimum (preferred size is reported at the maximum, so layouts don't move).
                    text.fontSizeMin = Mathf.Min(text.fontSizeMin > 0f ? text.fontSizeMin : text.fontSizeMax, Mathf.Max(8f, text.fontSizeMax * 0.6f));
                }
                // A single word that doesn't fit should shrink, not break mid-word onto a second line.
                if (text.text != null && text.text.IndexOf(' ') < 0) text.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        /// <summary>Applies the skin using the canvas the root lives on (falls back to the 1080x1920 reference).</summary>
        public static void Apply(Transform root)
        {
            if (root == null) return;
            var canvas = root.GetComponentInParent<Canvas>(true);
            Vector2 size = canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.size : new Vector2(1080f, 1920f);
            Apply(root, size);
        }

        public static void ApplyTo(Image image, Vector2 canvasSize)
        {
            if (image == null || image.GetComponent<Mask>() != null) return;
            if (image.type == Image.Type.Filled || image.type == Image.Type.Tiled) return;

            Sprite sprite = image.sprite;
            if (UiSprites.IsRounded(sprite)) return; // already skinned (rows can be re-applied)
            string spriteName = sprite != null ? sprite.name : null;
            bool outline = spriteName == "sprite_rectangle_outline";
            bool plain = sprite == null || spriteName == "sprite_rectangle" || IsBuiltinPanelSprite(spriteName);
            if (!plain && !outline) return;

            Vector2 size = image.rectTransform.rect.size;
            if (IsBackdrop(size, canvasSize))
            {
                // Full-screen: stays square. Opaque near-black screen backgrounds take the brand navy.
                if (plain && IsDarkOpaque(image.color)) image.color = WithAlpha(AppBackground, image.color.a);
                return;
            }
            float minSide = Mathf.Min(size.x, size.y);
            if (minSide > 0f && minSide < HairlineSize) return;

            bool selectable = image.GetComponent<Selectable>() != null;
            if (plain && !selectable)
            {
                if (IsFrame(image)) image.color = Frame;                                       // bright border → hairline
                else if (IsDarkOpaque(image.color)) image.color = WithAlpha(Surface, image.color.a); // near-black panel → navy
            }

            float radius = minSide > 0f ? Mathf.Clamp(minSide * RadiusPerSide, MinRadius, MaxRadius) : FallbackRadius;
            if (outline) UiSprites.ApplyRoundedOutline(image, radius);
            else UiSprites.ApplyRounded(image, radius);

            // Outline/Shadow just draw offset copies of the mesh; on a rounded shape that reads as a smear.
            foreach (var effect in image.GetComponents<Shadow>()) Object.Destroy(effect);
        }

        /// <summary>
        /// A "frame": an image drawn just behind a later sibling that covers all of it except a thin border
        /// (the GameOver/Pause/Leaderboard/Friends panels are built this way).
        /// </summary>
        static bool IsFrame(Image image)
        {
            var parent = image.transform.parent;
            if (parent == null) return false;
            Rect rect = image.rectTransform.rect;
            Vector2 center = (Vector2)image.rectTransform.localPosition + rect.center;
            for (int i = image.transform.GetSiblingIndex() + 1; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i).GetComponent<Image>();
                if (sibling == null || sibling.color.a < 0.9f) continue;
                Rect sr = sibling.rectTransform.rect;
                Vector2 sc = (Vector2)sibling.rectTransform.localPosition + sr.center;
                float dw = rect.width - sr.width, dh = rect.height - sr.height;
                // Strictly bigger: two same-size full-screen siblings (GameOver / PauseMenuRoot) are not a frame.
                bool bigger = dw > 0.5f || dh > 0.5f;
                if (bigger && dw >= 0f && dh >= 0f && dw <= FrameMaxInset && dh <= FrameMaxInset && (center - sc).magnitude <= FrameMaxInset)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Covers the screen, or the safe area (on phones with a cutout, full-screen panels live in a shorter
        /// SafeAreaPanel), so it is a backdrop/dim rather than a card.
        /// </summary>
        static bool IsBackdrop(Vector2 size, Vector2 canvasSize)
        {
            if (canvasSize.x <= 0f || canvasSize.y <= 0f) return false;
            Rect safe = Screen.safeArea;
            float safeW = Screen.width > 0 ? canvasSize.x * safe.width / Screen.width : canvasSize.x;
            float safeH = Screen.height > 0 ? canvasSize.y * safe.height / Screen.height : canvasSize.y;
            return size.x >= safeW * 0.98f && size.y >= safeH * 0.98f;
        }

        static bool IsDarkOpaque(Color c) =>
            c.a >= 0.9f && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < DarkPanelMaxChannel;

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static bool IsBuiltinPanelSprite(string spriteName) =>
            spriteName == "UISprite" || spriteName == "Background" || spriteName == "InputFieldBackground";
    }
}
