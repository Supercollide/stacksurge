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

        public string PlayerId => _playerId;

        public void Setup(
            LeaderboardEntryData entry,
            Sprite gold,
            Sprite silver,
            Sprite bronze,
            int delayIndex,
            Func<string, Task<FriendRequestResult>> onAddFriend = null,
            FriendRelationState relation = FriendRelationState.None)
        {
            bool isPlayer = entry.IsCurrentPlayer;
            _playerId = entry.PlayerId;
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

            SetRelationState(isPlayer ? FriendRelationState.Self : relation);

            transform.localScale = Vector3.one * 0.88f;
            transform.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetDelay(delayIndex * 0.055f).SetUpdate(true);
        }

        /// <summary>Applies the button state for the current relationship with this row's player.</summary>
        public void SetRelationState(FriendRelationState relation)
        {
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
            if (_addFriendButton == null) return;
            Style("Closed", DisabledColor, interactable: false);
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

            switch (result)
            {
                case FriendRequestResult.Sent:            Style("Sent!", DisabledColor, false); break;
                case FriendRequestResult.AlreadyFriends:  Style("Friends", DisabledColor, false); break;
                case FriendRequestResult.AlreadyPending:  Style("Pending", DisabledColor, false); break;
                case FriendRequestResult.TargetDisallows: Style("Closed", DisabledColor, false); break;
                case FriendRequestResult.Blocked:         _addFriendButton.gameObject.SetActive(false); break;
                default:
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
