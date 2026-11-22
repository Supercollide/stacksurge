using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;

namespace StackSurge.Meta
{
    /// <summary>
    /// Whether the Unity player is linked to a Google Play Games account, and the sign-in that links it (Android).
    /// A linked player comes back on a new phone or after a reinstall, because signing in with the same Play Games
    /// account signs in to the same player. The Settings row shows <see cref="State"/> and its button calls
    /// <see cref="SignInFromButtonAsync"/>.
    /// </summary>
    public class PlayGamesAccount
    {
        public enum LinkState
        {
            /// <summary>Not Android, or Play Games isn't set up in this build. Settings hides the row.</summary>
            Unavailable,
            SignedOut,
            SigningIn,
            /// <summary>This player is linked to the Play Games account.</summary>
            SignedIn,
            Failed,
            /// <summary>The Play Games account already belongs to another player (an older install or another phone).</summary>
            LinkedElsewhere,
        }

        /// <summary>What the switch dialog shows for one player.</summary>
        public struct PlayerSummary
        {
            public string Name;
            public int BestScore;
            /// <summary>The badge on their profile ("chain:3"), or "".</summary>
            public string Badge;
        }

        /// <summary>Remembers that this phone's player is linked, so Settings can say so while offline.</summary>
        const string LinkedPrefKey = "playgames_linked";
        /// <summary>The player ID that chose to stay this phone's player rather than switch; the dialog then doesn't open by itself.</summary>
        const string KeptPrefKey = "playgames_kept_player";
        /// <summary>Sign-in profile for the short look at the other player, so this phone's saved session isn't replaced.</summary>
        const string PeekProfile = "playgames_peek";

        readonly IOnlineConnection _connection;
        readonly Action _signedOut;
        bool _checkedThisLaunch;
        bool _busy;

        public LinkState State { get; private set; }

        /// <summary>The Play Games name, known once Play Games has signed in this session; otherwise "".</summary>
        public string DisplayName => PlayGamesSignIn.DisplayName;

        public event Action Changed;

        /// <summary>The player asked to choose between the two players (the Settings row's Choose button).</summary>
        public event Action ChoiceRequested;

        /// <summary>This phone now plays as the Play Games account's player. Everything tied to the player has to reload.</summary>
        public event Action PlayerSwitched;

        /// <summary>Another player was signed in for a moment (the look before the dialog); this phone's player is back.</summary>
        public event Action PlayerRestored;

        /// <summary>True while a sign-in, the look at the other player or a switch is running.</summary>
        public bool IsBusy => _busy || State == LinkState.SigningIn;

        /// <summary>This phone's player chose to stay, so the dialog only opens when the player asks for it.</summary>
        public bool KeptThisPlayer
        {
            get
            {
                try
                {
                    var auth = AuthenticationService.Instance;
                    return auth.IsSignedIn && PlayerPrefs.GetString(KeptPrefKey, "") == auth.PlayerId;
                }
                catch (Exception) { return false; }
            }
        }

        /// <param name="signedOut">Called when the game is left signed out (a failed switch), so it counts as offline and reconnects later.</param>
        public PlayGamesAccount(IOnlineConnection connection, Action signedOut = null)
        {
            _connection = connection;
            _signedOut = signedOut;
            if (!PlayGamesSignIn.IsAvailable) State = LinkState.Unavailable;
            else State = PlayerPrefs.GetInt(LinkedPrefKey, 0) == 1 ? LinkState.SignedIn : LinkState.SignedOut;
        }

