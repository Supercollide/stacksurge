using System.Collections.Generic;
using System.Text;

namespace StackSurge.Meta
{
    /// <summary>
    /// Configuration helper for OneSignal integration.
    /// Note: Private REST API Keys MUST NEVER be embedded in client-side game code.
    /// Server-to-device dispatches go through UGS Cloud Code (see NotificationService.PushFriend*Async).
    /// </summary>
    public static class OneSignalPushHelper
    {
        // Public OneSignal App ID for StackSurge (safe to include in client builds)
        public static string AppId = "f077f714-74c3-469e-b1a0-ec993de725a9";
    }

    /// <summary>
    /// Minimal JSON Serializer for Dictionary to JSON string conversion.
    /// </summary>
    public static class MiniJson
    {
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            if (obj is string s) return "\"" + EscapeString(s) + "\"";
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int || obj is long || obj is float || obj is double) return obj.ToString();

            if (obj is IDictionary<string, object> dict)
            {
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (var kvp in dict)
                {
                    if (!first) sb.Append(",");
                    sb.Append("\"").Append(EscapeString(kvp.Key)).Append("\":").Append(Serialize(kvp.Value));
                    first = false;
                }
                sb.Append("}");
                return sb.ToString();
            }

            if (obj is System.Collections.IEnumerable list)
            {
                var sb = new StringBuilder("[");
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(",");
                    sb.Append(Serialize(item));
                    first = false;
                }
                sb.Append("]");
                return sb.ToString();
            }

            return "\"" + EscapeString(obj.ToString()) + "\"";
        }

        private static string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
