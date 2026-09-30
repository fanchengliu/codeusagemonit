using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodeUsageMonit {
    public sealed class BotActivityDay { public string Day; public int UserMessages, AssistantMessages; }
    public sealed class BotActivity {
        public int Sessions, TodaySessions;
        public string Updated = "", Warning = "";
        public List<BotActivityDay> Days = new List<BotActivityDay>();
    }
    public static class GrokBotActivity {
        private static object SmallJson(string path) { try { return File.Exists(path) && new FileInfo(path).Length <= 65536 ? J.File(path) : null; } catch { return null; } }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        // Base32 cache filenames make ordinary transcript paths exceed MAX_PATH.
        // Use a read-only shared handle; keep the existing size limit and never modify the cache.
        internal static object ReadTranscript(string path) {
            string extended = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
            using (SafeFileHandle handle = CreateFileW(extended, 0x80000000, 7, IntPtr.Zero, 3, 0x80, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new IOException("Cannot read Grok Bot cache.");
                using (var stream = new FileStream(handle, FileAccess.Read)) {
                    if (stream.Length > 16 * 1024 * 1024) throw new IOException("Grok Bot cache exceeds the size limit.");
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true)) {
                        var text = new StringBuilder(); var buffer = new char[8192]; int n;
                        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0) {
                            if (text.Length + n > 16 * 1024 * 1024) throw new IOException("Grok Bot cache exceeds the size limit.");
                            text.Append(buffer, 0, n);
                        }
                        return J.Parse(text.ToString());
                    }
                }
            }
        }
        internal static string Base32(string input) {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
            int buffer = 0, bits = 0; var output = new StringBuilder();
            foreach (byte value in Encoding.UTF8.GetBytes(input)) { buffer = (buffer << 8) | value; bits += 8; while (bits >= 5) { bits -= 5; output.Append(alphabet[(buffer >> bits) & 31]); } }
            if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]); return output.ToString();
        }
        private static string DecodeName(string encoded) {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
            int buffer = 0, bits = 0; var output = new List<byte>();
            foreach (char ch in encoded.ToLowerInvariant()) { int value = alphabet.IndexOf(ch); if (value < 0) return ""; buffer = (buffer << 5) | value; bits += 5; if (bits >= 8) { bits -= 8; output.Add((byte)(buffer >> bits)); } }
            return Encoding.UTF8.GetString(output.ToArray());
        }
        public static BotActivity Read() {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grok Bot", "sand-client-persistence");
            return ReadDirectory(folder, DateTime.UtcNow, TimeZoneInfo.Local);
        }
        internal static BotActivity ReadDirectory(string folder, DateTime nowUtc, TimeZoneInfo zone) {
            try {
                string accountFile = Path.Combine(folder, Base32("sand.client.slice.client-meta.account-slot") + ".blob");
                string slot = J.Str(SmallJson(accountFile), "value");
                if (slot.Length == 0) return new BotActivity { Warning = "尚未找到 Grok Bot 当前账号的本机缓存。打开 Grok Bot 后再刷新。" };
                string prefix = "sand.client.slice.account." + Uri.EscapeDataString(slot) + ".transcript.replicas.";
                var documents = new List<object>(); int skipped = 0;
                foreach (string file in Directory.EnumerateFiles(folder, "*.blob").Take(500)) {
                    if (!DecodeName(Path.GetFileNameWithoutExtension(file)).StartsWith(prefix, StringComparison.Ordinal)) continue;
                    try { documents.Add(ReadTranscript(file)); } catch { skipped++; }
                }
                BotActivity result = FromTranscripts(documents, nowUtc, zone);
                if (skipped > 0) result.Warning = "部分正在写入或过大的缓存暂未读取，稍后刷新。";
                return result;
            } catch { return new BotActivity { Warning = "Grok Bot 本机缓存暂时无法读取。" }; }
        }
        public static BotActivity FromTranscripts(IEnumerable<object> documents, DateTime nowUtc, TimeZoneInfo zone) {
            var result = new BotActivity { Updated = nowUtc.ToString("o") };
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date, oldest = today.AddDays(-29);
            var days = new Dictionary<string, BotActivityDay>();
            foreach (object document in documents) {
                var seen = new HashSet<string>(); bool hasSession = false, hasToday = false;
                foreach (object entry in J.Arr(J.Get(document, "value", "entries"))) {
                    // Pending sends, UI events and secret-request widgets are not usage.
                    string role = J.Str(entry, "role"), id = J.Str(entry, "id");
                    if (J.Str(entry, "kind") != "message" || (role != "user" && role != "assistant") || id.Length == 0 || Object.Equals(J.Get(entry, "isStreaming"), true)) continue;
                    double? timestamp = J.Num(entry, "timestampMs"); if (!timestamp.HasValue) continue;
                    DateTime at; try { at = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timestamp.Value); } catch { continue; }
                    DateTime date = TimeZoneInfo.ConvertTimeFromUtc(at, zone).Date;
                    if (date < oldest || at > nowUtc || !seen.Add(id)) continue;
                    string key = HistoryService.DayKey(date); BotActivityDay day;
                    if (!days.TryGetValue(key, out day)) { day = new BotActivityDay { Day = key }; days[key] = day; }
                    if (role == "user") day.UserMessages++; else day.AssistantMessages++;
                    hasSession = true; if (date == today) hasToday = true;
                }
                if (hasSession) result.Sessions++; if (hasToday) result.TodaySessions++;
            }
            result.Days = days.Values.OrderBy(d => d.Day).ToList(); return result;
        }
    }
}
