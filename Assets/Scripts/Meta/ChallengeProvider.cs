using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.RemoteConfig;

namespace StackSurge.Meta
{
    /// <summary>
    /// Async service that manages the full challenge lifecycle:
    /// authentication → Remote Config fetch → today's set → Cloud Save load/save → offline fallback.
    ///
    /// Where the day's challenges come from, first match wins:
    /// 1. Challenge assets assigned in the Inspector (a fixed set, every day).
    /// 2. Remote Config "daily_challenges": a fixed set, every day.
    /// 3. Remote Config "challenge_pool", or the built-in <see cref="ChallengePool"/>: one easy, one medium and one hard
    ///    picked per UTC day, the same for every player.
    /// Finishing all of a day's challenges adds <see cref="AllDoneBonusPoints"/> to the next run.
    /// </summary>
    public class ChallengeProvider
    {
        public const int AllDoneBonusPoints = 2000; // on top of the three rewards (1,000 / 2,000 / 3,000)

        // ── Public state ──────────────────────────────────────────────
        public ChallengeDefinition[] ActiveChallenges { get; private set; } = Array.Empty<ChallengeDefinition>();
        public int[] Progress { get; private set; } = Array.Empty<int>();
        public int[] CompletionBits { get; private set; } = Array.Empty<int>();
        public bool[] Claimed { get; private set; } = Array.Empty<bool>();
        public bool IsOnline { get; private set; }
        public int PendingRewardPoints { get; private set; }

        /// <summary>True once today's all-done bonus has been added (it's added the moment the last one completes).</summary>
        public bool AllDoneBonusGranted =>
            !string.IsNullOrEmpty(_save?.ChallengeBonusDate) && _save.ChallengeBonusDate == _save.ChallengeSetDate;

        // ── Private ───────────────────────────────────────────────────
        SaveData _save;
        bool _initialised;
        ChallengeDefinition[] _fixedSet;           // set when the day's challenges don't rotate (Inspector or Remote Config)
        ChallengeDefinition[] _pool = Array.Empty<ChallengeDefinition>();

        const string CloudKey_Progress = "challenge_progress";
        const string CloudKey_Bits = "challenge_bits";
        const string CloudKey_Claimed = "challenge_claimed";
        const string CloudKey_SetDate = "challenge_set_date";
        const string CloudKey_SetIds = "challenge_set_ids";
        const string CloudKey_BonusDate = "challenge_bonus_date";

        static string TodayUtc => DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        // ── Remote Config fetch attributes (required by the API) ─────
        struct UserAttributes { }
        struct AppAttributes { }

        // ── Initialise ───────────────────────────────────────────────
        public async Task InitializeAsync(SaveData save, ChallengeDefinition[] fixedAssets)
        {
            _save = save;

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                IsOnline = true;
                Debug.Log("[ChallengeProvider] Signed in. PlayerID: " + AuthenticationService.Instance.PlayerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ChallengeProvider] Auth failed, falling back to local: " + e.Message);
                IsOnline = false;
            }

            // Where today's challenges come from, then which ones they are.
            if (fixedAssets != null && fixedAssets.Length > 0) _fixedSet = fixedAssets;
            else await FetchRemoteDefinitionsAsync();
            if (_fixedSet == null && _pool.Length == 0) _pool = ChallengePool.ToDefinitions(ChallengePool.BuiltIn());
            ActiveChallenges = PickFor(TodayUtc);

            // Load progress (cloud or local)
            await LoadProgressAsync();

            // A new day, or a different set than the saved progress belongs to, starts fresh.
            EnsureToday();

            _initialised = true;
        }

