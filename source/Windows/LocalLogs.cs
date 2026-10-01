using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeUsageMonit {
    // Hourly usage from local agent logs: tokens by kind, requests and the API-equivalent
    // cost of each request at the time it was read. Only numbers, model names and file
    // names are stored, never message content.
    public sealed class Bucket {
        // I fresh input, C cache read, W cache write, O output, X other tokens (e.g. hidden
        // reasoning counted separately), R requests, D USD, U tokens that had no price.
        // TN / TO / TS: requests with a measured duration, their output tokens and seconds
        // (output speed = TO / TS, request sent → last output written, see OutputTiming).
        public double I, C, W, O, X, R, D, U, TN, TO, TS;
        public double Tokens() { return I + C + W + O + X; }
        public void Add(Bucket other, double sign) { I += sign * other.I; C += sign * other.C; W += sign * other.W; O += sign * other.O; X += sign * other.X; R += sign * other.R; D += sign * other.D; U += sign * other.U; TN += sign * other.TN; TO += sign * other.TO; TS += sign * other.TS; }
        // Output tokens per second of the timed requests, or null without enough of them.
        public double? Speed() { return OutputTiming.Speed(TO, TS); }
    }
    public sealed class LogFile {
        public string Name = "";
        public long Length, Offset, Stamp;
        // Parser state carried between incremental reads (Codex: totals, model, tier, fork replay).
        public string LastTotal = "", Provider = "", Model = "", Tier = "", Replay = "", Parent = "";
        public bool Fallback;
        // Output timing carried between reads. Codex: last request start, current response
        // start and end (epoch ms). Claude: recent entry times and open messages (compact text).
        public long MarkMs, StartMs, EndMs;
        public string Recent = "", Open = "";
        // Key: "yyyyMMddHH|provider|model" (UTC hour).
        public Dictionary<string, Bucket> Hours = new Dictionary<string, Bucket>();
    }
    public sealed class HourUsage {
        // Endpoint: null on buckets without one (other agents, official Codex); "" official,
        // "?" before endpoint tracking began, else "host/fingerprint" of the third-party endpoint.
        public DateTime Hour; public string Provider = "", Model = "", Endpoint;
        public double Tokens, Requests, Cost, Input, Cached, CacheWrite, Output, Other, Unpriced, TimedRequests, TimedOutput, TimedSeconds;
    }
    public sealed class LogIndex {
        public const int CurrentVersion = 5;
        public int Version = CurrentVersion;
        public string CoveredFrom = "", Updated = "", Pricing = "";
        public List<LogFile> Files = new List<LogFile>();
        // De-duplication keys → "yyyyMMddHH \t owner file …" (the hour prefix lets old keys
        // expire; the owner lets a truncated file give back the keys it had claimed).
        public Dictionary<string, string> Seen = new Dictionary<string, string>();
        private List<HourUsage> entries;
        public bool Covers(DateTime utc) { DateTime from; return Parse(CoveredFrom, out from) && from <= utc; }
        public void InvalidateEntries() { entries = null; }
        public List<HourUsage> Entries() {
            if (entries != null) return entries;
            var list = new List<HourUsage>();
            foreach (LogFile file in Files) foreach (var pair in file.Hours) {
                string[] parts = pair.Key.Split('|'); DateTime at;
                if (!DateTime.TryParseExact(parts[0], "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) continue;
                Bucket b = pair.Value;
                list.Add(new HourUsage { Hour = at, Provider = parts.Length > 1 ? parts[1] : "", Model = parts.Length > 2 ? parts[2] : "", Endpoint = parts.Length > 3 ? parts[3] : null, Tokens = b.Tokens(), Requests = b.R, Cost = b.D, Input = b.I, Cached = b.C, CacheWrite = b.W, Output = b.O, Other = b.X, Unpriced = b.U, TimedRequests = b.TN, TimedOutput = b.TO, TimedSeconds = b.TS });
            }
            entries = list; return entries;
        }
        public double Tokens(DateTime startUtc, DateTime endUtc) { return Tokens(startUtc, endUtc, null); }
        // Tokens recorded in [startUtc, endUtc). Hour buckets that straddle an edge are
        // weighted by overlap (see Weight), so a window starting at 10:33 takes ~45% of a past
        // 10:00 bucket, while the running hour counts in full up to now.
        public double Tokens(DateTime startUtc, DateTime endUtc, Func<string, bool> provider) {
            double sum = 0; DateTime nowUtc = DateTime.UtcNow;
            foreach (HourUsage hour in Entries()) {
                if (provider != null && !provider(hour.Provider)) continue;
                sum += hour.Tokens * Weight(hour.Hour, startUtc, endUtc, nowUtc);
            }
            return sum;
        }
        // Share of the hour bucket starting at hourUtc that falls in [startUtc, endUtc). The
        // hour still running can only hold usage up to now, so it spans [hour, now]: asking
        // for "today until now" at 10:15 counts the whole 10:00 bucket, not a quarter of it.
        public static double Weight(DateTime hourUtc, DateTime startUtc, DateTime endUtc, DateTime nowUtc) {
            DateTime spanEnd = hourUtc.AddHours(1); if (nowUtc > hourUtc && nowUtc < spanEnd) spanEnd = nowUtc;
            DateTime a = hourUtc > startUtc ? hourUtc : startUtc, b = spanEnd < endUtc ? spanEnd : endUtc;
            if (b <= a) return 0;
            return Math.Min(1, (b - a).TotalSeconds / (spanEnd - hourUtc).TotalSeconds);
        }
        // Usage in [startUtc, endUtc), each hour weighted by its overlap with the range.
        public Bucket Usage(DateTime startUtc, DateTime endUtc, Func<string, bool> provider) { return Usage(startUtc, endUtc, provider, null); }
        public Bucket Usage(DateTime startUtc, DateTime endUtc, Func<string, bool> provider, Func<string, bool> model) { return Usage(startUtc, endUtc, provider, model, DateTime.UtcNow); }
        public Bucket Usage(DateTime startUtc, DateTime endUtc, Func<string, bool> provider, Func<string, bool> model, DateTime nowUtc) {
            var sum = new Bucket();
            foreach (HourUsage hour in Entries()) {
                if (provider != null && !provider(hour.Provider)) continue;
                if (model != null && !model(hour.Model)) continue;
                double w = Weight(hour.Hour, startUtc, endUtc, nowUtc);
                if (w <= 0) continue;
                sum.I += hour.Input * w; sum.C += hour.Cached * w; sum.W += hour.CacheWrite * w; sum.O += hour.Output * w; sum.X += hour.Other * w; sum.R += hour.Requests * w; sum.D += hour.Cost * w; sum.U += hour.Unpriced * w;
                sum.TN += hour.TimedRequests * w; sum.TO += hour.TimedOutput * w; sum.TS += hour.TimedSeconds * w;
            }
            return sum;
        }
        public static bool Parse(string iso, out DateTime utc) {
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc)) { utc = utc.ToUniversalTime(); return true; }
            return false;
        }
        public static string HourKey(DateTime when) { return when.ToUniversalTime().ToString("yyyyMMddHH", CultureInfo.InvariantCulture); }
        internal static string BucketKey(DateTime when, string provider, string model) { return BucketKey(when, provider, model, null); }
        internal static string BucketKey(DateTime when, string provider, string model, string endpoint) { return HourKey(when) + "|" + (provider ?? "").Replace('|', '/') + "|" + (model ?? "").Replace('|', '/') + (endpoint == null ? "" : "|" + endpoint.Replace('|', '/')); }
        internal static void Add(LogFile file, DateTime when, string provider, string model, Bucket amounts) { Add(file, BucketKey(when, provider, model), amounts, 1); }
        internal static void Add(LogFile file, DateTime when, string provider, string model, Bucket amounts, string endpoint) { Add(file, BucketKey(when, provider, model, endpoint), amounts, 1); }
        // Claims a de-duplication key for the file it was read from.
        internal static void See(LogIndex index, string key, DateTime when, LogFile owner) { index.Seen[key] = HourKey(when) + "\t" + (owner == null ? "" : owner.Name); }
        internal static string Owner(string seen) {
            int a = seen.IndexOf('\t'); if (a < 0) return "";
            int b = seen.IndexOf('\t', a + 1); return b < 0 ? seen.Substring(a + 1) : seen.Substring(a + 1, b - a - 1);
        }
        internal static void Add(LogFile file, string key, Bucket amounts, double sign) {
            Bucket bucket; if (!file.Hours.TryGetValue(key, out bucket)) { bucket = new Bucket(); file.Hours[key] = bucket; }
            bucket.Add(amounts, sign);
            if (sign < 0 && bucket.R <= 0.5 && bucket.Tokens() <= 0.5 && bucket.TN <= 0.5) file.Hours.Remove(key);
        }
    }

    // Incremental JSONL reader shared by the file-based agents.
    public static class LogReader {
        public const int HorizonDays = 31, RetainDays = 33;
        public static LogIndex Fresh(LogIndex index, DateTime nowUtc) {
            Pricing.EnsureLoaded();
            if (index == null || index.Version != LogIndex.CurrentVersion || index.Pricing != Pricing.Version) index = new LogIndex { Pricing = Pricing.Version };
            DateTime horizon = nowUtc.AddDays(-HorizonDays), updated;
            // Any file changed since the previous scan has a write time after it. If that
            // scan is older than the horizon, changes may have been missed: start over.
            bool continuous = LogIndex.Parse(index.Updated, out updated) && updated >= horizon && index.Covers(nowUtc);
            if (!continuous) index = new LogIndex { CoveredFrom = horizon.ToString("o"), Pricing = Pricing.Version };
            return index;
        }
        public static LogIndex Scan(LogIndex index, IEnumerable<string> paths, Func<FileInfo, string> keyOf, DateTime nowUtc, Func<LogIndex, LogFile, string, Stream, long, long> read) {
            index = Fresh(index, nowUtc);
            DateTime horizon = nowUtc.AddDays(-HorizonDays);
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
                if (info.Length < state.Offset) Reset(index, state);
                try {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan)) {
                        long length = stream.Length; stream.Seek(state.Offset, SeekOrigin.Begin);
                        state.Offset = read(index, state, path, stream, state.Offset); state.Length = length; state.Stamp = stamp;
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            Finish(index, nowUtc, seen);
            return index;
        }
        // A file that shrank (rewritten or truncated) is read again from the start: its hours,
        // parser and timing state go, and so do the de-duplication keys it had claimed;
        // otherwise its surviving records would be taken for copies of themselves.
        internal static void Reset(LogIndex index, LogFile state) {
            state.Offset = 0; state.LastTotal = ""; state.Provider = ""; state.Model = ""; state.Tier = ""; state.Replay = ""; state.Parent = ""; state.Fallback = false;
            state.MarkMs = 0; state.StartMs = 0; state.EndMs = 0; state.Recent = ""; state.Open = "";
            state.Hours.Clear();
            foreach (string key in index.Seen.Where(p => String.Equals(LogIndex.Owner(p.Value), state.Name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList()) index.Seen.Remove(key);
            index.InvalidateEntries();
        }
        // Deleted session files keep their counted hours, so usage does not shrink when a
        // client prunes old threads; only hours beyond the retention window are dropped.
        public static void Finish(LogIndex index, DateTime nowUtc, HashSet<string> present) {
            string cutoff = nowUtc.AddDays(-RetainDays).ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
            foreach (LogFile file in index.Files) foreach (string key in file.Hours.Keys.Where(k => String.CompareOrdinal(k, cutoff) < 0).ToList()) file.Hours.Remove(key);
            if (present != null) index.Files.RemoveAll(f => f.Hours.Count == 0 && !present.Contains(f.Name));
            foreach (string key in index.Seen.Where(p => String.CompareOrdinal(p.Value, cutoff) < 0).Select(p => p.Key).ToList()) index.Seen.Remove(key);
            DateTime covered; if (LogIndex.Parse(index.CoveredFrom, out covered) && covered < nowUtc.AddDays(-RetainDays)) index.CoveredFrom = nowUtc.AddDays(-RetainDays).ToString("o");
            index.Updated = nowUtc.ToString("o");
            index.InvalidateEntries();
        }
        public static IEnumerable<string> Files(string folder) { return Files(folder, "*.jsonl"); }
        public static IEnumerable<string> Files(string folder, string pattern) {
            if (!Directory.Exists(folder)) return new string[0];
            try { return Directory.GetFiles(folder, pattern, SearchOption.AllDirectories); } catch { return new string[0]; }
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
        public static bool Contains(string text, string needle) { return text.IndexOf(needle, StringComparison.Ordinal) >= 0; }
        public static bool ParseTime(string text, out DateTime utc) {
            utc = DateTime.MinValue; if (String.IsNullOrWhiteSpace(text)) return false;
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value)) return false;
            utc = value.UtcDateTime; return true;
        }
        public static DateTime FromMillis(double ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms); }
        public static long Millis(DateTime utc) { return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        public static double N(object obj, string key) { double? v = J.Num(obj, key); return v.HasValue && v.Value > 0 ? Math.Floor(v.Value) : 0; }
        public static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        public static List<string> EnvRoots(string variable) {
            string raw = Environment.GetEnvironmentVariable(variable);
            if (String.IsNullOrWhiteSpace(raw)) return null;
            return raw.Split(',').Select(p => Environment.ExpandEnvironmentVariables(p.Trim())).Where(p => p.Length > 0).ToList();
        }
    }

    // ~/.codex/sessions and archived_sessions. Follows the published behaviour of
    // Codex's rollout logs: each event_msg/token_count carries the request's usage
    // (last_token_usage) and the running total; a repeated total is a duplicate. Forked
    // sessions replay their parent's history, which is skipped; the same event copied into
    // several files is counted once. session_meta.model_provider tells official ("openai")
    // from custom endpoints; thread_settings_applied carries the service tier (priority =
    // "fast", billed at the model's fast multiplier).
    public static class CodexLogs {
        public const string Official = "openai";
        private const int MaxLine = 1 << 20;
        private const double BurstPauseMs = 1000;
        private sealed class Raw { public double Input, Cached, CacheWrite, Output, Reasoning, Total; public string Key() { return Input + "|" + Cached + "|" + CacheWrite + "|" + Output + "|" + Reasoning + "|" + Total; } }
        private sealed class Context { public List<Raw> Prefix; public bool FastDefault; public Func<string, string> ParentPath; public string Path = ""; }
        private static readonly byte[][] Needles = { Encoding.ASCII.GetBytes("\"token_count\""), Encoding.ASCII.GetBytes("\"session_meta\""), Encoding.ASCII.GetBytes("\"turn_context\""), Encoding.ASCII.GetBytes("thread_settings_applied"), Encoding.ASCII.GetBytes("\"usage\":"), Encoding.ASCII.GetBytes("\"input_tokens\":"), Encoding.ASCII.GetBytes("\"prompt_tokens\":") };
        private static readonly string[][] AutoReviewFallbacks = { new[] { "2026-07-30", "gpt-5.6-luna" }, new[] { "2026-03-05", "gpt-5.4" }, new[] { "2026-02-05", "gpt-5.3-codex" }, new[] { "2025-12-11", "gpt-5.2-codex" }, new[] { "2025-11-13", "gpt-5.1-codex" } };
        public static bool IsOfficial(string provider) { return provider.Length == 0 || provider == Official; }
        public static IEnumerable<string> Paths() {
            string home = ProviderService.CodexHome();
            return LogReader.Files(Path.Combine(home, "sessions")).Concat(LogReader.Files(Path.Combine(home, "archived_sessions"))).ToList();
        }
        public static LogIndex Scan(LogIndex index, DateTime nowUtc) {
            List<string> paths = Paths().ToList();
            var context = new Context { FastDefault = FastByDefault() };
            Dictionary<string, List<string>> sessions = null;
            context.ParentPath = parentId => {
                if (sessions == null) {
                    sessions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    foreach (string p in paths) { string id = Meta(p)[0]; if (id.Length == 0) continue; List<string> list; if (!sessions.TryGetValue(id, out list)) sessions[id] = list = new List<string>(); list.Add(p); }
                }
                List<string> found; return sessions.TryGetValue(parentId, out found) ? found.FirstOrDefault(p => !String.Equals(p, context.Path, StringComparison.OrdinalIgnoreCase)) ?? "" : "";
            };
            // Keyed by file name: archiving moves a rollout file without renaming it.
            return LogReader.Scan(index, paths, info => info.Name, nowUtc, (idx, state, path, stream, offset) => Read(idx, state, path, stream, offset, context));
        }
        private static bool FastByDefault() {
            try {
                string config = Path.Combine(ProviderService.CodexHome(), "config.toml"); if (!File.Exists(config)) return false;
                foreach (string raw in File.ReadAllLines(config)) {
                    string line = raw.Split('#')[0].Trim(); int eq = line.IndexOf('='); if (eq < 0 || line.Substring(0, eq).Trim() != "service_tier") continue;
                    string value = line.Substring(eq + 1).Trim().Trim('"', '\''); if (value == "fast" || value == "priority") return true;
                }
            } catch { }
            return false;
        }
        // session id, parent (fork or sub-agent spawn) id, first-line timestamp.
        private static string[] Meta(string path) {
            try {
                using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) {
                    string line = reader.ReadLine(); if (line == null) return new[] { "", "", "" };
                    object root = J.Parse(line); object payload = J.Str(root, "type") == "session_meta" ? J.Get(root, "payload") : null;
                    string parent = J.Str(payload, "forked_from_id"); if (parent.Length == 0) parent = J.Str(payload, "source", "subagent", "thread_spawn", "parent_thread_id");
                    return new[] { J.Str(payload, "id"), parent, TimeText(J.Get(root, "timestamp")) };
                }
            } catch { return new[] { "", "", "" }; }
        }
        // Tests and single files: no fork lookup, standard tier by default.
        public static long Read(LogIndex index, LogFile state, string path, Stream stream, long offset) { return Read(index, state, path, stream, offset, new Context { ParentPath = id => "" }); }
        private static long Read(LogIndex index, LogFile state, string path, Stream stream, long offset, Context context) {
            context.Path = path; context.Prefix = null;
            if (offset == 0) {
                string[] meta = Meta(path);
                state.Parent = meta[1]; state.Replay = meta[1].Length > 0 ? "match:0|" + meta[2] : "";
            }
            if (state.Replay.StartsWith("match:")) context.Prefix = ParentPrefix(state, context);
            // Response items only time the request (see OutputTiming); they are not decoded.
            return LogReader.Read(stream, offset, MaxLine, (bytes, length) => !OutputTiming.CodexItem(state, bytes, length) && Needles.Any(n => LogReader.Contains(bytes, length, n)), line => Consume(index, state, line, context));
        }
        // The parent's usage up to the fork instant: what the child replayed.
        private static List<Raw> ParentPrefix(LogFile state, Context context) {
            var prefix = new List<Raw>();
            string parent = context.ParentPath(state.Parent); if (parent.Length == 0) return prefix;
            string forkedText = state.Replay.Contains('|') ? state.Replay.Substring(state.Replay.IndexOf('|') + 1) : "";
            DateTime forkedAt; bool hasFork = LogReader.ParseTime(forkedText, out forkedAt);
            foreach (var pair in Events(parent)) {
                DateTime at; if (hasFork && LogReader.ParseTime(pair.Key, out at) && at > forkedAt) break;
                prefix.Add(pair.Value);
            }
            return prefix;
        }
        private static List<KeyValuePair<string, Raw>> Events(string path) {
            var list = new List<KeyValuePair<string, Raw>>(); var state = new LogFile();
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    LogReader.Read(stream, 0, MaxLine, (bytes, length) => Needles.Any(n => LogReader.Contains(bytes, length, n)), line => {
                        string timestamp; Raw raw; string model; if (Parse(state, line, out timestamp, out raw, out model)) list.Add(new KeyValuePair<string, Raw>(timestamp, raw));
                    });
                }
            } catch { }
            return list;
        }
        // Timestamp of the first of two back-to-back usage events at the head of the file:
        // Codex rewrites a replayed history to the fork instant and writes it in one burst.
        private static string RewrittenBurst(string path) {
            string first = null; DateTime firstAt = DateTime.MinValue; string found = "";
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    var probe = new LogFile(); bool done = false;
                    LogReader.Read(stream, 0, MaxLine, (bytes, length) => !done && LogReader.Contains(bytes, length, Needles[0]), line => {
                        if (done) return;
                        object root; try { root = J.Parse(line); } catch { return; }
                        object payload = J.Get(root, "payload");
                        if (J.Str(root, "type") != "event_msg" || J.Str(payload, "type") != "token_count") return;
                        object info = J.Get(payload, "info"); if (info == null || (J.Get(info, "last_token_usage") == null && J.Get(info, "total_token_usage") == null)) return;
                        string text = TimeText(J.Get(root, "timestamp")); DateTime at; if (!LogReader.ParseTime(text, out at)) return;
                        if (first == null) { first = text; firstAt = at; return; }
                        double gap = (at - firstAt).TotalMilliseconds; if (gap >= 0 && gap <= BurstPauseMs) found = first; done = true;
                    });
                }
            } catch { }
            return found;
        }
        private static string TimeText(object value) {
            if (value is string) return ((string)value).Trim();
            double? n = J.Num(value); if (!n.HasValue || n.Value <= 0) return "";
            return LogReader.FromMillis(n.Value > 1e11 ? n.Value : n.Value * 1000).ToString("o");
        }
        private static Raw Usage(object usage) {
            if (!(usage is IDictionary)) return null;
            double input = First(usage, "input_tokens", "prompt_tokens", "input"), output = First(usage, "output_tokens", "completion_tokens", "output");
            double reasoning = First(usage, "reasoning_output_tokens", "reasoning_tokens");
            double cached = Math.Min(First(usage, "cached_input_tokens", "cache_read_input_tokens", "cached_tokens"), input);
            double write = Math.Min(First(usage, "cache_write_input_tokens", "cache_creation_input_tokens"), input - cached);
            double total = LogReader.N(usage, "total_tokens"); if (total <= 0) total = input + output;
            return new Raw { Input = input, Cached = cached, CacheWrite = write, Output = output, Reasoning = reasoning, Total = total };
        }
        private static double First(object obj, params string[] keys) { foreach (string key in keys) { if (J.Get(obj, key) != null) return LogReader.N(obj, key); } return 0; }
        private static Raw Minus(Raw a, Raw b) {
            if (b == null) return Normalize(new Raw { Input = a.Input, Cached = a.Cached, CacheWrite = a.CacheWrite, Output = a.Output, Reasoning = a.Reasoning, Total = a.Total });
            return Normalize(new Raw { Input = Math.Max(0, a.Input - b.Input), Cached = Math.Max(0, a.Cached - b.Cached), CacheWrite = Math.Max(0, a.CacheWrite - b.CacheWrite), Output = Math.Max(0, a.Output - b.Output), Reasoning = Math.Max(0, a.Reasoning - b.Reasoning), Total = Math.Max(0, a.Total - b.Total) });
        }
        private static Raw Normalize(Raw u) { u.Cached = Math.Min(u.Cached, u.Input); u.CacheWrite = Math.Min(u.CacheWrite, Math.Max(0, u.Input - u.Cached)); return u; }
        private static Raw ParseTotals(string key) {
            if (key.Length == 0) return null; string[] p = key.Split('|'); if (p.Length < 6) return null;
            Func<int, double> n = i => { double v; return Double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; };
            return new Raw { Input = n(0), Cached = n(1), CacheWrite = n(2), Output = n(3), Reasoning = n(4), Total = n(5) };
        }
        private static string ModelOf(object o) {
            string m = J.Str(o, "model").Trim(); if (m.Length == 0) m = J.Str(o, "model_name").Trim(); if (m.Length == 0) m = J.Str(o, "metadata", "model").Trim(); return m;
        }
        // One session line → usage event. Updates the per-file state (model, totals, tier).
        private static bool Parse(LogFile state, string line, out string timestamp, out Raw raw, out string model) {
            timestamp = ""; raw = null; model = "";
            object root; try { root = J.Parse(line); } catch { return false; }
            string type = J.Str(root, "type"); object payload = J.Get(root, "payload");
            if (type == "session_meta") { string provider = J.Str(payload, "model_provider"); if (provider.Length > 0) state.Provider = provider; return false; }
            if (type == "turn_context") { string m = ModelOf(payload); if (m.Length > 0) { state.Model = m; state.Fallback = false; } return false; }
            if (type == "event_msg") {
                timestamp = TimeText(J.Get(root, "timestamp")); if (timestamp.Length == 0 || payload == null) return false;
                string kind = J.Str(payload, "type");
                if (kind == "thread_settings_applied") {
                    object settings = J.Get(payload, "thread_settings");
                    if (settings is IDictionary && ((IDictionary)settings).Contains("service_tier") && J.Get(settings, "service_tier") != null) {
                        string tier = J.Str(settings, "service_tier");
                        state.Tier = tier == "default" || tier == "standard" ? "standard" : tier == "fast" || tier == "priority" ? "fast" : "";
                    }
                    return false;
                }
                if (kind != "token_count") return false;
                object info = J.Get(payload, "info");
                Raw total = Usage(J.Get(info, "total_token_usage")), last = Usage(J.Get(info, "last_token_usage")), previous = ParseTotals(state.LastTotal);
                bool advanced = total == null || previous == null || total.Key() != previous.Key();
                raw = last != null && advanced ? Normalize(last) : total != null ? Minus(total, previous) : null;
                if (total != null) state.LastTotal = total.Key();
                if (raw == null || raw.Input + raw.Cached + raw.CacheWrite + raw.Output + raw.Reasoning <= 0) return false;
                string parsed = ModelOf(payload); if (parsed.Length == 0) parsed = ModelOf(info);
                model = Resolve(state, parsed, timestamp);
                return true;
            }
            // Headless `codex exec --json` usage records.
            object usage = J.Get(root, "usage") ?? J.Get(root, "data", "usage") ?? J.Get(root, "result", "usage") ?? J.Get(root, "response", "usage");
            raw = Usage(usage); if (raw == null || raw.Input + raw.Cached + raw.CacheWrite + raw.Output + raw.Reasoning + raw.Total <= 0) { raw = null; return false; }
            raw = Normalize(raw);
            timestamp = TimeText(J.Get(root, "timestamp") ?? J.Get(root, "created_at") ?? J.Get(root, "createdAt"));
            string headless = ModelOf(root); if (headless.Length == 0) foreach (string k in new[] { "data", "result", "response" }) { headless = ModelOf(J.Get(root, k)); if (headless.Length > 0) break; }
            model = Resolve(state, headless, timestamp);
            return timestamp.Length > 0;
        }
        private static string Resolve(LogFile state, string parsed, string timestamp) {
            if (parsed.Length > 0) { state.Model = parsed; state.Fallback = false; }
            string model = parsed.Length > 0 ? parsed : state.Model;
            if (model.Length == 0) { model = "gpt-5"; state.Model = model; state.Fallback = true; }
            if (model == "codex-auto-review") {
                string date = timestamp.Length >= 10 ? timestamp.Substring(0, 10) : "";
                model = "gpt-5"; foreach (string[] f in AutoReviewFallbacks) if (String.CompareOrdinal(date, f[0]) >= 0 && date.Length == 10) { model = f[1]; break; }
            }
            return model;
        }
        private static void Consume(LogIndex index, LogFile state, string line, Context context) {
            string timestamp; Raw raw; string model;
            if (!Parse(state, line, out timestamp, out raw, out model)) return;
            double seconds = OutputTiming.CodexTake(state);
            // Fork replay: skip the parent's history at the head of the child.
            while (state.Replay.Length > 0) {
                if (state.Replay.StartsWith("match:")) {
                    string body = state.Replay.Substring(6); int bar = body.IndexOf('|'); int at; Int32.TryParse(bar < 0 ? body : body.Substring(0, bar), out at);
                    if (context.Prefix != null && at < context.Prefix.Count && context.Prefix[at].Key() == raw.Key()) { state.Replay = "match:" + (at + 1) + (bar < 0 ? "" : body.Substring(bar)); return; }
                    string burst = at == 0 ? RewrittenBurst(context.Path) : "";
                    state.Replay = burst.Length > 0 ? "burst:" + burst : "";
                    continue;
                }
                if (state.Replay.StartsWith("burst:")) {
                    DateTime previous, now;
                    if (LogReader.ParseTime(state.Replay.Substring(6), out previous) && LogReader.ParseTime(timestamp, out now)) {
                        double gap = (now - previous).TotalMilliseconds;
                        if (gap >= 0 && gap <= BurstPauseMs) { state.Replay = "burst:" + timestamp; return; }
                    }
                    state.Replay = ""; continue;
                }
                state.Replay = "";
            }
            DateTime when; if (!LogReader.ParseTime(timestamp, out when)) return;
            // The same event copied into several files (resumed or forked threads) counts once.
            string key = "x|" + LogReader.Millis(when) + "|" + model + "|" + raw.Key();
            if (index.Seen.ContainsKey(key)) return;
            LogIndex.See(index, key, when, state);
            ModelPrice price = Pricing.Find(model);
            bool fast = state.Tier == "fast" || (context.FastDefault && state.Tier != "standard");
            double cost = Pricing.CodexCost(price, raw.Input, raw.Cached, raw.CacheWrite, raw.Output) * (fast && price != null ? price.Fast : 1);
            var bucket = new Bucket { I = raw.Input - raw.Cached - raw.CacheWrite, C = raw.Cached, W = raw.CacheWrite, O = raw.Output, R = 1, D = cost };
            if (price == null) bucket.U = bucket.Tokens();
            // Output includes the reasoning tokens (reasoning_output_tokens is a part of it).
            if (OutputTiming.Accept(raw.Output, seconds)) { bucket.TN = 1; bucket.TO = raw.Output; bucket.TS = seconds; }
            string provider = state.Provider.Length == 0 ? Official : state.Provider;
            // Relay requests remember which endpoint was configured at that moment.
            LogIndex.Add(state, when, provider, model, bucket, IsOfficial(provider) ? null : EndpointAttribution.At(Endpoints.CodexApp, when));
        }
    }

    // Claude Code projects/**/*.jsonl (also sub-agent progress lines). A response logged
    // more than once shares message.id + requestId (or, without a request id, the session
    // and timestamp); the copy with the most tokens wins, a main-thread copy beats a
    // /btw side-chain replay. costUSD in the log is used when present.
    public static class ClaudeLogs {
        public const string NoRequestId = "no-request-id";
        private const int MaxLine = 16 << 20;
        private static readonly byte[] Usage = Encoding.ASCII.GetBytes("\"usage\":{");
        private static readonly HashSet<string> NonNullable = new HashSet<string> { "id", "cwd", "model", "speed", "costUSD", "version", "sessionId", "requestId", "isApiErrorMessage", "cache_read_input_tokens", "cache_creation_input_tokens" };
        public static IEnumerable<string> ConfigDirs() {
            List<string> custom = LogReader.EnvRoots("CLAUDE_CONFIG_DIR"); if (custom != null) return custom;
            string home = LogReader.Home;
            return new[] { Path.Combine(home, ".claude"), Path.Combine(home, ".config", "claude") };
        }
        public static LogIndex Scan(LogIndex index, DateTime nowUtc) {
            var paths = ConfigDirs().SelectMany(dir => LogReader.Files(Path.Combine(dir, "projects"))).ToList();
            return LogReader.Scan(index, paths, info => info.FullName.ToLowerInvariant(), nowUtc, (idx, state, path, stream, offset) => Read(idx, state, path, stream, offset));
        }
        public static long Read(LogIndex index, LogFile state, string path, Stream stream, long offset) {
            string session = Path.GetFileNameWithoutExtension(path);
            OutputTiming.ClaudeState timing = OutputTiming.ClaudeState.Load(state);
            long end = LogReader.Read(stream, offset, MaxLine, (bytes, length) => {
                if (LogReader.Contains(bytes, length, Usage)) return true;
                string uuid; long ms; if (OutputTiming.UuidAndTime(bytes, length, out uuid, out ms)) timing.Note(uuid, ms);
                return false;
            }, line => Consume(index, state, line, session, timing));
            timing.Save(state);
            return end;
        }
        // Nulls are allowed only where Claude Code writes them; a null id, model or token
        // count marks a malformed line. Models inside usage.iterations may be null.
        private static bool HasUnsupportedNull(object value, bool iterationItem) {
            var d = value as IDictionary<string, object>;
            if (d != null) {
                foreach (var pair in d) {
                    if (pair.Value == null) { if (NonNullable.Contains(pair.Key) && !(iterationItem && pair.Key == "model")) return true; continue; }
                    if (pair.Key == "iterations" && !(pair.Value is IDictionary) && !(pair.Value is string)) { foreach (object item in J.Arr(pair.Value)) if (HasUnsupportedNull(item, true)) return true; continue; }
                    if (HasUnsupportedNull(pair.Value, false)) return true;
                }
                return false;
            }
            if (value is string || value == null) return false;
            var list = value as IEnumerable;
            if (list != null) foreach (object item in list) if (HasUnsupportedNull(item, false)) return true;
            return false;
        }
        private static bool Semver(string v) {
            int i = 0; Func<bool> digits = () => { int s = i; while (i < v.Length && Char.IsDigit(v[i])) i++; return i > s; };
            if (!digits() || i >= v.Length || v[i] != '.') return false; i++;
            if (!digits() || i >= v.Length || v[i] != '.') return false; i++;
            return i < v.Length && Char.IsDigit(v[i]);
        }
        public static void Consume(LogIndex index, LogFile state, string line, string pathSession) { Consume(index, state, line, pathSession, null); }
        private static void Consume(LogIndex index, LogFile state, string line, string pathSession, OutputTiming.ClaudeState timing) {
            object root; try { root = J.Parse(line); } catch { return; }
            if (line.Contains("null") && HasUnsupportedNull(root, false)) return;
            object message, holder; string sessionId = null;
            if (J.Get(root, "message", "usage") is IDictionary) { message = J.Get(root, "message"); holder = root; }
            else if (J.Get(root, "data", "message", "message", "usage") is IDictionary) { holder = J.Get(root, "data", "message"); message = J.Get(holder, "message"); }
            else return;
            var dict = root as IDictionary<string, object>;
            if (dict != null && dict.ContainsKey("sessionId")) sessionId = J.Str(root, "sessionId");
            object usage = J.Get(message, "usage");
            if (J.Num(usage, "input_tokens") == null || J.Num(usage, "output_tokens") == null) return;
            DateTime when; if (!LogReader.ParseTime(J.Str(holder, "timestamp"), out when)) return;
            if (holder == root && dict.ContainsKey("version") && !Semver(J.Str(root, "version"))) return;
            if (sessionId != null && sessionId.Length == 0) return;
            var h = holder as IDictionary<string, object>; string requestId = h != null && h.ContainsKey("requestId") ? J.Str(holder, "requestId") : null;
            if (requestId != null && requestId.Length == 0) return;
            var m = message as IDictionary<string, object>;
            string messageId = m != null && m.ContainsKey("id") ? J.Str(message, "id") : null; if (messageId != null && messageId.Length == 0) return;
            string model = m != null && m.ContainsKey("model") ? J.Str(message, "model") : null; if (model != null && model.Length == 0) return;
            bool sidechain = J.Get(holder, "isSidechain") is bool && (bool)J.Get(holder, "isSidechain");
            double? costUsd = J.Num(holder, "costUSD");
            string session = sessionId ?? pathSession;
            string bucketKey = Add(index, state, when, message, usage, model, costUsd, messageId, requestId, session, sidechain);
            if (timing != null) {
                // The first block of a response answers its parent entry; later blocks chain on.
                string parent = holder == root ? J.Str(root, "parentUuid") : null;
                if (bucketKey != null && messageId != null && model != null && model != "<synthetic>")
                    timing.Block(index, state, "t|" + messageId + "|" + (requestId ?? session), bucketKey, parent, LogReader.Millis(when), LogReader.N(usage, "output_tokens"), LogIndex.HourKey(when));
                if (holder == root) timing.Note(J.Str(root, "uuid"), LogReader.Millis(when));
            }
            // Advisor iterations run a second model inside the same response.
            if (line.Contains("\"advisor_message\"")) {
                int n = 0;
                foreach (object iteration in J.Arr(J.Get(usage, "iterations"))) {
                    if (J.Str(iteration, "type") != "advisor_message") continue;
                    string advisor = J.Str(iteration, "model"); if (advisor.Length == 0 || J.Num(iteration, "input_tokens") == null || J.Num(iteration, "output_tokens") == null) { continue; }
                    Add(index, state, when, null, iteration, advisor, null, messageId == null ? null : messageId + ":advisor:" + n, requestId, session, sidechain);
                    n++;
                }
            }
        }
        private static string Add(LogIndex index, LogFile state, DateTime when, object message, object usage, string model, double? costUsd, string messageId, string requestId, string session, bool sidechain) {
            var u = new TokenUse { Input = LogReader.N(usage, "input_tokens"), Output = LogReader.N(usage, "output_tokens"), CacheRead = LogReader.N(usage, "cache_read_input_tokens") };
            object breakdown = J.Get(usage, "cache_creation");
            if (breakdown is IDictionary) { u.CacheWrite5m = LogReader.N(breakdown, "ephemeral_5m_input_tokens"); u.CacheWrite1h = LogReader.N(breakdown, "ephemeral_1h_input_tokens"); }
            else u.CacheWrite5m = LogReader.N(usage, "cache_creation_input_tokens");
            string speed = J.Str(usage, "speed"); bool fast = speed == "fast";
            bool synthetic = model == null || model == "<synthetic>";
            ModelPrice price = synthetic ? null : Pricing.Find(model);
            double cost = costUsd ?? (price == null ? 0 : Pricing.Cost(price, u) * (fast ? price.Fast : 1));
            var bucket = new Bucket { I = u.Input, C = u.CacheRead, W = u.CacheWrite, O = u.Output, R = 1, D = cost };
            if (!costUsd.HasValue && !synthetic && price == null) bucket.U = bucket.Tokens();
            string display = synthetic ? "" : fast ? model + "-fast" : model;
            // Anthropic always returns a request-id; its absence is a strong hint of a relay.
            string provider = requestId != null ? "" : NoRequestId;
            // The endpoint configured at the moment of the request (not at the top of the hour),
            // so switching relays at 10:30 splits the 10:00 hour correctly.
            string endpoint = EndpointAttribution.At(Endpoints.ClaudeApp, when);
            Record(index, state, when, provider, display, endpoint, bucket, messageId, requestId, session, sidechain, speed.Length > 0);
            return LogIndex.BucketKey(when, provider, display, endpoint);
        }
        // Seen record: hour \t file \t bucket-key \t I C W O R D U \t sidechain \t speed
        private static string Encode(string hour, LogFile file, string key, Bucket b, bool sidechain, bool speed) {
            var c = CultureInfo.InvariantCulture;
            return hour + "\t" + file.Name + "\t" + key + "\t" + String.Join(" ", new[] { b.I, b.C, b.W, b.O, b.R, b.D, b.U }.Select(v => v.ToString("R", c))) + "\t" + (sidechain ? 1 : 0) + "\t" + (speed ? 1 : 0);
        }
        private static void Record(LogIndex index, LogFile file, DateTime when, string provider, string model, string endpoint, Bucket b, string messageId, string requestId, string session, bool sidechain, bool speed) {
            string key = LogIndex.BucketKey(when, provider, model, endpoint), hour = LogIndex.HourKey(when);
            if (messageId == null) { LogIndex.Add(file, key, b, 1); return; }
            string exact = "c|" + messageId + "|" + (requestId != null ? "r|" + requestId : "s|" + session + "|" + LogReader.Millis(when));
            string existing = null, value;
            if (index.Seen.TryGetValue(exact, out value)) existing = exact;
            else {
                // /btw side chains replay parent messages under new request ids.
                string route = (sidechain ? "route|" : "side|") + messageId + "|" + session;
                if (index.Seen.TryGetValue(route, out value)) existing = value.Substring(value.IndexOf('\t') + 1);
            }
            // Every value starts with its hour (for expiry); "hour \t @key" is an alias.
            if (existing != null && index.Seen.TryGetValue(existing, out value)) { string[] alias = value.Split('\t'); if (alias.Length == 2 && alias[1].StartsWith("@")) existing = alias[1].Substring(1); }
            if (existing == null || !index.Seen.TryGetValue(existing, out value)) {
                LogIndex.Add(file, key, b, 1);
                index.Seen[exact] = Encode(hour, file, key, b, sidechain, speed);
                index.Seen["route|" + messageId + "|" + session] = hour + "\t" + exact;
                if (sidechain) index.Seen["side|" + messageId + "|" + session] = hour + "\t" + exact;
                return;
            }
            string[] old = value.Split('\t'); if (old.Length < 6) return;
            double[] n = old[3].Split(' ').Select(s => { double v; return Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; }).ToArray();
            var previous = new Bucket { I = n[0], C = n[1], W = n[2], O = n[3], R = n[4], D = n[5], U = n.Length > 6 ? n[6] : 0 };
            bool oldSide = old[4] == "1", oldSpeed = old[5] == "1";
            bool replace = sidechain != oldSide ? oldSide : b.Tokens() != previous.Tokens() ? b.Tokens() > previous.Tokens() : b.D != previous.D ? b.D > previous.D : speed && !oldSpeed;
            if (exact != existing) index.Seen[exact] = hour + "\t@" + existing;
            if (!replace) return;
            LogFile owner = index.Files.FirstOrDefault(f => String.Equals(f.Name, old[1], StringComparison.OrdinalIgnoreCase));
            if (owner != null) LogIndex.Add(owner, old[2], previous, -1);
            LogIndex.Add(file, key, b, 1);
            index.Seen[existing] = Encode(hour, file, key, b, sidechain, speed);
            index.Seen["route|" + messageId + "|" + session] = hour + "\t" + existing;
            if (sidechain) index.Seen["side|" + messageId + "|" + session] = hour + "\t" + existing;
        }
    }
}
