using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.CloudSave.Models.Data.Player;
using Unity.Services.Friends.Models;
using Unity.Services.Friends.Notifications;
using UnityEngine;

// Alias to avoid name collision between StackSurge.Meta.FriendsService and Unity.Services.Friends.FriendsService
using UgsFriendsService = Unity.Services.Friends.FriendsService;
using SaveOptions = Unity.Services.CloudSave.Models.Data.Player.SaveOptions;
using LoadOptions = Unity.Services.CloudSave.Models.Data.Player.LoadOptions;

namespace StackSurge.Meta
{
    public enum PresenceStatus
    {
        Offline,
        Online,
        InGame,
        Busy
    }

    public struct FriendData
    {
        public string RelationshipId;
        public string PlayerId;
        public string PlayerName;
        public PresenceStatus Status;
        public string Activity;
    }

    public struct FriendRequestData
    {
        public string RelationshipId;
        public string PlayerId;
        public string PlayerName;
        public bool IsIncoming;
    }

    /// <summary>How the local player relates to another player. Used by the leaderboard to pick the row button state.</summary>
    public enum FriendRelationState
    {
        None,
        Friend,
        OutgoingPending,
        IncomingPending,
        Blocked,
        Self
    }

    public enum FriendRequestResult
    {
        Sent,
        AlreadyFriends,
        AlreadyPending,
        TargetDisallows,
        Blocked,
        NotFound,
        Failed
    }

    /// <summary>
    /// Publicly readable social preferences stored in Cloud Save (public access class)
    /// so other clients can honour them before contacting this player.
    /// </summary>
    [Serializable]
    public class SocialPrefs
    {
        public bool allowFriendRequests = true;
        public bool notifyFriendActivity = true;
    }

    /// <summary>
    /// Async service wrapper for Unity Gaming Services (UGS) Friends.
    /// Handles friend listing, sending/accepting requests, removing friends, blocking users,
    /// presence tracking and public social preferences. Includes offline mock data for testing.
    /// </summary>
    public class FriendsService
    {
        public const string SocialPrefsCloudSaveKey = "social_prefs";

        private static readonly TimeSpan RefreshThrottle = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan PrefsCacheTtl = TimeSpan.FromSeconds(60);

        private readonly bool _isOnline;
        private readonly SaveData _save;
        private bool _isInitialized = false;
        private Task _initTask;
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        private Task _refreshTask;

        // Member IDs whose accept/send we handled locally, so the echo event does not toast twice.
        private readonly HashSet<string> _locallyHandledMembers = new HashSet<string>();

        // Cache of other players' public social prefs.
        private readonly Dictionary<string, (SocialPrefs prefs, DateTime fetchedUtc)> _prefsCache =
            new Dictionary<string, (SocialPrefs, DateTime)>();

        public event Action OnFriendsUpdated;

        // Mock storage (offline mode)
        private readonly List<FriendData> _mockFriends = new List<FriendData>();
        private readonly List<FriendRequestData> _mockRequests = new List<FriendRequestData>();
        private readonly List<string> _mockBlocked = new List<string>();

        public FriendsService(bool isOnline, SaveData save)
        {
            _isOnline = isOnline;
            _save = save;

            if (!_isOnline)
            {
                InitializeMockData();
            }
        }

        private bool IsLive => _isOnline && _isInitialized && UgsFriendsService.Instance != null;

        // ── Initialization ──────────────────────────────────────────────────
        /// <summary>
        /// Initializes the UGS Friends SDK once. Concurrent callers share the same in-flight task
        /// so the SDK events are never subscribed twice.
        /// </summary>
        public Task EnsureInitializedAsync()
        {
            if (!_isOnline || _isInitialized) return Task.CompletedTask;
            if (_initTask != null) return _initTask;

            _initTask = InitializeInternalAsync();
            return _initTask;
        }

