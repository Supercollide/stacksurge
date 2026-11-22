using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace StackSurge.Meta
{
    /// <summary>
    /// Google Play Games sign-in (Android devices only). It produces the one-time server auth code that Unity
    /// Authentication trades for the player's Google identity, to sign in with it or link it to the current player.
    /// Off in the Editor, on iOS, and until the plugin's setup has an app ID and a web client ID
    /// (Window > Google Play Games > Setup > Android setup, which writes Resources/PlayGamesSettings).
    /// </summary>
    public static class PlayGamesSignIn
    {
        public enum Outcome { SignedIn, Canceled, Failed, Unavailable }

        public readonly struct Result
        {
            public readonly Outcome Outcome;
            /// <summary>One-time code for Unity Authentication; only set when <see cref="Outcome"/> is SignedIn.</summary>
            public readonly string AuthCode;

            public Result(Outcome outcome, string authCode = null)
            {
                Outcome = outcome;
                AuthCode = authCode;
            }
        }

        /// <summary>The player's Play Games name once signed in this session, otherwise "".</summary>
        public static string DisplayName { get; private set; } = "";

        public static bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var settings = PlayGamesSettings.LoadInstance();
                return settings != null && !string.IsNullOrEmpty(settings.AppId) && !string.IsNullOrEmpty(settings.WebClientId);
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Signs in to Play Games and gets a server auth code. With <paramref name="interactive"/> false it only takes
        /// the automatic sign-in Play Games does at launch (no UI); true shows Google's sign-in, for a button the player taps.
        /// </summary>
        public static async Task<Result> SignInAsync(bool interactive)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAvailable) return new Result(Outcome.Unavailable);
            try
            {
                var platform = PlayGamesPlatform.Activate();
                var signIn = new TaskCompletionSource<SignInStatus>();
                if (interactive) platform.ManuallyAuthenticate(status => signIn.TrySetResult(status));
                else platform.Authenticate(status => signIn.TrySetResult(status));
                var status = await signIn.Task;
                if (status == SignInStatus.Canceled) return new Result(Outcome.Canceled);
                if (status != SignInStatus.Success)
                {
                    Debug.LogWarning("[PlayGamesSignIn] Play Games sign-in failed: " + status);
                    return new Result(Outcome.Failed);
                }
                DisplayName = platform.GetUserDisplayName() ?? "";

                var code = new TaskCompletionSource<string>();
                platform.RequestServerSideAccess(true, authCode => code.TrySetResult(authCode));
                string serverCode = await code.Task;
                if (string.IsNullOrEmpty(serverCode))
                {
                    Debug.LogWarning("[PlayGamesSignIn] Play Games gave no server auth code (check the web client ID).");
                    return new Result(Outcome.Failed);
                }
                return new Result(Outcome.SignedIn, serverCode);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesSignIn] Play Games sign-in failed: " + e.Message);
                return new Result(Outcome.Failed);
            }
#else
            await Task.CompletedTask;
            return new Result(Outcome.Unavailable);
#endif
        }
    }
}
