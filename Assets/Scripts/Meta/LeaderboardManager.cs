using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using StackSurge.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.Meta
{
    public enum LeaderboardFilterMode
    {
        Global,
        FriendsOnly
    }

    public class LeaderboardManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _leaderboardRoot;
        [SerializeField] private Transform _leaderboardRowContainer;
        [SerializeField] private TextMeshProUGUI _leaderboardStatusText;
        [SerializeField] private TMP_InputField _playerNameInput;
        [SerializeField] private TextMeshProUGUI _yourPositionText;

        [Header("Leaderboard Tabs")]
        [SerializeField] private Button _dailyLeaderboardTabButton;
        [SerializeField] private Button _weeklyLeaderboardTabButton;
        [SerializeField] private Button _allTimeLeaderboardTabButton;
        [SerializeField] private TextMeshProUGUI _leaderboardTitleText;

        [Header("Leaderboard Filters")]
        [SerializeField] private Button _globalFilterButton;
        [SerializeField] private Button _friendsFilterButton;

        [SerializeField] private Button _leaderboardButton;

        [Header("Assets")]
        [SerializeField] private LeaderboardRowView _rowPrefab;
        [SerializeField] private Sprite _goldMedal, _silverMedal, _bronzeMedal;

        private Func<LeaderboardScope, Task<LeaderboardEntryData[]>> _getLeaderboardScores;
        private Func<LeaderboardScope, List<string>, Task<LeaderboardEntryData[]>> _getFriendsScores;
        private Func<Task<List<string>>> _getFriendIds;
        private Func<LeaderboardScope, Task<LeaderboardEntryData?>> _getPlayerEntry;
        private Func<string, Task> _setPlayerName;
        private Func<string> _getPlayerName;
        private Func<string, Task<FriendRequestResult>> _onAddFriend;
        private Func<string, FriendRelationState> _getRelationState;
        private Func<IEnumerable<string>, Task<Dictionary<string, SocialPrefs>>> _getSocialPrefs;
        private Func<Task> _refreshFriends;
        private LeaderboardScope _activeScope = LeaderboardScope.AllTime;
        private LeaderboardFilterMode _activeFilter = LeaderboardFilterMode.Global;
        private int _refreshGeneration = 0; // incremented on every refresh; stale calls self-abort
        private bool _frozeClockOnOpen;        // true only if WE paused the game when opening

        [SerializeField] private Button _setPlayerNameButton;
        [SerializeField] private Button _closeButton;

        //restore old leaderboard scores from archive
        //[SerializeField] private Button _restoreArchiveButton;
        //[SerializeField] private string _leaderboardId = "all_time_highs";
        //[SerializeField] private string _archivedVersionId = "20260617043612741436307";

        void Awake()
        {
            if (_leaderboardButton != null)
                _leaderboardButton.onClick.AddListener(ToggleLeaderboard);

            if (_dailyLeaderboardTabButton != null)
                _dailyLeaderboardTabButton.onClick.AddListener(() => SetLeaderboardScope(LeaderboardScope.Daily));
            if (_weeklyLeaderboardTabButton != null)
                _weeklyLeaderboardTabButton.onClick.AddListener(() => SetLeaderboardScope(LeaderboardScope.Weekly));
            if (_allTimeLeaderboardTabButton != null)
                _allTimeLeaderboardTabButton.onClick.AddListener(() => SetLeaderboardScope(LeaderboardScope.AllTime));

            if (_globalFilterButton != null)
                _globalFilterButton.onClick.AddListener(() => SetFilterMode(LeaderboardFilterMode.Global));
            if (_friendsFilterButton != null)
                _friendsFilterButton.onClick.AddListener(() => SetFilterMode(LeaderboardFilterMode.FriendsOnly));

            if (_setPlayerNameButton != null)
                _setPlayerNameButton.onClick.AddListener(OnSetPlayerName);

            if (_closeButton != null)
                _closeButton.onClick.AddListener(ToggleLeaderboard);
        }

        //Restore archived leaderboard scores when the button is clicked
        /*
        public async void OnRestoreArchive()
        {
            _leaderboardStatusText.text = "Restoring archived scores...";
            _leaderboardStatusText.gameObject.SetActive(true);

            await ArchiveRestoreService.RestoreFromArchive(_leaderboardId, _archivedVersionId);

            await Task.Delay(800); // Allow UGS to propagate
            _ = RefreshLeaderboard();
        } */

        // --- INITIALIZATION ---
        public void SetLeaderboardCallbacks(
            Func<LeaderboardScope, Task<LeaderboardEntryData[]>> getLeaderboardScores,
            Func<LeaderboardScope, Task<LeaderboardEntryData?>> getPlayerEntry,
            Func<string, Task> setPlayerName,
            Func<string> getPlayerName,
            Func<string, Task<FriendRequestResult>> onAddFriend = null,
            Func<LeaderboardScope, List<string>, Task<LeaderboardEntryData[]>> getFriendsScores = null,
            Func<Task<List<string>>> getFriendIds = null,
            Func<string, FriendRelationState> getRelationState = null,
            Func<IEnumerable<string>, Task<Dictionary<string, SocialPrefs>>> getSocialPrefs = null,
            Func<Task> refreshFriends = null)
        {
            _getLeaderboardScores = getLeaderboardScores;
            _getPlayerEntry       = getPlayerEntry;
            _setPlayerName        = setPlayerName;
            _getPlayerName        = getPlayerName;
            _onAddFriend          = onAddFriend;
            _getFriendsScores     = getFriendsScores;
            _getFriendIds         = getFriendIds;
            _getRelationState     = getRelationState;
            _getSocialPrefs       = getSocialPrefs;
            _refreshFriends       = refreshFriends;

            if (_playerNameInput != null)
                _playerNameInput.text = _getPlayerName?.Invoke() ?? "";

            RefreshLeaderboardScopeUi();
        }

        // --- REFRESH LOGIC ---
        public async Task RefreshLeaderboard()
        {
            if (_leaderboardRowContainer == null) return;

            // Claim this refresh slot; any older in-flight call will see a stale generation and abort.
            int generation = ++_refreshGeneration;

            // Clear existing rows
            foreach (Transform child in _leaderboardRowContainer)
                Destroy(child.gameObject);

            _leaderboardStatusText.text = _activeFilter == LeaderboardFilterMode.FriendsOnly
                ? $"Loading {_activeScope.ToString().ToLower()} friend scores..."
                : $"Loading {_activeScope.ToString().ToLower()} scores...";
            _leaderboardStatusText.gameObject.SetActive(true);

            // Make sure relationship states (Friends / Pending) are current before rows are built.
            if (_refreshFriends != null)
            {
                try { await _refreshFriends(); } catch { /* non-critical */ }
            }

            LeaderboardEntryData[] entries = Array.Empty<LeaderboardEntryData>();
            if (_activeFilter == LeaderboardFilterMode.FriendsOnly && _getFriendsScores != null && _getFriendIds != null)
            {
                try
                {
                    List<string> friendIds = await _getFriendIds();
                    entries = await _getFriendsScores(_activeScope, friendIds);
                }
                catch { entries = Array.Empty<LeaderboardEntryData>(); }
            }
            else if (_getLeaderboardScores != null)
            {
                try { entries = await _getLeaderboardScores(_activeScope); }
                catch { entries = Array.Empty<LeaderboardEntryData>(); }
            }

            // Another tab was clicked while we were awaiting – discard these results.
            if (generation != _refreshGeneration) return;

            _leaderboardStatusText.gameObject.SetActive(false);

            // Find current player in fetched entries list
            LeaderboardEntryData? currentInList = null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].IsCurrentPlayer)
                {
                    currentInList = entries[i];
                    break;
                }
            }

            int? displayRank = null;
            LeaderboardEntryData? globalPlayerEntry = null;

            if (_activeFilter == LeaderboardFilterMode.FriendsOnly)
            {
                // In Friends mode, rank is relative among friends
                if (currentInList.HasValue)
                {
                    displayRank = currentInList.Value.Rank;
                }
            }
            else
            {
                // In Global mode, fetch global rank even if player is outside top 15
                if (_getPlayerEntry != null)
                {
                    try { globalPlayerEntry = await _getPlayerEntry(_activeScope); }
                    catch { /* swallow – non-critical */ }
                }

                if (generation != _refreshGeneration) return;

                if (currentInList.HasValue)
                    displayRank = currentInList.Value.Rank;
                else if (globalPlayerEntry.HasValue)
                    displayRank = globalPlayerEntry.Value.Rank;
            }

            // Friends-relative ranks are not comparable across sessions; only track global rank drops.
            if (displayRank.HasValue && _activeFilter == LeaderboardFilterMode.Global)
            {
                NotificationService.Instance?.CheckAndNotifyRankDrop(displayRank.Value, _activeScope, $"{_activeScope} Leaderboard");
            }

            // Populate "Your position" header
            if (_yourPositionText != null)
            {
                _yourPositionText.text = displayRank.HasValue
                    ? $"Your position: #{displayRank.Value}"
                    : "Your position: —";
            }

            if (entries.Length == 0)
            {
                _leaderboardStatusText.gameObject.SetActive(true);
                _leaderboardStatusText.text = _activeFilter == LeaderboardFilterMode.FriendsOnly
                    ? "No friend scores available."
                    : "Could not load scores.";
                return;
            }

            // Build rows
            var rows = new List<LeaderboardRowView>(entries.Length + 1);
            for (int i = 0; i < entries.Length; i++)
            {
                var row = Instantiate(_rowPrefab, _leaderboardRowContainer, false);
                row.Setup(entries[i], _goldMedal, _silverMedal, _bronzeMedal, i, _onAddFriend, RelationFor(entries[i]));
                rows.Add(row);
            }

            // In Global mode, if player is outside top 15, append overflow row as #16
            if (_activeFilter == LeaderboardFilterMode.Global && !currentInList.HasValue && globalPlayerEntry.HasValue)
            {
                var overflowRow = Instantiate(_rowPrefab, _leaderboardRowContainer, false);
                overflowRow.Setup(globalPlayerEntry.Value, _goldMedal, _silverMedal, _bronzeMedal, entries.Length, _onAddFriend, FriendRelationState.Self);
                rows.Add(overflowRow);
            }

            _ = ApplyTargetPreferencesAsync(rows, entries, generation);
        }

        private FriendRelationState RelationFor(LeaderboardEntryData entry)
        {
            if (entry.IsCurrentPlayer) return FriendRelationState.Self;
            if (_getRelationState == null) return FriendRelationState.None;
            try { return _getRelationState(entry.PlayerId); }
            catch { return FriendRelationState.None; }
        }

        /// <summary>
        /// Players who turned off incoming friend requests publish that preference; hide Add Friend for them.
        /// Runs after rows are shown so the leaderboard never waits on these lookups.
        /// </summary>
        private async Task ApplyTargetPreferencesAsync(List<LeaderboardRowView> rows, LeaderboardEntryData[] entries, int generation)
        {
            if (_getSocialPrefs == null) return;

            var candidateIds = new List<string>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (!entries[i].IsCurrentPlayer && RelationFor(entries[i]) == FriendRelationState.None && !string.IsNullOrEmpty(entries[i].PlayerId))
                    candidateIds.Add(entries[i].PlayerId);
            }
            if (candidateIds.Count == 0) return;

            Dictionary<string, SocialPrefs> prefs;
            try { prefs = await _getSocialPrefs(candidateIds); }
            catch { return; }

            if (generation != _refreshGeneration || prefs == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.PlayerId)) continue;
                if (prefs.TryGetValue(row.PlayerId, out var p) && p != null && !p.allowFriendRequests)
                    row.SetTargetDisallowsRequests();
            }
        }

        public void HideLeaderboard()
        {
            if (_leaderboardRoot != null && _leaderboardRoot.activeSelf)
            {
                _leaderboardRoot.SetActive(false);
                RestoreClock();
            }
        }

        /// <summary>
        /// Freeze the game clock only if it was running when the panel opened. Opening from the main menu
        /// or the pause menu (clock already stopped) must not change anything, and closing must not unpause.
        /// </summary>
        private void FreezeClockIfRunning()
        {
            _frozeClockOnOpen = Time.timeScale > 0f;
            if (_frozeClockOnOpen) Time.timeScale = 0f;
        }

        private void RestoreClock()
        {
            if (_frozeClockOnOpen) Time.timeScale = 1f;
            _frozeClockOnOpen = false;
        }

        /// <summary>Opens the leaderboard if it is not already visible (used for push routing).</summary>
        public void ShowLeaderboard()
        {
            if (_leaderboardRoot == null || _leaderboardRoot.activeSelf) return;
            ToggleLeaderboard();
        }

        // --- UI INTERACTION ---
        public void ToggleLeaderboard()
        {
            if (_leaderboardRoot == null) return;

            bool active = !_leaderboardRoot.activeSelf;
            _leaderboardRoot.SetActive(active);

            if (active)
            {
                FreezeClockIfRunning();

                // Animation logic
                var cg = _leaderboardRoot.GetComponent<CanvasGroup>();
                if (cg)
                {
                    cg.alpha = 0f;
                    cg.DOFade(1f, 0.25f).SetUpdate(true);
                }
                _leaderboardRoot.transform.localScale = Vector3.one * 0.9f;
                _leaderboardRoot.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);

                if (_playerNameInput != null && _getPlayerName != null)
                    _playerNameInput.text = _getPlayerName();

                RefreshLeaderboardScopeUi();
                _ = RefreshLeaderboard(); // Trigger refresh when opening
            }
            else
            {
                RestoreClock();
            }
        }

        public async void OnSetPlayerName()
        {
            if (_playerNameInput == null || _setPlayerName == null) return;
            string name = _playerNameInput.text.Trim();
            if (name.Length < 1 || name.Length > 20) return;

            _leaderboardStatusText.text = "Updating name...";
            _leaderboardStatusText.gameObject.SetActive(true);

            await _setPlayerName(name);
            await Task.Delay(600); // Wait for propagation
            _ = RefreshLeaderboard();
        }

        public void SetLeaderboardScope(LeaderboardScope scope)
        {
            if (_activeScope == scope) return;
            _activeScope = scope;
            RefreshLeaderboardScopeUi();
            _ = RefreshLeaderboard();
        }

        public void SetFilterMode(LeaderboardFilterMode filterMode)
        {
            if (_activeFilter == filterMode) return;
            _activeFilter = filterMode;
            RefreshLeaderboardScopeUi();
            _ = RefreshLeaderboard();
        }

        private void RefreshLeaderboardScopeUi()
        {
            if (_leaderboardTitleText != null)
            {
                string scopeStr = _activeScope switch
                {
                    LeaderboardScope.Daily   => "DAILY LEADERBOARD",
                    LeaderboardScope.Weekly  => "WEEKLY LEADERBOARD",
                    _                        => "ALL-TIME LEADERBOARD",
                };

                _leaderboardTitleText.text = _activeFilter == LeaderboardFilterMode.FriendsOnly
                    ? $"{scopeStr} (FRIENDS)"
                    : scopeStr;
            }

            if (_dailyLeaderboardTabButton != null)
                _dailyLeaderboardTabButton.interactable = _activeScope != LeaderboardScope.Daily;
            if (_weeklyLeaderboardTabButton != null)
                _weeklyLeaderboardTabButton.interactable = _activeScope != LeaderboardScope.Weekly;
            if (_allTimeLeaderboardTabButton != null)
                _allTimeLeaderboardTabButton.interactable = _activeScope != LeaderboardScope.AllTime;

            if (_globalFilterButton != null)
                _globalFilterButton.interactable = _activeFilter != LeaderboardFilterMode.Global;
            if (_friendsFilterButton != null)
                _friendsFilterButton.interactable = _activeFilter != LeaderboardFilterMode.FriendsOnly;
        }
    }
}