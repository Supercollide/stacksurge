using System;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// The bar at the bottom of the main menu that offers a new version from Google Play: "Update available" with
    /// UPDATE, then the download's progress, then "Update ready" with RESTART. Its X hides it until the next launch.
    /// </summary>
    public class UpdateBannerView : MonoBehaviour
    {
        public const float Height = 172f;
        public const float BottomMargin = 40f; // above the safe area's bottom edge
        const float MaxWidth = 870f;
        const float SideMargin = 54f;
        const float Radius = 27f;
        const float TextLeft = 136f;
        const float ButtonWidth = 181f, ButtonHeight = 83f, Inset = 27f;
        const float CloseSize = 58f, CloseHitSize = 96f, CloseInset = 13f;

        static readonly Color Fill = new Color32(0x1E, 0x28, 0x40, 0xFF);
        static readonly Color Edge = new Color32(0x3A, 0x42, 0x58, 0xFF);
        static readonly Color Track = new Color32(0x2A, 0x33, 0x49, 0xFF);

        RectTransform _rt;
        RectTransform _root;
        CanvasGroup _group;
        TMP_FontAsset _font;
        TextMeshProUGUI _title, _body, _buttonLabel;
        GameObject _button, _bar, _close;
        RectTransform _barFill;

        public bool Visible { get; private set; }

        public static UpdateBannerView Create(RectTransform menuRoot, TMP_FontAsset font, Sprite logo, Action onButton, Action onClose)
        {
            var go = new GameObject("UpdateBanner", typeof(RectTransform));
            go.transform.SetParent(menuRoot, false);
            go.transform.SetAsLastSibling();
            var view = go.AddComponent<UpdateBannerView>();
            view.Build(menuRoot, font, logo, onButton, onClose);
            return view;
        }

        void Build(RectTransform root, TMP_FontAsset font, Sprite logo, Action onButton, Action onClose)
        {
            _root = root;
            _font = font;
            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(0.5f, 0f);
            _rt.sizeDelta = new Vector2(MaxWidth, Height);
            _group = gameObject.AddComponent<CanvasGroup>();

            var bg = gameObject.AddComponent<Image>();
            bg.color = Fill;
            UiSprites.ApplyRounded(bg, Radius);
            var edge = Stretch("Edge", _rt).gameObject.AddComponent<Image>();
            UiSprites.ApplyRoundedOutline(edge, Radius);
            edge.color = Edge;
            edge.raycastTarget = false;

            if (logo != null)
            {
                var mark = Child("Logo", _rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Inset, 0f), new Vector2(80f, 95f)).gameObject.AddComponent<Image>();
                mark.sprite = logo;
                mark.preserveAspect = true;
                mark.raycastTarget = false;
            }

            _title = Text("Title", 32f, Color.white, 34f, 40f);
            _body = Text("Body", 24f, UiSkin.TextMuted, 84f, 32f);

            var button = Child("Button", _rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-Inset, 0f), new Vector2(ButtonWidth, ButtonHeight));
            var buttonBg = button.gameObject.AddComponent<Image>();
            buttonBg.color = UiSkin.Primary;
            UiSprites.ApplyRounded(buttonBg, 16f);
            var b = button.gameObject.AddComponent<Button>();
            b.targetGraphic = buttonBg;
            b.onClick.AddListener(() => onButton?.Invoke());
            _buttonLabel = NewText("Label", button, 27f, Color.white);
            Stretch(_buttonLabel.rectTransform);
            _buttonLabel.alignment = TextAlignmentOptions.Center;
            _button = button.gameObject;

            // The download's progress, under the text, in place of the button.
            var bar = Child("Progress", _rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(TextLeft, -128f), new Vector2(MaxWidth - TextLeft - 40f, 12f));
            var track = bar.gameObject.AddComponent<Image>();
            track.color = Track;
            track.raycastTarget = false;
            UiSprites.ApplyRounded(track, 6f);
            _barFill = Stretch("Fill", bar);
            _barFill.anchorMax = new Vector2(0f, 1f);
            var fill = _barFill.gameObject.AddComponent<Image>();
            fill.color = UiSkin.Primary;
            fill.raycastTarget = false;
            UiSprites.ApplyRounded(fill, 6f);
            _bar = bar.gameObject;

            _close = BuildClose(onClose);
            gameObject.SetActive(false);
        }

        /// <summary>The X near the banner's top-right corner, partly outside it, like the badge details' X.</summary>
        GameObject BuildClose(Action onClose)
        {
            var hit = Child("Close", _rt, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-CloseInset, -CloseInset), new Vector2(CloseHitSize, CloseHitSize));
            var hitImage = hit.gameObject.AddComponent<Image>();
            hitImage.sprite = UiSprites.Circle;
            hitImage.color = new Color(0f, 0f, 0f, 0f); // invisible, but takes taps across the whole area
            var square = Child("Square", hit, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CloseSize, CloseSize));
            var bg = square.gameObject.AddComponent<Image>();
            bg.color = UiSkin.SurfaceRaised;
            bg.raycastTarget = false;
            UiSprites.ApplyRounded(bg, 13f);
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => onClose?.Invoke());
            foreach (float angle in new[] { 45f, -45f })
            {
                var bar = Child("Bar", square, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 5f));
                bar.localRotation = Quaternion.Euler(0f, 0f, angle);
                var img = bar.gameObject.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
                UiSprites.ApplyRounded(img, 2.5f);
            }
            return hit.gameObject;
        }

        /// <summary>Shows the banner for this stage (None hides it). It slides up the first time it appears.</summary>
        public void Show(PlayAppUpdate.Stage stage, float progress)
        {
            if (stage == PlayAppUpdate.Stage.None)
            {
                Hide();
                return;
            }
            bool downloading = stage == PlayAppUpdate.Stage.Downloading;
            _title.text = stage == PlayAppUpdate.Stage.Ready ? "Update ready" : downloading ? "Downloading update" : "Update available";
            _body.text = stage == PlayAppUpdate.Stage.Ready ? "Restart the game to finish."
                : downloading ? "You can keep playing meanwhile." : "A new version is on Google Play.";
            _button.SetActive(!downloading);
            _buttonLabel.text = stage == PlayAppUpdate.Stage.Ready ? "RESTART" : "UPDATE";
            _bar.SetActive(downloading);
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            _close.SetActive(!downloading);
            Place();

            if (Visible) return;
            Visible = true;
            gameObject.SetActive(true);
            DOTween.Kill(this);
            _group.alpha = 0f;
            float y = _rt.anchoredPosition.y;
            _rt.anchoredPosition = new Vector2(0f, y - 40f);
            DOTween.Sequence().SetUpdate(true).SetTarget(this)
                .Join(_group.DOFade(1f, 0.25f))
                .Join(_rt.DOAnchorPosY(y, 0.35f).SetEase(Ease.OutBack));
        }

        public void Hide()
        {
            if (!Visible) return;
            Visible = false;
            DOTween.Kill(this);
            _group.DOFade(0f, 0.2f).SetUpdate(true).SetTarget(this).OnComplete(() => gameObject.SetActive(false));
        }

        /// <summary>Fits the banner to the menu's width and keeps it clear of the gesture bar or home indicator.</summary>
        void Place()
        {
            Rect area = _root.rect;
            float width = area.width > 0f ? Mathf.Min(MaxWidth, area.width - 2 * SideMargin) : MaxWidth;
            _rt.sizeDelta = new Vector2(width, Height);
            float safeBottom = Screen.height > 0 && area.height > 0f ? Screen.safeArea.yMin / Screen.height * area.height : 0f;
            _rt.anchoredPosition = new Vector2(0f, safeBottom + BottomMargin);
            var bar = (RectTransform)_bar.transform;
            bar.sizeDelta = new Vector2(width - TextLeft - 40f, bar.sizeDelta.y);
            foreach (var t in new[] { _title, _body })
                t.rectTransform.sizeDelta = new Vector2(width - TextLeft - ButtonWidth - 2 * Inset, t.rectTransform.sizeDelta.y);
        }

        void OnDestroy() => DOTween.Kill(this);

        // ── Helpers ─────────────────────────────────────────────────────
        TextMeshProUGUI Text(string name, float size, Color color, float top, float height)
        {
            var t = NewText(name, _rt, size, color);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(TextLeft, -top);
            rt.sizeDelta = new Vector2(MaxWidth - TextLeft - ButtonWidth - 2 * Inset, height);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.7f;
            return t;
        }

        TextMeshProUGUI NewText(string name, Transform parent, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform Child(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