        private async Task InitializeInternalAsync()
        {
            try
            {
                if (UgsFriendsService.Instance != null && AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                {
                    await UgsFriendsService.Instance.InitializeAsync();
                    _isInitialized = true;
                    _lastRefreshUtc = DateTime.UtcNow;
                    SubscribeToServiceEvents();
                    Debug.Log("[FriendsService] UGS Friends Service initialized successfully.");

                    _ = PublishPresenceAsync(PresenceStatus.Online);
                    _ = PublishSocialPrefsAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] Initialization failed (falling back to local mode): " + e.Message);
            }
            finally
            {
                _initTask = null;
            }
        }

        /// <summary>
        /// Re-fetches the relationship list from the server. The SDK only refreshes its cache on
        /// InitializeAsync (first call) and on realtime events, so any event missed while the app
        /// was backgrounded would otherwise be invisible until restart.
        /// </summary>
        /// <param name="force">Bypass the throttle window.</param>
        public async Task RefreshAsync(bool force = false)
        {
            await EnsureInitializedAsync();
            if (!IsLive) return;

            if (!force && DateTime.UtcNow - _lastRefreshUtc < RefreshThrottle) return;

            if (_refreshTask != null)
            {
                await _refreshTask;
                return;
            }

            _refreshTask = RefreshInternalAsync();
            try { await _refreshTask; }
            finally { _refreshTask = null; }
        }

        private async Task RefreshInternalAsync()
        {
            try
            {
                await UgsFriendsService.Instance.ForceRelationshipsRefreshAsync();
                _lastRefreshUtc = DateTime.UtcNow;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] Refresh failed: " + e.Message);
            }
        }

        // ── Presence ────────────────────────────────────────────────────────
        /// <summary>
        /// Publishes the local player's presence availability to UGS Friends.
        /// Online on sign-in / resume, Offline on pause / quit. No-op in offline mode.
        /// </summary>
        public async Task PublishPresenceAsync(PresenceStatus status)
        {
            if (!IsLive) return;

            try
            {
                Availability availability = status switch
                {
                    PresenceStatus.Online => Availability.Online,
                    PresenceStatus.Busy   => Availability.Busy,
                    PresenceStatus.InGame => Availability.Online,
                    _                     => Availability.Offline
                };

                await UgsFriendsService.Instance.SetPresenceAvailabilityAsync(availability);
                Debug.Log($"[FriendsService] Presence published: {status}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] PublishPresence failed: " + e.Message);
            }
        }

        // ── Realtime events ─────────────────────────────────────────────────
        private void SubscribeToServiceEvents()
        {
            try
            {
                UgsFriendsService.Instance.RelationshipAdded += OnRelationshipAdded;
                UgsFriendsService.Instance.RelationshipDeleted += OnRelationshipDeleted;
                UgsFriendsService.Instance.PresenceUpdated += OnPresenceUpdated;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] Failed to subscribe to UGS Friends events: " + e.Message);
            }
        }

        private void OnRelationshipAdded(IRelationshipAddedEvent e)
        {
            _ = HandleRelationshipAddedAsync(e?.Relationship);
        }

        private async Task HandleRelationshipAddedAsync(Relationship eventRel)
        {
            if (eventRel == null) return;
            Debug.Log($"[FriendsService] Relationship added event: {eventRel.Type} ({eventRel.Id})");

            // The event payload's Member can be the local player rather than the other party,
            // so re-fetch and resolve the relationship from the authoritative list.
            await RefreshAsync(force: true);

            Relationship rel = FindRelationshipById(eventRel.Id) ?? eventRel;
            string myId = GetCurrentPlayerId();
            string otherId = rel.Member?.Id;
            string otherName = DisplayNameFor(rel.Member);

            if (string.IsNullOrEmpty(otherId) || otherId == myId)
            {
                // Could not resolve the other party; still refresh the UI but skip any toast.
                OnFriendsUpdated?.Invoke();
                return;
            }

            bool handledLocally = _locallyHandledMembers.Remove(otherId);

            switch (rel.Type)
            {
                case RelationshipType.FriendRequest:
                {
                    bool isIncoming = UgsFriendsService.Instance.IncomingFriendRequests?.Any(r => r.Id == rel.Id) ?? false;
                    if (!isIncoming) break; // Echo of our own outgoing request.

                    if (_save != null && !_save.AllowFriendRequests)
                    {
                        // Enforce "no friend requests" on our side regardless of the sender's client.
                        _ = AutoDeclineAsync(otherId);
                        return;
                    }

                    NotificationService.Instance?.ShowIncomingFriendRequestToast(otherName);
                    break;
                }
                case RelationshipType.Friend:
                    if (!handledLocally)
                        NotificationService.Instance?.ShowNowFriendsToast(otherName);
                    break;
            }

            OnFriendsUpdated?.Invoke();
        }