        /// <summary>
        /// For a new install (no saved Unity session): if Play Games signed in by itself, signs in to Unity with that
        /// account, which brings back the player linked to it or creates one. False when that isn't possible.
        /// </summary>
        public async Task<bool> TrySignInWithPlayGamesAsync()
        {
            if (State == LinkState.Unavailable) return false;
            bool finishingSwitch = State == LinkState.LinkedElsewhere; // a switch that lost the connection halfway
            var result = await PlayGamesSignIn.SignInAsync(interactive: false);
            if (result.Outcome != PlayGamesSignIn.Outcome.SignedIn) return false;
            try
            {
                await AuthenticationService.Instance.SignInWithGooglePlayGamesAsync(result.AuthCode);
                Debug.Log("[PlayGamesAccount] Signed in with Play Games.");
                Set(LinkState.SignedIn);
                if (finishingSwitch)
                {
                    try { PlayerSwitched?.Invoke(); }
                    catch (Exception e) { Debug.LogWarning("[PlayGamesAccount] PlayerSwitched handler failed: " + e.Message); }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesAccount] Play Games sign-in was refused, signing in anonymously: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// After the Unity sign-in: finds out whether this player is linked and, if not, links it when Play Games
        /// signed in by itself. Silent (no UI, a failure leaves it signed out) and once per launch.
        /// </summary>
        public async Task CheckLinkAsync()
        {
            if (State == LinkState.Unavailable || IsBusy || _checkedThisLaunch) return;
            _checkedThisLaunch = true;
            try
            {
                if (await IsLinkedAsync()) { Set(LinkState.SignedIn); return; }
                if (State == LinkState.SignedIn) Set(LinkState.SignedOut); // the remembered link is gone (unlinked elsewhere)

                var result = await PlayGamesSignIn.SignInAsync(interactive: false);
                if (result.Outcome == PlayGamesSignIn.Outcome.SignedIn && State != LinkState.SigningIn)
                    await LinkAsync(result.AuthCode, silent: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesAccount] Checking the Play Games link failed: " + e.Message);
            }
        }

        /// <summary>The Settings button: shows Google's sign-in, then links this player to that account.</summary>
        public async Task SignInFromButtonAsync()
        {
            if (State == LinkState.Unavailable || IsBusy) return;
            Set(LinkState.SigningIn);

            var result = await PlayGamesSignIn.SignInAsync(interactive: true);
            if (result.Outcome == PlayGamesSignIn.Outcome.Canceled) { Set(LinkState.SignedOut); return; }
            if (result.Outcome != PlayGamesSignIn.Outcome.SignedIn) { Set(LinkState.Failed); return; }

            try
            {
                // The game may still be offline since launch; a reconnect can itself sign in with Play Games.
                if (!AuthenticationService.Instance.IsSignedIn && (_connection == null || !await _connection.TryReconnectAsync(userAction: true)))
                {
                    Set(LinkState.Failed);
                    return;
                }
                if (await IsLinkedAsync()) { Set(LinkState.SignedIn); return; }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesAccount] Play Games sign-in failed: " + e.Message);
                Set(LinkState.Failed);
                return;
            }
            await LinkAsync(result.AuthCode, silent: false);
        }

        public void RequestChoice()
        {
            if (State != LinkState.LinkedElsewhere) return;
            try { ChoiceRequested?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[PlayGamesAccount] ChoiceRequested handler failed: " + e.Message); }
        }

        /// <summary>
        /// Reads the player the Play Games account is linked to: signs in to it under a separate profile, runs
        /// <paramref name="read"/> while it's signed in, then signs back in to this phone's player (its saved session).
        /// Null when the other player couldn't be reached.
        /// </summary>
        public async Task<PlayerSummary?> PeekLinkedPlayerAsync(Func<Task<PlayerSummary>> read)
        {
            if (State != LinkState.LinkedElsewhere || _busy) return null;
            _busy = true;
            var auth = AuthenticationService.Instance;
            string home = auth.Profile;
            bool signedOut = false;
            PlayerSummary? summary = null;
            try
            {
                var result = await PlayGamesSignIn.SignInAsync(interactive: false);
                if (result.Outcome != PlayGamesSignIn.Outcome.SignedIn) return null;

                auth.SignOut(); // keeps this phone's session, so it can sign back in below
                signedOut = true;
                auth.SwitchProfile(PeekProfile);
                await auth.SignInWithGooglePlayGamesAsync(result.AuthCode);
                summary = await read();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesAccount] Couldn't read the other player: " + e.Message);
            }
            finally
            {
                if (signedOut) await SignBackInAsync(home);
                _busy = false;
            }
            return summary;
        }

        async Task SignBackInAsync(string home)
        {
            var auth = AuthenticationService.Instance;
            try
            {
                if (auth.IsSignedIn) auth.SignOut(clearCredentials: true); // drops the look's session
                if (auth.Profile != home) auth.SwitchProfile(home);
                await auth.SignInAnonymouslyAsync(); // this phone's saved session: the same player as before
                Debug.Log("[PlayGamesAccount] Back to this phone's player.");
                try { PlayerRestored?.Invoke(); }
                catch (Exception e) { Debug.LogWarning("[PlayGamesAccount] PlayerRestored handler failed: " + e.Message); }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesAccount] Couldn't sign back in to this phone's player: " + e.Message);
                _signedOut?.Invoke();
            }
        }

