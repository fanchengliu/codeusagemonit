using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeUsageMonit {
    // Read-only SQLite access through Windows' own winsqlite3.dll (no bundled engine).
    public sealed class Sqlite : IDisposable {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern double sqlite3_column_double(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        private IntPtr db;
        private Sqlite(IntPtr db) { this.db = db; }
        public static Sqlite Open(string path) {
            if (!File.Exists(path)) return null;
            try { IntPtr db; if (sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 1, IntPtr.Zero) != 0) { if (db != IntPtr.Zero) sqlite3_close(db); return null; } sqlite3_busy_timeout(db, 2000); return new Sqlite(db); }
            catch { return null; }
        }
        // Rows as object[] (long, double, string, byte[] or null); empty on error.
        public List<object[]> Query(string sql) {
            var rows = new List<object[]>(); IntPtr stmt = IntPtr.Zero;
            try {
                byte[] text = Encoding.UTF8.GetBytes(sql + "\0");
                if (sqlite3_prepare_v2(db, text, text.Length, out stmt, IntPtr.Zero) != 0) return rows;
                int columns = sqlite3_column_count(stmt);
                while (sqlite3_step(stmt) == 100) {
                    var row = new object[columns];
                    for (int c = 0; c < columns; c++) {
                        switch (sqlite3_column_type(stmt, c)) {
                            case 1: row[c] = sqlite3_column_int64(stmt, c); break;
                            case 2: row[c] = sqlite3_column_double(stmt, c); break;
                            case 3: { int n = sqlite3_column_bytes(stmt, c); IntPtr p = sqlite3_column_text(stmt, c); byte[] b = new byte[Math.Max(0, n)]; if (n > 0) Marshal.Copy(p, b, 0, n); row[c] = Encoding.UTF8.GetString(b); break; }
                            case 4: { int n = sqlite3_column_bytes(stmt, c); IntPtr p = sqlite3_column_blob(stmt, c); byte[] b = new byte[Math.Max(0, n)]; if (n > 0) Marshal.Copy(p, b, 0, n); row[c] = b; break; }
                            default: row[c] = null; break;
                        }
                    }
                    rows.Add(row);
                }
            } catch { }
            finally { if (stmt != IntPtr.Zero) sqlite3_finalize(stmt); }
            return rows;
        }
        public bool HasTable(string name) { return Query("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '" + name.Replace("'", "''") + "' LIMIT 1").Count > 0; }
        public HashSet<string> Columns(string table) { return new HashSet<string>(Query("PRAGMA table_info(\"" + table.Replace("\"", "") + "\")").Select(r => Convert.ToString(r[1])), StringComparer.OrdinalIgnoreCase); }
        public static long Long(object value) { if (value is long) return (long)value; if (value is double) { double d = (double)value; return Double.IsNaN(d) || Double.IsInfinity(d) ? 0 : (long)Math.Round(d); } long n; return Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0; }
        public static string Text(object value) { return value == null || value is byte[] ? (value == null ? null : Encoding.UTF8.GetString((byte[])value)) : Convert.ToString(value, CultureInfo.InvariantCulture); }
        public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close(db); db = IntPtr.Zero; } }
    }

    // Minimal protobuf wire-format reader (varint, fixed, length-delimited fields).
    public static class Proto {
        public sealed class Field { public int Number, Wire; public ulong Varint; public byte[] Bytes; }
        public static List<Field> Decode(byte[] data) {
            var fields = new List<Field>(); if (data == null) return fields; int i = 0;
            while (i < data.Length) {
                ulong tag = Varint(data, ref i); int number = (int)(tag >> 3), wire = (int)(tag & 7);
                if (number <= 0) throw new FormatException("protobuf field number");
                var f = new Field { Number = number, Wire = wire };
                if (wire == 0) f.Varint = Varint(data, ref i);
                else if (wire == 1) { if (i + 8 > data.Length) throw new FormatException("truncated"); i += 8; }
                else if (wire == 2) { ulong len = Varint(data, ref i); if (len > (ulong)(data.Length - i)) throw new FormatException("truncated"); f.Bytes = new byte[(int)len]; Buffer.BlockCopy(data, i, f.Bytes, 0, (int)len); i += (int)len; }
                else if (wire == 5) { if (i + 4 > data.Length) throw new FormatException("truncated"); i += 4; }
                else throw new FormatException("wire type");
                fields.Add(f);
            }
            return fields;
        }
        private static ulong Varint(byte[] data, ref int i) {
            ulong value = 0;
            for (int shift = 0; shift < 70; shift += 7) {
                if (i >= data.Length) throw new FormatException("truncated varint");
                byte b = data[i++]; value |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new FormatException("varint overflow");
        }
        public static ulong? VarintField(List<Field> fields, int number) { for (int k = fields.Count - 1; k >= 0; k--) if (fields[k].Number == number && fields[k].Wire == 0) return fields[k].Varint; return null; }
        public static byte[] BytesField(List<Field> fields, int number) { foreach (Field f in fields) if (f.Number == number && f.Wire == 2) return f.Bytes; return null; }
        public static List<byte[]> BytesAll(List<Field> fields, int number) { return fields.Where(f => f.Number == number && f.Wire == 2).Select(f => f.Bytes).ToList(); }
        public static string TextField(List<Field> fields, int number) {
            for (int k = fields.Count - 1; k >= 0; k--) if (fields[k].Number == number && fields[k].Wire == 2) {
                try { string s = new UTF8Encoding(false, true).GetString(fields[k].Bytes); return s.Trim().Length == 0 ? null : s; } catch { return null; }
            }
            return null;
        }
    }

    // Agents whose history lives in SQLite or per-session JSONL (Antigravity, ZCode,
    // Grok, Kimi). Each keeps an hourly index in the same shape as Codex / Claude.
    public static class AgentLogs {
        private static Bucket Priced(TokenUse u, double extra, ModelPrice price, double? recordedCost) {
            double cost = recordedCost ?? (price == null ? 0 : Pricing.Cost(price, u));
            var b = new Bucket { I = u.Input, C = u.CacheRead, W = u.CacheWrite, O = u.Output, X = extra, R = 1, D = cost };
            if (!recordedCost.HasValue && price == null) b.U = b.Tokens();
            return b;
        }
        // Tokens known from parts vs. a recorded total: the missing remainder becomes output
        // when none was recorded, otherwise "other" tokens.
        private static void TotalFallback(ref TokenUse u, ref double extra, double total) {
            double missing = total - (u.Total + extra); if (missing <= 0) return;
            if (u.Output == 0) u.Output = missing; else extra += missing;
        }
        private static IEnumerable<string> Roots(string variable, params string[] defaults) {
            List<string> env = LogReader.EnvRoots(variable); if (env != null) return env;
            return defaults.Select(d => Path.Combine(LogReader.Home, d));
        }

        // ── ZCode: ~/.zcode/cli/db/db.sqlite, table model_usage ──────────────
        public static LogIndex ScanZcode(LogIndex index, DateTime nowUtc) {
            index = new LogIndex { CoveredFrom = nowUtc.AddDays(-LogReader.HorizonDays).ToString("o"), Pricing = Pricing.Version };
            long horizon = LogReader.Millis(nowUtc.AddDays(-LogReader.HorizonDays));
            var file = new LogFile { Name = "zcode" }; index.Files.Add(file);
            var seenDb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in Roots("ZCODE_HOME", ".zcode")) {
                string path = Path.Combine(root, "cli", "db", "db.sqlite"); string full;
                try { full = Path.GetFullPath(path); } catch { continue; }
                if (!File.Exists(full) || !seenDb.Add(full)) continue;
                using (Sqlite db = Sqlite.Open(full)) {
                    if (db == null) continue;
                    HashSet<string> m = db.Columns("model_usage"), s = db.Columns("session");
                    if (!m.Contains("id") || !m.Contains("started_at") || !m.Contains("model_id")) continue;
                    string sql = "SELECT m.id, m.session_id, m.started_at, m.model_id, m.input_tokens, m.output_tokens, "
                        + (m.Contains("cache_creation_input_tokens") ? "m.cache_creation_input_tokens" : "0") + ", " + (m.Contains("cache_read_input_tokens") ? "m.cache_read_input_tokens" : "0") + ", "
                        + (m.Contains("computed_total_tokens") ? "m.computed_total_tokens" : "m.input_tokens + m.output_tokens") + ", " + (m.Contains("provider_id") ? "m.provider_id" : "NULL") + ", "
                        + (m.Contains("completed_at") ? "m.completed_at" : "NULL")
                        + " FROM model_usage m LEFT JOIN session s ON s.id = m.session_id WHERE m.status = 'completed' AND m.started_at >= " + horizon;
                    foreach (object[] r in db.Query(sql)) {
                        string id = Sqlite.Text(r[0]) ?? "", session = Sqlite.Text(r[1]) ?? "", model = (Sqlite.Text(r[3]) ?? "").Trim(), provider = Sqlite.Text(r[9]);
                        long started = Sqlite.Long(r[2]);
                        if (id.Trim().Length == 0 || session.Trim().Length == 0 || model.Length == 0 || started <= 0) continue;
                        double input = Math.Max(0, Sqlite.Long(r[4])), output = Math.Max(0, Sqlite.Long(r[5])), write = Math.Max(0, Sqlite.Long(r[6])), read = Math.Max(0, Sqlite.Long(r[7])), total = Math.Max(0, Sqlite.Long(r[8]));
                        read = Math.Min(read, input); write = Math.Min(write, input - read);
                        var u = new TokenUse { Input = input - read - write, Output = output, CacheWrite5m = write, CacheRead = read }; double extra = 0;
                        TotalFallback(ref u, ref extra, total);
                        if (u.Total + extra <= 0) continue;
                        string lower = model.ToLowerInvariant();
                        bool zai = IsZai(provider) || (provider == null && (lower.StartsWith("glm-") || lower.StartsWith("glm/")));
                        ModelPrice price = null;
                        if (provider == null || IsZai(provider)) {
                            var candidates = new List<string> { model, lower }; if (zai) { candidates.Add("zai/" + model); candidates.Add("zai/" + lower); }
                            price = candidates.Where(c => c.StartsWith("zai/")).Select(Pricing.Find).FirstOrDefault(p => p != null) ?? candidates.Where(c => !c.StartsWith("zai/")).Select(Pricing.Find).FirstOrDefault(p => p != null);
                        }
                        // z.ai bills no cache writes; they are charged as plain input.
                        TokenUse costUse = zai ? new TokenUse { Input = u.Input + u.CacheWrite5m, Output = u.Output + extra, CacheRead = u.CacheRead } : new TokenUse { Input = u.Input, Output = u.Output + extra, CacheRead = u.CacheRead, CacheWrite5m = u.CacheWrite5m };
                        Bucket b = Priced(u, extra, null, price == null ? (double?)null : Pricing.Cost(price, costUse));
                        if (price == null) b.U = b.Tokens();
                        // ZCode records start and completion per request.
                        long completed = r[10] == null ? 0 : Sqlite.Long(r[10]); double seconds = completed > started ? (completed - started) / 1000.0 : 0;
                        if (OutputTiming.Accept(output, seconds)) { b.TN = 1; b.TO = output; b.TS = seconds; }
                        LogIndex.Add(file, LogReader.FromMillis(started), "", model, b);
                    }
                }
            }
            LogReader.Finish(index, nowUtc, null);
            return index;
        }
        private static bool IsZai(string provider) {
            if (provider == null) return false;
            string p = provider.Trim().ToLowerInvariant();
            return p == "zai" || p == "z.ai" || p == "zai-coding-plan" || p == "builtin:zai-coding-plan" || p == "builtin:bigmodel-coding-plan";
        }

        // ── Grok: ~/.grok/sessions/**/updates.jsonl ────────────────────────
        // turn_completed events carry per-model usage and Grok's own cost in fixed-point
        // ticks (1e-10 USD); the recorded cost is used, list prices only when missing.
        public static LogIndex ScanGrok(LogIndex index, DateTime nowUtc) {
            string root = Environment.GetEnvironmentVariable("GROK_HOME");
            root = String.IsNullOrWhiteSpace(root) ? Path.Combine(LogReader.Home, ".grok") : root.Trim();
            var paths = LogReader.Files(Path.Combine(root, "sessions")).Where(p => Path.GetFileName(p) == "updates.jsonl").ToList();
            return LogReader.Scan(index, paths, info => info.FullName.ToLowerInvariant(), nowUtc, (idx, state, path, stream, offset) => {
                string session = Path.GetFileName(Path.GetDirectoryName(path)), model = null;
                try { object summary = J.File(Path.Combine(Path.GetDirectoryName(path), "summary.json")); string id = J.Str(summary, "info", "id"); if (id.Trim().Length > 0) session = id; string m = J.Str(summary, "current_model_id"); if (m.Trim().Length > 0) model = m; } catch { }
                return LogReader.Read(stream, offset, 16 << 20, (bytes, length) => LogReader.Contains(bytes, length, TurnCompleted), line => GrokLine(idx, state, line, session, model));
            });
        }
        private static readonly byte[] TurnCompleted = Encoding.ASCII.GetBytes("\"turn_completed\"");
        internal static void GrokLine(LogIndex index, LogFile file, string line, string fileSession, string defaultModel) {
            object root; try { root = J.Parse(line); } catch { return; }
            object parameters = J.Get(root, "params"), update = J.Get(parameters, "update");
            if (J.Str(update, "sessionUpdate") != "turn_completed") return;
            object usage = J.Get(update, "usage"); if (!(usage is IDictionary)) return;
            string eventId = J.Str(parameters, "_meta", "eventId"); if (eventId.Trim().Length == 0) eventId = null;
            double? agentMs = J.Num(parameters, "_meta", "agentTimestampMs"), seconds = J.Num(root, "timestamp");
            DateTime when = agentMs.HasValue && agentMs > 0 ? LogReader.FromMillis(agentMs.Value) : seconds.HasValue && seconds > 0 ? LogReader.FromMillis(seconds.Value * 1000) : LogReader.FromMillis(0);
            string session = J.Str(parameters, "sessionId"); if (session.Trim().Length == 0) session = fileSession;
            var rows = new List<KeyValuePair<string, object>>();
            var byModel = J.Get(usage, "modelUsage") as IDictionary<string, object>;
            if (byModel != null && byModel.Count > 0) rows.AddRange(byModel.OrderBy(p => p.Key, StringComparer.Ordinal));
            else rows.Add(new KeyValuePair<string, object>(defaultModel ?? "unknown", usage));
            foreach (var row in rows) {
                object mu = row.Value; double input = LogReader.N(mu, "inputTokens");
                double cache = Math.Min(LogReader.N(mu, "cachedReadTokens"), input), uncached = input - cache, write = Math.Min(LogReader.N(mu, "cacheCreationTokens"), uncached); uncached -= write;
                double output = LogReader.N(mu, "outputTokens"), reasoning = LogReader.N(mu, "reasoningTokens");
                if (uncached == 0 && cache == 0 && write == 0 && output == 0 && reasoning == 0) continue;
                string key = "g|" + (eventId != null ? eventId + "|" + row.Key : session + "|" + LogReader.Millis(when) + "|" + row.Key + "|" + uncached + "|" + output + "|" + cache + "|" + write + "|" + reasoning);
                if (index.Seen.ContainsKey(key)) continue;
                index.Seen[key] = LogIndex.HourKey(when);
                double ticks = LogReader.N(mu, "costUsdTicks");
                var u = new TokenUse { Input = uncached, Output = output, CacheRead = cache, CacheWrite5m = write };
                ModelPrice price = ticks > 0 ? null : GrokPrice(row.Key);
                Bucket b = Priced(u, 0, price, ticks > 0 ? ticks / 1e10 : (double?)null);
                // Grok records the API time of the turn's model calls (outputTokens include reasoning).
                double calls = Math.Max(1, LogReader.N(mu, "modelCalls")), apiSeconds = LogReader.N(mu, "apiDurationMs") / 1000.0;
                if (OutputTiming.Accept(output, apiSeconds, calls)) { b.TN = calls; b.TO = output; b.TS = apiSeconds; }
                LogIndex.Add(file, when, "", row.Key, b);
            }
        }
        private static ModelPrice GrokPrice(string raw) {
            string stripped = (raw.StartsWith("[grok] ") ? raw.Substring(7) : raw).Trim(); if (stripped.Length == 0) return null;
            string normalized = stripped.EndsWith("-build") ? stripped.Substring(0, stripped.Length - 6) : stripped;
            var candidates = new[] { stripped, "xai/" + stripped, "x-ai/" + stripped, normalized, "xai/" + normalized, "x-ai/" + normalized }.Distinct().ToList();
            return candidates.Select(Pricing.FindExact).FirstOrDefault(p => p != null) ?? candidates.Select(Pricing.Find).FirstOrDefault(p => p != null);
        }

        // ── Kimi CLI: ~/.kimi(-code)/sessions/**/wire.jsonl ──────────────────
        public static LogIndex ScanKimi(LogIndex index, DateTime nowUtc) {
            var paths = new List<string>();
            foreach (string root in Roots("KIMI_DATA_DIR", ".kimi", ".kimi-code")) {
                string sessions = Path.Combine(root, "sessions");
                foreach (string p in LogReader.Files(sessions)) {
                    if (Path.GetFileName(p) != "wire.jsonl") continue;
                    int depth = p.Substring(sessions.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    if (depth == 3 || depth == 5) paths.Add(p);
                }
            }
            return LogReader.Scan(index, paths, info => info.FullName.ToLowerInvariant(), nowUtc, (idx, state, path, stream, offset) => {
                string configModel = KimiConfigModel(path), session = KimiSession(path); DateTime modified = File.GetLastWriteTimeUtc(path);
                return LogReader.Read(stream, offset, 16 << 20, (bytes, length) => LogReader.Contains(bytes, length, TokenUsageNeedle) || LogReader.Contains(bytes, length, UsageRecordNeedle), line => KimiLine(idx, state, line, configModel, session, modified));
            });
        }
        private static readonly byte[] TokenUsageNeedle = Encoding.ASCII.GetBytes("\"token_usage\""), UsageRecordNeedle = Encoding.ASCII.GetBytes("\"usage.record\"");
        private static string KimiRoot(string wire) {
            DirectoryInfo dir = new FileInfo(wire).Directory; int up = dir.Parent != null && dir.Parent.Name == "agents" ? 5 : 4;
            DirectoryInfo current = new FileInfo(wire).Directory; for (int i = 1; i < up && current != null; i++) current = current.Parent;
            return current == null ? null : current.FullName;
        }
        private static string KimiConfigModel(string wire) {
            try { string root = KimiRoot(wire); if (root == null) return "kimi-for-coding"; string m = J.Str(J.File(Path.Combine(root, "config.json")), "model"); return m.Trim().Length > 0 ? m : "kimi-for-coding"; } catch { return "kimi-for-coding"; }
        }
        private static string KimiSession(string wire) {
            DirectoryInfo parent = new FileInfo(wire).Directory;
            DirectoryInfo session = parent.Parent != null && parent.Parent.Name == "agents" ? parent.Parent.Parent : parent;
            return session == null || session.Name.Length == 0 ? "unknown" : session.Name;
        }
        internal static void KimiLine(LogIndex index, LogFile file, string line, string configModel, string session, DateTime modified) {
            object root; try { root = J.Parse(line); } catch { return; }
            string type = J.Str(root, "type"); TokenUse u; double extra = 0; DateTime when; string model, messageId = "";
            if (type == "usage.record") {
                if (J.Str(root, "usageScope") != "turn") return;
                object usage = J.Get(root, "usage"); if (!(usage is IDictionary)) return;
                u = new TokenUse { Input = LogReader.N(usage, "inputOther"), Output = LogReader.N(usage, "output"), CacheWrite5m = LogReader.N(usage, "inputCacheCreation"), CacheRead = LogReader.N(usage, "inputCacheRead") };
                double? time = J.Num(root, "time"); when = time.HasValue ? LogReader.FromMillis(time.Value) : modified;
                model = J.Str(root, "model"); if (model.Trim().Length == 0) model = "kimi-for-coding"; if (model.StartsWith("kimi-code/")) model = model.Substring(10);
            } else if (type == "metadata") return;
            else {
                object message = J.Get(root, "message"); if (J.Str(message, "type") != "StatusUpdate") return;
                object payload = J.Get(message, "payload"), usage = J.Get(payload, "token_usage"); if (!(usage is IDictionary)) return;
                u = new TokenUse { Input = LogReader.N(usage, "input_other"), Output = LogReader.N(usage, "output"), CacheWrite5m = LogReader.N(usage, "input_cache_creation"), CacheRead = LogReader.N(usage, "input_cache_read") };
                TotalFallback(ref u, ref extra, LogReader.N(usage, "total"));
                double? seconds = J.Num(root, "timestamp"); when = seconds.HasValue ? LogReader.FromMillis(Math.Truncate(seconds.Value * 1000)) : modified;
                model = configModel; messageId = J.Str(payload, "message_id");
            }
            if (u.Total + extra <= 0) return;
            string key = "k|" + session + ":" + messageId + ":" + LogReader.Millis(when) + ":" + model + ":" + u.Input + ":" + u.Output + ":" + u.CacheWrite + ":" + u.CacheRead + ":" + extra;
            if (index.Seen.ContainsKey(key)) return;
            index.Seen[key] = LogIndex.HourKey(when);
            var candidates = new List<string>();
            if (model == "kimi-for-coding") candidates.Add(LogReader.Millis(when) < 1776698890072L ? "moonshot/kimi-k2.5" : "moonshot/kimi-k2.6");
            candidates.Add("moonshot/" + model); candidates.Add("kimi/" + model); candidates.Add(model);
            ModelPrice price = candidates.Distinct().Select(Pricing.Find).FirstOrDefault(p => p != null);
            LogIndex.Add(file, when, "", model, Priced(u, extra, price, null));
        }

        // ── Antigravity: ~/.gemini/antigravity*/conversations/*.db ────────────
        // Each conversation database stores generation and step metadata as protobuf.
        private sealed class AgEvent {
            public DateTime When; public int TimeRank, IdRank; public string Model = "", MessageId; public ulong? Provider;
            public double Input, Output, CacheWrite, CacheRead, Reasoning, TotalOutput;
            public List<string> Identities = new List<string>();
        }
        private sealed class AgUsage {
            public ulong? ModelId, Provider; public double Input, TotalOutput, CacheWrite, CacheRead, Reasoning, VisibleOutput;
            public string MessageId, ResponseId, ProviderMessageId;
            public bool Bearing { get { return Input > 0 || TotalOutput > 0 || CacheWrite > 0 || CacheRead > 0 || Reasoning > 0 || VisibleOutput > 0; } }
            public List<string> Identities() {
                var list = new List<string>();
                if (!String.IsNullOrEmpty(ResponseId)) list.Add("response:" + ResponseId);
                if (!String.IsNullOrEmpty(ProviderMessageId)) list.Add("provider:" + ProviderMessageId);
                if (!String.IsNullOrEmpty(MessageId)) list.Add("message:" + MessageId);
                return list;
            }
        }
        private sealed class AgMeta { public string Model; public ulong? ModelId, Provider; public AgUsage Usage; public List<AgUsage> Retries = new List<AgUsage>(); public DateTime? When; }
        private const string DefaultAgModel = "gemini-internal-model";
        public static LogIndex ScanAntigravity(LogIndex index, DateTime nowUtc) {
            index = new LogIndex { CoveredFrom = nowUtc.AddDays(-LogReader.HorizonDays).ToString("o"), Pricing = Pricing.Version };
            DateTime horizon = nowUtc.AddDays(-LogReader.HorizonDays);
            var file = new LogFile { Name = "antigravity" }; index.Files.Add(file);
            var events = new List<AgEvent>(); var dbs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in Roots("ANTIGRAVITY_DATA_DIR", @".gemini\antigravity", @".gemini\antigravity-cli", @".gemini\antigravity-ide", @".gemini\antigravity-backup", @".config\antigravity")) {
                string dir = Directory.Exists(Path.Combine(root, "conversations")) ? Path.Combine(root, "conversations") : root;
                foreach (string db in LogReader.Files(dir, "*.db")) { try { if (File.GetLastWriteTimeUtc(db) >= horizon) dbs.Add(Path.GetFullPath(db)); } catch { } }
            }
            foreach (string db in dbs) { try { events.AddRange(ParseAntigravity(db)); } catch { } }
            foreach (AgEvent e in Deduplicate(events).OrderBy(e => e.When)) {
                if (e.When < horizon) continue;
                var u = new TokenUse { Input = e.Input, Output = e.TotalOutput, CacheWrite5m = e.CacheWrite, CacheRead = e.CacheRead };
                ModelPrice price = AntigravityPrice(e.Model, e.Provider);
                double cost = price == null ? 0 : Pricing.Cost(price, u);
                var b = new Bucket { I = e.Input, C = e.CacheRead, W = e.CacheWrite, O = e.Output, X = Math.Max(0, e.TotalOutput - e.Output), R = 1, D = cost };
                if (price == null) b.U = b.Tokens();
                LogIndex.Add(file, e.When, "", e.Model, b);
            }
            LogReader.Finish(index, nowUtc, null);
            return index;
        }
        private static IEnumerable<AgEvent> ParseAntigravity(string path) {
            var events = new List<AgEvent>();
            DateTime fallback = File.GetLastWriteTimeUtc(path);
            using (Sqlite db = Sqlite.Open(path)) {
                if (db == null) return events;
                var pages = db.Query("PRAGMA page_count"); if (pages.Count == 0 || Sqlite.Long(pages[0][0]) == 0) return events;
                DateTime? trajectory = null;
                if (db.HasTable("trajectory_metadata_blob"))
                    foreach (object[] r in db.Query("SELECT data FROM trajectory_metadata_blob ORDER BY rowid ASC")) { if (trajectory == null) trajectory = Timestamp(Proto.BytesField(Proto.Decode(r[0] as byte[]), 2)); }
                var generations = db.HasTable("gen_metadata") ? db.Query("SELECT idx, data FROM gen_metadata ORDER BY idx ASC").Select(r => GeneratorMeta(r[1] as byte[])).ToList() : new List<AgMeta>();
                var steps = db.HasTable("steps") ? db.Query("SELECT idx, metadata FROM steps WHERE metadata IS NOT NULL ORDER BY idx ASC").Select(r => StepMeta(r[1] as byte[])).ToList() : new List<AgMeta>();
                string generationModel = null;
                for (int k = generations.Count - 1; k >= 0 && generationModel == null; k--) generationModel = MetaModel(generations[k]);
                var identityTimes = new Dictionary<string, KeyValuePair<DateTime, int>>();
                foreach (AgMeta step in steps) {
                    string model = MetaModel(step) ?? generationModel;
                    foreach (AgUsage usage in new[] { step.Usage }.Concat(step.Retries)) if (usage != null) Append(events, identityTimes, usage, model, step.Provider, step.When, trajectory, fallback);
                }
                string current = null;
                foreach (AgMeta gen in generations) {
                    string rowModel = MetaModel(gen) ?? (gen.Usage != null && gen.Usage.ModelId.HasValue ? NormalizeAg(ModelName(gen.Usage.ModelId.Value)) : null);
                    if (rowModel != null) current = rowModel;
                    foreach (AgUsage usage in new[] { gen.Usage }.Concat(gen.Retries)) if (usage != null) Append(events, identityTimes, usage, current, null, gen.When, trajectory, fallback);
                }
            }
            return events;
        }
        private static string MetaModel(AgMeta m) { return (m.Model != null ? NormalizeAg(m.Model) : null) ?? (m.ModelId.HasValue ? NormalizeAg(ModelName(m.ModelId.Value)) : null); }
        private static void Append(List<AgEvent> events, Dictionary<string, KeyValuePair<DateTime, int>> identityTimes, AgUsage usage, string model, ulong? provider, DateTime? when, DateTime? trajectory, DateTime fallback) {
            if (!usage.Bearing) return;
            List<string> identities = usage.Identities();
            DateTime time; int rank;
            if (when.HasValue) { time = when.Value; rank = 3; }
            else {
                KeyValuePair<DateTime, int> known = default(KeyValuePair<DateTime, int>); bool found = false;
                foreach (string id in identities) if (identityTimes.TryGetValue(id, out known)) { found = true; break; }
                if (found) { time = known.Key; rank = known.Value; } else if (trajectory.HasValue) { time = trajectory.Value; rank = 1; } else { time = fallback; rank = 0; }
            }
            double totalOutput = Math.Max(usage.TotalOutput, usage.VisibleOutput + usage.Reasoning);
            double output = Math.Max(usage.VisibleOutput, Math.Max(0, totalOutput - usage.Reasoning));
            double reasoning = Math.Max(usage.Reasoning, Math.Max(0, totalOutput - output));
            string resolved = (usage.ModelId.HasValue ? NormalizeAg(ModelName(usage.ModelId.Value)) : null) ?? (model != null ? NormalizeAg(model) : null) ?? DefaultAgModel;
            string messageId; int idRank;
            if (!String.IsNullOrEmpty(usage.ResponseId)) { messageId = usage.ResponseId; idRank = 3; } else if (!String.IsNullOrEmpty(usage.ProviderMessageId)) { messageId = usage.ProviderMessageId; idRank = 2; } else { messageId = String.IsNullOrEmpty(usage.MessageId) ? null : usage.MessageId; idRank = 1; }
            foreach (string id in identities) {
                KeyValuePair<DateTime, int> old;
                if (!identityTimes.TryGetValue(id, out old) || rank > old.Value || (rank == old.Value && time < old.Key)) identityTimes[id] = new KeyValuePair<DateTime, int>(time, rank);
            }
            events.Add(new AgEvent { When = time, TimeRank = rank, Model = resolved, Provider = usage.Provider ?? provider, Input = usage.Input, Output = output, CacheWrite = usage.CacheWrite, CacheRead = usage.CacheRead, Reasoning = reasoning, TotalOutput = totalOutput, MessageId = messageId, IdRank = idRank, Identities = identities });
        }
        // The same response can be stored under several ids and in several databases;
        // copies merge, keeping the largest counts and the best timestamp.
        private static List<AgEvent> Deduplicate(List<AgEvent> events) {
            var slots = new List<AgEvent>(); var byIdentity = new Dictionary<string, int>();
            foreach (AgEvent e in events) {
                var matches = e.Identities.Where(byIdentity.ContainsKey).Select(id => byIdentity[id]).Distinct().OrderBy(i => i).ToList();
                if (matches.Count == 0) { int index = slots.Count; foreach (string id in e.Identities) byIdentity[id] = index; slots.Add(e); continue; }
                int target = matches[0];
                foreach (int duplicate in matches.Skip(1)) if (slots[duplicate] != null) { Merge(slots[target], slots[duplicate]); slots[duplicate] = null; }
                Merge(slots[target], e);
                foreach (string id in slots[target].Identities) byIdentity[id] = target;
            }
            return slots.Where(s => s != null).ToList();
        }
        private static void Merge(AgEvent t, AgEvent d) {
            t.Input = Math.Max(t.Input, d.Input); t.Output = Math.Max(t.Output, d.Output); t.CacheWrite = Math.Max(t.CacheWrite, d.CacheWrite); t.CacheRead = Math.Max(t.CacheRead, d.CacheRead);
            t.Reasoning = Math.Max(t.Reasoning, d.Reasoning); t.TotalOutput = Math.Max(Math.Max(t.TotalOutput, d.TotalOutput), t.Output + t.Reasoning);
            if (t.Model == DefaultAgModel && d.Model != DefaultAgModel) t.Model = d.Model;
            if (t.Provider == null) t.Provider = d.Provider;
            if (d.TimeRank > t.TimeRank || (d.TimeRank == t.TimeRank && d.When < t.When)) { t.When = d.When; t.TimeRank = d.TimeRank; }
            if (d.IdRank > t.IdRank) { t.MessageId = d.MessageId; t.IdRank = d.IdRank; }
            foreach (string id in d.Identities) if (!t.Identities.Contains(id)) t.Identities.Add(id);
        }
        private static AgMeta GeneratorMeta(byte[] blob) {
            var meta = new AgMeta(); if (blob == null) return meta;
            try {
                var root = Proto.Decode(blob); byte[] chat = Proto.BytesField(root, 1); if (chat == null) return meta;
                var f = Proto.Decode(chat);
                byte[] usage = Proto.BytesField(f, 4); if (usage != null) meta.Usage = Usage(usage);
                foreach (byte[] retry in Proto.BytesAll(f, 17)) { byte[] u = Proto.BytesField(Proto.Decode(retry), 2); if (u != null) meta.Retries.Add(Usage(u)); }
                byte[] info = Proto.BytesField(f, 9); if (info != null) meta.When = Timestamp(Proto.BytesField(Proto.Decode(info), 4));
                meta.Model = Proto.TextField(f, 19) ?? Proto.TextField(f, 21);
                ulong? id = Proto.VarintField(f, 3); if (id.HasValue && id.Value != 0) meta.ModelId = id;
            } catch { }
            return meta;
        }
        private static AgMeta StepMeta(byte[] blob) {
            var meta = new AgMeta(); if (blob == null) return meta;
            try {
                var f = Proto.Decode(blob);
                byte[] usage = Proto.BytesField(f, 9); if (usage != null) meta.Usage = Usage(usage);
                foreach (byte[] retry in Proto.BytesAll(f, 28)) { byte[] u = Proto.BytesField(Proto.Decode(retry), 2); if (u != null) meta.Retries.Add(Usage(u)); }
                byte[] info = Proto.BytesField(f, 24);
                if (info != null) {
                    var mf = Proto.Decode(info); meta.Model = Proto.TextField(mf, 12) ?? Proto.TextField(mf, 8);
                    ulong? id = Proto.VarintField(mf, 1); if (id.HasValue && id.Value != 0) meta.ModelId = id;
                    ulong? provider = Proto.VarintField(mf, 7); if (provider.HasValue && provider.Value != 0) meta.Provider = provider;
                }
                byte[] time = Proto.BytesField(f, 8) ?? Proto.BytesField(f, 1); if (time != null) meta.When = Timestamp(time);
            } catch { }
            return meta;
        }
        private static AgUsage Usage(byte[] blob) {
            var f = Proto.Decode(blob);
            Func<int, double> n = k => { ulong? v = Proto.VarintField(f, k); return v.HasValue ? (double)v.Value : 0; };
            ulong? model = Proto.VarintField(f, 1), provider = Proto.VarintField(f, 6);
            return new AgUsage { ModelId = model.HasValue && model.Value != 0 ? model : null, Input = n(2), TotalOutput = n(3), CacheWrite = n(4), CacheRead = n(5), Reasoning = n(9), VisibleOutput = n(10), Provider = provider.HasValue && provider.Value != 0 ? provider : null, MessageId = Proto.TextField(f, 7), ResponseId = Proto.TextField(f, 11), ProviderMessageId = Proto.TextField(f, 12) };
        }
        private static DateTime? Timestamp(byte[] blob) {
            if (blob == null) return null;
            var f = Proto.Decode(blob); ulong? seconds = Proto.VarintField(f, 1); if (!seconds.HasValue || seconds.Value == 0 || seconds.Value > long.MaxValue / 1000) return null;
            ulong nanos = Math.Min(Proto.VarintField(f, 2) ?? 0, 999999999UL);
            return LogReader.FromMillis((double)seconds.Value * 1000 + nanos / 1000000);
        }
        private static string ModelName(ulong id) {
            switch (id) {
                case 246: return "gemini-2.5-pro"; case 312: return "gemini-2.5-flash"; case 313: case 329: return "gemini-2.5-flash-thinking"; case 330: return "gemini-2.5-flash-lite";
                case 281: case 282: return "claude-4-sonnet"; case 290: case 291: return "claude-4-opus"; case 333: case 334: return "claude-4.5-sonnet"; case 340: case 341: return "claude-4.5-haiku";
                case 342: return "model_openai_gpt_oss_120b_medium";
                case 1318: return "gemini-3.8-flash-high"; case 1319: return "gemini-3.8-flash-medium"; case 1320: return "gemini-3.8-flash-low";
                case 1298: return "gemini-3.7-flash-high"; case 1299: return "gemini-3.7-flash-medium"; case 1300: return "gemini-3.7-flash-low";
                case 1071: return "gemini-3.6-flash-high"; case 1072: return "gemini-3.6-flash-medium"; case 1073: return "gemini-3.6-flash-low";
            }
            return id >= 1000 ? "model_placeholder_m" + (id - 1000) : "antigravity-model-" + id;
        }
        private static readonly Dictionary<string, string> AgNames = new Dictionary<string, string> {
            { "gemini 3.8 flash", "gemini-3.8-flash" }, { "gemini 3.8 flash thinking", "gemini-3.8-flash" }, { "gemini 3.7 flash", "gemini-3.7-flash" }, { "gemini 3.7 flash thinking", "gemini-3.7-flash" },
            { "gemini 3.7 pro", "gemini-3.7-pro" }, { "gemini 3.7 pro thinking", "gemini-3.7-pro" }, { "gemini 3.6 flash", "gemini-3.6-flash" }, { "gemini 3 flash", "gemini-3.6-flash" }, { "gemini 3.6 pro", "gemini-3.6-pro" },
            { "gemini 3 pro", "gemini-3-pro" }, { "gemini 3 pro thinking", "gemini-3-pro" }, { "gemini 2.5 flash", "gemini-2.5-flash" }, { "gemini 2.5 pro", "gemini-2.5-pro" }, { "gemini 2.0 flash", "gemini-2.0-flash" }, { "gemini 2 flash", "gemini-2.0-flash" },
            { "gemini 2.0 pro", "gemini-2.0-pro" }, { "gemini 1.5 flash", "gemini-1.5-flash" }, { "gemini 1.5 pro", "gemini-1.5-pro" },
            { "model_placeholder_m318", "gemini-3.8-flash-high" }, { "model_placeholder_m319", "gemini-3.8-flash-medium" }, { "model_placeholder_m320", "gemini-3.8-flash-low" },
            { "model_placeholder_m298", "gemini-3.7-flash-high" }, { "model_placeholder_m299", "gemini-3.7-flash-medium" }, { "model_placeholder_m300", "gemini-3.7-flash-low" },
            { "model_placeholder_m71", "gemini-3.6-flash-high" }, { "model_placeholder_m72", "gemini-3.6-flash-medium" }, { "model_placeholder_m73", "gemini-3.6-flash-low" },
            { "model_placeholder_m26", "claude-opus-4-6" }, { "model_placeholder_m35", "claude-sonnet-4-6" }, { "model_placeholder_m36", "gemini-3.1-pro" }, { "model_placeholder_m37", "gemini-3.1-pro" }, { "model_placeholder_m16", "gemini-3.1-pro" },
            { "model_placeholder_m18", "gemini-3-flash-preview" }, { "model_placeholder_m84", "gemini-3-flash-preview" }, { "model_placeholder_m47", "gemini-3-flash-preview" },
            { "model_placeholder_m132", "gemini-3.5-flash-high" }, { "model_placeholder_m133", "gemini-3.5-flash-high" }, { "model_placeholder_m187", "gemini-3.5-flash-extra-low" }, { "model_placeholder_m20", "gemini-3.5-flash-medium" },
            { "model_openai_gpt_oss_120b_medium", "gpt-oss-120b-medium" }, { "gemini-pro-default", "gemini-3.1-pro" }, { "gemini-pro-agent", "gemini-3.1-pro" },
            { "gemini-3-flash-agent", "gemini-3.5-flash-high" }, { "gemini-3-flash-agent-a", "gemini-3.5-flash-high" }, { "gemini-3-flash-agent-b", "gemini-3.5-flash-high" }, { "gemini-3-flash-a", "gemini-3.5-flash-high" }, { "gemini-3-flash-b", "gemini-3.5-flash-high" },
            { "gemini-3-flash-c", "gemini-3-flash-preview" }, { "gemini-3-flash", "gemini-3-flash-preview" }, { "gemini-3.5-flash-low", "gemini-3.5-flash-medium" },
            { "gemini-3.1-pro-high", "gemini-3.1-pro" }, { "gemini-3.1-pro-low", "gemini-3.1-pro" }, { "gemini-3-pro-high", "gemini-3-pro" }, { "gemini-3-pro-low", "gemini-3-pro" },
            { "claude 3.7 sonnet", "claude-3-7-sonnet" }, { "claude 3.7 sonnet thinking", "claude-3-7-sonnet" }, { "claude 3.5 sonnet", "claude-3-5-sonnet" }, { "claude 3.5 haiku", "claude-3-5-haiku" }, { "claude 3 opus", "claude-3-opus" }
        };
        private static string NormalizeAg(string raw) {
            string trimmed = (raw ?? "").Trim(); if (trimmed.Length == 0) return null;
            string lower = trimmed.ToLowerInvariant();
            foreach (string version in new[] { "3.8", "3.7", "3.6" }) foreach (string effort in new[] { "high", "medium", "low" })
                if (lower == "gemini " + version + " flash (" + effort + ")") return "gemini-" + version + "-flash-" + effort;
            string b = lower.Contains('(') ? lower.Substring(0, lower.IndexOf('(')).Trim() : lower;
            string mapped; if (AgNames.TryGetValue(b, out mapped)) return mapped;
            string converted = b.Replace(' ', '-');
            return converted.StartsWith("gemini-") || converted.StartsWith("claude-") || converted.StartsWith("gpt-") ? converted : trimmed;
        }
        private static ModelPrice AntigravityPrice(string model, ulong? provider) {
            string alias = null;
            foreach (string v in new[] { "3.8", "3.7", "3.6" }) foreach (string e in new[] { "high", "medium", "low" }) if (model == "gemini-" + v + "-flash-" + e) alias = "gemini-" + v + "-flash";
            var models = new List<string> { model }; if (alias != null) models.Add(alias);
            bool google = provider == 3 || provider == 24 || provider == 30;
            var candidates = new List<string>();
            foreach (string m in models) { candidates.Add(m); if (google) foreach (string p in new[] { "google", "gemini", "vertex_ai", "openrouter/google" }) candidates.Add(p + "/" + m); }
            // Effort variants must not borrow a neighbour's price through fuzzy matching.
            return alias != null ? candidates.Distinct().Select(Pricing.FindExact).FirstOrDefault(p => p != null) : candidates.Distinct().Select(Pricing.Find).FirstOrDefault(p => p != null);
        }
    }
}
