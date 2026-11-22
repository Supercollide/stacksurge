using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>
    /// Data returned per leaderboard row to the UI layer.
    /// Named *Data to avoid collision with the SDK's own LeaderboardEntry model.
    /// Serializable so the leaderboard panel can keep the last list on the device.
    /// </summary>
    [Serializable]
    public struct LeaderboardEntryData
    {
        public int    Rank;
        public string PlayerId;
        public string PlayerName;
        public double Score;
        public bool   IsCurrentPlayer;
    }

    public enum LeaderboardScope
    {
        Daily,
        Weekly,
        AllTime
    }

    /// <summary>
    /// Thin async wrapper around Unity Gaming Services Leaderboards.
    /// Supports daily, weekly, and all-time leaderboards via separate leaderboard IDs.
    ///
    /// Offline behaviour: scores are saved locally and submitted automatically the next time
    /// the service is online (either at startup or after reconnecting).
    /// </summary>
    public class LeaderboardService
    {
        const string DailyLeaderboardId   = "daily_highs";
        const string WeeklyLeaderboardId  = "weekly_highs";
        const string AllTimeLeaderboardId = "all_time_highs";
        const int    TopCount             = 15;

        readonly IOnlineConnection _connection;
        readonly SaveData          _save;
        readonly FriendsService    _friendsService;

        bool _isOnline => _connection != null && _connection.IsOnline;

        public LeaderboardService(IOnlineConnection connection, SaveData save, FriendsService friendsService = null)
        {
            _connection     = connection;
            _save           = save;
            _friendsService = friendsService;

            // Flush any score that was queued while the player was offline
            if (_isOnline) CatchUp();
            if (_connection != null) _connection.WentOnline += CatchUp;
        }

        /// <summary>Sends what waited for a connection: the best offline score, and a name changed while offline.</summary>
        void CatchUp()
        {
            if (_save == null) return;
            if (_save.PendingLeaderboardScore >= 0) _ = FlushPendingScoreAsync();
            if (!string.IsNullOrEmpty(_save.PendingPlayerName)) _ = SetPlayerNameAsync(_save.PendingPlayerName);
        }

        /// <summary>When the game is offline, asks the connection to try signing in again (it limits how often).</summary>
        async Task ReconnectIfOfflineAsync(bool userAction)
        {
            if (!_isOnline && _connection != null) await _connection.TryReconnectAsync(userAction);
        }

        // ── Submit ──────────────────────────────────────────────────────────
        /// <summary>
        /// Submits the player's score. If offline the score is saved locally and will be
        /// flushed automatically on the next online session.
        /// The dashboard "Best score" update type means UGS only stores the value if it is higher.
        /// </summary>
        /// <param name="score">The run's score.</param>
        /// <param name="previousBest">The player's all-time best BEFORE this run, or -1 if unknown.
        /// Friend "overtaken" notifications only fire when this run is a new personal best.</param>
        public async Task SubmitScoreAsync(int score, int previousBest = -1)
        {
            // Google's pre-launch robots play real runs; keep them off the leaderboard (not queued either).
            if (PlayTestDevice.IsTestLab) return;

            await ReconnectIfOfflineAsync(userAction: false);
            if (!_isOnline)
            {
                QueueForLater(score);
                return;
            }

            if (await SubmitToCloudAsync(score, previousBest))
            {
                if (_save != null && _save.PendingLeaderboardScore >= 0) _ = FlushPendingScoreAsync();
            }
            else
            {
                QueueForLater(score); // signed in but the connection dropped; sent with the next score or sign-in
            }
        }

        /// <summary>Keeps the best score that couldn't be sent, for the next successful submit or sign-in.</summary>
        void QueueForLater(int score)
        {
            if (_save == null || score <= _save.PendingLeaderboardScore) return;
            _save.PendingLeaderboardScore = score;
            LocalProgress.Save(_save);
            Debug.Log($"[LeaderboardService] Score {score} queued for later upload.");
        }

        // ── Flush pending offline score ─────────────────────────────────────
        bool _flushing;

        private async Task FlushPendingScoreAsync()
        {
            if (_flushing || _save == null || _save.PendingLeaderboardScore < 0) return;
            _flushing = true;
            try
            {
                int pending = _save.PendingLeaderboardScore;
                Debug.Log($"[LeaderboardService] Flushing queued score: {pending}");

                // Previous best unknown for a queued score; skip friend notifications to avoid spurious pushes.
                bool sent = await SubmitToCloudAsync(pending, previousBest: int.MaxValue);

                // Clear the queue only when every board took it, and only if no higher score was queued meanwhile.
                if (sent && _save.PendingLeaderboardScore == pending)
                {
                    _save.PendingLeaderboardScore = -1;
                    LocalProgress.Save(_save);
                }
            }
            finally
            {
                _flushing = false;
            }
        }

        // ── Internal submit ─────────────────────────────────────────────────
        /// <summary>Sends the score to the daily, weekly and all-time boards. True when all three took it.</summary>
        private async Task<bool> SubmitToCloudAsync(int score, int previousBest)
        {
            bool allSent = true, allTimeSent = false;
            foreach (string leaderboardId in new[] { DailyLeaderboardId, WeeklyLeaderboardId, AllTimeLeaderboardId })
            {
                try
                {
                    await LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, score);
                    Debug.Log($"[LeaderboardService] Score {score} submitted to '{leaderboardId}'.");
                    if (leaderboardId == AllTimeLeaderboardId) allTimeSent = true;
                }
                catch (Exception e)
                {
                    allSent = false;
                    Debug.LogWarning($"[LeaderboardService] Submit failed for '{leaderboardId}': " + e.Message);
                }
            }

            // The "you passed a friend" push is about the all-time board, so only once that board has the score.
            if (allTimeSent) await NotifyOvertakenFriendsAsync(score, previousBest);
            return allSent;
        }

        /// <summary>
        /// Notifies friends whose all-time score this run just passed. Only runs on a new personal best,
        /// and only for friends who were at or above the previous best, so nobody is pinged twice.
        /// </summary>
        private async Task NotifyOvertakenFriendsAsync(int score, int previousBest)
        {
            if (_friendsService == null) return;
            if (previousBest >= 0 && score <= previousBest) return; // not a new personal best

            try
            {
                var friendIds = await _friendsService.GetFriendPlayerIdsAsync();
                if (friendIds == null || friendIds.Count == 0) return;

                var friendEntries = await GetFriendsScoresAsync(LeaderboardScope.AllTime, friendIds);
                if (friendEntries == null) return; // couldn't load; try again on the next best
                var passed = new List<LeaderboardEntryData>();
                foreach (var friend in friendEntries)
                {
                    if (friend.IsCurrentPlayer || friend.Score <= 0) continue;
                    if (score <= friend.Score) continue;                          // not beaten
                    if (previousBest >= 0 && friend.Score < previousBest) continue; // were already above them

                    passed.Add(friend);
                    _ = NotificationService.Instance?.PushFriendBeatScoreAsync(friend.PlayerId, score, "AllTime");
                }

                // One toast for the whole submit, naming the strongest friend passed.
                if (passed.Count > 0)
                {
                    var names = passed.OrderByDescending(f => f.Score).Select(f => f.PlayerName).ToList();
                    NotificationService.Instance?.ShowBeatFriendsToast(names, score);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LeaderboardService] Check beaten friend scores warning: {ex.Message}");
            }
        }

        private static string GetLeaderboardId(LeaderboardScope scope)
        {
            return scope switch
            {
                LeaderboardScope.Daily   => DailyLeaderboardId,
                LeaderboardScope.Weekly  => WeeklyLeaderboardId,
                _                        => AllTimeLeaderboardId,
            };
        }

        // ── Fetch top scores ────────────────────────────────────────────────
        /// <summary>
        /// Top scores for the scope. An empty array means the board has no scores yet; null means they could not be
        /// loaded (offline or a service error), so the UI can tell "nobody has played" from "try again".
        /// </summary>
        public async Task<LeaderboardEntryData[]> GetTopScoresAsync(LeaderboardScope scope = LeaderboardScope.AllTime)
        {
            await ReconnectIfOfflineAsync(userAction: true);
            if (!_isOnline) return null;
            try
            {
                var options = new GetScoresOptions { Limit = TopCount, Offset = 0 };
                string leaderboardId = GetLeaderboardId(scope);
                var page = await LeaderboardsService.Instance.GetScoresAsync(leaderboardId, options);

                string currentId = AuthenticationService.Instance.PlayerId;
                var entries = new LeaderboardEntryData[page.Results.Count];
                Debug.Log($"[LeaderboardService] Fetched {page.Results.Count} scores from '{leaderboardId}'.");

                for (int i = 0; i < page.Results.Count; i++)
                {
                    LeaderboardEntry r = page.Results[i];
                    entries[i] = new LeaderboardEntryData
                    {
                        Rank            = r.Rank + 1, // SDK ranks are 0-indexed
                        PlayerId        = r.PlayerId,
                        PlayerName      = string.IsNullOrEmpty(r.PlayerName)
                                              ? FormatAnonymousId(r.PlayerId)
                                              : r.PlayerName,
                        Score           = r.Score,
                        IsCurrentPlayer = r.PlayerId == currentId
                    };
                }
                return entries;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardService] Fetch failed: " + e.Message);
                return null;
            }
        }

        // ── Fetch friend scores ─────────────────────────────────────────────
        /// <summary>Your score and your friends' scores, highest first. Empty = none yet; null = could not load.</summary>
        public async Task<LeaderboardEntryData[]> GetFriendsScoresAsync(
            LeaderboardScope scope = LeaderboardScope.AllTime,
            List<string> friendPlayerIds = null)
        {
            await ReconnectIfOfflineAsync(userAction: true);
            if (!_isOnline) return null;

            try
            {
                string currentId = AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn
                    ? AuthenticationService.Instance.PlayerId
                    : null;

                var targetIds = new List<string>();
                if (!string.IsNullOrEmpty(currentId))
                    targetIds.Add(currentId);

                if (friendPlayerIds != null)
                {
                    foreach (var id in friendPlayerIds)
                    {
                        if (!string.IsNullOrEmpty(id) && !targetIds.Contains(id))
                            targetIds.Add(id);
                    }
                }

                if (targetIds.Count == 0) return Array.Empty<LeaderboardEntryData>();

                string leaderboardId = GetLeaderboardId(scope);
                var resultPage = await LeaderboardsService.Instance.GetScoresByPlayerIdsAsync(leaderboardId, targetIds);

                if (resultPage == null || resultPage.Results == null || resultPage.Results.Count == 0)
                    return Array.Empty<LeaderboardEntryData>();

                // Sort results by score descending
                var sortedResults = resultPage.Results.OrderByDescending(r => r.Score).ToList();
                var entries = new LeaderboardEntryData[sortedResults.Count];

                for (int i = 0; i < sortedResults.Count; i++)
                {
                    LeaderboardEntry r = sortedResults[i];
                    entries[i] = new LeaderboardEntryData
                    {
                        Rank            = i + 1, // Relative rank among friends
                        PlayerId        = r.PlayerId,
                        PlayerName      = string.IsNullOrEmpty(r.PlayerName)
                                              ? FormatAnonymousId(r.PlayerId)
                                              : r.PlayerName,
                        Score           = r.Score,
                        IsCurrentPlayer = r.PlayerId == currentId
                    };
                }

                return entries;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardService] GetFriendsScores failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Puts a best score on the all-time board only (the board keeps the higher of the two). For the best score a
        /// phone brings along when it switches to another player: no friend pushes, and the daily and weekly boards
        /// are left alone because the score wasn't set today.
        /// </summary>
        public async Task SubmitAllTimeBestAsync(int score)
        {
            if (score <= 0 || PlayTestDevice.IsTestLab || !_isOnline) return;
            try
            {
                await LeaderboardsService.Instance.AddPlayerScoreAsync(AllTimeLeaderboardId, score);
                Debug.Log($"[LeaderboardService] Best score {score} carried over to '{AllTimeLeaderboardId}'.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardService] Carrying the best score over failed: " + e.Message);
            }
        }

        // ── Fetch current player's own entry ────────────────────────────────
        /// <summary>
        /// Returns the current player's leaderboard entry for the given scope,
        /// or null if they have no score or the service is offline.
        /// </summary>
        public async Task<LeaderboardEntryData?> GetPlayerEntryAsync(LeaderboardScope scope = LeaderboardScope.AllTime)
        {
            await ReconnectIfOfflineAsync(userAction: true);
            if (!_isOnline) return null;
            try
            {
                string leaderboardId = GetLeaderboardId(scope);
                var result = await LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId);
                if (result == null) return null;

                string currentId = AuthenticationService.Instance.PlayerId;
                return new LeaderboardEntryData
                {
                    Rank            = result.Rank + 1,
                    PlayerId        = result.PlayerId,
                    PlayerName      = string.IsNullOrEmpty(result.PlayerName)
                                          ? FormatAnonymousId(result.PlayerId)
                                          : result.PlayerName,
                    Score           = result.Score,
                    IsCurrentPlayer = true
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardService] GetPlayerEntry failed: " + e.Message);
                return null;
            }
        }

        // ── Set display name ────────────────────────────────────────────────
        /// <summary>
        /// Updates the player name on UGS Authentication. The leaderboard will
        /// reflect this name on subsequent score submissions and fetches.
        /// </summary>
        public async Task SetPlayerNameAsync(string name)
        {
            if (!_isOnline)
            {
                RememberPendingName(name); // sent when the game next signs in
                return;
            }
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(name);
                Debug.Log("[LeaderboardService] Player name updated to: " + name);
                RememberPendingName(""); // this name is the newest, whether it was typed now or waited offline
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LeaderboardService] Name update failed: " + e.Message);
            }
        }

        void RememberPendingName(string name)
        {
            if (_save == null || _save.PendingPlayerName == name) return;
            _save.PendingPlayerName = name;
            LocalProgress.Save(_save);
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        static string FormatAnonymousId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Player";
            return "Player #" + (id.Length >= 6 ? id.Substring(id.Length - 6).ToUpper() : id.ToUpper());
        }
    }
}
