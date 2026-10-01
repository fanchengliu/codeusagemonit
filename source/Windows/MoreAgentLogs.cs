using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeUsageMonit {
    // Pi, GitHub Copilot CLI and OpenCode local history. These follow the same record
    // formats as the other agents' readers but were written without sample data on the
    // development machine; see VERIFICATION.md.
    public static class MoreAgentLogs {
        private static Bucket Priced(TokenUse u, double extra, ModelPrice price, double? recordedCost) {
            double cost = recordedCost ?? (price == null ? 0 : Pricing.Cost(price, new TokenUse { Input = u.Input, Output = u.Output + extra, CacheRead = u.CacheRead, CacheWrite5m = u.CacheWrite5m, CacheWrite1h = u.CacheWrite1h }));
            var b = new Bucket { I = u.Input, C = u.CacheRead, W = u.CacheWrite, O = u.Output, X = extra, R = 1, D = cost };
            if (!recordedCost.HasValue && price == null) b.U = b.Tokens();
            return b;
        }
        private static void TotalFallback(ref TokenUse u, ref double extra, double total) {
            double missing = total - (u.Total + extra); if (missing <= 0) return;
            if (u.Output == 0) u.Output = missing; else extra += missing;
        }

        // ── Pi: ~/.pi/agent/sessions/**/*.jsonl ────────────────────────────
        // Assistant messages carry usage {input, output, cacheRead, cacheWrite,
        // totalTokens, cost.total}. A forked session repeats its parent's messages with the
        // same timestamps and counts; those copies are counted once.
        public static LogIndex ScanPi(LogIndex index, DateTime nowUtc) {
            List<string> roots = LogReader.EnvRoots("PI_AGENT_DIR") ?? new List<string> { Path.Combine(LogReader.Home, ".pi", "agent", "sessions") };
            var paths = roots.SelectMany(r => LogReader.Files(r)).ToList();
            return LogReader.Scan(index, paths, info => info.FullName.ToLowerInvariant(), nowUtc, (idx, state, path, stream, offset) =>
                LogReader.Read(stream, offset, 16 << 20, (bytes, length) => LogReader.Contains(bytes, length, UsageKey) && LogReader.Contains(bytes, length, MessageKey), line => PiLine(idx, state, line)));
        }
        private static readonly byte[] UsageKey = Encoding.ASCII.GetBytes("\"usage\""), MessageKey = Encoding.ASCII.GetBytes("\"message\"");
        internal static void PiLine(LogIndex index, LogFile file, string line) {
            object root; try { root = J.Parse(line); } catch { return; }
            string type = J.Str(root, "type"); if (type.Length > 0 && type != "message") return;
            object message = J.Get(root, "message"), usage = J.Get(message, "usage");
            if (J.Str(message, "role") != "assistant" || !(usage is IDictionary)) return;
            DateTime when; if (!LogReader.ParseTime(J.Str(root, "timestamp"), out when)) return;
            var u = new TokenUse { Input = LogReader.N(usage, "input"), Output = LogReader.N(usage, "output"), CacheRead = LogReader.N(usage, "cacheRead"), CacheWrite5m = LogReader.N(usage, "cacheWrite") };
            double extra = 0; TotalFallback(ref u, ref extra, LogReader.N(usage, "totalTokens"));
            if (u.Total + extra <= 0) return;
            string model = J.Str(message, "model").Trim();
            double? recorded = J.Num(usage, "cost", "total");
            string key = "p|" + LogReader.Millis(when) + "|" + model + "|" + u.Input + "|" + u.Output + "|" + u.CacheRead + "|" + u.CacheWrite + "|" + extra;
            if (index.Seen.ContainsKey(key)) return;
            LogIndex.See(index, key, when, file);
            string provider = J.Str(message, "provider").Trim();
            ModelPrice price = model.Length == 0 ? null : (provider.Length > 0 ? Pricing.Find(provider + "/" + model) : null) ?? Pricing.Find(model);
            LogIndex.Add(file, when, "", model, Priced(u, extra, price, recorded));
        }

        // ── GitHub Copilot CLI: ~/.copilot/session-state/*/events.jsonl, otel/*.jsonl ─
        // session.shutdown events hold cumulative per-model usage for a session: each one
        // counts only the growth since the previous shutdown. OpenTelemetry chat spans
        // (gen_ai.usage.*) count per request.
        public static LogIndex ScanCopilot(LogIndex index, DateTime nowUtc) {
            index = new LogIndex { CoveredFrom = nowUtc.AddDays(-LogReader.HorizonDays).ToString("o"), Pricing = Pricing.Version };
            DateTime horizon = nowUtc.AddDays(-LogReader.HorizonDays);
            var file = new LogFile { Name = "copilot" }; index.Files.Add(file);
            string root = Environment.GetEnvironmentVariable("COPILOT_HOME"); root = String.IsNullOrWhiteSpace(root) ? Path.Combine(LogReader.Home, ".copilot") : root.Trim();
            var shutdowns = new List<Tuple<string, string, DateTime, double[]>>(); // session, model, time, [input, output, cacheRead, cacheWrite, reasoning, requests]
            string state = Path.Combine(root, "session-state");
            if (Directory.Exists(state)) {
                foreach (string dir in SafeDirs(state)) {
                    string events = Path.Combine(dir, "events.jsonl"); if (!File.Exists(events)) continue;
                    string session = Path.GetFileName(dir);
                    foreach (string line in SafeLines(events)) {
                        if (!line.Contains("session.shutdown")) continue;
                        object root2; try { root2 = J.Parse(line); } catch { continue; }
                        if (J.Str(root2, "type") != "session.shutdown") continue;
                        DateTime when; if (!LogReader.ParseTime(J.Str(root2, "timestamp"), out when)) continue;
                        foreach (var pair in J.Dict(J.Get(root2, "data", "modelMetrics"))) {
                            object usage = J.Get(pair.Value, "usage"); if (!(usage is IDictionary)) continue;
                            double input = LogReader.N(usage, "inputTokens"), read = LogReader.N(usage, "cacheReadTokens"), write = LogReader.N(usage, "cacheWriteTokens");
                            shutdowns.Add(Tuple.Create(session, pair.Key.Trim(), when, new[] { Math.Max(0, input - read - write), LogReader.N(usage, "outputTokens"), read, write, LogReader.N(usage, "reasoningTokens"), LogReader.N(J.Get(pair.Value, "requests"), "count") }));
                        }
                    }
                }
            }
            foreach (var group in shutdowns.GroupBy(s => s.Item1 + "|" + s.Item2)) {
                double[] previous = null;
                foreach (var entry in group.OrderBy(s => s.Item3)) {
                    double[] v = entry.Item4, d = previous == null ? v : v.Select((x, i) => Math.Max(0, x - previous[i])).ToArray();
                    previous = v;
                    if (entry.Item3 < horizon || d.All(x => x <= 0)) continue;
                    var u = new TokenUse { Input = d[0], Output = d[1], CacheRead = d[2], CacheWrite5m = d[3] };
                    Bucket b = Priced(u, 0, Pricing.Find(entry.Item2), null); b.R = Math.Max(1, d[5]);
                    LogIndex.Add(file, entry.Item3, "", entry.Item2, b);
                }
            }
            string otel = Path.Combine(root, "otel");
            var otelFiles = Directory.Exists(otel) ? LogReader.Files(otel).ToList() : new List<string>();
            string exporter = Environment.GetEnvironmentVariable("COPILOT_OTEL_FILE_EXPORTER_PATH"); if (!String.IsNullOrWhiteSpace(exporter) && File.Exists(exporter.Trim())) otelFiles.Add(exporter.Trim());
            var latest = shutdowns.GroupBy(s => s.Item1 + "|" + s.Item2).ToDictionary(g => g.Key, g => g.Max(s => s.Item3));
            foreach (string path in otelFiles.Distinct(StringComparer.OrdinalIgnoreCase)) {
                foreach (string line in SafeLines(path)) {
                    if (!line.Contains("gen_ai.usage")) continue;
                    object record; try { record = J.Parse(line); } catch { continue; }
                    object attributes = J.Get(record, "attributes"); if (!(attributes is IDictionary)) continue;
                    string name = J.Str(record, "name"); if (!name.StartsWith("chat")) continue;
                    string model = J.Str(attributes, "gen_ai.response.model"); if (model.Length == 0) model = J.Str(attributes, "gen_ai.request.model"); if (model.Length == 0) continue;
                    DateTime when; if (!OtelTime(record, out when) || when < horizon) continue;
                    string session = J.Str(attributes, "gen_ai.conversation.id"); DateTime shut;
                    // Sessions summarised by a later shutdown event are already counted there.
                    if (session.Length > 0 && latest.TryGetValue(session + "|" + model, out shut) && when <= shut) continue;
                    string span = J.Str(record, "spanId"); if (span.Length == 0) span = J.Str(record, "spanContext", "spanId");
                    string key = "o|" + (span.Length > 0 ? span : LogReader.Millis(when) + "|" + model);
                    if (index.Seen.ContainsKey(key)) continue; LogIndex.See(index, key, when, file);
                    double input = LogReader.N(attributes, "gen_ai.usage.input_tokens"), read = LogReader.N(attributes, "gen_ai.usage.cache_read.input_tokens");
                    double write = Math.Max(LogReader.N(attributes, "gen_ai.usage.cache_write.input_tokens"), LogReader.N(attributes, "gen_ai.usage.cache_creation.input_tokens"));
                    var u = new TokenUse { Input = Math.Max(0, input - read - write), Output = LogReader.N(attributes, "gen_ai.usage.output_tokens"), CacheRead = read, CacheWrite5m = write };
                    if (u.Total <= 0) continue;
                    LogIndex.Add(file, when, "", model, Priced(u, 0, Pricing.Find(model), null));
                }
            }
            LogReader.Finish(index, nowUtc, null);
            return index;
        }
        private static bool OtelTime(object record, out DateTime when) {
            when = DateTime.MinValue;
            foreach (string key in new[] { "endTime", "startTime", "timestamp", "time", "observedTimestamp" }) {
                object v = J.Get(record, key); if (v == null) continue;
                var parts = J.Arr(v).ToList();
                if (parts.Count == 2) { double? s = J.Num(parts[0]), ns = J.Num(parts[1]); if (s.HasValue) { when = LogReader.FromMillis(s.Value * 1000 + (ns ?? 0) / 1e6); return true; } }
                double? n = J.Num(v);
                if (n.HasValue && n.Value > 0) { double ms = n.Value > 1e17 ? n.Value / 1e6 : n.Value > 1e14 ? n.Value / 1e3 : n.Value > 1e11 ? n.Value : n.Value * 1000; when = LogReader.FromMillis(ms); return true; }
                if (LogReader.ParseTime(J.Str(record, key), out when)) return true;
            }
            return false;
        }
        private static IEnumerable<string> SafeDirs(string root) { try { return Directory.GetDirectories(root); } catch { return new string[0]; } }
        private static IEnumerable<string> SafeLines(string path) {
            var lines = new List<string>();
            try { using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) { string line; while ((line = reader.ReadLine()) != null) lines.Add(line); } } catch { }
            return lines;
        }

        // ── OpenCode: ~/.local/share/opencode (opencode*.db, storage/message/**) ─
        // Assistant messages carry tokens {input, output, reasoning, cache{read, write}},
        // modelID / providerID and often a cost in USD. Database rows win over the legacy
        // JSON files for the same message id.
        public static LogIndex ScanOpenCode(LogIndex index, DateTime nowUtc) {
            index = new LogIndex { CoveredFrom = nowUtc.AddDays(-LogReader.HorizonDays).ToString("o"), Pricing = Pricing.Version };
            long horizon = LogReader.Millis(nowUtc.AddDays(-LogReader.HorizonDays));
            var file = new LogFile { Name = "opencode" }; index.Files.Add(file);
            List<string> roots = LogReader.EnvRoots("OPENCODE_DATA_DIR");
            if (roots == null) {
                string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                roots = new List<string> { Path.Combine(!String.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(LogReader.Home, ".local", "share"), "opencode") };
            }
            var seen = new HashSet<string>();
            foreach (string root in roots.Where(Directory.Exists)) {
                IEnumerable<string> dbs; try { dbs = Directory.GetFiles(root, "opencode*.db"); } catch { dbs = new string[0]; }
                foreach (string path in dbs) using (Sqlite db = Sqlite.Open(path)) {
                    if (db == null) continue;
                    if (db.HasTable("message")) foreach (object[] r in db.Query("SELECT id, session_id, data FROM message")) OpenCodeMessage(file, seen, Sqlite.Text(r[0]), Sqlite.Text(r[1]), Sqlite.Text(r[2]), null, horizon);
                    if (db.HasTable("session_message")) {
                        HashSet<string> columns = db.Columns("session_message");
                        if (columns.Contains("id") && columns.Contains("session_id") && columns.Contains("data"))
                            foreach (object[] r in db.Query("SELECT id, session_id, data" + (columns.Contains("time_created") ? ", time_created" : ", NULL") + " FROM session_message")) OpenCodeMessage(file, seen, Sqlite.Text(r[0]), Sqlite.Text(r[1]), Sqlite.Text(r[2]), r[3] == null ? (long?)null : Sqlite.Long(r[3]), horizon);
                    }
                }
                foreach (string json in LogReader.Files(Path.Combine(root, "storage", "message"), "*.json")) {
                    string text; try { text = File.ReadAllText(json); } catch { continue; }
                    OpenCodeMessage(file, seen, null, null, text, null, horizon);
                }
            }
            LogReader.Finish(index, nowUtc, null);
            return index;
        }
        private static void OpenCodeMessage(LogFile file, HashSet<string> seen, string id, string session, string data, long? created, long horizon) {
            if (String.IsNullOrEmpty(data) || !data.Contains("tokens")) return;
            object v; try { v = J.Parse(data); } catch { return; }
            object tokens = J.Get(v, "tokens"); if (!(tokens is IDictionary)) return;
            string model = J.Str(v, "model", "id"); if (model.Length == 0) model = J.Str(v, "model", "modelID"); if (model.Length == 0) model = J.Str(v, "modelID");
            string provider = J.Str(v, "model", "providerID"); if (provider.Length == 0) provider = J.Str(v, "providerID");
            if (model.Trim().Length == 0 || provider.Trim().Length == 0) return;
            id = id ?? J.Str(v, "id"); string key = "oc|" + (id ?? "") + "|" + (session ?? J.Str(v, "sessionID"));
            if (!String.IsNullOrEmpty(id) && !seen.Add(key)) return;
            double? ms = J.Num(v, "time", "created"); if ((!ms.HasValue || ms <= 0) && created.HasValue) ms = created;
            if (!ms.HasValue || ms <= 0 || ms < horizon) return;
            var u = new TokenUse { Input = LogReader.N(tokens, "input"), Output = LogReader.N(tokens, "output"), CacheRead = LogReader.N(J.Get(tokens, "cache"), "read"), CacheWrite5m = LogReader.N(J.Get(tokens, "cache"), "write") };
            double extra = LogReader.N(tokens, "reasoning"); TotalFallback(ref u, ref extra, LogReader.N(tokens, "total"));
            if (u.Total + extra <= 0) return;
            double? cost = J.Num(v, "cost"); if (cost.HasValue && cost.Value <= 0) cost = null;
            ModelPrice price = cost.HasValue ? null : Pricing.Find(provider + "/" + model) ?? Pricing.Find(model);
            Bucket b = Priced(u, extra, price, cost);
            // Assistant messages record time.created and time.completed; reasoning is counted apart.
            double? completed = J.Num(v, "time", "completed"); double seconds = completed.HasValue && completed.Value > ms.Value ? (completed.Value - ms.Value) / 1000.0 : 0;
            if (OutputTiming.Accept(u.Output + extra, seconds)) { b.TN = 1; b.TO = u.Output + extra; b.TS = seconds; }
            LogIndex.Add(file, LogReader.FromMillis(ms.Value), "", model, b);
        }
    }
}
