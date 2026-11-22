using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using StackSurge.Meta;

namespace StackSurge.UI
{
    /// <summary>
    /// Leaderboard row. The Add Friend button reflects the real relationship with that player
    /// and only flips to "Sent!" once the request actually succeeds.
    /// </summary>
    public class LeaderboardRowView : MonoBehaviour
    {
        [SerializeField] private Image _rankIcon;
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _scoreText;
        [SerializeField] private Image _background;
        [SerializeField] private Button _addFriendButton;

        private static readonly Color AddColor      = new Color(0.25f, 0.55f, 0.95f);
        private static readonly Color DisabledColor = new Color(0.35f, 0.35f, 0.40f);
        private static readonly Color AcceptColor   = new Color(0.25f, 0.75f, 0.45f);
        private static readonly Color ErrorColor    = new Color(0.85f, 0.35f, 0.35f);

        private string _playerId;
        private Func<string, Task<FriendRequestResult>> _onAddFriend;
        private bool _busy;
        private FriendRelationState _relation;
        private bool _locked; // a request result or "requests off" is showing; later relation refreshes leave it alone
        private float _scoreFontSize; // the prefab's score size, the most the score may grow back to

        // Row layout (canvas units). The score gets a fixed-width column, so the Add Friend button lines up in every
        // row whatever the score; the name stops before the button and is cut off with "…" if too long.
        private const float ScoreRightInset = 14f; // the prefab's score box offset from the row's right edge
        private const string ScoreColumnSample = "999,999"; // column fits six digits; longer scores shrink to fit
        private const float ScoreMinFontSize = 16f;
        private const float ButtonWidth = 156f;
        private const float ButtonHeight = 46f;
        private const float ButtonFontSize = 20f;
        private const float GapAfterScore = 16f;
        private const float GapAfterName = 12f;
        private const float BadgeSize = 50f;  // the row is 66 tall
        private const float BadgeGap = 10f;

        private Image _badge;
        private string _badgeValue = "";
        private string _playerName = "";
        private float _nameLeft = float.NaN; // the prefab's left edge for the name, before a badge moves it
        private Action<string, string> _onBadgeTapped;

        public string PlayerId => _playerId;
        public bool IsCurrentPlayer { get; private set; }

        public void Setup(
            LeaderboardEntryData entry,
            Sprite gold,
            Sprite silver,
            Sprite bronze,
            int delayIndex,
            Func<string, Task<FriendRequestResult>> onAddFriend = null,
            FriendRelationState relation = FriendRelationState.None,
            bool animate = true)
        {
            bool isPlayer = entry.IsCurrentPlayer;
            _playerId = entry.PlayerId;
            _playerName = entry.PlayerName ?? "";
            IsCurrentPlayer = isPlayer;
            _onAddFriend = onAddFriend;

            if (_background != null)
                _background.color = isPlayer ? new Color(1f, 0.85f, 0.3f, 0.18f) : new Color(0.1f, 0.1f, 0.13f, 0.7f);

            if (entry.Rank <= 3)
            {
                if (_rankIcon != null) { _rankIcon.gameObject.SetActive(true); _rankIcon.sprite = entry.Rank switch { 1 => gold, 2 => silver, 3 => bronze, _ => null }; }
                if (_rankText != null) _rankText.gameObject.SetActive(false);
            }
            else
            {
                if (_rankIcon != null) _rankIcon.gameObject.SetActive(false);
                if (_rankText != null) { _rankText.gameObject.SetActive(true); _rankText.text = $"{entry.Rank}"; }
            }

            if (_nameText != null)
            {
                _nameText.text = entry.PlayerName + (isPlayer ? " <color=#FFD700><size=70%>(You)</size></color>" : "");
                _nameText.fontStyle = isPlayer ? FontStyles.Bold : FontStyles.Normal;
            }

            if (_scoreText != null)
                _scoreText.text = ((int)entry.Score).ToString("N0", CultureInfo.InvariantCulture);

            _locked = false;
            SetRelationState(isPlayer ? FriendRelationState.Self : relation);
            LayoutRow();

            if (!animate) return; // redrawn with fresher data in place; popping in again would read as a flicker
            transform.localScale = Vector3.one * 0.88f;
            transform.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetDelay(delayIndex * 0.055f).SetUpdate(true);
        }

        /// <summary>
        /// Shows the badge the player chose ("chain:3") between the rank and the name; "" or an unknown value shows none.
        /// With reserveSlot the name starts after the badge's space even without a badge, so names in a list that has
        /// badges line up. Tapping the badge calls onTapped with the badge and the player's name.
        /// </summary>
        public void SetBadge(string badge, Action<string, string> onTapped, bool reserveSlot)
        {
            _onBadgeTapped = onTapped;
            _badgeValue = badge ?? "";
            if (_nameText == null) return;

            var name = _nameText.rectTransform;
            if (float.IsNaN(_nameLeft)) _nameLeft = name.offsetMin.x;
            Sprite sprite = BadgeArt.ForBadge(_badgeValue);

            if (sprite == null)
            {
                if (_badge != null) _badge.gameObject.SetActive(false);
                name.offsetMin = new Vector2(_nameLeft + (reserveSlot ? BadgeSize + BadgeGap : 0f), name.offsetMin.y);
                return;
            }

            if (_badge == null)
            {
                var go = new GameObject("Badge", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(_nameLeft, 0f);
                rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);
                _badge = go.AddComponent<Image>();
                _badge.preserveAspect = true;
                var button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => _onBadgeTapped?.Invoke(_badgeValue, _playerName));
            }

            _badge.sprite = sprite;
            _badge.gameObject.SetActive(true);
            name.offsetMin = new Vector2(_nameLeft + BadgeSize + BadgeGap, name.offsetMin.y);
        }

