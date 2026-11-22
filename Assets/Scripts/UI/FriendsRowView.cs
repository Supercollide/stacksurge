using System;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// Component attached to a Friends list item prefab or programmatic row object.
    /// Supports rendering friends, pending requests, and action callbacks with responsive layout.
    /// </summary>
    public class FriendsRowView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TextMeshProUGUI _activityText;
        [SerializeField] private Image _statusIndicatorImage;
        [SerializeField] private Image _backgroundImage;

        [Header("Action Buttons")]
        [SerializeField] private Button _acceptButton;
        [SerializeField] private Button _declineButton;
        [SerializeField] private Button _removeButton;
        [SerializeField] private Button _blockButton;

        [Header("Colors")]
        [SerializeField] private Color _onlineColor = new Color(0.2f, 0.85f, 0.4f);
        [SerializeField] private Color _inGameColor = new Color(0.2f, 0.7f, 1f);
        [SerializeField] private Color _busyColor = new Color(1f, 0.6f, 0.2f);
        [SerializeField] private Color _offlineColor = new Color(0.5f, 0.5f, 0.55f);

        // The prefab's status line is 12pt. Its row is laid out by a layout group (so auto-size can't be used
        // safely); 16 still fits the row next to the name.
        const float MinActivityFontSize = 16f;

        // The prefab's 100-wide action buttons only fit "Remove"/"Decline"/"Unblock" by shrinking them to ~17pt
        // once the labels are padded. 120 fits every label at one shared size, so a row's two buttons match.
        const float ActionButtonWidth = 120f;
        const float ActionLabelSize = 21f;

        private void Awake()
        {
            if (_activityText != null && _activityText.fontSize < MinActivityFontSize)
                _activityText.fontSize = MinActivityFontSize;

            // A long name gets "…" instead of pushing the buttons out of the row: the name column takes only the
            // space the badge and buttons leave (it used to ask for the whole name's width).
            if (_nameText != null)
            {
                _nameText.textWrappingMode = TextWrappingModes.NoWrap;
                _nameText.overflowMode = TextOverflowModes.Ellipsis;
                if (_nameText.transform.parent != null && _nameText.transform.parent.TryGetComponent(out LayoutElement info))
                    info.preferredWidth = 0f;
            }

            FitActionButton(_acceptButton);
            FitActionButton(_declineButton);
            FitActionButton(_removeButton);
            FitActionButton(_blockButton);
        }

        static void FitActionButton(Button button)
        {
            if (button == null) return;
            var rect = (RectTransform)button.transform;
            if (rect.sizeDelta.x < ActionButtonWidth) rect.sizeDelta = new Vector2(ActionButtonWidth, rect.sizeDelta.y);
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) return;
            label.enableAutoSizing = true;
            label.fontSizeMax = ActionLabelSize;
            label.fontSize = ActionLabelSize;
        }



        public void SetupFriend(
            FriendData friend,
            Action<string> onRemove,
            Action<string> onBlock,
            int delayIndex = 0)
        {
            if (_nameText != null) _nameText.text = friend.PlayerName;
            if (_activityText != null) _activityText.text = friend.Activity;

            if (_statusText != null)
            {
                _statusText.text = friend.Status switch
                {
                    PresenceStatus.Online => "Online",
                    PresenceStatus.InGame => "In Game",
                    PresenceStatus.Busy => "Busy",
                    _ => "Offline"
                };
            }

            if (_statusIndicatorImage != null)
            {
                _statusIndicatorImage.color = friend.Status switch
                {
                    PresenceStatus.Online => _onlineColor,
                    PresenceStatus.InGame => _inGameColor,
                    PresenceStatus.Busy => _busyColor,
                    _ => _offlineColor
                };
            }

            // Friend Item Button Visibility
            if (_acceptButton != null) _acceptButton.gameObject.SetActive(false);
            if (_declineButton != null) _declineButton.gameObject.SetActive(false);

            if (_removeButton != null)
            {
                _removeButton.gameObject.SetActive(true);
                SetButtonText(_removeButton, "Remove");
                _removeButton.onClick.RemoveAllListeners();
                _removeButton.onClick.AddListener(() => onRemove?.Invoke(friend.RelationshipId));
            }

            if (_blockButton != null)
            {
                _blockButton.gameObject.SetActive(true);
                SetButtonText(_blockButton, "Block");
                _blockButton.onClick.RemoveAllListeners();
                _blockButton.onClick.AddListener(() => onBlock?.Invoke(friend.PlayerId));
            }

            AnimateIn(delayIndex);
        }

        public void SetupRequest(
            FriendRequestData request,
            Action<string> onAccept,
            Action<string> onDecline,
            Action<string> onBlock,
            int delayIndex = 0)
        {
            if (_nameText != null) _nameText.text = request.PlayerName;
            if (_activityText != null) _activityText.text = request.IsIncoming ? "Incoming Request" : "Outgoing Request";

            if (_statusText != null) _statusText.text = request.IsIncoming ? "Pending Action" : "Waiting for response";

            if (_statusIndicatorImage != null)
            {
                _statusIndicatorImage.color = _busyColor;
            }

            if (request.IsIncoming)
            {
                // Incoming Request: Show Accept and Decline
                if (_acceptButton != null)
                {
                    _acceptButton.gameObject.SetActive(true);
                    SetButtonText(_acceptButton, "Accept");
                    _acceptButton.onClick.RemoveAllListeners();
                    _acceptButton.onClick.AddListener(() => onAccept?.Invoke(request.PlayerId));
                }

                if (_declineButton != null)
                {
                    _declineButton.gameObject.SetActive(true);
                    SetButtonText(_declineButton, "Decline");
                    _declineButton.onClick.RemoveAllListeners();
                    _declineButton.onClick.AddListener(() => onDecline?.Invoke(request.RelationshipId));
                }

                if (_removeButton != null) _removeButton.gameObject.SetActive(false);
                if (_blockButton != null) _blockButton.gameObject.SetActive(false);
            }
            else
            {
                // Outgoing Request: Show Cancel Request only (hide Accept, Remove, and Block)
                if (_acceptButton != null) _acceptButton.gameObject.SetActive(false);
                if (_removeButton != null) _removeButton.gameObject.SetActive(false);
                if (_blockButton != null) _blockButton.gameObject.SetActive(false);

                if (_declineButton != null)
                {
                    _declineButton.gameObject.SetActive(true);
                    SetButtonText(_declineButton, "Cancel");
                    _declineButton.onClick.RemoveAllListeners();
                    _declineButton.onClick.AddListener(() => onDecline?.Invoke(request.RelationshipId));
                }
            }

            AnimateIn(delayIndex);
        }

        public void SetupBlocked(
            FriendData blocked,
            Action<string> onUnblock,
            int delayIndex = 0)
        {
            if (_nameText != null) _nameText.text = blocked.PlayerName;
            if (_activityText != null) _activityText.text = "Blocked";

            if (_statusText != null) _statusText.text = "Blocked";

            if (_statusIndicatorImage != null)
                _statusIndicatorImage.color = _offlineColor;

            // Only show the Unblock button; hide all others
            if (_acceptButton != null)  _acceptButton.gameObject.SetActive(false);
            if (_declineButton != null) _declineButton.gameObject.SetActive(false);
            if (_removeButton != null)  _removeButton.gameObject.SetActive(false);

            if (_blockButton != null)
            {
                _blockButton.gameObject.SetActive(true);
                SetButtonText(_blockButton, "Unblock");
                _blockButton.onClick.RemoveAllListeners();
                _blockButton.onClick.AddListener(() => onUnblock?.Invoke(blocked.PlayerId));
            }

            AnimateIn(delayIndex);
        }

        private const float BadgeSize = 48f; // the row's inner height (64 minus its padding)
        private Image _badge;

        /// <summary>
        /// Shows the badge the player chose, after the status bar. With no badge and reserveSlot, the space stays empty
        /// so names line up with rows that have one.
        /// </summary>
        public void SetBadge(Sprite sprite, bool reserveSlot)
        {
            if (sprite == null && !reserveSlot)
            {
                if (_badge != null) _badge.gameObject.SetActive(false);
                return;
            }

            if (_badge == null)
            {
                var go = new GameObject("Badge", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                bool dotHere = _statusIndicatorImage != null && _statusIndicatorImage.transform.parent == transform;
                go.transform.SetSiblingIndex(dotHere ? _statusIndicatorImage.transform.GetSiblingIndex() + 1 : 0);
                var layout = go.AddComponent<LayoutElement>();
                layout.minWidth = layout.preferredWidth = BadgeSize;
                layout.minHeight = layout.preferredHeight = BadgeSize;
                layout.flexibleWidth = 0;
                _badge = go.AddComponent<Image>();
                _badge.preserveAspect = true;
                _badge.raycastTarget = false;
            }

            _badge.sprite = sprite;
            _badge.color = sprite != null ? Color.white : Color.clear;
            _badge.gameObject.SetActive(true);
        }

        private static void SetButtonText(Button btn, string label)
        {
            if (btn == null) return;
            TextMeshProUGUI txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.text = label;
        }

        private void AnimateIn(int delayIndex)
        {
            transform.localScale = Vector3.one * 0.9f;
            transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetDelay(delayIndex * 0.04f).SetUpdate(true);
        }
    }
}
