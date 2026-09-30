using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CodeUsageMonit {
    // Which endpoint a client (Claude Code / Codex) was configured for, from a point in
    // time on. Keys are never stored: only a 6-hex SHA-256 fingerprint that tells two
    // accounts on the same host apart.
    public sealed class EndpointMark {
        public string App = "", Host = "", Key = "", Name = "", Since = "";
        public bool Official;
        public string Id { get { return App + "|" + Host + "|" + Key; } }
    }
    public sealed class EndpointLog { public List<EndpointMark> Marks = new List<EndpointMark>(); }
    public sealed class EndpointUsage {
        public string App = "", Host = "", Key = "", Name = "", LastUsed = "", MainModel = "";
        public bool Current, CostPartial;
        public double Today, Week, Month, Requests, CostToday, CostWeek, CostMonth, TimedOutput, TimedSeconds;
        public double? Speed { get { return OutputTiming.Speed(TimedOutput, TimedSeconds); } }
        public double[] Daily = new double[30];
        public string Id { get { return App + "|" + Host + "|" + Key; } }
        public string Title { get { return Name.Length > 0 ? Name : Host.Length > 0 ? Host : "地址未记录的第三方接口"; } }
    }
    public sealed class ThirdPartySummary {
        public List<EndpointUsage> Endpoints = new List<EndpointUsage>();
        public double ClaudeUnattributed;
        public string ClaudeTrackedSince = "";
        public EndpointMark ClaudeNow, CodexNow;
        public double AppMonth(string app) { return Endpoints.Where(e => e.App == app).Sum(e => e.Month); }
    }

    public static class Endpoints {
        public const string ClaudeApp = "claude", CodexApp = "codex";
        public static EndpointMark Current(string app) { try { return app == ClaudeApp ? Claude() : Codex(); } catch { return null; } }
        private static EndpointMark Claude() {
            string path = Path.Combine(ClaudeLogs.ConfigDirs().First(), "settings.json");
            object settings = File.Exists(path) ? J.File(path) : null;
            string url = J.Str(settings, "env", "ANTHROPIC_BASE_URL"); if (url.Length == 0) url = Env("ANTHROPIC_BASE_URL");
            string key = FirstNonEmpty(J.Str(settings, "env", "ANTHROPIC_AUTH_TOKEN"), J.Str(settings, "env", "ANTHROPIC_API_KEY"), Env("ANTHROPIC_AUTH_TOKEN"), Env("ANTHROPIC_API_KEY"));
            string host = Host(url);
            return Mark(ClaudeApp, host, host.Length == 0 || host == "api.anthropic.com", key, File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow);
        }
        private static EndpointMark Codex() {
            string home = ProviderService.CodexHome(), configPath = Path.Combine(home, "config.toml"), authPath = Path.Combine(home, "auth.json");
            string toml = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
            string provider = ActiveCodexProvider(toml);
            DateTime changed = new[] { configPath, authPath }.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.UtcNow).Max();
            if (provider == CodexLogs.Official) return Mark(CodexApp, "", true, "", changed);
            string section = "model_providers." + provider, host = Host(Toml(toml, section, "base_url"));
            string envKey = Toml(toml, section, "env_key");
            string key = FirstNonEmpty(envKey.Length > 0 ? Env(envKey) : "", Toml(toml, section, "experimental_bearer_token"), File.Exists(authPath) ? J.Str(J.File(authPath), "OPENAI_API_KEY") : "");
            return Mark(CodexApp, host.Length > 0 ? host : provider, host == "api.openai.com", key, changed);
        }
        public static string ActiveCodexProvider(string toml) {
            string provider = Toml(toml, "", "model_provider"), profile = Toml(toml, "", "profile");
            if (profile.Length > 0) { string fromProfile = Toml(toml, "profiles." + profile, "model_provider"); if (fromProfile.Length > 0) provider = fromProfile; }
            return provider.Length == 0 ? CodexLogs.Official : provider;
        }
        private static EndpointMark Mark(string app, string host, bool official, string key, DateTime changedUtc) {
            return new EndpointMark { App = app, Host = official ? "" : host, Official = official, Key = official ? "" : Fingerprint(key), Since = changedUtc.ToUniversalTime().ToString("o") };
        }
        // Records a new mark when the configured endpoint differs from the last one. The
        // config file's write time is used as the switch time: every request after it
        // certainly used the new endpoint. Requests before the first mark stay unknown.
        public static bool Observe(EndpointLog log, DateTime nowUtc, Dictionary<string, string> names) {
            bool changed = false;
            foreach (string app in new[] { ClaudeApp, CodexApp }) {
                EndpointMark now = Current(app); if (now == null) continue;
                string name; if (!now.Official && names != null && names.TryGetValue(app + "|" + now.Host, out name)) now.Name = name;
                EndpointMark last = log.Marks.LastOrDefault(m => m.App == app);
                if (last != null && last.Official == now.Official && last.Host == now.Host && last.Key == now.Key) {
                    if (now.Name.Length > 0 && last.Name != now.Name) { last.Name = now.Name; changed = true; }
                    continue;
                }
                DateTime since, previous; LogIndex.Parse(now.Since, out since);
                if (since > nowUtc || (last != null && LogIndex.Parse(last.Since, out previous) && since <= previous)) since = nowUtc;
                now.Since = since.ToString("o"); log.Marks.Add(now); changed = true;
            }
            if (log.Marks.Count > 500) { log.Marks.RemoveRange(0, log.Marks.Count - 500); changed = true; }
            return changed;
        }
        public static EndpointMark Resolve(EndpointLog log, string app, DateTime utc) {
            EndpointMark found = null; DateTime since;
            foreach (EndpointMark mark in log.Marks) if (mark.App == app && LogIndex.Parse(mark.Since, out since) && since <= utc) found = mark;
            return found;
        }
        public static EndpointMark Latest(EndpointLog log, string app) { return log.Marks.LastOrDefault(m => m.App == app); }
        // Optional, read-only: CC Switch provider names by host, so a relay shows as the
        // name the user gave it. Keys in that database are not read.
        public static Dictionary<string, string> CcSwitchNames() {
            var grouped = new Dictionary<string, List<string>>();
            string db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cc-switch", "cc-switch.db");
            foreach (string[] row in NativeCredentials.SqliteRows(db, "SELECT app_type, name, settings_config FROM providers ORDER BY sort_index")) {
                if (row.Length < 3) continue;
                string app = row[0] == "codex" ? CodexApp : row[0] == "claude" ? ClaudeApp : null; if (app == null) continue;
                string url = "";
                try {
                    object config = J.Parse(row[2]);
                    if (app == ClaudeApp) url = J.Str(config, "env", "ANTHROPIC_BASE_URL");
                    else { string toml = J.Str(config, "config"); url = Toml(toml, "model_providers." + ActiveCodexProvider(toml), "base_url"); }
                } catch { continue; }
                string host = Host(url); if (host.Length == 0) continue;
                List<string> list; if (!grouped.TryGetValue(app + "|" + host, out list)) grouped[app + "|" + host] = list = new List<string>();
                if (!list.Contains(row[1])) list.Add(row[1]);
            }
            return grouped.ToDictionary(g => g.Key, g => g.Value.Count <= 2 ? String.Join(" / ", g.Value) : g.Value[0] + " 等 " + g.Value.Count + " 个");
        }
        public static string Fingerprint(string key) {
            if (String.IsNullOrWhiteSpace(key)) return "";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key.Trim())), 0, 3).Replace("-", "").ToLowerInvariant();
        }
        public static string Host(string url) {
            Uri uri; if (String.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)) return "";
            return (uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port).ToLowerInvariant();
        }
        // Minimal TOML lookup: string/bare values of `key` in `[section]` ("" = top level).
        public static string Toml(string text, string section, string key) {
            string current = "", wanted = Section(section);
            foreach (string raw in (text ?? "").Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[")) { current = Section(line.Trim('[', ']', ' ', '\r')); continue; }
                if (current != wanted) continue;
                int eq = line.IndexOf('='); if (eq <= 0 || line.Substring(0, eq).Trim().Trim('"', '\'') != key) continue;
                string value = line.Substring(eq + 1).Trim();
                if (value.StartsWith("\"") || value.StartsWith("'")) { int end = value.IndexOf(value[0], 1); return end > 0 ? value.Substring(1, end - 1) : ""; }
                int hash = value.IndexOf('#'); return (hash >= 0 ? value.Substring(0, hash) : value).Trim();
            }
            return "";
        }
        private static string Section(string name) { return String.Join(".", name.Split('.').Select(p => p.Trim().Trim('"', '\''))); }
        private static string Env(string name) {
            string value = null;
            try { value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User); } catch { }
            if (String.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(name);
            return value == null ? "" : value.Trim();
        }
        private static string FirstNonEmpty(params string[] values) { return values.FirstOrDefault(v => !String.IsNullOrWhiteSpace(v)) ?? ""; }
    }

    public static class ThirdPartyReport {
        public static ThirdPartySummary Build(LogIndex codex, LogIndex claude, EndpointLog log, UsageHistory history, DateTime nowUtc, TimeZoneInfo zone) {
            var summary = new ThirdPartySummary { ClaudeNow = Endpoints.Latest(log, Endpoints.ClaudeApp), CodexNow = Endpoints.Latest(log, Endpoints.CodexApp) };
            EndpointMark firstClaude = log.Marks.FirstOrDefault(m => m.App == Endpoints.ClaudeApp);
            if (firstClaude != null) summary.ClaudeTrackedSince = firstClaude.Since;
            var map = new Dictionary<string, EndpointUsage>();
            var models = new Dictionary<string, Dictionary<string, double>>();
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date, first = today.AddDays(-29);
            foreach (var source in new[] { new { App = Endpoints.CodexApp, Index = codex }, new { App = Endpoints.ClaudeApp, Index = claude } }) {
                if (source.Index == null) continue;
                foreach (HourUsage hour in source.Index.Entries()) {
                    DateTime day = TimeZoneInfo.ConvertTimeFromUtc(hour.Hour, zone).Date;
                    if (day < first || day > today) continue;
                    EndpointMark mark = Endpoints.Resolve(log, source.App, hour.Hour);
                    EndpointMark target;
                    if (source.App == Endpoints.CodexApp) {
                        // Codex logs say exactly whether a session used a custom provider.
                        if (CodexLogs.IsOfficial(hour.Provider)) continue;
                        target = mark != null && !mark.Official ? mark : new EndpointMark { App = Endpoints.CodexApp, Key = hour.Provider, Name = "地址未记录的第三方接口" };
                    } else if (mark != null && !mark.Official) target = mark;
                    // Anthropic always returns a request-id, so records without one did not
                    // come from the official API even when settings.json looks official
                    // (e.g. Claude Desktop routed through a relay, or before tracking began).
                    else if (hour.Provider == ClaudeLogs.NoRequestId) target = new EndpointMark { App = Endpoints.ClaudeApp, Key = ClaudeLogs.NoRequestId, Name = "疑似中转站" };
                    else { if (mark == null) summary.ClaudeUnattributed += hour.Tokens; continue; }
                    EndpointUsage usage;
                    if (!map.TryGetValue(target.Id, out usage)) { usage = new EndpointUsage { App = target.App, Host = target.Host, Key = target.Key, Name = target.Name }; map[target.Id] = usage; models[target.Id] = new Dictionary<string, double>(); }
                    if (usage.Name.Length == 0 && target.Name.Length > 0) usage.Name = target.Name;
                    int slot = (day - first).Days;
                    usage.Daily[slot] += hour.Tokens; usage.Month += hour.Tokens; usage.Requests += hour.Requests; usage.TimedOutput += hour.TimedOutput; usage.TimedSeconds += hour.TimedSeconds;
                    if (day == today) usage.Today += hour.Tokens;
                    if (slot >= 23) usage.Week += hour.Tokens;
                    // Each hour carries the API-equivalent cost of its requests (list prices,
                    // long-context tiers included); tokens without a known price mark it partial.
                    usage.CostMonth += hour.Cost;
                    if (day == today) usage.CostToday += hour.Cost;
                    if (slot >= 23) usage.CostWeek += hour.Cost;
                    if (hour.Unpriced > 0) usage.CostPartial = true;
                    double tally; models[target.Id].TryGetValue(hour.Model, out tally); models[target.Id][hour.Model] = tally + hour.Tokens;
                    string last = hour.Hour.AddHours(1).ToString("o"); if (String.CompareOrdinal(last, usage.LastUsed) > 0) usage.LastUsed = last;
                }
            }
            foreach (var pair in map) PickModel(pair.Value, models[pair.Key]);
            foreach (EndpointMark now in new[] { summary.ClaudeNow, summary.CodexNow }) {
                if (now == null || now.Official) continue;
                EndpointUsage current; if (!map.TryGetValue(now.Id, out current)) { current = new EndpointUsage { App = now.App, Host = now.Host, Key = now.Key, Name = now.Name }; map[now.Id] = current; }
                current.Current = true; if (now.Name.Length > 0) current.Name = now.Name;
            }
            summary.Endpoints = map.Values.OrderByDescending(e => e.Current).ThenByDescending(e => e.Month).ToList();
            return summary;
        }
        private static void PickModel(EndpointUsage target, Dictionary<string, double> tally) {
            var top = tally.Where(p => p.Key.Length > 0).OrderByDescending(p => p.Value).FirstOrDefault();
            target.MainModel = top.Key ?? "";
        }
    }
}
