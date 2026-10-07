// UGS Cloud Code Script: NotifyFriendRequestAccepted
// Sends a OneSignal push to the original sender when their request is accepted.
//
// Parameters:
//   originalSenderPlayerId (String) — UGS Player ID of the person who sent the original request
//   acceptorDisplayName    (String) — Display name of the person who accepted
//
// The acceptor is always the authenticated caller (context.playerId), never a client param.
// Returns { success: true, notificationId } or { success: false, error } (never throws).
//
// TODO(server-side checks still missing):
//   - Verify the caller and recipient are actually friends now (UGS Friends API).
//   - Read the recipient's social_prefs opt-out from Cloud Save and skip opted-out players.
//   - Rate-limit per (sender, recipient, type).

const axios = require("axios");

const ONESIGNAL_APP_ID  = "f077f714-74c3-469e-b1a0-ec993de725a9";
// NOTE: The real REST API key is set only in the deployed script in the UGS Dashboard.
// This placeholder is intentional so the secret is never committed to the repo.
// If you edit this file, re-paste the real key when deploying.
const ONESIGNAL_API_KEY = "YOUR_ONESIGNAL_REST_API_KEY";

const ANDROID_CHANNEL_ID = "friends_activity"; // must match the channel the client registers
const ANDROID_GROUP      = "stacksurge_social";
const TTL_SECONDS        = 86400;              // drop the push if it can't be delivered within 24 h
const MAX_NAME_CHARS     = 32;
const FALLBACK_NAME      = "A StackSurge player";

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

function fail(logger, error) {
  logger.error(`NotifyFriendRequestAccepted failed: ${error}`);
  return { success: false, error };
}

module.exports = async ({ params, context, logger }) => {
  const acceptorId             = context.playerId;
  const originalSenderPlayerId = typeof params.originalSenderPlayerId === "string" ? params.originalSenderPlayerId.trim() : "";
  const acceptorDisplayName    = friendlyName(params.acceptorDisplayName);

  if (!acceptorId) {
    return fail(logger, "caller must be an authenticated player");
  }
  if (!originalSenderPlayerId) {
    return fail(logger, "originalSenderPlayerId is required");
  }
  if (originalSenderPlayerId === acceptorId) {
    return fail(logger, "originalSenderPlayerId cannot be the caller");
  }

  const payload = {
    app_id: ONESIGNAL_APP_ID,
    include_aliases: {
      external_id: [originalSenderPlayerId]
    },
    target_channel: "push",
    existing_android_channel_id: ANDROID_CHANNEL_ID,
    android_group: ANDROID_GROUP,
    ttl: TTL_SECONDS,
    headings: { en: "Friend Request Accepted! 🎉" },
    contents: { en: `${acceptorDisplayName} accepted your friend request. You're now friends!` },
    data: {
      type: "friend_request_accepted",
      acceptorId: acceptorId
    }
  };

  logger.info(`Sending accepted push to: ${originalSenderPlayerId}`);

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
