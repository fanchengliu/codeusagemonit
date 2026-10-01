using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace CodeUsageMonit {
    // ── Usage from other devices ──────────────────────────────────────
    // Each install has an id (data\device.json). Its hourly usage can be exported as SQL, or
    // uploaded to a WebDAV folder, and imported on another device. Imported usage is kept
    // per device (data\devices\<id>.json) and added to the local indexes in memory only, so
    // a rescan never overwrites it and importing the same file twice changes nothing.
    public sealed class DeviceIdentity { public string Id = "", Name = ""; }
    public sealed class DeviceData {
        public string Id = "", Name = "", Exported = "", Imported = "", Version = "";
        // agent → hour bucket key (as in LogFile.Hours) → usage of that hour.
        public Dictionary<string, Dictionary<string, Bucket>> Agents = new Dictionary<string, Dictionary<string, Bucket>>();
        public double Tokens() { return Agents.Values.SelectMany(a => a.Values).Sum(b => b.Tokens()); }
        public double Cost() { return Agents.Values.SelectMany(a => a.Values).Sum(b => b.D); }
    }
    public sealed class ImportResult {
        // Added: hours not seen before. Updated: a newer export of the same hours. Same: already
        // imported, unchanged (or an older export). Duplicate: identical to this device's own
        // records for that hour and model, i.e. the same logs copied or synced here.
        public int Added, Updated, Same, Duplicate, Own, Skipped;
        public int Rows { get { return Added + Updated; } }
        public List<string> Devices = new List<string>();
        public string Summary() {
            if (Added + Updated + Same + Duplicate == 0) return Own > 0 ? I18n.T("文件里只有本机的数据，无需导入。") : I18n.T("没有可导入的用量记录。") + (Skipped > 0 ? I18n.T("；{0} 条格式不对或已超过保留期，未导入", Skipped) : "");
            string text = Rows == 0 ? I18n.T("{0}：没有新的用量，之前已经导入过或与本机记录相同", String.Join("、", Devices))
                : I18n.T("{0}：新增 {1} 条、更新 {2} 条小时记录", String.Join("、", Devices), Added, Updated);
            if (Same > 0 && Rows > 0) text += I18n.T("；{0} 条之前已导入，未重复计算", Same);
            if (Duplicate > 0) text += I18n.T("；{0} 条与本机自己的记录完全一致（多半是复制或同步过来的同一份日志），已自动去重", Duplicate);
            if (Own > 0) text += I18n.T("；跳过本机的 {0} 条", Own);
            if (Skipped > 0) text += I18n.T("；{0} 条格式不对或已超过保留期，未导入", Skipped);
            return text + I18n.T("。");
        }
    }

    public static class Devices {
        public const string Folder = "devices";
        private const string FilePrefix = "device:";
        // Cursor's usage is read from the account, so every device already sees all of it.
        public static readonly string[] AccountWide = { "cursor" };
        private static readonly object gate = new object();
        private static readonly Regex SafeId = new Regex("^[A-Za-z0-9_-]{1,64}$");

        public static DeviceIdentity Self() {
            lock (gate) {
                var d = Store.Read<DeviceIdentity>("device.json");
                if (String.IsNullOrEmpty(d.Id) || !SafeId.IsMatch(d.Id)) {
                    d = new DeviceIdentity { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Name = Environment.MachineName };
                    try { Store.Write("device.json", d); } catch { }
                }
                if (String.IsNullOrWhiteSpace(d.Name)) d.Name = Environment.MachineName;
                return d;
            }
        }
        public static void Rename(string name) {
            lock (gate) { var d = Self(); d.Name = String.IsNullOrWhiteSpace(name) ? Environment.MachineName : name.Trim(); if (d.Name.Length > 40) d.Name = d.Name.Substring(0, 40); Store.Write("device.json", d); }
        }
        public static List<DeviceData> Imported() {
            var list = new List<DeviceData>();
            string dir = Path.Combine(Store.Data, Folder);
            if (!Directory.Exists(dir)) return list;
            foreach (string file in Directory.GetFiles(dir, "*.json")) {
                DeviceData d = Store.Read<DeviceData>(Path.Combine(Folder, Path.GetFileName(file)));
                if (SafeId.IsMatch(d.Id ?? "")) list.Add(d);
            }
            return list.OrderBy(d => d.Name, StringComparer.CurrentCulture).ToList();
        }
        public static void Remove(string id) {
            if (!SafeId.IsMatch(id ?? "")) return;
            string path = Path.Combine(Store.Data, Folder, id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        private static void Save(DeviceData d) { Directory.CreateDirectory(Path.Combine(Store.Data, Folder)); Store.Write(Path.Combine(Folder, d.Id + ".json"), d); }

        // The local indexes plus every imported device's hours, as new index objects (the
        // local ones are what the scanners persist and must stay this device's own).
        public static Dictionary<string, LogIndex> Attach(Dictionary<string, LogIndex> local, IEnumerable<DeviceData> devices) {
            var result = new Dictionary<string, LogIndex>(local);
            List<DeviceData> list = devices.ToList();
            foreach (string agent in list.SelectMany(d => d.Agents.Keys).Distinct().ToList()) {
                if (AccountWide.Contains(agent)) continue;
                LogIndex mine; local.TryGetValue(agent, out mine);
                var merged = new LogIndex();
                if (mine != null) { merged.CoveredFrom = mine.CoveredFrom; merged.Updated = mine.Updated; merged.Pricing = mine.Pricing; merged.Seen = mine.Seen; merged.Files.AddRange(mine.Files); }
                string earliest = null;
                foreach (DeviceData d in list) {
                    Dictionary<string, Bucket> hours;
                    if (!d.Agents.TryGetValue(agent, out hours) || hours.Count == 0) continue;
                    merged.Files.Add(new LogFile { Name = FilePrefix + d.Id, Hours = hours });
                    string first = hours.Keys.OrderBy(k => k, StringComparer.Ordinal).First(); if (earliest == null || String.CompareOrdinal(first, earliest) < 0) earliest = first;
                }
                if (merged.Files.Count == (mine == null ? 0 : mine.Files.Count)) continue;
                if (mine == null && earliest != null) {
                    DateTime at; if (DateTime.TryParseExact(earliest.Substring(0, 10), "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) merged.CoveredFrom = at.ToString("o");
                }
                result[agent] = merged;
            }
            return result;
        }
        public static bool IsImported(LogFile file) { return file.Name.StartsWith(FilePrefix, StringComparison.Ordinal); }

        // ── SQL export ────────────────────────────────────────────────
        private static readonly string[] Columns = { "device_id", "agent", "hour_utc", "provider", "model", "endpoint", "input_tokens", "cache_read_tokens", "cache_write_tokens", "output_tokens", "other_tokens", "requests", "cost_usd", "unpriced_tokens", "timed_requests", "timed_output_tokens", "timed_seconds" };
        // This device's usage (from the saved local indexes) and, with withImported, the
        // devices imported here. SQLite-compatible: sqlite3 usage.db < export.sql.
        public static string ExportSql(DateTime nowUtc, bool withImported) {
            DeviceIdentity self = Self();
            var sql = new StringBuilder();
            sql.Append("-- codeusagemonit usage export\n");
            sql.Append("-- device: ").Append(OneLine(self.Name)).Append(" (").Append(self.Id).Append("), exported ").Append(nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(", codeusagemonit ").Append(AppInfo.Version).Append('\n');
            sql.Append("-- One row per device, agent, UTC hour, provider, model and endpoint. Token counts, requests,\n");
            sql.Append("-- API-equivalent cost and timing only: no conversation text, keys or account details.\n");
            sql.Append("-- Import it in codeusagemonit (Settings > Data), or load it into SQLite: sqlite3 usage.db < this.sql\n");
            sql.Append("BEGIN TRANSACTION;\n");
            sql.Append("CREATE TABLE IF NOT EXISTS cum_devices (device_id TEXT PRIMARY KEY, name TEXT, exported_utc TEXT, app_version TEXT);\n");
            sql.Append("CREATE TABLE IF NOT EXISTS cum_usage_hours (device_id TEXT NOT NULL, agent TEXT NOT NULL, hour_utc TEXT NOT NULL, provider TEXT NOT NULL, model TEXT NOT NULL, endpoint TEXT, input_tokens REAL, cache_read_tokens REAL, cache_write_tokens REAL, output_tokens REAL, other_tokens REAL, requests REAL, cost_usd REAL, unpriced_tokens REAL, timed_requests REAL, timed_output_tokens REAL, timed_seconds REAL, PRIMARY KEY (device_id, agent, hour_utc, provider, model, endpoint));\n");
            var devices = new List<DeviceData> { Own(self, nowUtc) };
            if (withImported) devices.AddRange(Imported().Where(d => d.Id != self.Id));
            foreach (DeviceData d in devices)
                sql.Append("INSERT OR REPLACE INTO cum_devices (device_id, name, exported_utc, app_version) VALUES (").Append(Lit(d.Id)).Append(", ").Append(Lit(d.Name)).Append(", ").Append(Lit(d.Exported)).Append(", ").Append(Lit(d.Version)).Append(");\n");
            string head = "INSERT OR REPLACE INTO cum_usage_hours (" + String.Join(", ", Columns) + ") VALUES (";
            foreach (DeviceData d in devices)
                foreach (var agent in d.Agents.OrderBy(a => a.Key, StringComparer.Ordinal))
                    foreach (var hour in agent.Value.OrderBy(h => h.Key, StringComparer.Ordinal)) {
                        string[] parts = hour.Key.Split('|'); DateTime at;
                        if (!DateTime.TryParseExact(parts[0], "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) continue;
                        Bucket b = hour.Value;
                        sql.Append(head).Append(Lit(d.Id)).Append(", ").Append(Lit(agent.Key)).Append(", ").Append(Lit(at.ToString("yyyy-MM-ddTHH:00:00Z", CultureInfo.InvariantCulture))).Append(", ")
                           .Append(Lit(parts.Length > 1 ? parts[1] : "")).Append(", ").Append(Lit(parts.Length > 2 ? parts[2] : "")).Append(", ").Append(parts.Length > 3 ? Lit(parts[3]) : "NULL");
                        foreach (double v in new[] { b.I, b.C, b.W, b.O, b.X, b.R, b.D, b.U, b.TN, b.TO, b.TS }) sql.Append(", ").Append(Num(v));
                        sql.Append(");\n");
                    }
            sql.Append("COMMIT;\n");
            return sql.ToString();
        }
        // This device's hours from the saved indexes (several log files may share an hour).
        // Claude / Codex hours read before endpoint tracking carry no endpoint: the one in
        // effect then is written out, so another device attributes them the same way.
        private static DeviceData Own(DeviceIdentity self, DateTime nowUtc) {
            var d = new DeviceData { Id = self.Id, Name = self.Name, Exported = nowUtc.ToString("o"), Version = AppInfo.Version };
            EndpointLog log = Store.Read<EndpointLog>("endpoints.json");
            foreach (UsageScanner.Agent agent in UsageScanner.Agents) {
                if (AccountWide.Contains(agent.Id)) continue;
                LogIndex index = Store.Read<LogIndex>(agent.File);
                if (index.Version != LogIndex.CurrentVersion) continue;
                var hours = new Dictionary<string, Bucket>(StringComparer.Ordinal);
                foreach (HourUsage h in index.Entries()) {
                    string endpoint = h.Endpoint;
                    if (endpoint == null && (agent.Id == "claude" || (agent.Id == "codex" && !CodexLogs.IsOfficial(h.Provider)))) {
                        EndpointMark mark = Endpoints.Resolve(log, agent.Id == "claude" ? Endpoints.ClaudeApp : Endpoints.CodexApp, h.Hour);
                        endpoint = mark == null ? "?" : mark.Official ? "" : EndpointAttribution.Id(mark);
                    }
                    string key = LogIndex.BucketKey(h.Hour, h.Provider, h.Model, endpoint);
                    Bucket b; if (!hours.TryGetValue(key, out b)) hours[key] = b = new Bucket();
                    b.I += h.Input; b.C += h.Cached; b.W += h.CacheWrite; b.O += h.Output; b.X += h.Other; b.R += h.Requests; b.D += h.Cost; b.U += h.Unpriced; b.TN += h.TimedRequests; b.TO += h.TimedOutput; b.TS += h.TimedSeconds;
                }
                if (hours.Count > 0) d.Agents[agent.Id] = hours;
            }
            return d;
        }
        private static string Lit(string s) { return s == null ? "NULL" : "'" + s.Replace("'", "''") + "'"; }
        private static string Num(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string OneLine(string s) { return (s ?? "").Replace('\r', ' ').Replace('\n', ' '); }

        // ── SQL import ────────────────────────────────────────────────
        // Reads the INSERT statements of an export (other statements are ignored) and merges them
        // into what this device already has, deciding per hour without asking:
        //   · rows of this device are skipped (its own logs are the source);
        //   · a device's hour seen before is updated only by a newer export (totals only grow);
        //   · an hour / provider / model whose totals equal this device's own record exactly is the
        //     same usage (logs copied or synced here) and is left out, so it is not counted twice.
        public static ImportResult ImportSql(string text, DateTime nowUtc) {
            DeviceIdentity self = Self();
            var result = new ImportResult();
            var names = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            var usage = new List<Dictionary<string, object>>();
            foreach (var insert in Inserts(text)) {
                if (insert.Key == "cum_devices") { object id; if (insert.Value.TryGetValue("device_id", out id) && id is string) names[(string)id] = insert.Value; }
                else if (insert.Key == "cum_usage_hours") usage.Add(insert.Value);
            }
            var known = new HashSet<string>(UsageScanner.Agents.Select(a => a.Id).Where(id => !AccountWide.Contains(id)));
            DateTime oldest = nowUtc.AddDays(-LogReader.RetainDays);
            // Valid rows, and their totals per device / agent / hour / provider / model.
            var rows = new List<ImportRow>();
            var groups = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            foreach (var row in usage) {
                string device = Text(row, "device_id"), agent = Text(row, "agent");
                if (device == self.Id) { result.Own++; continue; }
                DateTime hour;
                if (!SafeId.IsMatch(device ?? "") || agent == null || !known.Contains(agent) || !DateTime.TryParse(Text(row, "hour_utc") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out hour) || hour < oldest) { result.Skipped++; continue; }
                hour = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, DateTimeKind.Utc);
                var r = new ImportRow { Device = device, Agent = agent, Group = agent + "|" + LogIndex.BucketKey(hour, Text(row, "provider") ?? "", Text(row, "model") ?? ""), Key = LogIndex.BucketKey(hour, Text(row, "provider") ?? "", Text(row, "model") ?? "", Text(row, "endpoint")),
                    Usage = new Bucket { I = Number(row, "input_tokens"), C = Number(row, "cache_read_tokens"), W = Number(row, "cache_write_tokens"), O = Number(row, "output_tokens"), X = Number(row, "other_tokens"), R = Number(row, "requests"),
                        D = Number(row, "cost_usd"), U = Number(row, "unpriced_tokens"), TN = Number(row, "timed_requests"), TO = Number(row, "timed_output_tokens"), TS = Number(row, "timed_seconds") } };
                rows.Add(r);
                Bucket sum; if (!groups.TryGetValue(device + "|" + r.Group, out sum)) groups[device + "|" + r.Group] = sum = new Bucket(); sum.Add(r.Usage, 1);
            }
            Dictionary<string, Bucket> local = LocalTotals(rows.Select(r => r.Agent).Distinct());
            var touched = new Dictionary<string, DeviceData>(StringComparer.Ordinal);
            lock (gate) {
                foreach (ImportRow r in rows) {
                    Bucket mine;
                    if (local.TryGetValue(r.Group, out mine) && SameUsage(groups[r.Device + "|" + r.Group], mine)) { result.Duplicate++; continue; }
                    DeviceData d;
                    if (!touched.TryGetValue(r.Device, out d)) {
                        d = Imported().FirstOrDefault(x => x.Id == r.Device) ?? new DeviceData { Id = r.Device, Name = r.Device };
                        touched[r.Device] = d;
                    }
                    Dictionary<string, Bucket> hours; if (!d.Agents.TryGetValue(r.Agent, out hours)) d.Agents[r.Agent] = hours = new Dictionary<string, Bucket>(StringComparer.Ordinal);
                    Bucket before;
                    if (!hours.TryGetValue(r.Key, out before)) { hours[r.Key] = r.Usage; result.Added++; }
                    else if (SameUsage(before, r.Usage) || r.Usage.Tokens() < before.Tokens() || (r.Usage.Tokens() == before.Tokens() && r.Usage.R <= before.R)) result.Same++;
                    else { hours[r.Key] = r.Usage; result.Updated++; }
                }
                string cutoff = LogIndex.HourKey(oldest);
                foreach (DeviceData d in touched.Values) {
                    Dictionary<string, object> meta;
                    if (names.TryGetValue(d.Id, out meta)) { string n = Text(meta, "name"); if (!String.IsNullOrWhiteSpace(n)) d.Name = n.Length > 40 ? n.Substring(0, 40) : n; d.Exported = Text(meta, "exported_utc") ?? ""; d.Version = Text(meta, "app_version") ?? ""; }
                    d.Imported = nowUtc.ToString("o");
                    foreach (var hours in d.Agents.Values) foreach (string old in hours.Keys.Where(k => String.CompareOrdinal(k, cutoff) < 0).ToList()) hours.Remove(old);
                    Save(d);
                    result.Devices.Add(d.Name);
                }
                foreach (string device in rows.Where(r => !touched.ContainsKey(r.Device)).Select(r => r.Device).Distinct()) {
                    Dictionary<string, object> meta; string n = names.TryGetValue(device, out meta) ? Text(meta, "name") : null;
                    result.Devices.Add(String.IsNullOrWhiteSpace(n) ? device : n);
                }
            }
            return result;
        }
        private sealed class ImportRow { public string Device, Agent, Group, Key; public Bucket Usage; }
        // This device's own totals per agent / hour / provider / model (all endpoints together).
        private static Dictionary<string, Bucket> LocalTotals(IEnumerable<string> agents) {
            var totals = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            foreach (UsageScanner.Agent agent in UsageScanner.Agents.Where(a => agents.Contains(a.Id))) {
                LogIndex index = Store.Read<LogIndex>(agent.File);
                if (index.Version != LogIndex.CurrentVersion) continue;
                foreach (HourUsage h in index.Entries()) {
                    string key = agent.Id + "|" + LogIndex.BucketKey(h.Hour, h.Provider, h.Model);
                    Bucket b; if (!totals.TryGetValue(key, out b)) totals[key] = b = new Bucket();
                    b.I += h.Input; b.C += h.Cached; b.W += h.CacheWrite; b.O += h.Output; b.X += h.Other; b.R += h.Requests;
                }
            }
            return totals;
        }
        // Token counts and requests equal: the same usage, not a coincidence.
        private static bool SameUsage(Bucket a, Bucket b) {
            return a.Tokens() > 0 && Math.Abs(a.I - b.I) < .5 && Math.Abs(a.C - b.C) < .5 && Math.Abs(a.W - b.W) < .5 && Math.Abs(a.O - b.O) < .5 && Math.Abs(a.X - b.X) < .5 && Math.Abs(a.R - b.R) < .5;
        }
        private static string Text(Dictionary<string, object> row, string column) { object v; return row.TryGetValue(column, out v) ? v as string : null; }
        private static double Number(Dictionary<string, object> row, string column) {
            object v; if (!row.TryGetValue(column, out v) || v == null) return 0;
            if (v is double) return (double)v;
            double n; return Double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) ? n : 0;
        }
        // INSERT [OR …] INTO table [(columns)] VALUES (…), (…); → (table, column → value) per
        // row. Values: 'text' (with '' escapes), numbers and NULL. Comments are skipped.
        internal static IEnumerable<KeyValuePair<string, Dictionary<string, object>>> Inserts(string sql) {
            foreach (string statement in Statements(sql ?? "")) {
                Match m = Regex.Match(statement, @"^\s*INSERT\s+(?:OR\s+\w+\s+)?INTO\s+[""`\[]?(\w+)[""`\]]?\s*(?:\(([^)]*)\))?\s*VALUES\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!m.Success) continue;
                string table = m.Groups[1].Value.ToLowerInvariant();
                string[] columns = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0
                    ? m.Groups[2].Value.Split(',').Select(c => c.Trim().Trim('"', '`', '[', ']').ToLowerInvariant()).ToArray()
                    : table == "cum_usage_hours" ? Columns : table == "cum_devices" ? new[] { "device_id", "name", "exported_utc", "app_version" } : new string[0];
                foreach (List<object> tuple in Tuples(m.Groups[3].Value)) {
                    var row = new Dictionary<string, object>(StringComparer.Ordinal);
                    for (int i = 0; i < columns.Length && i < tuple.Count; i++) row[columns[i]] = tuple[i];
                    yield return new KeyValuePair<string, Dictionary<string, object>>(table, row);
                }
            }
        }
        private static IEnumerable<string> Statements(string sql) {
            var current = new StringBuilder();
            for (int i = 0; i < sql.Length; i++) {
                char c = sql[i];
                if (c == '\'') {
                    current.Append(c);
                    for (i++; i < sql.Length; i++) { current.Append(sql[i]); if (sql[i] == '\'') { if (i + 1 < sql.Length && sql[i + 1] == '\'') { current.Append('\''); i++; } else break; } }
                } else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { while (i < sql.Length && sql[i] != '\n') i++; current.Append('\n'); }
                else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal); i = end < 0 ? sql.Length : end + 1; current.Append(' '); }
                else if (c == ';') { if (current.ToString().Trim().Length > 0) yield return current.ToString(); current.Clear(); }
                else current.Append(c);
            }
            if (current.ToString().Trim().Length > 0) yield return current.ToString();
        }
        private static IEnumerable<List<object>> Tuples(string values) {
            int i = 0;
            while (i < values.Length) {
                while (i < values.Length && values[i] != '(') i++;
                if (i >= values.Length) yield break;
                i++;
                var tuple = new List<object>();
                while (i < values.Length) {
                    while (i < values.Length && Char.IsWhiteSpace(values[i])) i++;
                    if (i >= values.Length) break;
                    if (values[i] == ')') { i++; break; }
                    if (values[i] == ',') { i++; continue; }
                    if (values[i] == '\'') {
                        var text = new StringBuilder();
                        for (i++; i < values.Length; i++) { if (values[i] == '\'') { if (i + 1 < values.Length && values[i + 1] == '\'') { text.Append('\''); i++; } else { i++; break; } } else text.Append(values[i]); }
                        tuple.Add(text.ToString());
                    } else {
                        int start = i; while (i < values.Length && values[i] != ',' && values[i] != ')') i++;
                        string token = values.Substring(start, i - start).Trim();
                        double n;
                        tuple.Add(token.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? (object)n : token);
                    }
                }
                yield return tuple;
            }
        }
    }

    // ── Backups ───────────────────────────────────────────────────────
    public sealed class BackupInfo { public string Path = "", Name = ""; public DateTime Created; public long Size; public bool Auto; }
    public static class Backups {
        private const string Pattern = "codeusagemonit-backup-*.zip", Manifest = "backup.json";
        public static string Folder(AppConfig config) {
            return config != null && !String.IsNullOrWhiteSpace(config.BackupFolder) ? config.BackupFolder.Trim() : Path.Combine(Store.Data, "backups");
        }
        // What a backup holds: settings, custom providers, the endpoint timeline, the usage
        // indexes, imported devices and the background picture. Not the DPAPI keys (bound to
        // this Windows user, they stay where they are) and not this install's device id.
        public static bool Included(string relative) {
            string name = (relative ?? "").Replace('\\', '/');
            if (name.Contains("..") || name.StartsWith("/")) return false;
            if (name.StartsWith(Devices.Folder + "/", StringComparison.OrdinalIgnoreCase)) return name.Count(c => c == '/') == 1 && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            if (name.Contains("/")) return false;
            return name == "settings.json" || name == "endpoints.json" || name == "custom-providers.json" || name == "history.json"
                || name.EndsWith("-logs.json", StringComparison.OrdinalIgnoreCase) || name.StartsWith("background.", StringComparison.OrdinalIgnoreCase);
        }
        public static string Create(AppConfig config, bool auto, DateTime nowUtc) {
            string dir = Folder(config); Directory.CreateDirectory(dir);
            string stamp = nowUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, "codeusagemonit-backup-" + stamp + (auto ? "-auto" : "") + ".zip"), temp = path + ".tmp";
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, "codeusagemonit-backup-" + stamp + "-" + n + (auto ? "-auto" : "") + ".zip");
            try {
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create)) {
                    string root = Store.Data.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    foreach (string file in Directory.GetFiles(Store.Data, "*", SearchOption.AllDirectories)) {
                        string relative = file.Substring(root.Length).Replace('\\', '/');
                        if (!Included(relative)) continue;
                        // A scan may replace an index at this moment: read it into memory first.
                        byte[] bytes = null;
                        for (int attempt = 0; attempt < 3 && bytes == null; attempt++) { try { bytes = File.ReadAllBytes(file); } catch (IOException) { Thread.Sleep(150); } }
                        if (bytes == null) throw new IOException(I18n.T("无法读取 {0}", relative));
                        using (Stream s = zip.CreateEntry(relative, CompressionLevel.Optimal).Open()) s.Write(bytes, 0, bytes.Length);
                    }
                    DeviceIdentity self = Devices.Self();
                    string manifest = J.Serializer().Serialize(new Dictionary<string, object> { { "app", "codeusagemonit" }, { "version", AppInfo.Version }, { "created", nowUtc.ToString("o") }, { "device", self.Name }, { "auto", auto } });
                    using (var writer = new StreamWriter(zip.CreateEntry(Manifest).Open(), new UTF8Encoding(false))) writer.Write(manifest);
                }
                File.Move(temp, path);
            } finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch { } } }
            return path;
        }
        public static List<BackupInfo> List(AppConfig config) {
            string dir = Folder(config);
            if (!Directory.Exists(dir)) return new List<BackupInfo>();
            return Directory.GetFiles(dir, Pattern).Select(p => { var info = new FileInfo(p); return new BackupInfo { Path = p, Name = info.Name, Created = info.LastWriteTimeUtc, Size = info.Length, Auto = info.Name.EndsWith("-auto.zip", StringComparison.OrdinalIgnoreCase) }; })
                .OrderByDescending(b => b.Created).ToList();
        }
        // Keeps the newest `keep` automatic backups; backups made by hand are never removed.
        public static int Prune(AppConfig config) {
            int removed = 0;
            foreach (BackupInfo old in List(config).Where(b => b.Auto).Skip(Math.Max(1, config.BackupKeep))) { try { File.Delete(old.Path); removed++; } catch { } }
            return removed;
        }
        public static bool Due(AppConfig config, DateTime nowUtc) {
            if (config.BackupHours <= 0) return false;
            BackupInfo newest = List(config).FirstOrDefault(b => b.Auto);
            return newest == null || nowUtc - newest.Created >= TimeSpan.FromHours(config.BackupHours);
        }
        // Writes the backup's files over the current ones, after a backup of the current
        // state (made by hand, so pruning never removes it). Returns that safety backup.
        public static string Restore(AppConfig config, string zipPath, DateTime nowUtc) {
            using (var zip = ZipFile.OpenRead(zipPath)) {
                if (zip.GetEntry(Manifest) == null) throw new InvalidDataException(I18n.T("这不是 codeusagemonit 的备份文件。"));
                string safety = Create(config, false, nowUtc);
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (!Included(entry.FullName) || entry.Length > 512L * 1024 * 1024) continue;
                    string target = Path.Combine(Store.Data, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (Stream source = entry.Open()) using (FileStream output = File.Create(temp)) source.CopyTo(output);
                    if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                }
                return safety;
            }
        }
    }

    // ── WebDAV sync ───────────────────────────────────────────────────
    // <folder>/devices/<device id>.sql per device: each device uploads only its own usage and
    // imports everyone else's, so devices never overwrite each other.
    public sealed class SyncState { public string Last = "", Message = ""; public bool Ok; }
    public static class WebDavSync {
        public const string KeyId = "webdav";
        public static string Validate(string url) {
            if (String.IsNullOrWhiteSpace(url)) return I18n.T("请填写 WebDAV 文件夹地址。");
            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return I18n.T("地址应为 https://… 开头的完整网址。");
            if (uri.UserInfo.Length > 0) return I18n.T("用户名和密码请填在下面的输入框里，不要写进地址。");
            if (uri.Scheme == Uri.UriSchemeHttp && !CustomProviders.IsPrivateHost(uri)) return I18n.T("公网地址必须使用 https；http 只允许本机或局域网地址。");
            return null;
        }
        private static string Folder(string url) { url = url.Trim(); return url.EndsWith("/") ? url : url + "/"; }
        private static HttpClient Client(AppConfig config, string user, string password) {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            string proxy = ProviderService.ResolveProxy(config.Proxy); handler.UseProxy = proxy.Length > 0; if (handler.UseProxy) handler.Proxy = new WebProxy(proxy);
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
            if (!String.IsNullOrEmpty(user) || !String.IsNullOrEmpty(password)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes((user ?? "") + ":" + (password ?? ""))));
            return client;
        }
        private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string url, string body, string depth) {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (depth != null) request.Headers.TryAddWithoutValidation("Depth", depth);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, method == "PROPFIND" ? "application/xml" : "application/sql");
            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status == 401) throw new ProviderException(I18n.T("WebDAV 用户名或密码不对（401）。坚果云等服务需要使用“应用密码”。"), "error");
            if (status == 403) throw new ProviderException(I18n.T("WebDAV 拒绝访问（403），请检查文件夹权限。"), "error");
            if (status >= 300 && status < 400) throw new ProviderException(I18n.T("WebDAV 地址被重定向（HTTP {0}），请填写最终的文件夹地址。", status), "error");
            return response;
        }
        private static async Task Ensure(HttpClient client, string folder) {
            using (HttpResponseMessage probe = await Send(client, "PROPFIND", folder, null, "0").ConfigureAwait(false)) if ((int)probe.StatusCode == 207 || probe.IsSuccessStatusCode) return;
            using (HttpResponseMessage made = await Send(client, "MKCOL", folder, null, null).ConfigureAwait(false)) {
                int status = (int)made.StatusCode;
                if (!made.IsSuccessStatusCode && status != 405) throw new ProviderException(I18n.T("无法在 WebDAV 上创建文件夹（HTTP {0}）。", status), "error");
            }
        }
        // Names of the .sql files directly inside folder.
        internal static List<string> Files(string multistatus, string folder) {
            var names = new List<string>();
            var xml = new XmlDocument { XmlResolver = null };
            xml.LoadXml(multistatus);
            var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("d", "DAV:");
            string folderPath = new Uri(folder).AbsolutePath.TrimEnd('/');
            foreach (XmlNode href in xml.SelectNodes("//d:response/d:href", ns)) {
                string path = href.InnerText.Trim(); Uri absolute;
                if (Uri.TryCreate(path, UriKind.Absolute, out absolute)) path = absolute.AbsolutePath;
                path = Uri.UnescapeDataString(path).TrimEnd('/');
                string name = path.Substring(path.LastIndexOf('/') + 1);
                if (String.Equals(Uri.UnescapeDataString(folderPath), path, StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,64}\\.sql$")) names.Add(name);
            }
            return names;
        }
        public static async Task<string> Test(AppConfig config, string url, string user, string password) {
            string error = Validate(url); if (error != null) throw new ProviderException(error, "setup");
            using (HttpClient client = Client(config, user, password))
            using (HttpResponseMessage response = await Send(client, "PROPFIND", Folder(url), null, "0").ConfigureAwait(false)) {
                int status = (int)response.StatusCode;
                if (status == 404) return I18n.T("连接成功；文件夹还不存在，第一次同步时会创建。");
                if (status != 207 && !response.IsSuccessStatusCode) throw new ProviderException(I18n.T("WebDAV 返回 HTTP {0}。", status), "error");
                return I18n.T("连接成功。");
            }
        }
        public static async Task<string> Run(AppConfig config, string url, string user, string password, DateTime nowUtc) {
            string error = Validate(url); if (error != null) throw new ProviderException(error, "setup");
            string root = Folder(url), devices = root + "devices/";
            DeviceIdentity self = Devices.Self();
            using (HttpClient client = Client(config, user, password)) {
                await Ensure(client, root).ConfigureAwait(false);
                await Ensure(client, devices).ConfigureAwait(false);
                using (HttpResponseMessage put = await Send(client, "PUT", devices + self.Id + ".sql", Devices.ExportSql(nowUtc, false), null).ConfigureAwait(false))
                    if (!put.IsSuccessStatusCode) throw new ProviderException(I18n.T("上传本机数据失败（HTTP {0}）。", (int)put.StatusCode), "error");
                string listing;
                using (HttpResponseMessage list = await Send(client, "PROPFIND", devices, "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getlastmodified/></d:prop></d:propfind>", "1").ConfigureAwait(false)) {
                    if ((int)list.StatusCode != 207) throw new ProviderException(I18n.T("读取 WebDAV 文件列表失败（HTTP {0}）。", (int)list.StatusCode), "error");
                    listing = await list.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                var merged = new List<string>(); int rows = 0, others = 0;
                foreach (string name in Files(listing, devices)) {
                    if (name == self.Id + ".sql") continue;
                    others++;
                    using (HttpResponseMessage get = await Send(client, "GET", devices + Uri.EscapeDataString(name), null, null).ConfigureAwait(false)) {
                        if (!get.IsSuccessStatusCode) continue;
                        if (get.Content.Headers.ContentLength > 64L * 1024 * 1024) continue;
                        ImportResult result = Devices.ImportSql(await get.Content.ReadAsStringAsync().ConfigureAwait(false), nowUtc);
                        rows += result.Rows; merged.AddRange(result.Devices);
                    }
                }
                if (others == 0) return I18n.T("已上传本机数据；文件夹里还没有其他设备。");
                return merged.Count == 0 ? I18n.T("已上传本机数据；另外 {0} 台设备没有可合并的用量。", others) : I18n.T("已上传本机数据，并合并 {0} 台设备（{1}）。", merged.Count, String.Join("、", merged.Distinct()));
            }
        }
        public static SyncState State() { return Store.Read<SyncState>("sync-state.json"); }
        public static void Record(bool ok, string message, DateTime nowUtc) { try { Store.Write("sync-state.json", new SyncState { Last = nowUtc.ToString("o"), Ok = ok, Message = message }); } catch { } }
        public static bool Due(AppConfig config, DateTime nowUtc) {
            if (config.SyncMinutes <= 0 || String.IsNullOrWhiteSpace(config.WebDavUrl)) return false;
            DateTime last; return !LogIndex.Parse(State().Last, out last) || nowUtc - last >= TimeSpan.FromMinutes(config.SyncMinutes);
        }
    }
}