        /// <summary>Applies a relationship that changed after the row was shown (friend list refreshed late).</summary>
        public void UpdateRelation(FriendRelationState relation)
        {
            if (_locked || _busy || relation == _relation || _relation == FriendRelationState.Self) return;
            SetRelationState(relation);
            LayoutRow();
        }

        private void OnRectTransformDimensionsChange() => LayoutRow();

        /// <summary>Places the Add Friend button left of the score column and fits the name into the space that remains.</summary>
        private void LayoutRow()
        {
            var row = transform as RectTransform;
            if (row == null || _nameText == null || _scoreText == null) return;
            if (row.rect.width <= 0f) return; // not laid out yet; runs again when the list sizes the row

            if (_scoreFontSize <= 0f) _scoreFontSize = _scoreText.enableAutoSizing ? _scoreText.fontSizeMax : _scoreText.fontSize;
            _scoreText.enableAutoSizing = true;
            _scoreText.fontSizeMax = _scoreFontSize;
            _scoreText.fontSizeMin = Mathf.Min(_scoreFontSize, ScoreMinFontSize);
            _scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            float scoreColumn = Mathf.Ceil(_scoreText.GetPreferredValues(ScoreColumnSample).x) + 2f;
            var score = _scoreText.rectTransform;
            score.sizeDelta = new Vector2(scoreColumn, score.sizeDelta.y);

            float fromRight = ScoreRightInset + scoreColumn + GapAfterScore;

            if (_addFriendButton != null)
            {
                var button = (RectTransform)_addFriendButton.transform;
                if (button.parent != transform) button.SetParent(transform, false); // the prefab nests it under the name
                button.anchorMin = button.anchorMax = new Vector2(1f, 0.5f);
                button.pivot = new Vector2(1f, 0.5f);
                button.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
                button.anchoredPosition = new Vector2(-fromRight, 0f);

                var label = _addFriendButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMax = ButtonFontSize;
                    label.fontSizeMin = Mathf.Min(label.fontSizeMin, 14f);
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                }

                if (_addFriendButton.gameObject.activeSelf) fromRight += ButtonWidth;
            }

            var name = _nameText.rectTransform;
            float left = name.offsetMin.x, bottom = name.offsetMin.y, top = name.offsetMax.y;
            name.anchorMax = new Vector2(1f, name.anchorMax.y);
            name.offsetMin = new Vector2(left, bottom);
            name.offsetMax = new Vector2(-(fromRight + GapAfterName), top);
            _nameText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>Applies the button state for the current relationship with this row's player.</summary>
        public void SetRelationState(FriendRelationState relation)
        {
            _relation = relation;
            if (_addFriendButton == null) return;

            if (_onAddFriend == null || relation == FriendRelationState.Self || relation == FriendRelationState.Blocked)
            {
                _addFriendButton.gameObject.SetActive(false);
                return;
            }

            _addFriendButton.gameObject.SetActive(true);
            _addFriendButton.onClick.RemoveAllListeners();

            switch (relation)
            {
                case FriendRelationState.Friend:
                    Style("Friends", DisabledColor, interactable: false);
                    break;
                case FriendRelationState.OutgoingPending:
                    Style("Pending", DisabledColor, interactable: false);
                    break;
                case FriendRelationState.IncomingPending:
                    Style("Accept", AcceptColor, interactable: true);
                    _addFriendButton.onClick.AddListener(() => _ = HandleAddClickedAsync());
                    break;
                default:
                    Style("Add Friend", AddColor, interactable: true);
                    _addFriendButton.onClick.AddListener(() => _ = HandleAddClickedAsync());
                    break;
            }
        }

        /// <summary>Hides the button when the target has turned off incoming friend requests.</summary>
        public void SetTargetDisallowsRequests()
        {
            if (_relation != FriendRelationState.None) return; // friends / pending / you: their button already says so
            _locked = true;
            if (_addFriendButton == null || _busy) return;
            _addFriendButton.gameObject.SetActive(false);
            LayoutRow();
        }

        private async Task HandleAddClickedAsync()
        {
            if (_busy || _onAddFriend == null || string.IsNullOrEmpty(_playerId)) return;
            _busy = true;
            Style("Sending…", DisabledColor, interactable: false);

            FriendRequestResult result;
            try { result = await _onAddFriend(_playerId); }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardRowView] Add friend failed: " + e.Message);
                result = FriendRequestResult.Failed;
            }

            _busy = false;
            if (this == null || _addFriendButton == null) return;
            _locked = true;

            switch (result)
            {
                case FriendRequestResult.Sent:            Style("Sent!", DisabledColor, false); break;
                case FriendRequestResult.AlreadyFriends:  Style("Friends", DisabledColor, false); break;
                case FriendRequestResult.AlreadyPending:  Style("Pending", DisabledColor, false); break;
                case FriendRequestResult.TargetDisallows:
                case FriendRequestResult.Blocked:
                    _addFriendButton.gameObject.SetActive(false); // they don't take requests from you; nothing to offer
                    LayoutRow();
                    break;
                default:
                    _locked = false;
                    Style("Retry", ErrorColor, true);
                    _addFriendButton.onClick.RemoveAllListeners();
                    _addFriendButton.onClick.AddListener(() => _ = HandleAddClickedAsync());
                    break;
            }
        }

        private void Style(string label, Color color, bool interactable)
        {
            Image img = _addFriendButton.GetComponent<Image>();
            if (img != null) img.color = color;
            TextMeshProUGUI txt = _addFriendButton.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.text = label;
            _addFriendButton.interactable = interactable;
        }
    }
}
