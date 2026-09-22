// UGS Cloud Code Script: NotifyFriendBeatScore
// Sends a OneSignal push to a friend whose score was beaten.
//
// Parameters:
//   beatenFriendPlayerId (String) — UGS Player ID of the friend whose score was beaten
//   scorerDisplayName    (String) — Display name of the player who achieved the new score
//   newScore             (Numeric) — The new score value
//   leaderboardScope     (String) — "Daily", "Weekly", or "AllTime"

const axios = require("axios");

const ONESIGNAL_APP_ID  = "f077f714-74c3-469e-b1a0-ec993de725a9";
// NOTE: The real REST API key is set only in the deployed script in the UGS Dashboard.
// This placeholder is intentional so the secret is never committed to the repo.
// If you edit this file, re-paste the real key when deploying.
const ONESIGNAL_API_KEY = "YOUR_ONESIGNAL_REST_API_KEY";

module.exports = async ({ params, context, logger }) => {
  const beatenFriendPlayerId = params.beatenFriendPlayerId;
  const scorerDisplayName    = params.scorerDisplayName || "Your friend";
  const newScore             = params.newScore || 0;
  const leaderboardScope     = params.leaderboardScope || "AllTime";

  if (!beatenFriendPlayerId) {
    throw new Error("beatenFriendPlayerId is required");
  }

  const scoreStr = Number(newScore).toLocaleString();

  const payload = {
    app_id: ONESIGNAL_APP_ID,
    include_aliases: {
      external_id: [beatenFriendPlayerId]
    },
    target_channel: "push",
    headings: { en: "You've Been Overtaken! 🔥" },
    contents: { en: `${scorerDisplayName} just scored ${scoreStr} on the ${leaderboardScope} leaderboard and passed you!` },
    data: {
      type: "friend_beat_score",
      scorerId: context.playerId,
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

    logger.info(`Push sent successfully. Notification ID: ${response.data.id}`);
    return { success: true, notificationId: response.data.id };
  } catch (error) {
    const errData = error.response ? JSON.stringify(error.response.data) : error.message;
    logger.error(`OneSignal error: ${errData}`);
    throw new Error(`Push failed: ${errData}`);
  }
};