        private async Task AutoDeclineAsync(string memberId)
        {
            try
            {
                await UgsFriendsService.Instance.DeleteIncomingFriendRequestAsync(memberId);
                Debug.Log($"[FriendsService] Auto-declined friend request from {memberId} (requests disabled).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] Auto-decline failed: " + e.Message);
            }
            OnFriendsUpdated?.Invoke();
        }

        private void OnRelationshipDeleted(IRelationshipDeletedEvent e)
        {
            Debug.Log("[FriendsService] Relationship deleted event received.");
            OnFriendsUpdated?.Invoke();
        }

        private void OnPresenceUpdated(IPresenceUpdatedEvent e)
        {
            OnFriendsUpdated?.Invoke();
        }

        private Relationship FindRelationshipById(string id)
        {
            if (string.IsNullOrEmpty(id) || !IsLive) return null;
            return UgsFriendsService.Instance.Relationships?.FirstOrDefault(r => r.Id == id);
        }

        // ── Identity ────────────────────────────────────────────────────────
        public string GetCurrentPlayerId()
        {
            if (_isOnline && AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                return AuthenticationService.Instance.PlayerId;
            }
            return _save != null ? "OfflineLocalPlayer" : "GuestPlayer";
        }

        /// <summary>Full UGS player name including the discriminator (Name#1234), or a fallback.</summary>
        public string GetCurrentPlayerName()
        {
            if (_isOnline && AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                string n = AuthenticationService.Instance.PlayerName;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return _save != null && !string.IsNullOrEmpty(_save.PlayerDisplayName) ? _save.PlayerDisplayName : "A StackSurge Player";
        }

        // ── Relationship snapshot (sync, from cache) ────────────────────────
        /// <summary>Synchronous lookup against the SDK cache. Call RefreshAsync first if freshness matters.</summary>
        public FriendRelationState GetRelationState(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return FriendRelationState.None;
            if (playerId == GetCurrentPlayerId()) return FriendRelationState.Self;

            if (!IsLive)
            {
                if (_mockBlocked.Contains(playerId)) return FriendRelationState.Blocked;
                if (_mockFriends.Any(f => f.PlayerId == playerId)) return FriendRelationState.Friend;
                var req = _mockRequests.FirstOrDefault(r => r.PlayerId == playerId);
                if (!string.IsNullOrEmpty(req.PlayerId)) return req.IsIncoming ? FriendRelationState.IncomingPending : FriendRelationState.OutgoingPending;
                return FriendRelationState.None;
            }

            var svc = UgsFriendsService.Instance;
            if (svc.Blocks?.Any(r => r.Member?.Id == playerId) ?? false) return FriendRelationState.Blocked;
            if (svc.Friends?.Any(r => r.Member?.Id == playerId) ?? false) return FriendRelationState.Friend;
            if (svc.OutgoingFriendRequests?.Any(r => r.Member?.Id == playerId) ?? false) return FriendRelationState.OutgoingPending;
            if (svc.IncomingFriendRequests?.Any(r => r.Member?.Id == playerId) ?? false) return FriendRelationState.IncomingPending;
            return FriendRelationState.None;
        }

        // ── Fetch Friends List ──────────────────────────────────────────────
        public async Task<List<FriendData>> GetFriendsAsync()
        {
            await RefreshAsync();

            if (!IsLive)
                return new List<FriendData>(_mockFriends);

            try
            {
                var friendsList = new List<FriendData>();
                IReadOnlyList<Relationship> relationships = UgsFriendsService.Instance.Friends;
                if (relationships == null) return friendsList;

                foreach (var rel in relationships)
                {
                    var member = rel.Member;
                    PresenceStatus status = PresenceStatus.Offline;
                    if (member?.Presence != null)
                        status = ParsePresenceStatus(member.Presence.Availability);

                    friendsList.Add(new FriendData
                    {
                        RelationshipId = rel.Id,
                        PlayerId = member?.Id ?? "Unknown",
                        PlayerName = DisplayNameFor(member),
                        Status = status,
                        Activity = status == PresenceStatus.Offline ? "Offline" : "Online"
                    });
                }
                return friendsList;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] GetFriends failed: " + e.Message);
                return new List<FriendData>();
            }
        }

