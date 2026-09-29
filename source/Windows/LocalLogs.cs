using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeUsageMonit {
    // Hourly token counts from local session logs. Only numbers, model names and file
    // names are kept, never message content.
    public sealed class Bucket { public double T, R; }
    public sealed class LogFile {
        public string Name = "";
        public long Length, Offset, Stamp;
        public string LastTotal = "", Provider = "", Model = "";
        // Key: "yyyyMMddHH|provider|model" (UTC hour). Claude logs have no provider.
        public Dictionary<string, Bucket> Hours = new Dictionary<string, Bucket>();
    }
    public sealed class HourUsage { public DateTime Hour; public string Provider = "", Model = ""; public double Tokens, Requests; }
    public sealed class LogIndex {
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public string CoveredFrom = "", Updated = "";
        public List<LogFile> Files = new List<LogFile>();
        // Claude: message.id:requestId → hour, for de-duplication across resumed sessions.
        public Dictionary<string, string> Seen = new Dictionary<string, string>();
        private List<HourUsage> entries;
        public bool Covers(DateTime utc) { DateTime from; return Parse(CoveredFrom, out from) && from <= utc; }
        public List<HourUsage> Entries() {
            if (entries != null) return entries;
            entries = new List<HourUsage>();
            foreach (LogFile file in Files) foreach (var pair in file.Hours) {
                string[] parts = pair.Key.Split('|'); DateTime at;
                if (!DateTime.TryParseExact(parts[0], "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) continue;
                entries.Add(new HourUsage { Hour = at, Provider = parts.Length > 1 ? parts[1] : "", Model = parts.Length > 2 ? parts[2] : "", Tokens = pair.Value.T, Requests = pair.Value.R });
            }
            return entries;
        }
        public double Tokens(DateTime startUtc, DateTime endUtc) { return Tokens(startUtc, endUtc, null); }
        // Tokens recorded in [startUtc, endUtc). Hour buckets that straddle an edge are
        // weighted by overlap, so a window starting at 10:33 takes ~45% of the 10:00 bucket.
        public double Tokens(DateTime startUtc, DateTime endUtc, Func<string, bool> provider) {
            double sum = 0;
            foreach (HourUsage hour in Entries()) {
                if (provider != null && !provider(hour.Provider)) continue;
                DateTime a = hour.Hour > startUtc ? hour.Hour : startUtc, b = hour.Hour.AddHours(1) < endUtc ? hour.Hour.AddHours(1) : endUtc;
                if (b > a) sum += hour.Tokens * (b - a).TotalSeconds / 3600;
            }
            return sum;
        }
        public static bool Parse(string iso, out DateTime utc) {
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc)) { utc = utc.ToUniversalTime(); return true; }
            return false;
        }
        internal static void Add(LogFile file, DateTime when, string provider, string model, double tokens) {
            string key = when.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + "|" + provider.Replace('|', '/') + "|" + model.Replace('|', '/');
            Bucket bucket; if (!file.Hours.TryGetValue(key, out bucket)) { bucket = new Bucket(); file.Hours[key] = bucket; }
            bucket.T += tokens; bucket.R += 1;
        }
    }

    // Incremental JSONL reader shared by the Codex and Claude indexes.
    public static class LogReader {
        public const int HorizonDays = 31, RetainDays = 33;
        public static LogIndex Scan(LogIndex index, IEnumerable<string> paths, Func<FileInfo, string> keyOf, DateTime nowUtc, Func<LogIndex, LogFile, Stream, long, long> read) {
            if (index == null || index.Version != LogIndex.CurrentVersion) index = new LogIndex();
            DateTime horizon = nowUtc.AddDays(-HorizonDays), updated;
            // Any file changed since the previous scan has a write time after it. If that
            // scan is older than the horizon, changes may have been missed: start over.
            bool continuous = LogIndex.Parse(index.Updated, out updated) && updated >= horizon && index.Covers(nowUtc);
            if (!continuous) { index = new LogIndex(); index.CoveredFrom = horizon.ToString("o"); }
            var byName = new Dictionary<string, LogFile>(StringComparer.OrdinalIgnoreCase);
            foreach (LogFile file in index.Files) byName[file.Name] = file;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths) {
                FileInfo info;
                try { info = new FileInfo(path); if (!info.Exists || info.LastWriteTimeUtc < horizon) continue; } catch { continue; }
                string name = keyOf(info); if (!seen.Add(name)) continue;
                LogFile state; if (!byName.TryGetValue(name, out state)) { state = new LogFile { Name = name }; byName[name] = state; index.Files.Add(state); }
                long stamp = info.LastWriteTimeUtc.Ticks;
                if (state.Length == info.Length && state.Stamp == stamp) continue;
                if (info.Length < state.Offset) { state.Offset = 0; state.LastTotal = ""; state.Provider = ""; state.Model = ""; state.Hours.Clear(); }
                try {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan)) {
                        long length = stream.Length; stream.Seek(state.Offset, SeekOrigin.Begin);
                        state.Offset = read(index, state, stream, state.Offset); state.Length = length; state.Stamp = stamp;
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            // Deleted session files keep their counted hours, so usage does not shrink
            // when a client prunes old threads.
            string cutoff = nowUtc.AddDays(-RetainDays).ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
            foreach (LogFile file in index.Files) foreach (string key in file.Hours.Keys.Where(k => String.CompareOrdinal(k, cutoff) < 0).ToList()) file.Hours.Remove(key);
            index.Files.RemoveAll(f => f.Hours.Count == 0 && !seen.Contains(f.Name));
            foreach (string key in index.Seen.Where(p => String.CompareOrdinal(p.Value, cutoff) < 0).Select(p => p.Key).ToList()) index.Seen.Remove(key);
            DateTime covered; if (LogIndex.Parse(index.CoveredFrom, out covered) && covered < nowUtc.AddDays(-RetainDays)) index.CoveredFrom = nowUtc.AddDays(-RetainDays).ToString("o");
            index.Updated = nowUtc.ToString("o");
            return index;
        }
        public static IEnumerable<string> Files(string folder) {
            if (!Directory.Exists(folder)) return new string[0];
            try { return Directory.GetFiles(folder, "*.jsonl", SearchOption.AllDirectories); } catch { return new string[0]; }
        }
        // Reads complete lines from the current position and returns the byte offset after
        // the last complete line; an unterminated tail is left for the next scan. Lines
        // longer than maxLine (huge tool outputs) are skipped without buffering.
        public static long Read(Stream stream, long offset, int maxLine, Func<byte[], int, bool> wanted, Action<string> consume) {
            byte[] buffer = new byte[1 << 20], line = new byte[16384];
            int lineLength = 0, read; bool overflow = false; long position = offset, committed = offset;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                int start = 0;
                while (start < read) {
                    int newline = Array.IndexOf(buffer, (byte)10, start, read - start);
                    int end = newline < 0 ? read : newline, count = end - start;
                    if (!overflow) {
                        if (lineLength + count > maxLine) overflow = true;
                        else {
                            if (lineLength + count > line.Length) Array.Resize(ref line, Math.Max(line.Length * 2, lineLength + count));
                            Buffer.BlockCopy(buffer, start, line, lineLength, count); lineLength += count;
                        }
                    }
                    position += count;
                    if (newline < 0) break;
                    position += 1;
                    if (!overflow && lineLength > 0 && wanted(line, lineLength)) { try { consume(Encoding.UTF8.GetString(line, 0, lineLength)); } catch { } }
                    lineLength = 0; overflow = false; committed = position; start = newline + 1;
                }
            }
            return committed;
        }
        public static bool Contains(byte[] haystack, int length, byte[] needle) {
            for (int i = 0; i <= length - needle.Length; i++) {
                if (haystack[i] != needle[0]) continue;
                int j = 1; while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }
    }

    // ~/.codex/sessions and archived_sessions. Counting rule (matches ccusage 20.0.26
    // token-for-token on full days): each event_msg/token_count with
    // info.last_token_usage adds input_tokens (already includes cached input) +
    // output_tokens; a token_count repeating the previous total is a duplicate.
    // session_meta.model_provider tells official ("openai") from custom endpoints.
    public static class CodexLogs {
        public const string Official = "openai";
        private const int MaxLine = 1 << 20;
        private static readonly byte[][] Needles = { Encoding.ASCII.GetBytes("\"token_count\""), Encoding.ASCII.GetBytes("\"session_meta\""), Encoding.ASCII.GetBytes("\"turn_context\"") };
        public static LogIndex Scan(LogIndex index, DateTime nowUtc) {
            string home = ProviderService.CodexHome();
            var paths = LogReader.Files(Path.Combine(home, "sessions")).Concat(LogReader.Files(Path.Combine(home, "archived_sessions")));
            // Keyed by file name: archiving moves a rollout file without renaming it.
            return LogReader.Scan(index, paths, info => info.Name, nowUtc, (idx, state, stream, offset) => Read(state, stream, offset));
        }
        public static long Read(LogFile state, Stream stream, long offset) {
            return LogReader.Read(stream, offset, MaxLine, (bytes, length) => Needles.Any(n => LogReader.Contains(bytes, length, n)), line => Consume(state, line));
        }
        public static bool IsOfficial(string provider) { return provider.Length == 0 || provider == Official; }
        public static void Consume(LogFile state, string line) {
            object root; try { root = J.Parse(line); } catch { return; }
            string type = J.Str(root, "type"); object payload = J.Get(root, "payload");
            if (type == "session_meta") { string provider = J.Str(payload, "model_provider"); if (provider.Length > 0) state.Provider = provider; return; }
            if (type == "turn_context") { string model = J.Str(payload, "model"); if (model.Length > 0) state.Model = model; return; }
            if (J.Str(payload, "type") != "token_count") return;
            object info = J.Get(payload, "info"), last = J.Get(info, "last_token_usage");
            if (last == null) return;
            object total = J.Get(info, "total_token_usage");
            string key = String.Join("|", new[] { "input_tokens", "cached_input_tokens", "output_tokens", "reasoning_output_tokens", "total_tokens" }.Select(k => J.Str(total, k)));
            if (key == state.LastTotal) return;
            state.LastTotal = key;
            DateTime when; if (!LogIndex.Parse(J.Str(root, "timestamp"), out when)) return;
            double tokens = (J.Num(last, "input_tokens") ?? 0) + (J.Num(last, "output_tokens") ?? 0);
            if (tokens <= 0) return;
            LogIndex.Add(state, when, state.Provider.Length == 0 ? Official : state.Provider, state.Model, tokens);
        }
    }

    // Claude Code projects/**/*.jsonl. Tokens = input + output + cache write + cache read
    // (ccusage's totalTokens); duplicates share message.id + requestId (or timestamp).
    public static class ClaudeLogs {
        public const string NoRequestId = "no-request-id";
        private const int MaxLine = 16 << 20;
        private static readonly byte[] Assistant = Encoding.ASCII.GetBytes("\"type\":\"assistant\""), Usage = Encoding.ASCII.GetBytes("\"usage\":{");
        public static IEnumerable<string> ConfigDirs() {
            string custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!String.IsNullOrWhiteSpace(custom)) return custom.Split(',').Select(p => Environment.ExpandEnvironmentVariables(p.Trim())).Where(p => p.Length > 0).ToList();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] { Path.Combine(home, ".claude"), Path.Combine(home, ".config", "claude") };
        }
        public static LogIndex Scan(LogIndex index, DateTime nowUtc) {
            var paths = ConfigDirs().SelectMany(dir => LogReader.Files(Path.Combine(dir, "projects"))).ToList();
            return LogReader.Scan(index, paths, info => info.FullName.ToLowerInvariant(), nowUtc, (idx, state, stream, offset) => Read(idx, state, stream, offset));
        }
        public static long Read(LogIndex index, LogFile state, Stream stream, long offset) {
            return LogReader.Read(stream, offset, MaxLine, (bytes, length) => LogReader.Contains(bytes, length, Usage) && LogReader.Contains(bytes, length, Assistant), line => Consume(index, state, line));
        }
        public static void Consume(LogIndex index, LogFile state, string line) {
            object root; try { root = J.Parse(line); } catch { return; }
            if (J.Str(root, "type") != "assistant") return;
            object message = J.Get(root, "message"), usage = J.Get(message, "usage");
            if (usage == null) return;
            string model = J.Str(message, "model");
            if (model.Length == 0 || model == "<synthetic>") return;
            DateTime when; if (!LogIndex.Parse(J.Str(root, "timestamp"), out when)) return;
            // Many relays do not pass Anthropic's request-id through; then the duplicate
            // copies of a message share id + timestamp (same rule as ccusage 20.0.26).
            string id = J.Str(message, "id"), request = J.Str(root, "requestId");
            if (id.Length > 0) {
                string key = id + ":" + (request.Length > 0 ? request : J.Str(root, "timestamp"));
                if (index.Seen.ContainsKey(key)) return;
                index.Seen[key] = when.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
            }
            double tokens = (J.Num(usage, "input_tokens") ?? 0) + (J.Num(usage, "output_tokens") ?? 0) + (J.Num(usage, "cache_creation_input_tokens") ?? 0) + (J.Num(usage, "cache_read_input_tokens") ?? 0);
            if (tokens <= 0) return;
            // Anthropic always returns a request-id; its absence is a strong hint of a relay.
            LogIndex.Add(state, when, request.Length > 0 ? "" : NoRequestId, model, tokens);
        }
    }
}
