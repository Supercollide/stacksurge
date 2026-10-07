// UGS Cloud Code Script: NotifyFriendRequest
// Sends a OneSignal push to the recipient when they receive a friend request.
//
// Parameters:
//   recipientPlayerId (String) — UGS Player ID of the person receiving the request
//   senderDisplayName (String) — Display name of the person sending the request
//
// The sender is always the authenticated caller (context.playerId), never a client param.
// Returns { success: true, notificationId } or { success: false, error } (never throws).
//
// TODO(server-side checks still missing):
//   - Verify the caller and recipient actually have a pending request / are friends (UGS Friends API).
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

// Trims, flattens whitespace/control chars and caps the name at MAX_NAME_CHARS.
// The "#1234" discriminator is kept here: it tells the recipient which "Alex" sent the request.
function cleanDisplayName(raw) {
  const name = typeof raw === "string"
    ? raw.replace(/[\u0000-\u001F\u007F]+/g, " ").replace(/\s+/g, " ").trim()
    : "";
  const tagMatch = name.match(/#\d{4}$/);
  const tag = tagMatch ? tagMatch[0] : "";
  const base = Array.from(tag ? name.slice(0, -tag.length) : name)
    .slice(0, MAX_NAME_CHARS).join("").trim();
  return base ? base + tag : FALLBACK_NAME;
}

function fail(logger, error) {
  logger.error(`NotifyFriendRequest failed: ${error}`);
  return { success: false, error };
}

module.exports = async ({ params, context, logger }) => {
  const senderId          = context.playerId;
  const recipientPlayerId = typeof params.recipientPlayerId === "string" ? params.recipientPlayerId.trim() : "";
  const senderDisplayName = cleanDisplayName(params.senderDisplayName);

  if (!senderId) {
    return fail(logger, "caller must be an authenticated player");
  }
  if (!recipientPlayerId) {
    return fail(logger, "recipientPlayerId is required");
  }
  if (recipientPlayerId === senderId) {
    return fail(logger, "recipientPlayerId cannot be the caller");
  }

  const payload = {
    app_id: ONESIGNAL_APP_ID,
    include_aliases: {
      external_id: [recipientPlayerId]
    },
    target_channel: "push",
    existing_android_channel_id: ANDROID_CHANNEL_ID,
    android_group: ANDROID_GROUP,
    ttl: TTL_SECONDS,
    headings: { en: "New Friend Request 👋" },
    contents: { en: `${senderDisplayName} sent you a friend request!` },
    data: {
      type: "friend_request",
      senderId: senderId
    }
  };

  logger.info(`Sending friend request push to: ${recipientPlayerId}`);

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