        public async Task<List<string>> GetFriendPlayerIdsAsync()
        {
            var friends = await GetFriendsAsync();
            return friends.Select(f => f.PlayerId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        }

        // ── Fetch Incoming & Outgoing Requests ──────────────────────────────
        public async Task<List<FriendRequestData>> GetIncomingRequestsAsync()
        {
            await RefreshAsync();

            if (!IsLive)
                return _mockRequests.FindAll(r => r.IsIncoming);

            try
            {
                return MapRequests(UgsFriendsService.Instance.IncomingFriendRequests, isIncoming: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] GetIncomingRequests failed: " + e.Message);
                return new List<FriendRequestData>();
            }
        }

        public async Task<List<FriendRequestData>> GetOutgoingRequestsAsync()
        {
            await RefreshAsync();

            if (!IsLive)
                return _mockRequests.FindAll(r => !r.IsIncoming);

            try
            {
                return MapRequests(UgsFriendsService.Instance.OutgoingFriendRequests, isIncoming: false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] GetOutgoingRequests failed: " + e.Message);
                return new List<FriendRequestData>();
            }
        }

        private List<FriendRequestData> MapRequests(IReadOnlyList<Relationship> rels, bool isIncoming)
        {
            var list = new List<FriendRequestData>();
            if (rels == null) return list;
            foreach (var rel in rels)
            {
                list.Add(new FriendRequestData
                {
                    RelationshipId = rel.Id,
                    PlayerId = rel.Member?.Id ?? "Unknown",
                    PlayerName = DisplayNameFor(rel.Member),
                    IsIncoming = isIncoming
                });
            }
            return list;
        }

        // ── Add / Request Friend ────────────────────────────────────────────
        /// <summary>
        /// Sends a friend request to a player by full name (Name#1234) or Player ID.
        /// Honours the target's public "allow friend requests" preference.
        /// </summary>
        public async Task<FriendRequestResult> SendFriendRequestAsync(string targetInput)
        {
            if (string.IsNullOrWhiteSpace(targetInput)) return FriendRequestResult.Failed;
            string cleanInput = targetInput.Trim();

            await EnsureInitializedAsync();

            if (!IsLive)
            {
                _mockRequests.Add(new FriendRequestData
                {
                    RelationshipId = "mock_req_" + UnityEngine.Random.Range(100, 999),
                    PlayerId = cleanInput,
                    PlayerName = cleanInput,
                    IsIncoming = false
                });
                OnFriendsUpdated?.Invoke();
                return FriendRequestResult.Sent;
            }

            // Pre-checks when the input is a player ID we already know.
            switch (GetRelationState(cleanInput))
            {
                case FriendRelationState.Self:            return FriendRequestResult.Failed;
                case FriendRelationState.Friend:          return FriendRequestResult.AlreadyFriends;
                case FriendRelationState.OutgoingPending: return FriendRequestResult.AlreadyPending;
                case FriendRelationState.Blocked:         return FriendRequestResult.Blocked;
                case FriendRelationState.IncomingPending:
                    // They already asked us; accepting is the right action.
                    return await AcceptFriendRequestAsync(cleanInput) ? FriendRequestResult.Sent : FriendRequestResult.Failed;
            }

            bool looksLikePlayerId = !cleanInput.Contains('#') && cleanInput.Length > 16 && !cleanInput.Contains(' ');
            if (looksLikePlayerId && !await TargetAllowsRequestsAsync(cleanInput))
                return FriendRequestResult.TargetDisallows;

            Relationship created = null;
            try
            {
                if (cleanInput.Contains('#'))
                {
                    created = await UgsFriendsService.Instance.AddFriendByNameAsync(cleanInput);
                }
                else
                {
                    try { created = await UgsFriendsService.Instance.AddFriendAsync(cleanInput); }
                    catch when (!looksLikePlayerId)
                    {
                        // Might be a name typed without the discriminator; UGS requires the full form.
                        created = await UgsFriendsService.Instance.AddFriendByNameAsync(cleanInput);
                    }
                }
            }
            catch (Unity.Services.Friends.Exceptions.FriendsServiceException fe)
            {
                Debug.LogWarning($"[FriendsService] SendFriendRequest to '{cleanInput}' failed: {fe.ErrorCode} {fe.Message}");
                return fe.ErrorCode switch
                {
                    Unity.Services.Friends.Exceptions.FriendsErrorCode.FriendshipAlreadyExists   => FriendRequestResult.AlreadyFriends,
                    Unity.Services.Friends.Exceptions.FriendsErrorCode.RelationshipAlreadyExists  => FriendRequestResult.AlreadyPending,
                    Unity.Services.Friends.Exceptions.FriendsErrorCode.ActionUnauthorizedWhenBlocked => FriendRequestResult.Blocked,
                    Unity.Services.Friends.Exceptions.FriendsErrorCode.RelationshipNotFound       => FriendRequestResult.NotFound,
                    _ => fe.StatusCode == System.Net.HttpStatusCode.NotFound ? FriendRequestResult.NotFound : FriendRequestResult.Failed
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FriendsService] SendFriendRequest to '{cleanInput}' failed: " + e.Message);
                return FriendRequestResult.Failed;
            }

            string targetId = created?.Member?.Id;

            // If we only learned the target's ID now (added by name), check their preference and roll back if needed.
            if (!looksLikePlayerId && !string.IsNullOrEmpty(targetId) && !await TargetAllowsRequestsAsync(targetId))
            {
                try { await UgsFriendsService.Instance.DeleteOutgoingFriendRequestAsync(targetId); }
                catch (Exception e) { Debug.LogWarning("[FriendsService] Rollback of disallowed request failed: " + e.Message); }
                await RefreshAsync(force: true);
                OnFriendsUpdated?.Invoke();
                return FriendRequestResult.TargetDisallows;
            }

            // The server may have auto-accepted (they had already requested us).
            if (created != null && created.Type == RelationshipType.Friend)
            {
                _locallyHandledMembers.Add(targetId);
                NotificationService.Instance?.ShowNowFriendsToast(DisplayNameFor(created.Member));
                _ = NotificationService.Instance?.PushFriendRequestAcceptedAsync(targetId);
            }
            else if (!string.IsNullOrEmpty(targetId))
            {
                _ = NotificationService.Instance?.PushFriendRequestAsync(targetId, GetCurrentPlayerName());
            }

            Debug.Log($"[FriendsService] Friend request sent to: {cleanInput}");
            await RefreshAsync(force: true);
            OnFriendsUpdated?.Invoke();
            return FriendRequestResult.Sent;
        }

        // ── Accept Request ──────────────────────────────────────────────────
        public async Task<bool> AcceptFriendRequestAsync(string requesterPlayerId)
        {
            if (string.IsNullOrWhiteSpace(requesterPlayerId)) return false;

            await EnsureInitializedAsync();

            if (!IsLive)
            {
                int index = _mockRequests.FindIndex(r => r.RelationshipId == requesterPlayerId || r.PlayerId == requesterPlayerId);
                if (index < 0) return false;
                var req = _mockRequests[index];
                _mockRequests.RemoveAt(index);
                _mockFriends.Add(new FriendData
                {
                    RelationshipId = "mock_rel_" + UnityEngine.Random.Range(100, 999),
                    PlayerId = req.PlayerId,
                    PlayerName = req.PlayerName,
                    Status = PresenceStatus.Online,
                    Activity = "Just connected"
                });
                NotificationService.Instance?.ShowNowFriendsToast(req.PlayerName);
                OnFriendsUpdated?.Invoke();
                return true;
            }

            try
            {
                // Resolve the requester's display name before the request disappears from the cache.
                var incoming = UgsFriendsService.Instance.IncomingFriendRequests?.FirstOrDefault(r => r.Member?.Id == requesterPlayerId);
                string requesterName = DisplayNameFor(incoming?.Member) ?? FormatPlayerId(requesterPlayerId);

                _locallyHandledMembers.Add(requesterPlayerId);

                // In UGS Friends, AddFriendAsync with the requester's ID accepts their pending request.
                await UgsFriendsService.Instance.AddFriendAsync(requesterPlayerId);
                Debug.Log($"[FriendsService] Friend request accepted from: {requesterPlayerId}");

                NotificationService.Instance?.ShowNowFriendsToast(requesterName);
                _ = NotificationService.Instance?.PushFriendRequestAcceptedAsync(requesterPlayerId);

                await RefreshAsync(force: true);
                OnFriendsUpdated?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                _locallyHandledMembers.Remove(requesterPlayerId);
                Debug.LogWarning($"[FriendsService] AcceptFriendRequest '{requesterPlayerId}' failed: " + e.Message);
                return false;
            }
        }

        // ── Decline / Cancel / Remove ───────────────────────────────────────
        public async Task<bool> DeleteRelationshipAsync(string relationshipId)
        {
            if (string.IsNullOrWhiteSpace(relationshipId)) return false;

            await EnsureInitializedAsync();

            if (!IsLive)
            {
                _mockFriends.RemoveAll(f => f.RelationshipId == relationshipId);
                _mockRequests.RemoveAll(r => r.RelationshipId == relationshipId);
                OnFriendsUpdated?.Invoke();
                return true;
            }

            try
            {
                await UgsFriendsService.Instance.DeleteRelationshipAsync(relationshipId);
                Debug.Log($"[FriendsService] Relationship deleted: {relationshipId}");
                await RefreshAsync(force: true);
                OnFriendsUpdated?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FriendsService] DeleteRelationship '{relationshipId}' failed: " + e.Message);
                return false;
            }
        }

