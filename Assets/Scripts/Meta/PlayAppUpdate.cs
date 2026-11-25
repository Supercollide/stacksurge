using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Google.Play.AppUpdate;
#endif

namespace StackSurge.Meta
{
    /// <summary>
    /// Google Play's in-app updates. Asks Play whether a newer version is available to this player (Play knows about
    /// staged rollouts, so it only says yes when this player can get it). If they agree in Google's prompt, Play
    /// downloads it in the background (a "flexible" update) while they keep playing, and the game restarts into it when
    /// they choose. Only works on Android, for copies installed from Google Play; elsewhere it stays at <see cref="Stage.None"/>.
    /// </summary>
    public class PlayAppUpdate : MonoBehaviour
    {
        public enum Stage { None, Available, Downloading, Ready }

        /// <summary>How often a download that was already running (started before a restart) is checked again.</summary>
        const float PollSeconds = 2f;

        public Stage Current { get; private set; }
        /// <summary>0 to 1 while downloading.</summary>
        public float Progress { get; private set; }
        public event Action Changed;

#if UNITY_ANDROID && !UNITY_EDITOR
        AppUpdateManager _manager;
        AppUpdateInfo _info;
#endif
        bool _checking, _updating;

        /// <summary>Asks Play for the update state (at launch and when the game comes back to the foreground).</summary>
        public void Check()
        {
            if (_checking || _updating) return;
            StartCoroutine(CheckRoutine());
        }

        /// <summary>Shows Google's prompt for the update and, if the player agrees, downloads it in the background.</summary>
        public void StartUpdate()
        {
            if (Current != Stage.Available || _updating) return;
            StartCoroutine(UpdateRoutine());
        }

        /// <summary>Installs the downloaded update; the game closes and opens again on the new version.</summary>
        public void Restart()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Current == Stage.Ready && _manager != null) _manager.CompleteUpdate();
#endif
        }

        IEnumerator CheckRoutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_manager == null)
            {
                try { _manager = new AppUpdateManager(); }
                catch (Exception e) { Debug.LogWarning("[PlayAppUpdate] In-app updates unavailable: " + e.Message); }
            }
            if (_manager == null) yield break;
            _checking = true;

            while (true)
            {
                var op = _manager.GetAppUpdateInfo();
                yield return op;
                if (op.Error != AppUpdateErrorCode.NoError)
                {
                    // Expected on copies not installed from Google Play, and offline.
                    Debug.Log("[PlayAppUpdate] No update info: " + op.Error);
                    break;
                }
                _info = op.GetResult();
                var status = _info.AppUpdateStatus;
                if (status == AppUpdateStatus.Downloaded)
                {
                    Set(Stage.Ready, 1f);
                    break;
                }
                if (status == AppUpdateStatus.Pending || status == AppUpdateStatus.Downloading)
                {
                    // A download from before the game restarted: follow it until it's done.
                    float progress = _info.TotalBytesToDownload > 0 ? (float)_info.BytesDownloaded / _info.TotalBytesToDownload : 0f;
                    Set(Stage.Downloading, progress);
                    yield return new WaitForSecondsRealtime(PollSeconds);
                    continue;
                }
                bool available = _info.UpdateAvailability == UpdateAvailability.UpdateAvailable
                    && _info.IsUpdateTypeAllowed(AppUpdateOptions.FlexibleAppUpdateOptions());
                Set(available ? Stage.Available : Stage.None, 0f);
                break;
            }
            _checking = false;
#else
            yield break;
#endif
        }

        IEnumerator UpdateRoutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_manager == null || _info == null) yield break;
            _updating = true;
            var request = _manager.StartUpdate(_info, AppUpdateOptions.FlexibleAppUpdateOptions());
            while (!request.IsDone)
            {
                if (request.Status == AppUpdateStatus.Downloading) Set(Stage.Downloading, request.DownloadProgress);
                else if (request.Status == AppUpdateStatus.Downloaded) Set(Stage.Ready, 1f);
                yield return null;
            }
            _updating = false;

            if (request.Status == AppUpdateStatus.Downloaded || request.Status == AppUpdateStatus.Installing)
            {
                Set(Stage.Ready, 1f);
                yield break;
            }
            // Declined in Google's prompt, or the download failed: offer it again.
            if (request.Error != AppUpdateErrorCode.NoError) Debug.Log("[PlayAppUpdate] Update not started: " + request.Error);
            Set(Stage.Available, 0f);
            Check(); // Play may say something else now (e.g. no longer available)
#else
            yield break;
#endif
        }

        void Set(Stage stage, float progress)
        {
            if (stage == Current && Mathf.Approximately(progress, Progress)) return;
            Current = stage;
            Progress = Mathf.Clamp01(progress);
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[PlayAppUpdate] Changed handler failed: " + e.Message); }
        }
    }
}
