// UGS Cloud Code Script: NotifyFriendBeatScore
// Sends a OneSignal push to a friend whose score was beaten.
//
// Parameters:
//   beatenFriendPlayerId (String) — UGS Player ID of the friend whose score was beaten
//   scorerDisplayName    (String) — Display name of the player who achieved the new score
//   newScore             (Numeric) — The new score value (non-negative integer)
//   leaderboardScope     (String) — "Daily", "Weekly", or "AllTime" (anything else -> "AllTime")
//
// The scorer is always the authenticated caller (context.playerId), never a client param.
// Returns { success: true, notificationId } or { success: false, error } (never throws).
//
// TODO(server-side checks still missing):
//   - Verify the caller and recipient are actually friends (UGS Friends API).
//   - Read the recipient's social_prefs opt-out from Cloud Save and skip opted-out players.
//   - Rate-limit per (sender, recipient, type).

const axios = require("axios");

const ONESIGNAL_APP_ID  = "f077f714-74c3-469e-b1a0-ec993de725a9";
// NOTE: The real REST API key is set only in the deployed script in the UGS Dashboard.
// This placeholder is intentional so the secret is never committed to the repo.
// If you edit this file, re-paste the real key when deploying.
const ONESIGNAL_API_KEY = "YOUR_ONESIGNAL_REST_API_KEY";

const ANDROID_CHANNEL_ID = "rivalry";           // must match the channel the client registers
const ANDROID_GROUP      = "stacksurge_social";
const TTL_SECONDS        = 21600;               // a 6 h old "you've been passed" is no longer worth showing
const MAX_NAME_CHARS     = 32;
const FALLBACK_NAME      = "Your friend";
const DEFAULT_SCOPE      = "AllTime";
const SCOPE_LABELS       = { AllTime: "all-time", Weekly: "weekly", Daily: "daily" };

// Trims, flattens whitespace/control chars, strips a trailing "#1234" discriminator
// and caps the name at MAX_NAME_CHARS. "Alex#4821" -> "Alex".
function friendlyName(raw) {
  const name = typeof raw === "string"
    ? raw.replace(/[\u0000-\u001F\u007F]+/g, " ").replace(/\s+/g, " ").trim()
    : "";
  const base = Array.from(name.replace(/#\d{4}$/, "").trim())
    .slice(0, MAX_NAME_CHARS).join("").trim();
  return base || FALLBACK_NAME;
}

// 1234567 -> "1,234,567" (no dependence on the runtime's ICU/locale data).
function formatThousands(n) {
  return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

function fail(logger, error) {
  logger.error(`NotifyFriendBeatScore failed: ${error}`);
  return { success: false, error };
}

module.exports = async ({ params, context, logger }) => {
  const scorerId             = context.playerId;
  const beatenFriendPlayerId = typeof params.beatenFriendPlayerId === "string" ? params.beatenFriendPlayerId.trim() : "";
  const scorerDisplayName    = friendlyName(params.scorerDisplayName);
  const newScore             = params.newScore;
  const rawScope             = typeof params.leaderboardScope === "string" ? params.leaderboardScope.trim() : "";
  const leaderboardScope     = Object.prototype.hasOwnProperty.call(SCOPE_LABELS, rawScope) ? rawScope : DEFAULT_SCOPE;
  const scopeLabel           = SCOPE_LABELS[leaderboardScope];

  if (!scorerId) {
    return fail(logger, "caller must be an authenticated player");
  }
  if (!beatenFriendPlayerId) {
    return fail(logger, "beatenFriendPlayerId is required");
  }
  if (beatenFriendPlayerId === scorerId) {
    return fail(logger, "beatenFriendPlayerId cannot be the caller");
  }
  if (typeof newScore !== "number" || !Number.isSafeInteger(newScore) || newScore < 0) {
    return fail(logger, `newScore must be a non-negative integer (got ${JSON.stringify(newScore)})`);
  }

  const payload = {
    app_id: ONESIGNAL_APP_ID,
    include_aliases: {
      external_id: [beatenFriendPlayerId]
    },
    target_channel: "push",
    existing_android_channel_id: ANDROID_CHANNEL_ID,
    android_group: ANDROID_GROUP,
    ttl: TTL_SECONDS,
    // Repeated beats from the same friend replace each other instead of piling up.
    collapse_id: `friend_beat_score_${scorerId}`,
    headings: { en: "You've been passed!" },
    contents: { en: `${scorerDisplayName} just beat your ${scopeLabel} best with ${formatThousands(newScore)}. Take it back?` },
    data: {
      type: "friend_beat_score",
      scorerId: scorerId,
      score: newScore,
      scope: leaderboardScope
    }
  };

  logger.info(`Sending beat-score push to: ${beatenFriendPlayerId}`);

  try {
    const response = await axios.post("https://onesignal.com/api/v1/notifications", payload, {
      headers: {
        "Content-Type": "application/json",
        "Authorization": `Basic ${ONESIGNAL_API_KEY}`
      }
    });

    // OneSignal answers 200 with an empty id and/or an `errors` field when nothing was delivered
    // (e.g. the recipient has no subscribed device).
    const data = response.data || {};
    if (!data.id || data.errors) {
      return fail(logger, `OneSignal did not deliver: ${JSON.stringify(data.errors || data)}`);
    }

    logger.info(`Push sent successfully. Notification ID: ${data.id}`);
    return { success: true, notificationId: data.id };
  } catch (error) {
    const errData = error.response ? JSON.stringify(error.response.data) : error.message;
    return fail(logger, `OneSignal error: ${errData}`);
  }
};
