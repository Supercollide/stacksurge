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

        /// <summary>The badge on your own profile ("chain:3" or ""), shown on your row without a lookup.</summary>
        public Func<string> OwnBadge;
        /// <summary>A badge on a row was tapped: (badge, player name).</summary>
        public Action<string, string> BadgeTapped;

        /// <summary>True while the leaderboard panel is visible (used by the Android Back handler).</summary>
        public bool IsOpen => _leaderboardRoot != null && _leaderboardRoot.activeSelf;
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

            LoadSavedBoard();
            RefreshLeaderboardScopeUi();
        }

        // --- REFRESH LOGIC ---
        // Opening the panel shows the last list for that tab straight away (kept in memory; All-time Global is also kept
        // on the device) and swaps in fresh scores when they arrive. With nothing saved yet, placeholder rows hold the
        // space. Friend states and the "requests off" lookup fill in after the rows are already on screen.

        private struct BoardSnapshot
        {
            public LeaderboardEntryData[] Entries;
            public bool HasPlayerEntry;             // your own entry, for when you're outside the top list
            public LeaderboardEntryData PlayerEntry;
        }

        [Serializable]
        private class SavedBoard
        {
            public LeaderboardEntryData[] entries;
            public bool hasPlayerEntry;
            public LeaderboardEntryData playerEntry;
            public string[] requestsOff;
            public string[] badgeIds;   // players' chosen badges, parallel arrays (JsonUtility has no dictionaries)
            public string[] badges;
        }

        private const string SavedBoardKey = "LeaderboardCache.AllTimeGlobal";
        private const int PlaceholderRowCount = 6;
        private static readonly float[] PlaceholderNameWidths = { 250f, 190f, 220f, 170f, 240f, 200f };

        private readonly Dictionary<(LeaderboardScope, LeaderboardFilterMode), BoardSnapshot> _boards =
            new Dictionary<(LeaderboardScope, LeaderboardFilterMode), BoardSnapshot>();
        private readonly HashSet<string> _requestsOff = new HashSet<string>(); // players known to have friend requests off
        private readonly Dictionary<string, string> _badges = new Dictionary<string, string>(); // player id -> badge they show
        private readonly List<LeaderboardRowView> _rows = new List<LeaderboardRowView>();
        private bool _savedBoardLoaded;
        private int _lastFriendCount = -1;

        public async Task RefreshLeaderboard()
        {
            if (_leaderboardRowContainer == null) return;

            // Claim this refresh slot; any older in-flight call will see a stale generation and abort.
            int generation = ++_refreshGeneration;
            LeaderboardScope scope = _activeScope;
            LeaderboardFilterMode filter = _activeFilter;
            LoadSavedBoard();

            bool showingSaved = _boards.TryGetValue((scope, filter), out BoardSnapshot saved);
            if (showingSaved) ShowBoard(saved, scope, filter, animate: true);
            else ShowLoading(scope);

            // Relationship states (Friends / Pending on the Add Friend buttons) refresh alongside the scores.
            Task friendsRefresh = RefreshFriendsSafeAsync();
            BoardSnapshot? fresh = await FetchBoardAsync(scope, filter);
            if (generation != _refreshGeneration) return; // another tab was picked meanwhile

            if (fresh.HasValue)
            {
                StoreBoard(scope, filter, fresh.Value);
                if (!showingSaved || !SameBoard(saved, fresh.Value))
                    ShowBoard(fresh.Value, scope, filter, animate: !showingSaved);

                // Friends-relative ranks are not comparable across sessions; only track global rank drops.
                if (filter == LeaderboardFilterMode.Global && YourRank(fresh.Value) is int rank)
                    NotificationService.Instance?.CheckAndNotifyRankDrop(rank, scope, $"{scope} Leaderboard");
            }
            else if (!showingSaved)
            {
                ShowMessage("Couldn't load scores.\nCheck your connection and try again.", retry: true);
            }
            // A failed refresh over a saved list keeps the saved list rather than replacing it with an error.

            await friendsRefresh;
            if (generation != _refreshGeneration) return;
            foreach (var row in _rows)
                if (row != null) row.UpdateRelation(RelationFor(row.PlayerId));

            if (fresh.HasValue) _ = ApplyTargetPreferencesAsync(fresh.Value.Entries, generation);
        }

        /// <summary>
        /// Loads the selected tab in the background so the next open shows it straight away. Called once services
        /// are up and after a score is submitted. The open panel refreshes itself, so this does nothing then.
        /// </summary>
        public void Prefetch()
        {
            if (IsOpen || (_getLeaderboardScores == null && _getFriendsScores == null)) return;
            _ = PrefetchAsync(_activeScope, _activeFilter);
        }

        private async Task<BoardSnapshot?> PrefetchAsync(LeaderboardScope scope, LeaderboardFilterMode filter)
        {
            LoadSavedBoard();
            BoardSnapshot? board = await FetchBoardAsync(scope, filter);
            if (board.HasValue) StoreBoard(scope, filter, board.Value);
            return board;
        }

        /// <summary>
        /// After a score is submitted: reloads All-time Global (and the selected tab, if different and the panel is
        /// closed) so the next open shows the new score. Returns your All-time rank, or null if it couldn't be loaded,
        /// so a stale rank from before the run is never shown.
        /// </summary>
        public async Task<int?> RefreshAfterSubmitAsync()
        {
            var allTime = (LeaderboardScope.AllTime, LeaderboardFilterMode.Global);
            Task other = (_activeScope, _activeFilter) != allTime && !IsOpen
                ? PrefetchAsync(_activeScope, _activeFilter)
                : Task.CompletedTask;
            BoardSnapshot? board = await PrefetchAsync(LeaderboardScope.AllTime, LeaderboardFilterMode.Global);
            await other;
            return board.HasValue ? YourRank(board.Value) : null;
        }

        /// <summary>Scores for one tab; null when they could not be loaded (an empty board is a real, empty result).</summary>
        private async Task<BoardSnapshot?> FetchBoardAsync(LeaderboardScope scope, LeaderboardFilterMode filter)
        {
            // Your own entry is only needed when you're outside the top list; start it now so it runs alongside.
            Task<LeaderboardEntryData?> playerTask = filter == LeaderboardFilterMode.Global && _getPlayerEntry != null
                ? GetPlayerEntrySafeAsync(scope)
                : null;

            LeaderboardEntryData[] entries = null;
            try
            {
                if (filter == LeaderboardFilterMode.FriendsOnly && _getFriendsScores != null && _getFriendIds != null)
                {
                    List<string> friendIds = await _getFriendIds();
                    _lastFriendCount = friendIds?.Count ?? 0;
                    entries = await _getFriendsScores(scope, friendIds);
                }
                else if (_getLeaderboardScores != null)
                {
                    entries = await _getLeaderboardScores(scope);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardManager] Loading scores failed: " + e.Message);
                entries = null;
            }
            if (entries == null) return null;

            var board = new BoardSnapshot { Entries = entries };
            if (playerTask != null && IndexOfCurrentPlayer(entries) < 0)
            {
                LeaderboardEntryData? me = await playerTask;
                if (me.HasValue)
                {
                    board.HasPlayerEntry = true;
                    board.PlayerEntry = me.Value;
                }
            }
            return board;
        }

        private async Task<LeaderboardEntryData?> GetPlayerEntrySafeAsync(LeaderboardScope scope)
        {
            try { return await _getPlayerEntry(scope); }
            catch { return null; } // non-critical: only "Your position" and your row below the list depend on it
        }

        private async Task RefreshFriendsSafeAsync()
        {
            if (_refreshFriends == null) return;
            try { await _refreshFriends(); }
            catch { /* non-critical: rows keep their last known relationship */ }
        }

        private void ShowBoard(BoardSnapshot board, LeaderboardScope scope, LeaderboardFilterMode filter, bool animate)
        {
            ClearRows();
            int? rank = YourRank(board);
            if (_yourPositionText != null)
                _yourPositionText.text = rank.HasValue ? $"Your position: #{rank.Value}" : "Your position: not ranked yet";

            if (board.Entries.Length == 0)
            {
                ShowMessage(EmptyMessage(scope, filter), retry: false);
                return;
            }

            HideMessage();
            for (int i = 0; i < board.Entries.Length; i++)
                AddRow(board.Entries[i], i, RelationFor(board.Entries[i]), animate);

            // Global: when you're outside the top list, your own row goes underneath it.
            if (filter == LeaderboardFilterMode.Global && board.HasPlayerEntry && IndexOfCurrentPlayer(board.Entries) < 0)
                AddRow(board.PlayerEntry, board.Entries.Length, FriendRelationState.Self, animate);
            ApplyBadges();
        }

        /// <summary>Puts each row's badge on it, or the player's block when they show none.</summary>
        private void ApplyBadges()
        {
            foreach (var row in _rows)
                if (row != null) row.SetBadge(BadgeFor(row.IsCurrentPlayer, row.PlayerId), BadgeTapped);
        }

        private void AddRow(LeaderboardEntryData entry, int index, FriendRelationState relation, bool animate)
        {
            var row = Instantiate(_rowPrefab, _leaderboardRowContainer, false);
            UiSkin.Apply(row.transform);
            row.Setup(entry, _goldMedal, _silverMedal, _bronzeMedal, index, _onAddFriend, relation, animate);
            if (!string.IsNullOrEmpty(entry.PlayerId) && _requestsOff.Contains(entry.PlayerId))
                row.SetTargetDisallowsRequests();
            _rows.Add(row);
        }

        private string BadgeFor(bool isCurrentPlayer, string playerId)
        {
            if (isCurrentPlayer)
            {
                try { return OwnBadge?.Invoke() ?? ""; }
                catch { return ""; }
            }
            return !string.IsNullOrEmpty(playerId) && _badges.TryGetValue(playerId, out string badge) ? badge : "";
        }

        /// <summary>First load with nothing saved: a line saying what is loading and placeholder rows in the list's shape.</summary>
        private void ShowLoading(LeaderboardScope scope)
        {
            ClearRows();
            HideMessage();
            if (_yourPositionText != null) _yourPositionText.text = string.Empty;

            string what = scope switch
            {
                LeaderboardScope.Daily  => "today's",
                LeaderboardScope.Weekly => "this week's",
                _                       => "all-time",
            };
            AddNote($"Loading {what} scores…");
            for (int i = 0; i < PlaceholderRowCount; i++) AddPlaceholderRow(i);
        }

        private void AddNote(string text, float size = 24f, float height = 44f)
        {
            var go = new GameObject("Note", typeof(RectTransform));
            go.transform.SetParent(_leaderboardRowContainer, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(0f, height);
            var label = go.AddComponent<TextMeshProUGUI>();
            if (_leaderboardStatusText != null) label.font = _leaderboardStatusText.font;
            label.text = text;
            label.fontSize = size;
            label.color = UiSkin.TextMuted;
            label.alignment = TextAlignmentOptions.Center;
            label.margin = new Vector4(24f, 0f, 24f, 0f);
            label.raycastTarget = false;
        }

        private void AddPlaceholderRow(int index)
        {
            float height = _rowPrefab != null ? ((RectTransform)_rowPrefab.transform).sizeDelta.y : 66f;
            var go = new GameObject("PlaceholderRow", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_leaderboardRowContainer, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, height);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.13f, 0.7f); // the row prefab's background
            bg.raycastTarget = false;
            UiSprites.ApplyRounded(bg, 14f);

            AddPlaceholderBar(rt, new Vector2(0f, 0.5f), 80f, PlaceholderNameWidths[index % PlaceholderNameWidths.Length]);
            AddPlaceholderBar(rt, new Vector2(1f, 0.5f), -14f, 96f);

            // A slow fade tells the player the list is on its way; it stops when real rows replace these.
            go.GetComponent<CanvasGroup>().DOFade(0.45f, 0.7f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine)
                .SetDelay(index * 0.08f).SetUpdate(true).SetLink(go);
        }

        private static void AddPlaceholderBar(RectTransform row, Vector2 anchor, float x, float width)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, 18f);
            var bar = go.AddComponent<Image>();
            bar.color = new Color(1f, 1f, 1f, 0.08f);
            bar.raycastTarget = false;
            UiSprites.ApplyRounded(bar, 9f);
        }

        private void ClearRows()
        {
            foreach (Transform child in _leaderboardRowContainer)
            {
                child.gameObject.SetActive(false); // out of the layout now; Destroy only lands at the end of the frame
                Destroy(child.gameObject);
            }
            _rows.Clear();
        }

        /// <summary>Empty boards and load errors, shown where the rows would be; an error also gets a Try again button.</summary>
        private void ShowMessage(string text, bool retry)
        {
            ClearRows();
            HideMessage();
            if (retry && _yourPositionText != null) _yourPositionText.text = string.Empty;
            AddNote(text, 28f, 140f);
            if (retry) AddTryAgainButton();
        }

        private void AddTryAgainButton()
        {
            var holder = new GameObject("TryAgainRow", typeof(RectTransform));
            holder.transform.SetParent(_leaderboardRowContainer, false);
            ((RectTransform)holder.transform).sizeDelta = new Vector2(0f, 80f); // the list stretches its children; the button keeps its own size inside

            var go = new GameObject("TryAgainBtn", typeof(RectTransform));
            go.transform.SetParent(holder.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 68f);
            var image = go.AddComponent<Image>();
            image.color = UiSkin.Primary;
            UiSprites.ApplyRounded(image, 16f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { if (IsOpen) _ = RefreshLeaderboard(); });

            var labelGo = new GameObject("Text", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (_leaderboardStatusText != null) label.font = _leaderboardStatusText.font;
            label.text = "TRY AGAIN";
            label.fontSize = 26f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            UiSkin.PadLabels(button);
        }

        private void HideMessage()
        {
            if (_leaderboardStatusText != null) _leaderboardStatusText.gameObject.SetActive(false);
        }

        private string EmptyMessage(LeaderboardScope scope, LeaderboardFilterMode filter)
        {
            string when = scope switch
            {
                LeaderboardScope.Daily  => " today",
                LeaderboardScope.Weekly => " this week",
                _                       => string.Empty,
            };
            if (filter == LeaderboardFilterMode.FriendsOnly)
            {
                return _lastFriendCount == 0
                    ? "Add friends to compare\nscores with them here."
                    : $"No scores{when} yet\nfrom you or your friends.";
            }
            return $"No scores{when} yet.\nFinish a run to be the first.";
        }

        private void StoreBoard(LeaderboardScope scope, LeaderboardFilterMode filter, BoardSnapshot board)
        {
            _boards[(scope, filter)] = board;
            if (scope == LeaderboardScope.AllTime && filter == LeaderboardFilterMode.Global) SaveBoardToDevice(board);
        }

        private void SaveBoardToDevice(BoardSnapshot board)
        {
            try
            {
                var saved = new SavedBoard
                {
                    entries = board.Entries,
                    hasPlayerEntry = board.HasPlayerEntry,
                    playerEntry = board.PlayerEntry,
                    requestsOff = new List<string>(_requestsOff).ToArray(),
                    badgeIds = new List<string>(_badges.Keys).ToArray(),
                    badges = new List<string>(_badges.Values).ToArray(),
                };
                PlayerPrefs.SetString(SavedBoardKey, JsonUtility.ToJson(saved));
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[LeaderboardManager] Saving the list failed: " + e.Message); }
        }

        private void LoadSavedBoard()
        {
            if (_savedBoardLoaded) return;
            _savedBoardLoaded = true;
            try
            {
                string json = PlayerPrefs.GetString(SavedBoardKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return;
                var saved = JsonUtility.FromJson<SavedBoard>(json);
                if (saved?.entries == null) return;
                if (saved.requestsOff != null) _requestsOff.UnionWith(saved.requestsOff);
                if (saved.badgeIds != null && saved.badges != null && saved.badgeIds.Length == saved.badges.Length)
                    for (int i = 0; i < saved.badgeIds.Length; i++)
                        if (!string.IsNullOrEmpty(saved.badgeIds[i]) && !_badges.ContainsKey(saved.badgeIds[i]))
                            _badges[saved.badgeIds[i]] = saved.badges[i] ?? "";
                var key = (LeaderboardScope.AllTime, LeaderboardFilterMode.Global);
                if (!_boards.ContainsKey(key)) // a fetch that already finished is newer
                {
                    _boards[key] = new BoardSnapshot
                    {
                        Entries = saved.entries,
                        HasPlayerEntry = saved.hasPlayerEntry,
                        PlayerEntry = saved.playerEntry,
                    };
                }
            }
            catch (Exception e) { Debug.LogWarning("[LeaderboardManager] Reading the saved list failed: " + e.Message); }
        }

        private static bool SameBoard(BoardSnapshot a, BoardSnapshot b)
        {
            if (a.Entries == null || b.Entries == null || a.Entries.Length != b.Entries.Length) return false;
            for (int i = 0; i < a.Entries.Length; i++)
                if (!SameEntry(a.Entries[i], b.Entries[i])) return false;
            if (a.HasPlayerEntry != b.HasPlayerEntry) return false;
            return !a.HasPlayerEntry || SameEntry(a.PlayerEntry, b.PlayerEntry);
        }

        private static bool SameEntry(LeaderboardEntryData x, LeaderboardEntryData y) =>
            x.Rank == y.Rank && x.PlayerId == y.PlayerId && x.PlayerName == y.PlayerName &&
            x.Score.Equals(y.Score) && x.IsCurrentPlayer == y.IsCurrentPlayer;

        private static int IndexOfCurrentPlayer(LeaderboardEntryData[] entries)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].IsCurrentPlayer) return i;
            return -1;
        }

        private static int? YourRank(BoardSnapshot board)
        {
            int index = IndexOfCurrentPlayer(board.Entries);
            if (index >= 0) return board.Entries[index].Rank;
            return board.HasPlayerEntry ? board.PlayerEntry.Rank : (int?)null;
        }

        private FriendRelationState RelationFor(LeaderboardEntryData entry) =>
            entry.IsCurrentPlayer ? FriendRelationState.Self : RelationFor(entry.PlayerId);

        private FriendRelationState RelationFor(string playerId)
        {
            if (_getRelationState == null || string.IsNullOrEmpty(playerId)) return FriendRelationState.None;
            try { return _getRelationState(playerId); }
            catch { return FriendRelationState.None; }
        }

        /// <summary>
        /// Every player's public preferences: the badge they show, and whether they take friend requests (players who
        /// turned requests off get no Add Friend button). Runs after rows are shown so the leaderboard never waits on
        /// these lookups. The answers are remembered, so the next time the list opens they are already applied.
        /// </summary>
        private async Task ApplyTargetPreferencesAsync(LeaderboardEntryData[] entries, int generation)
        {
            if (_getSocialPrefs == null) return;

            var ids = new List<string>();
            for (int i = 0; i < entries.Length; i++)
            {
                if (!entries[i].IsCurrentPlayer && !string.IsNullOrEmpty(entries[i].PlayerId))
                    ids.Add(entries[i].PlayerId);
            }
            if (ids.Count == 0) return;

            Dictionary<string, SocialPrefs> prefs;
            try { prefs = await _getSocialPrefs(ids); }
            catch { return; }
            if (prefs == null) return;

            bool changed = false;
            foreach (string id in ids)
            {
                if (!prefs.TryGetValue(id, out var p) || p == null) continue;
                string badge = p.badge ?? "";
                if (!_badges.TryGetValue(id, out string known) || known != badge)
                {
                    _badges[id] = badge;
                    changed = true;
                }
                if (RelationFor(id) == FriendRelationState.None)
                    changed |= p.allowFriendRequests ? _requestsOff.Remove(id) : _requestsOff.Add(id);
            }
            if (changed && _boards.TryGetValue((LeaderboardScope.AllTime, LeaderboardFilterMode.Global), out var allTime))
                SaveBoardToDevice(allTime);

            if (generation != _refreshGeneration) return;
            foreach (var row in _rows)
            {
                if (row == null || string.IsNullOrEmpty(row.PlayerId)) continue;
                if (_requestsOff.Contains(row.PlayerId) && RelationFor(row.PlayerId) == FriendRelationState.None)
                    row.SetTargetDisallowsRequests();
            }
            ApplyBadges();
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