        // ── Blocked Users ───────────────────────────────────────────────────
        public async Task<List<FriendData>> GetBlockedUsersAsync()
        {
            await RefreshAsync();

            if (!IsLive)
            {
                return _mockBlocked.Select(pid => new FriendData
                {
                    RelationshipId = "blocked_" + pid,
                    PlayerId = pid,
                    PlayerName = pid,
                    Status = PresenceStatus.Offline,
                    Activity = "Blocked"
                }).ToList();
            }

            try
            {
                var blockedList = new List<FriendData>();
                IReadOnlyList<Relationship> blocks = UgsFriendsService.Instance.Blocks;
                if (blocks == null) return blockedList;
                foreach (var rel in blocks)
                {
                    blockedList.Add(new FriendData
                    {
                        RelationshipId = rel.Id,
                        PlayerId = rel.Member?.Id ?? "Unknown",
                        PlayerName = DisplayNameFor(rel.Member),
                        Status = PresenceStatus.Offline,
                        Activity = "Blocked"
                    });
                }
                return blockedList;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] GetBlockedUsers failed: " + e.Message);
                return new List<FriendData>();
            }
        }

        public async Task<bool> BlockUserAsync(string targetPlayerId)
        {
            if (string.IsNullOrWhiteSpace(targetPlayerId)) return false;

            await EnsureInitializedAsync();

            if (!IsLive)
            {
                if (!_mockBlocked.Contains(targetPlayerId))
                {
                    _mockBlocked.Add(targetPlayerId);
                    _mockFriends.RemoveAll(f => f.PlayerId == targetPlayerId);
                    _mockRequests.RemoveAll(r => r.PlayerId == targetPlayerId);
                }
                OnFriendsUpdated?.Invoke();
                return true;
            }

            try
            {
                await UgsFriendsService.Instance.AddBlockAsync(targetPlayerId);
                Debug.Log($"[FriendsService] User blocked: {targetPlayerId}");
                await RefreshAsync(force: true);
                OnFriendsUpdated?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FriendsService] BlockUser '{targetPlayerId}' failed: " + e.Message);
                return false;
            }
        }

        public async Task<bool> UnblockUserAsync(string targetPlayerId)
        {
            if (string.IsNullOrWhiteSpace(targetPlayerId)) return false;

            await EnsureInitializedAsync();

            if (!IsLive)
            {
                _mockBlocked.Remove(targetPlayerId);
                OnFriendsUpdated?.Invoke();
                return true;
            }

            try
            {
                await UgsFriendsService.Instance.DeleteBlockAsync(targetPlayerId);
                Debug.Log($"[FriendsService] User unblocked: {targetPlayerId}");
                await RefreshAsync(force: true);
                OnFriendsUpdated?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FriendsService] UnblockUser '{targetPlayerId}' failed: " + e.Message);
                return false;
            }
        }

        // ── Public social preferences (Cloud Save, public access) ───────────
        /// <summary>Writes the local player's social preferences where any other player can read them.</summary>
        public async Task PublishSocialPrefsAsync()
        {
            if (!_isOnline || _save == null) return;
            if (AuthenticationService.Instance == null || !AuthenticationService.Instance.IsSignedIn) return;

            try
            {
                var prefs = new SocialPrefs
                {
                    allowFriendRequests = _save.AllowFriendRequests,
                    notifyFriendActivity = _save.NotifyFriendActivity
                };
                var data = new Dictionary<string, object> { { SocialPrefsCloudSaveKey, prefs } };
                await CloudSaveService.Instance.Data.Player.SaveAsync(data, new SaveOptions(new PublicWriteAccessClassOptions()));
                Debug.Log($"[FriendsService] Social prefs published (allowRequests={prefs.allowFriendRequests}, notifyFriends={prefs.notifyFriendActivity}).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsService] PublishSocialPrefs failed: " + e.Message);
            }
        }

        /// <summary>Reads another player's public social preferences. Defaults to permissive when unavailable.</summary>
        public async Task<SocialPrefs> GetSocialPrefsAsync(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return new SocialPrefs();

            if (_prefsCache.TryGetValue(playerId, out var cached) && DateTime.UtcNow - cached.fetchedUtc < PrefsCacheTtl)
                return cached.prefs;

            var prefs = new SocialPrefs();
            if (!_isOnline) return prefs;

            try
            {
                var keys = new HashSet<string> { SocialPrefsCloudSaveKey };
                Dictionary<string, Item> result = await CloudSaveService.Instance.Data.Player.LoadAsync(keys, new LoadOptions(new PublicReadAccessClassOptions(playerId)));
                if (result != null && result.TryGetValue(SocialPrefsCloudSaveKey, out var item) && item?.Value != null)
                {
                    prefs = item.Value.GetAs<SocialPrefs>() ?? new SocialPrefs();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FriendsService] GetSocialPrefs for {playerId} failed: " + e.Message);
            }

            _prefsCache[playerId] = (prefs, DateTime.UtcNow);
            return prefs;
        }

        /// <summary>Fetches several players' public preferences in parallel.</summary>
        public async Task<Dictionary<string, SocialPrefs>> GetSocialPrefsBatchAsync(IEnumerable<string> playerIds)
        {
            var ids = playerIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList() ?? new List<string>();
            var tasks = ids.Select(GetSocialPrefsAsync).ToArray();
            var results = await Task.WhenAll(tasks);
            var map = new Dictionary<string, SocialPrefs>();
            for (int i = 0; i < ids.Count; i++) map[ids[i]] = results[i];
            return map;
        }

        public async Task<bool> TargetAllowsRequestsAsync(string playerId)
        {
            var prefs = await GetSocialPrefsAsync(playerId);
            return prefs == null || prefs.allowFriendRequests;
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static PresenceStatus ParsePresenceStatus(Availability availability)
        {
            return availability switch
            {
                Availability.Online => PresenceStatus.Online,
                Availability.Busy   => PresenceStatus.Busy,
                Availability.Away   => PresenceStatus.Online,
                _                   => PresenceStatus.Offline
            };
        }

        /// <summary>UGS profile name (Name#1234) when available, else a shortened player ID.</summary>
        private static string DisplayNameFor(Member member)
        {
            if (member == null) return null;
            return !string.IsNullOrEmpty(member.Profile?.Name) ? member.Profile.Name : FormatPlayerId(member.Id);
        }

        private static string FormatPlayerId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Player";
            if (id.Length > 16 && !id.Contains(' '))
                return "player#" + id.Substring(id.Length - 5).ToUpper();
            return id;
        }

        private void InitializeMockData()
        {
            _mockFriends.Add(new FriendData { RelationshipId = "mock_rel_1", PlayerId = "player_alpha_99", PlayerName = "StackMaster#1001", Status = PresenceStatus.Online, Activity = "Online" });
            _mockFriends.Add(new FriendData { RelationshipId = "mock_rel_2", PlayerId = "player_beta_42", PlayerName = "SurgeKing#2002", Status = PresenceStatus.InGame, Activity = "Online" });
            _mockFriends.Add(new FriendData { RelationshipId = "mock_rel_3", PlayerId = "player_gamma_07", PlayerName = "BlockBuster#3003", Status = PresenceStatus.Offline, Activity = "Offline" });
            _mockRequests.Add(new FriendRequestData { RelationshipId = "mock_req_1", PlayerId = "player_delta_12", PlayerName = "ChallengerPro#4004", IsIncoming = true });
            _mockRequests.Add(new FriendRequestData { RelationshipId = "mock_req_2", PlayerId = "player_epsilon_88", PlayerName = "CasualStacker#5005", IsIncoming = false });
        }
    }
}