        // ── Remote Config: a fixed set or a replacement pool ─────────
        async Task FetchRemoteDefinitionsAsync()
        {
            if (!IsOnline) return;
            try
            {
                RemoteConfigService.Instance.FetchCompleted += ApplyRemoteConfig;
                await RemoteConfigService.Instance.FetchConfigsAsync(new UserAttributes(), new AppAttributes());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ChallengeProvider] Remote Config fetch failed: " + e.Message);
            }
            finally
            {
                RemoteConfigService.Instance.FetchCompleted -= ApplyRemoteConfig;
            }
        }

        void ApplyRemoteConfig(ConfigResponse response)
        {
            var fixedSet = ReadChallengeList("daily_challenges");
            if (fixedSet != null) _fixedSet = fixedSet;
            var pool = ReadChallengeList("challenge_pool");
            if (pool != null) _pool = pool;
        }

        static ChallengeDefinition[] ReadChallengeList(string key)
        {
            try
            {
                string json = RemoteConfigService.Instance.appConfig.GetJson(key, "");
                if (string.IsNullOrEmpty(json)) return null;

                // JsonUtility needs a wrapper object for arrays
                var list = JsonUtility.FromJson<ChallengeDataList>("{\"Items\":" + json + "}");
                if (list?.Items == null || list.Items.Length == 0)
                {
                    Debug.LogWarning($"[ChallengeProvider] Remote Config '{key}' parsed but has no challenges.");
                    return null;
                }
                Debug.Log($"[ChallengeProvider] Loaded {list.Items.Length} challenges from Remote Config '{key}'.");
                return ChallengePool.ToDefinitions(list.Items);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChallengeProvider] Failed to parse Remote Config '{key}': " + e.Message);
                return null;
            }
        }

        ChallengeDefinition[] PickFor(string day) => _fixedSet ?? ChallengePool.PickForDay(_pool, day);

        static string SetIds(ChallengeDefinition[] set)
        {
            var ids = new string[set.Length];
            for (int i = 0; i < set.Length; i++) ids[i] = string.IsNullOrEmpty(set[i].Id) ? set[i].Title : set[i].Id;
            return string.Join(",", ids);
        }

        // ── Load progress from Cloud Save or local ───────────────────
        async Task LoadProgressAsync()
        {
            int count = ActiveChallenges.Length;
            // Claimed points wait in the local save until the next run starts; restore them on every launch
            // (the signed-in path used to skip this, so claimed points were lost if the app restarted first).
            PendingRewardPoints = _save.PendingRewardPoints;

            if (IsOnline)
            {
                try
                {
                    var keys = new HashSet<string>
                    {
                        CloudKey_Progress,
                        CloudKey_Bits,
                        CloudKey_Claimed,
                        CloudKey_SetDate,
                        CloudKey_SetIds,
                        CloudKey_BonusDate
                    };

                    var data = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

                    Progress = DeserializeIntArray(data, CloudKey_Progress, count);
                    CompletionBits = DeserializeIntArray(data, CloudKey_Bits, count);
                    Claimed = DeserializeBoolArray(data, CloudKey_Claimed, count);

                    if (data.TryGetValue(CloudKey_SetDate, out var dateItem))
                        _save.ChallengeSetDate = dateItem.Value.GetAs<string>() ?? "";
                    if (data.TryGetValue(CloudKey_SetIds, out var idsItem))
                        _save.ChallengeSetIds = idsItem.Value.GetAs<string>() ?? "";
                    if (data.TryGetValue(CloudKey_BonusDate, out var bonusItem))
                        _save.ChallengeBonusDate = bonusItem.Value.GetAs<string>() ?? "";

                    // Sync to local save as backup
                    StoreLocally();

                    Debug.Log("[ChallengeProvider] Progress loaded from Cloud Save.");
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[ChallengeProvider] Cloud Save load failed, using local: " + e.Message);
                }
            }

            // Fallback to local
            Progress = EnsureLength(_save.ChallengeProgress, count);
            CompletionBits = EnsureLength(_save.ChallengeBits, count);
            Claimed = EnsureLength(_save.ChallengeClaimed, count); // kept locally too, so an offline claim can't repeat
        }

        // ── Daily rotation ───────────────────────────────────────────
        /// <summary>
        /// Moves to today's challenges when the UTC day has changed (also mid-session: called as each run starts), or
        /// when the saved progress belongs to a different set. Returns true when progress was reset.
        /// </summary>
        public bool EnsureToday()
        {
            if (_save == null) return false;
            string today = TodayUtc;
            if (_save.ChallengeSetDate != today) ActiveChallenges = PickFor(today);

            string ids = SetIds(ActiveChallenges);
            if (_save.ChallengeSetDate == today && _save.ChallengeSetIds == ids &&
                Progress.Length == ActiveChallenges.Length) return false;

            Debug.Log($"[ChallengeProvider] New challenge set for {today}: {ids}");
            int count = ActiveChallenges.Length;
            Progress = new int[count];
            CompletionBits = new int[count];
            Claimed = new bool[count];
            _save.ChallengeSetDate = today;
            _save.ChallengeSetIds = ids;
            StoreLocally();
            return true;
        }

        // ── Record progress for a specific challenge ─────────────────
        public void RecordProgress(int index, int value)
        {
            if (index < 0 || index >= ActiveChallenges.Length) return;
            if (CompletionBits[index] != 0) return; // already complete

            Progress[index] = Mathf.Max(Progress[index], value);

            if (Progress[index] >= ActiveChallenges[index].TargetValue)
            {
                CompletionBits[index] = 1;
                GrantAllDoneBonusIfEarned();
            }
        }

        /// <summary>The moment the day's last challenge completes, the all-done bonus waits for the next run.</summary>
        void GrantAllDoneBonusIfEarned()
        {
            if (ActiveChallenges.Length == 0 || AllDoneBonusGranted) return;
            foreach (int bit in CompletionBits)
                if (bit == 0) return;
            _save.ChallengeBonusDate = _save.ChallengeSetDate;
            AddBonusPoints(AllDoneBonusPoints);
        }

        /// <summary>Points that start the next run (claimed challenges, the all-done bonus, the 7-day streak).</summary>
        public void AddBonusPoints(int points)
        {
            if (points <= 0) return;
            PendingRewardPoints += points;
            _save.PendingRewardPoints = PendingRewardPoints;
            LocalProgress.Save(_save);
        }

        // ── Claim reward ─────────────────────────────────────────────
        public bool ClaimReward(int index)
        {
            if (index < 0 || index >= ActiveChallenges.Length) return false;
            if (CompletionBits[index] == 0 || Claimed[index]) return false;

            Claimed[index] = true;
            _save.ChallengeClaimed = (bool[])Claimed.Clone();
            AddBonusPoints(ActiveChallenges[index].RewardPoints);
            return true;
        }

        /// <summary>Consume pending reward points (call at run start).</summary>
        public int ConsumePendingRewards()
        {
            int pts = PendingRewardPoints;
            PendingRewardPoints = 0;
            _save.PendingRewardPoints = 0;
            LocalProgress.Save(_save);
            return pts;
        }

        // ── Save progress to Cloud Save + local ──────────────────────
        public async Task SaveAsync()
        {
            // Always persist locally
            StoreLocally();

            if (!IsOnline) return;

            try
            {
                var data = new Dictionary<string, object>
                {
                    { CloudKey_Progress, JsonUtility.ToJson(new IntArrayWrapper { Values = Progress }) },
                    { CloudKey_Bits, JsonUtility.ToJson(new IntArrayWrapper { Values = CompletionBits }) },
                    { CloudKey_Claimed, JsonUtility.ToJson(new BoolArrayWrapper { Values = Claimed }) },
                    { CloudKey_SetDate, _save.ChallengeSetDate },
                    { CloudKey_SetIds, _save.ChallengeSetIds ?? "" },
                    { CloudKey_BonusDate, _save.ChallengeBonusDate ?? "" }
                };

                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                Debug.Log("[ChallengeProvider] Progress saved to Cloud Save.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ChallengeProvider] Cloud Save write failed: " + e.Message);
            }
        }

        void StoreLocally()
        {
            _save.ChallengeProgress = (int[])Progress.Clone();
            _save.ChallengeBits = (int[])CompletionBits.Clone();
            _save.ChallengeClaimed = (bool[])Claimed.Clone();
            LocalProgress.Save(_save);
        }

        // ── Get display data for the UI ──────────────────────────────
        public ChallengeDisplayData[] GetDisplayData()
        {
            var result = new ChallengeDisplayData[ActiveChallenges.Length];
            for (int i = 0; i < ActiveChallenges.Length; i++)
            {
                var def = ActiveChallenges[i];
                result[i] = new ChallengeDisplayData
                {
                    Title = def.Title,
                    Description = def.Description,
                    Type = def.Type,
                    Difficulty = def.Difficulty,
                    Progress = Mathf.Min(Progress[i], def.TargetValue),
                    TargetValue = def.TargetValue,
                    Completed = CompletionBits[i] != 0,
                    Claimed = Claimed[i],
                    RewardPoints = def.RewardPoints
                };
            }
            return result;
        }

        /// <summary>
        /// Returns the time remaining until the next daily rotation (midnight UTC).
        /// </summary>
        public TimeSpan GetTimeUntilReset()
        {
            var now = DateTime.UtcNow;
            var midnight = now.Date.AddDays(1);
            return midnight - now;
        }

        // ── Helpers ──────────────────────────────────────────────────
        static T[] EnsureLength<T>(T[] arr, int len)
        {
            if (arr != null && arr.Length >= len) return arr;
            var n = new T[len];
            if (arr != null) Array.Copy(arr, n, Math.Min(arr.Length, len));
            return n;
        }

        static int[] DeserializeIntArray(Dictionary<string, Unity.Services.CloudSave.Models.Item> data, string key, int count)
        {
            if (data.TryGetValue(key, out var item))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<IntArrayWrapper>(item.Value.GetAs<string>());
                    if (wrapper?.Values != null && wrapper.Values.Length >= count)
                        return wrapper.Values;
                }
                catch { }
            }
            return new int[count];
        }

        static bool[] DeserializeBoolArray(Dictionary<string, Unity.Services.CloudSave.Models.Item> data, string key, int count)
        {
            if (data.TryGetValue(key, out var item))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<BoolArrayWrapper>(item.Value.GetAs<string>());
                    if (wrapper?.Values != null && wrapper.Values.Length >= count)
                        return wrapper.Values;
                }
                catch { }
            }
            return new bool[count];
        }

        [Serializable]
        class IntArrayWrapper { public int[] Values; }

        [Serializable]
        class BoolArrayWrapper { public bool[] Values; }
    }
}
