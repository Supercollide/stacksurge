// UGS Cloud Code Script: NotifyFriendRequestAccepted
// Sends a OneSignal push to the original sender when their request is accepted.
//
// Parameters:
//   originalSenderPlayerId (String) — UGS Player ID of the person who sent the original request
//   acceptorDisplayName    (String) — Display name of the person who accepted

const axios = require("axios");

const ONESIGNAL_APP_ID  = "f077f714-74c3-469e-b1a0-ec993de725a9";
// NOTE: The real REST API key is set only in the deployed script in the UGS Dashboard.
// This placeholder is intentional so the secret is never committed to the repo.
// If you edit this file, re-paste the real key when deploying.
const ONESIGNAL_API_KEY = "YOUR_ONESIGNAL_REST_API_KEY";

module.exports = async ({ params, context, logger }) => {
  const originalSenderPlayerId = params.originalSenderPlayerId;
  const acceptorDisplayName    = params.acceptorDisplayName || "A StackSurge player";

  if (!originalSenderPlayerId) {
    throw new Error("originalSenderPlayerId is required");
  }

  const payload = {
    app_id: ONESIGNAL_APP_ID,
    include_aliases: {
      external_id: [originalSenderPlayerId]
    },
    target_channel: "push",
    headings: { en: "Friend Request Accepted! 🎉" },
    contents: { en: `${acceptorDisplayName} accepted your friend request. You're now friends!` },
    data: {
      type: "friend_request_accepted",
      acceptorId: context.playerId
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

    logger.info(`Push sent successfully. Notification ID: ${response.data.id}`);
    return { success: true, notificationId: response.data.id };
  } catch (error) {
    const errData = error.response ? JSON.stringify(error.response.data) : error.message;
    logger.error(`OneSignal error: ${errData}`);
    throw new Error(`Push failed: ${errData}`);
  }
};
