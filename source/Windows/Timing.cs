using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CodeUsageMonit {
    // Output speed of local requests: output tokens (including thinking / reasoning) divided
    // by the request's duration, from the request being sent to its last output being
    // written. It therefore includes time to first token, network and relay latency; it is
    // what a user experiences, not a benchmark of the model. Only numbers are kept.
    //   Claude Code: the entry a response answers (its parentUuid) → the response's last block.
    //   Codex:       the last tool output / user message → the response's last item.
    //   ZCode / OpenCode: the start and completion times the client records per request.
    //   Grok:        the API duration the client records per turn.
    public static class OutputTiming {
        public const double MinOutput = 50, MinSeconds = .2, MaxSeconds = 600, MinTimedSeconds = 1;
        // Agents whose local records carry request durations.
        public static readonly string[] Agents = { "codex", "claude", "zcode", "grok", "opencode" };
        public static bool Supported(string agent) { return Array.IndexOf(Agents, agent) >= 0; }
        public const string Definition = "输出速度 = 输出 Token（含思考 / 推理）÷ 请求耗时（发出请求 → 最后一段输出写入本机日志），包含首字延迟和网络 / 中转站延迟；只统计输出 ≥ 50 Token、耗时 ≤ 10 分钟的请求。这是实际使用的快慢，不是模型基准测试。";
        // Short replies are dominated by the time to first token; stuck or retried requests
        // by waiting. Both are left out.
        public static bool Accept(double output, double seconds, double requests) {
            return requests > 0 && output >= MinOutput * requests && seconds >= MinSeconds * requests && seconds <= MaxSeconds * requests;
        }
        public static bool Accept(double output, double seconds) { return Accept(output, seconds, 1); }
        public static Bucket Of(double output, double seconds, double requests) { return new Bucket { TN = requests, TO = output, TS = seconds }; }
        public static double? Speed(double output, double seconds) { return seconds >= MinTimedSeconds && output > 0 ? output / seconds : (double?)null; }
        public static string Text(double? speed) {
            if (!speed.HasValue) return "—";
            double v = speed.Value; return (v >= 100 ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture)) + " t/s";
        }

        // ── Line prefixes read without decoding the whole line ─────────────
        private static readonly byte[] ResponseItem = Encoding.ASCII.GetBytes("\"type\":\"response_item\""), PayloadType = Encoding.ASCII.GetBytes("\"payload\":{\"type\":\""),
            Timestamp = Encoding.ASCII.GetBytes("\"timestamp\":\""), Assistant = Encoding.ASCII.GetBytes("\"role\":\"assistant\""),
            UuidKey = Encoding.ASCII.GetBytes("\"uuid\":\""), UuidThenTime = Encoding.ASCII.GetBytes("\",\"timestamp\":\""), TimeThenUuid = Encoding.ASCII.GetBytes("\",\"uuid\":\"");
        private const int CodexPrefix = 400;

        // Codex rollout line {"timestamp":…,"type":"response_item","payload":{"type":…}}:
        // tool outputs and user / developer messages start a request, model items (reasoning,
        // assistant message, *_call) belong to the response. Returns true when the line was
        // such an item (it carries no usage, so it need not be decoded).
        public static bool CodexItem(LogFile state, byte[] line, int length) {
            int limit = Math.Min(length, CodexPrefix);
            if (IndexOf(line, limit, ResponseItem, 0) < 0) return false;
            string type = ValueAfter(line, limit, PayloadType), stamp = ValueAfter(line, limit, Timestamp);
            DateTime at; if (type.Length == 0 || !LogReader.ParseTime(stamp, out at)) return true;
            long ms = LogReader.Millis(at);
            bool assistant = type == "message" && IndexOf(line, limit, Assistant, 0) >= 0;
            if (type.EndsWith("_output", StringComparison.Ordinal) || (type == "message" && !assistant)) { state.MarkMs = ms; return true; }
            if (assistant || type == "reasoning" || type.EndsWith("_call", StringComparison.Ordinal)) {
                // A new response began at the last start mark (also when the previous response
                // was never charged, e.g. an interrupted turn).
                if (state.StartMs == 0 || state.MarkMs > state.EndMs) { state.StartMs = state.MarkMs; state.EndMs = 0; }
                state.EndMs = Math.Max(state.EndMs, ms);
            }
            return true;
        }
        // The response that a usage record closes: seconds, or 0 when it was not timed.
        public static double CodexTake(LogFile state) {
            double seconds = state.StartMs > 0 && state.EndMs > state.StartMs ? (state.EndMs - state.StartMs) / 1000.0 : 0;
            state.StartMs = 0; state.EndMs = 0;
            return seconds;
        }

        // Claude Code entry: its uuid and timestamp, which sit next to each other after the
        // message ("uuid":"…","timestamp":"…" or "timestamp":"…","uuid":"…"). Quotes inside
        // message text are escaped, so only real keys match.
        public static bool UuidAndTime(byte[] line, int length, out string uuid, out long ms) {
            uuid = null; ms = 0;
            for (int i = IndexOf(line, length, UuidKey, 0); i >= 0; i = IndexOf(line, length, UuidKey, i + 1)) {
                int start = i + UuidKey.Length, end = IndexOfByte(line, length, (byte)'"', start, 80); if (end < 0) continue;
                string id = Encoding.ASCII.GetString(line, start, end - start); string stamp = null;
                if (StartsAt(line, length, end, UuidThenTime)) { int s = end + UuidThenTime.Length, e = IndexOfByte(line, length, (byte)'"', s, 48); if (e > s) stamp = Encoding.ASCII.GetString(line, s, e - s); }
                else {
                    // "timestamp":"…","uuid":"… — the timestamp ends right before this key.
                    int keyStart = i - (TimeThenUuid.Length - UuidKey.Length) - 1;
                    if (keyStart > 0 && StartsAt(line, length, keyStart + 1, TimeThenUuid)) {
                        int e = keyStart + 1, s = e; while (s > 0 && line[s - 1] != (byte)'"' && e - s < 48) s--;
                        if (s >= Timestamp.Length && StartsAt(line, length, s - Timestamp.Length, Timestamp)) stamp = Encoding.ASCII.GetString(line, s, e - s);
                    }
                }
                DateTime at; if (stamp != null && LogReader.ParseTime(stamp, out at)) { uuid = id; ms = LogReader.Millis(at); return true; }
            }
            return false;
        }

        // Per-file Claude state: recent entry times (uuid → ms) and responses still open.
        public sealed class ClaudeState {
            private const int RecentLimit = 24, OpenLimit = 16;
            private readonly List<KeyValuePair<string, long>> recent = new List<KeyValuePair<string, long>>();
            private readonly Dictionary<string, Open> open = new Dictionary<string, Open>(StringComparer.Ordinal);
            private sealed class Open { public string Key; public long Start, Last; public double Output, Charged; public double ChargedOutput, ChargedSeconds; }
            public static ClaudeState Load(LogFile file) {
                var state = new ClaudeState();
                foreach (string part in (file.Recent ?? "").Split(';')) { int eq = part.IndexOf('='); long ms; if (eq > 0 && Int64.TryParse(part.Substring(eq + 1), out ms)) state.recent.Add(new KeyValuePair<string, long>(part.Substring(0, eq), ms)); }
                foreach (string row in (file.Open ?? "").Split('\n')) {
                    string[] f = row.Split('\t'); if (f.Length < 8) continue;
                    var c = CultureInfo.InvariantCulture; long a, b; double o, n, co, cs;
                    if (!Int64.TryParse(f[2], out a) || !Int64.TryParse(f[3], out b) || !Double.TryParse(f[4], NumberStyles.Float, c, out o) || !Double.TryParse(f[5], NumberStyles.Float, c, out n) || !Double.TryParse(f[6], NumberStyles.Float, c, out co) || !Double.TryParse(f[7], NumberStyles.Float, c, out cs)) continue;
                    state.open[f[0]] = new Open { Key = f[1], Start = a, Last = b, Output = o, Charged = n, ChargedOutput = co, ChargedSeconds = cs };
                }
                return state;
            }
            public void Save(LogFile file) {
                var c = CultureInfo.InvariantCulture;
                file.Recent = String.Join(";", recent.Skip(Math.Max(0, recent.Count - RecentLimit)).Select(p => p.Key + "=" + p.Value.ToString(c)));
                file.Open = String.Join("\n", open.OrderByDescending(p => p.Value.Last).Take(OpenLimit).Select(p => String.Join("\t", new[] { p.Key, p.Value.Key, p.Value.Start.ToString(c), p.Value.Last.ToString(c), p.Value.Output.ToString("R", c), p.Value.Charged.ToString("R", c), p.Value.ChargedOutput.ToString("R", c), p.Value.ChargedSeconds.ToString("R", c) })));
            }
            public void Note(string uuid, long ms) {
                if (String.IsNullOrEmpty(uuid) || ms <= 0) return;
                recent.Add(new KeyValuePair<string, long>(uuid, ms));
                if (recent.Count > RecentLimit * 4) recent.RemoveRange(0, recent.Count - RecentLimit);
            }
            private long Time(string uuid) { for (int i = recent.Count - 1; i >= 0; i--) if (recent[i].Key == uuid) return recent[i].Value; return 0; }
            // One block of a response: the first block fixes the start (its parent entry),
            // later blocks move the end. The response is charged (or re-charged) each time.
            public void Block(LogIndex index, LogFile file, string timingKey, string bucketKey, string parent, long ms, double output, string hour) {
                Open entry;
                if (!open.TryGetValue(timingKey, out entry)) {
                    if (index.Seen.ContainsKey(timingKey)) return; // timed in another file or an earlier scan
                    index.Seen[timingKey] = hour + "\t" + file.Name;
                    long start = parent == null ? 0 : Time(parent);
                    entry = new Open { Key = bucketKey, Start = start > 0 && start <= ms ? start : -1, Last = ms, Output = output };
                    open[timingKey] = entry;
                    if (open.Count > OpenLimit * 2) foreach (string old in open.OrderBy(p => p.Value.Last).Take(open.Count - OpenLimit).Select(p => p.Key).ToList()) open.Remove(old);
                } else { entry.Last = Math.Max(entry.Last, ms); entry.Output = Math.Max(entry.Output, output); }
                if (entry.Start < 0) return;
                double seconds = (entry.Last - entry.Start) / 1000.0;
                if (entry.Charged > 0) LogIndex.Add(file, entry.Key, OutputTiming.Of(entry.ChargedOutput, entry.ChargedSeconds, entry.Charged), -1);
                entry.Charged = 0; entry.ChargedOutput = 0; entry.ChargedSeconds = 0;
                if (!Accept(entry.Output, seconds)) return;
                LogIndex.Add(file, entry.Key, OutputTiming.Of(entry.Output, seconds, 1), 1);
                entry.Charged = 1; entry.ChargedOutput = entry.Output; entry.ChargedSeconds = seconds;
            }
        }

        // ── Byte helpers ───────────────────────────────────────────────────
        private static int IndexOf(byte[] hay, int length, byte[] needle, int from) {
            for (int i = Math.Max(0, from); i <= length - needle.Length; i++) {
                if (hay[i] != needle[0]) continue;
                int j = 1; while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
        private static int IndexOfByte(byte[] hay, int length, byte value, int from, int max) {
            int end = Math.Min(length, from + max);
            for (int i = from; i < end; i++) if (hay[i] == value) return i;
            return -1;
        }
        private static bool StartsAt(byte[] hay, int length, int at, byte[] needle) {
            if (at < 0 || at + needle.Length > length) return false;
            for (int j = 0; j < needle.Length; j++) if (hay[at + j] != needle[j]) return false;
            return true;
        }
        private static string ValueAfter(byte[] line, int limit, byte[] key) {
            int i = IndexOf(line, limit, key, 0); if (i < 0) return "";
            int s = i + key.Length, e = IndexOfByte(line, limit, (byte)'"', s, 64);
            return e > s ? Encoding.ASCII.GetString(line, s, e - s) : "";
        }
    }
}
