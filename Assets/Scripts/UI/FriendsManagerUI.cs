using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using StackSurge.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    public enum FriendsTab
    {
        FriendsList,
        PendingRequests,
        AddFriend,
        BlockedUsers
    }

    /// <summary>
    /// UI Manager for the Friends Panel modal.
    /// Connects user input and tab selection with FriendsService backend logic.
    /// Supports full programmatic UI generation at runtime if inspector references are not set.
    /// </summary>
    public class FriendsManagerUI : MonoBehaviour
    {
        [Header("Root & Containers")]
        [SerializeField] private GameObject _friendsRoot;
        [SerializeField] private Transform _friendsRowContainer;
        [SerializeField] private Button _friendsOpenButton;
        [SerializeField] private Button _closeButton;

        [Header("Tab Buttons")]
        [SerializeField] private Button _friendsTabButton;
        [SerializeField] private Button _requestsTabButton;
        [SerializeField] private Button _addFriendTabButton;
        [SerializeField] private Button _blockedTabButton;

        [Header("Add Friend Section")]
        [SerializeField] private GameObject _addFriendSection;
        [SerializeField] private TMP_InputField _targetPlayerIdInput;
        [SerializeField] private Button _sendRequestButton;
        [SerializeField] private TextMeshProUGUI _myPlayerIdText;
        [SerializeField] private Button _copyMyIdButton;

        [Header("Status & Assets")]
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private FriendsRowView _rowPrefab;

        private FriendsService _friendsService;
        private FriendsTab _activeTab = FriendsTab.FriendsList;
        private int _refreshGeneration = 0;
        private GameObject _cachedScrollViewObj;
        private bool _frozeClockOnOpen; // true only if WE paused the game when opening

        private void Awake()
        {
            BindButtonListeners();

            if (_friendsRoot != null)
                _friendsRoot.SetActive(false);
        }

        private GameObject GetScrollViewGameObject()
        {
            if (_cachedScrollViewObj != null) return _cachedScrollViewObj;

            if (_friendsRowContainer != null)
            {
                ScrollRect sr = _friendsRowContainer.GetComponentInParent<ScrollRect>(true);
                if (sr != null)
                {
                    _cachedScrollViewObj = sr.gameObject;
                }
                else if (_friendsRowContainer.parent != null && _friendsRowContainer.parent != transform)
                {
                    _cachedScrollViewObj = _friendsRowContainer.parent.gameObject;
                }
                else
                {
                    _cachedScrollViewObj = _friendsRowContainer.gameObject;
                }
            }
            return _cachedScrollViewObj;
        }

        private void BindButtonListeners()
        {
            if (_friendsOpenButton != null)
            {
                _friendsOpenButton.onClick.RemoveAllListeners();
                _friendsOpenButton.onClick.AddListener(TogglePanel);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveAllListeners();
                _closeButton.onClick.AddListener(ClosePanel);
            }

            if (_friendsTabButton != null)
            {
                _friendsTabButton.onClick.RemoveAllListeners();
                _friendsTabButton.onClick.AddListener(() => SetTab(FriendsTab.FriendsList));
            }

            if (_requestsTabButton != null)
            {
                _requestsTabButton.onClick.RemoveAllListeners();
                _requestsTabButton.onClick.AddListener(() => SetTab(FriendsTab.PendingRequests));
            }

            if (_addFriendTabButton != null)
            {
                _addFriendTabButton.onClick.RemoveAllListeners();
                _addFriendTabButton.onClick.AddListener(() => SetTab(FriendsTab.AddFriend));
            }

            if (_blockedTabButton != null)
            {
                _blockedTabButton.onClick.RemoveAllListeners();
                _blockedTabButton.onClick.AddListener(() => SetTab(FriendsTab.BlockedUsers));
            }

            if (_sendRequestButton != null)
            {
                _sendRequestButton.onClick.RemoveAllListeners();
                _sendRequestButton.onClick.AddListener(OnSendRequestClicked);
            }

            if (_copyMyIdButton != null)
            {
                _copyMyIdButton.onClick.RemoveAllListeners();
                _copyMyIdButton.onClick.AddListener(OnCopyMyIdClicked);
            }
        }

        public void Initialize(FriendsService service)
        {
            _friendsService = service;
            if (_friendsService != null)
            {
                _friendsService.OnFriendsUpdated += OnFriendsServiceUpdated;
            }

            if (_myPlayerIdText != null && _friendsService != null)
            {
                _myPlayerIdText.text = $"My ID: {_friendsService.GetCurrentPlayerId()}";
            }
        }

        private void OnDestroy()
        {
            if (_friendsService != null)
            {
                _friendsService.OnFriendsUpdated -= OnFriendsServiceUpdated;
            }
        }

        private void OnFriendsServiceUpdated()
        {
            if (_friendsRoot != null && _friendsRoot.activeSelf)
            {
                _ = RefreshTabAsync();
            }
        }

        public void TogglePanel()
        {
            if (_friendsRoot == null) return;

            bool isActive = !_friendsRoot.activeSelf;
            if (isActive)
            {
                OpenPanel();
            }
            else
            {
                ClosePanel();
            }
        }

        public void OpenPanel() => OpenPanel(_activeTab);

        /// <summary>Opens the panel on a specific tab (used when routing from a push notification).</summary>
        public void OpenPanel(FriendsTab tab)
        {
            if (_friendsRoot == null) return;

            _activeTab = tab;
            bool wasOpen = _friendsRoot.activeSelf;
            _friendsRoot.SetActive(true);

            // Freeze the game clock only if it was running (mid-run). From the main menu or pause menu, leave it alone.
            if (!wasOpen)
            {
                _frozeClockOnOpen = Time.timeScale > 0f;
                if (_frozeClockOnOpen) Time.timeScale = 0f;
            }

            if (_myPlayerIdText != null && _friendsService != null)
            {
                _myPlayerIdText.text = $"My ID: {_friendsService.GetCurrentPlayerId()}";
            }

            // POP animation
            _friendsRoot.transform.localScale = Vector3.one * 0.95f;
            _friendsRoot.transform.DOScale(1f, 0.2f).SetEase(Ease.OutCubic).SetUpdate(true);

            _ = RefreshTabAsync(forceServerRefresh: true);
        }

        public void ClosePanel()
        {
            if (_friendsRoot == null || !_friendsRoot.activeSelf) return;

            // Restore the clock only if we were the ones who stopped it.
            if (_frozeClockOnOpen) Time.timeScale = 1f;
            _frozeClockOnOpen = false;

            _friendsRoot.transform.DOScale(0.95f, 0.15f).SetEase(Ease.InCubic).SetUpdate(true).OnComplete(() =>
            {
                _friendsRoot.SetActive(false);
            });
        }

        public void SetTab(FriendsTab tab)
        {
            _activeTab = tab;
            UpdateTabVisuals();
            _ = RefreshTabAsync();
        }

        private void UpdateTabVisuals()
        {
            bool isAddFriendTab = _activeTab == FriendsTab.AddFriend;

            if (_addFriendSection != null)
            {
                _addFriendSection.SetActive(isAddFriendTab);
            }

            GameObject scrollViewObj = GetScrollViewGameObject();
            if (scrollViewObj != null)
            {
                scrollViewObj.SetActive(!isAddFriendTab);
            }

            // Highlight Active Tab Button
            SetTabButtonHighlight(_friendsTabButton,   _activeTab == FriendsTab.FriendsList);
            SetTabButtonHighlight(_requestsTabButton,  _activeTab == FriendsTab.PendingRequests);
            SetTabButtonHighlight(_addFriendTabButton, _activeTab == FriendsTab.AddFriend);
            SetTabButtonHighlight(_blockedTabButton,   _activeTab == FriendsTab.BlockedUsers);
        }

        private static void SetTabButtonHighlight(Button btn, bool isActive)
        {
            if (btn == null) return;
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = isActive ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.18f, 0.2f, 0.28f);
            }
        }

        public async Task RefreshTabAsync(bool forceServerRefresh = false)
        {
            if (_friendsService == null) return;

            int generation = ++_refreshGeneration;

            UpdateTabVisuals();

            if (forceServerRefresh)
            {
                // Pull the latest relationships from the server so changes made while the app was
                // backgrounded (accepted requests, removals) show without a restart.
                try { await _friendsService.RefreshAsync(force: true); }
                catch (Exception e) { Debug.LogWarning("[FriendsManagerUI] Refresh failed: " + e.Message); }
                if (generation != _refreshGeneration) return;
            }

            switch (_activeTab)
            {
                case FriendsTab.FriendsList:
                    await LoadFriendsListAsync(generation);
                    break;
                case FriendsTab.PendingRequests:
                    await LoadPendingRequestsAsync(generation);
                    break;
                case FriendsTab.AddFriend:
                    SetStatus("Enter a Player ID or full name (Name#1234) to send a friend request.");
                    break;
                case FriendsTab.BlockedUsers:
                    await LoadBlockedUsersAsync(generation);
                    break;
            }
        }

        private async Task LoadFriendsListAsync(int generation)
        {
            SetStatus("Loading friends...");
            List<FriendData> friends = null;
            try
            {
                if (_friendsService != null)
                {
                    friends = await _friendsService.GetFriendsAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsManagerUI] LoadFriendsList failed: " + e.Message);
            }

            if (generation != _refreshGeneration) return;

            ClearContainer();

            if (friends == null || friends.Count == 0)
            {
                SetStatus("");
                DisplayEmptyState("No friends added yet.\nGo to 'ADD FRIEND' to send requests!");
                return;
            }

            SetStatus("");

            for (int i = 0; i < friends.Count; i++)
            {
                if (_friendsRowContainer == null || _rowPrefab == null) break;
                FriendsRowView row = Instantiate(_rowPrefab, _friendsRowContainer);

                row.SetupFriend(
                    friends[i],
                    onRemove: async (relId) => await RemoveFriendAsync(relId),
                    onBlock: async (playerId) => await BlockUserAsync(playerId),
                    delayIndex: i
                );
            }
        }

        private async Task LoadPendingRequestsAsync(int generation)
        {
            SetStatus("Loading requests...");
            List<FriendRequestData> incoming = null;
            List<FriendRequestData> outgoing = null;
            try
            {
                if (_friendsService != null)
                {
                    incoming = await _friendsService.GetIncomingRequestsAsync();
                    outgoing = await _friendsService.GetOutgoingRequestsAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsManagerUI] LoadPendingRequests failed: " + e.Message);
            }

            if (generation != _refreshGeneration) return;

            ClearContainer();

            var allRequests = new List<FriendRequestData>();
            if (incoming != null) allRequests.AddRange(incoming);
            if (outgoing != null) allRequests.AddRange(outgoing);

            if (allRequests.Count == 0)
            {
                SetStatus("");
                DisplayEmptyState("No pending friend requests.");
                return;
            }

            SetStatus("");

            for (int i = 0; i < allRequests.Count; i++)
            {
                if (_friendsRowContainer == null || _rowPrefab == null) break;
                FriendsRowView row = Instantiate(_rowPrefab, _friendsRowContainer);

                row.SetupRequest(
                    allRequests[i],
                    onAccept: async (targetId) => await AcceptRequestAsync(targetId),
                    onDecline: async (relId) => await DeclineRequestAsync(relId),
                    onBlock: async (playerId) => await BlockUserAsync(playerId),
                    delayIndex: i
                );
            }
        }

        private async void OnSendRequestClicked()
        {
            if (_targetPlayerIdInput == null || string.IsNullOrWhiteSpace(_targetPlayerIdInput.text))
            {
                SetStatus("Please enter a Player ID or full name (Name#1234).");
                return;
            }

            string targetId = _targetPlayerIdInput.text.Trim();
            if (_sendRequestButton != null) _sendRequestButton.interactable = false;
            SetStatus($"Sending request to {targetId}...");

            FriendRequestResult result = await _friendsService.SendFriendRequestAsync(targetId);

            if (_sendRequestButton != null) _sendRequestButton.interactable = true;

            switch (result)
            {
                case FriendRequestResult.Sent:
                    SetStatus($"Friend request sent to {targetId}!");
                    _targetPlayerIdInput.text = "";
                    await Task.Delay(1000);
                    SetTab(FriendsTab.PendingRequests);
                    break;
                case FriendRequestResult.AlreadyFriends:
                    SetStatus("You are already friends with that player.");
                    break;
                case FriendRequestResult.AlreadyPending:
                    SetStatus("A request to that player is already pending.");
                    break;
                case FriendRequestResult.TargetDisallows:
                    SetStatus("That player isn't accepting friend requests right now.");
                    break;
                case FriendRequestResult.Blocked:
                    SetStatus("You can't send a request to that player.");
                    break;
                case FriendRequestResult.NotFound:
                    SetStatus("No player found. Use their Player ID or full name (Name#1234).");
                    break;
                default:
                    SetStatus($"Failed to send friend request to {targetId}. Check your connection.");
                    break;
            }
        }

        private void OnCopyMyIdClicked()
        {
            if (_friendsService == null) return;
            string myId = _friendsService.GetCurrentPlayerId();
            GUIUtility.systemCopyBuffer = myId;
            SetStatus($"Copied My ID ({myId}) to clipboard!");
        }

        private async Task AcceptRequestAsync(string targetPlayerId)
        {
            SetStatus("Accepting request...");
            bool success = await _friendsService.AcceptFriendRequestAsync(targetPlayerId);
            if (success)
            {
                SetStatus("Friend request accepted!");
                await RefreshTabAsync();
            }
            else
            {
                SetStatus("Failed to accept request.");
            }
        }

        private async Task DeclineRequestAsync(string relationshipId)
        {
            SetStatus("Declining request...");
            bool success = await _friendsService.DeleteRelationshipAsync(relationshipId);
            if (success)
            {
                SetStatus("Request declined.");
                await RefreshTabAsync();
            }
            else
            {
                SetStatus("Failed to decline request.");
            }
        }

        private async Task RemoveFriendAsync(string relationshipId)
        {
            SetStatus("Removing friend...");
            bool success = await _friendsService.DeleteRelationshipAsync(relationshipId);
            if (success)
            {
                SetStatus("Friend removed.");
                await RefreshTabAsync();
            }
            else
            {
                SetStatus("Failed to remove friend.");
            }
        }

        private async Task BlockUserAsync(string playerId)
        {
            SetStatus("Blocking user...");
            bool success = await _friendsService.BlockUserAsync(playerId);
            if (success)
            {
                SetStatus($"User {playerId} blocked.");
                await RefreshTabAsync();
            }
            else
            {
                SetStatus("Failed to block user.");
            }
        }

        private async Task LoadBlockedUsersAsync(int generation)
        {
            SetStatus("Loading blocked users...");
            List<FriendData> blocked = null;
            try
            {
                if (_friendsService != null)
                    blocked = await _friendsService.GetBlockedUsersAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsManagerUI] LoadBlockedUsers failed: " + e.Message);
            }

            if (generation != _refreshGeneration) return;

            ClearContainer();

            if (blocked == null || blocked.Count == 0)
            {
                SetStatus("");
                DisplayEmptyState("No blocked users.");
                return;
            }

            SetStatus("");

            for (int i = 0; i < blocked.Count; i++)
            {
                if (_friendsRowContainer == null || _rowPrefab == null) break;
                FriendsRowView row = Instantiate(_rowPrefab, _friendsRowContainer);
                row.SetupBlocked(
                    blocked[i],
                    onUnblock: async (playerId) => await UnblockUserAsync(playerId),
                    delayIndex: i
                );
            }
        }

        private async Task UnblockUserAsync(string playerId)
        {
            SetStatus("Unblocking user...");
            bool success = await _friendsService.UnblockUserAsync(playerId);
            if (success)
            {
                SetStatus($"User {playerId} unblocked.");
                await RefreshTabAsync();
            }
            else
            {
                SetStatus("Failed to unblock user.");
            }
        }

        private void ClearContainer()
        {
            if (_friendsRowContainer == null) return;
            for (int i = _friendsRowContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = _friendsRowContainer.GetChild(i);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }

        private void DisplayEmptyState(string message)
        {
            if (_friendsRowContainer == null) return;
            GameObject emptyObj = new GameObject("EmptyStateMessage", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            emptyObj.transform.SetParent(_friendsRowContainer, false);

            LayoutElement le = emptyObj.GetComponent<LayoutElement>();
            le.minHeight = 160;
            le.flexibleWidth = 1;

            TextMeshProUGUI txt = emptyObj.GetComponent<TextMeshProUGUI>();
            txt.text = message;
            txt.fontSize = 15;
            txt.color = new Color(0.7f, 0.72f, 0.8f);
            txt.alignment = TextAlignmentOptions.Center;
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }
        }
    }
}