        /// <summary>
        /// Makes the Play Games account's player this phone's player. This phone's own (unlinked) player is let go:
        /// its saved session is cleared. True when the switch is done; on false the game is offline and the next
        /// sign-in, finding no saved session, signs in with Play Games.
        /// </summary>
        public async Task<bool> SwitchToLinkedPlayerAsync()
        {
            if (State != LinkState.LinkedElsewhere || _busy) return false;
            _busy = true;
            try
            {
                var result = await PlayGamesSignIn.SignInAsync(interactive: false);
                if (result.Outcome != PlayGamesSignIn.Outcome.SignedIn) return false;

                var auth = AuthenticationService.Instance;
                try
                {
                    if (auth.IsSignedIn) auth.SignOut();
                    auth.ClearSessionToken();
                    await auth.SignInWithGooglePlayGamesAsync(result.AuthCode);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PlayGamesAccount] Switch failed: " + e.Message);
                    if (!auth.IsSignedIn) _signedOut?.Invoke();
                    return false;
                }

                Debug.Log("[PlayGamesAccount] Switched to the Play Games account's player.");
                PlayerPrefs.DeleteKey(KeptPrefKey);
                Set(LinkState.SignedIn);
                try { PlayerSwitched?.Invoke(); }
                catch (Exception e) { Debug.LogWarning("[PlayGamesAccount] PlayerSwitched handler failed: " + e.Message); }
                return true;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>The player chose to stay this phone's player; the dialog won't open by itself again for it.</summary>
        public void KeepThisPlayer()
        {
            try { PlayerPrefs.SetString(KeptPrefKey, AuthenticationService.Instance.PlayerId ?? ""); }
            catch (Exception e) { Debug.LogWarning("[PlayGamesAccount] Couldn't remember the choice: " + e.Message); }
        }

        async Task LinkAsync(string authCode, bool silent)
        {
            try
            {
                await AuthenticationService.Instance.LinkWithGooglePlayGamesAsync(authCode);
                Debug.Log("[PlayGamesAccount] Linked this player to Play Games.");
                Set(LinkState.SignedIn);
            }
            catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                Debug.Log("[PlayGamesAccount] The Play Games account is linked to another player.");
                Set(LinkState.LinkedElsewhere);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayGamesAccount] Linking Play Games failed: " + e.Message);
                Set(silent ? LinkState.SignedOut : LinkState.Failed);
            }
        }

        static async Task<bool> IsLinkedAsync()
        {
            var auth = AuthenticationService.Instance;
            if (!auth.IsSignedIn) return false;
            if (!string.IsNullOrEmpty(auth.PlayerInfo?.GetGooglePlayGamesId())) return true;
            await auth.GetPlayerInfoAsync();
            return !string.IsNullOrEmpty(auth.PlayerInfo?.GetGooglePlayGamesId());
        }

        void Set(LinkState state)
        {
            if (state == LinkState.SignedIn) PlayerPrefs.SetInt(LinkedPrefKey, 1);
            else if (state == LinkState.SignedOut || state == LinkState.LinkedElsewhere) PlayerPrefs.DeleteKey(LinkedPrefKey);
            State = state; // raised even when unchanged: the Play Games name may have arrived
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[PlayGamesAccount] Changed handler failed: " + e.Message); }
        }
    }
}
