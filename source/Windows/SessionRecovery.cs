using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Keeping a sign-in usable without taking it over: when an access token has expired, the
    // platform's own CLI is run in the background so that it refreshes and saves its tokens
    // itself; this program then reads them again. Refresh tokens are never used or written here.
    // (Grok / Antigravity recovery follows the analysis in GitHub issue #17 by @xing-skyline.)
    public static class SessionRecovery {
        private static readonly Dictionary<string, DateTime> lastTry = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        // At most one recovery per platform in this window, so a real sign-out does not start a
        // CLI on every refresh.
        private static readonly TimeSpan Spacing = TimeSpan.FromMinutes(10);
        internal static bool MayTry(string platform, DateTime nowUtc) {
            lock (lastTry) {
                DateTime last; if (lastTry.TryGetValue(platform, out last) && nowUtc - last < Spacing) return false;
                lastTry[platform] = nowUtc; return true;
            }
        }
        internal static void ResetSpacing() { lock (lastTry) { lastTry.Clear(); agyFailed = DateTime.MinValue; } }

        // ── Claude Code ───────────────────────────────────────────────────
        // Its access token lasts about 8 hours and is renewed only when the claude CLI runs; the
        // desktop app keeps a sign-in of its own. Starting `claude` interactively in a hidden
        // console renews it within seconds; nothing is typed and no request is sent to a model.
        public static bool ClaudeExpired(object credentials, DateTime nowUtc) {
            double? expires = J.Num(credentials, "claudeAiOauth", "expiresAt");
            return expires.HasValue && LogReader.FromMillis(expires.Value) <= nowUtc.AddMinutes(1);
        }
        public static async Task<bool> RenewClaude(string credentialsPath, DateTime nowUtc) {
            if (!MayTry("claude", nowUtc)) return false;
            string claude = BackgroundCli.Find("claude"); if (claude == null) return false;
            DateTime before = File.Exists(credentialsPath) ? File.GetLastWriteTimeUtc(credentialsPath) : DateTime.MinValue;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return await BackgroundCli.RunUntil("claude", claude, "", home, () => {
                try { return File.Exists(credentialsPath) && File.GetLastWriteTimeUtc(credentialsPath) > before && !ClaudeExpired(J.File(credentialsPath), DateTime.UtcNow); } catch { return false; }
            }, 25).ConfigureAwait(false);
        }

        // ── Grok ──────────────────────────────────────────────────────────
        public sealed class GrokCredential { public string Key = "", UserId = "", ExpiresAt = ""; }
        // The newest sign-in entry of the Grok CLI's auth.json.
        public static GrokCredential ReadGrok(object auth) {
            object entry = J.Dict(auth).Where(p => p.Key == "https://accounts.x.ai/sign-in" || p.Key.StartsWith("https://auth.x.ai::", StringComparison.Ordinal)).Where(p => J.Str(p.Value, "key").Length > 0).OrderByDescending(p => J.Str(p.Value, "expires_at"), StringComparer.Ordinal).Select(p => p.Value).FirstOrDefault();
            return new GrokCredential { Key = J.Str(entry, "key"), UserId = J.Str(entry, "user_id"), ExpiresAt = J.Str(entry, "expires_at") };
        }
        public static GrokCredential ReadGrok(string path) { try { return ReadGrok(J.File(path)); } catch { return new GrokCredential(); } }
        public static bool GrokExpired(GrokCredential c, DateTime nowUtc) {
            DateTime at; return c.ExpiresAt.Length > 0 && DateTime.TryParse(c.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at) && at <= nowUtc.AddMinutes(1);
        }
        // `grok models` makes the CLI refresh its session; it lists models and sends no task.
        public static async Task<bool> RenewGrok(DateTime nowUtc) {
            if (!MayTry("grok", nowUtc)) return false;
            string grok = BackgroundCli.Find("grok"); if (grok == null) return false;
            BackgroundCli.Result r = await BackgroundCli.Run("grok", grok, "models", 30, null).ConfigureAwait(false);
            return !r.TimedOut && r.ExitCode == 0;
        }
        // GetGrokCreditsConfig, asked when the billing answer has no percentage (billing itself
        // can list a new period without one, which is not evidence of 0 %). An explicit
        // percentage is taken as is. Its protobuf JSON leaves zero values out, so a missing
        // percentage means 0 % only for a complete answer whose recognised period covers now;
        // anything else (empty, error, future or past period) yields no quota at all.
        public static void AddGrokCredits(ProviderState s, object root, DateTime nowUtc) {
            object config = J.Get(root, "config") ?? root;
            object period = J.Get(config, "currentPeriod");
            DateTime start, end;
            bool known = new[] { "USAGE_PERIOD_TYPE_WEEKLY", "USAGE_PERIOD_TYPE_MONTHLY", "USAGE_PERIOD_TYPE_DAILY" }.Contains(J.Str(period, "type"));
            bool covers = Moment(J.Get(period, "start"), out start) && Moment(J.Get(period, "end"), out end) && start <= nowUtc && nowUtc < end && end - start <= TimeSpan.FromDays(32);
            double? used = J.Num(config, "creditUsagePercent");
            if (used.HasValue && (used < 0 || used > 100)) return;
            if (!used.HasValue && !(known && covers)) return;
            Parsers.Add(s, "当前账期", used ?? 0, J.Iso(J.Get(period, "end") is IDictionary<string, object> ? J.Get(period, "end", "seconds") : J.Get(period, "end")));
        }
        private static bool Moment(object value, out DateTime utc) {
            utc = DateTime.MinValue; if (value == null) return false;
            object seconds = J.Get(value, "seconds"); if (seconds != null) { double? n = J.Num(seconds); if (!n.HasValue) return false; utc = LogReader.FromMillis(n.Value * 1000); return true; }
            return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        }

        // ── Antigravity CLI (agy) ─────────────────────────────────────────
        // When the desktop app is closed: `agy -p /usage --output-format json` (agy ≥ 1.1.11). Only
        // this child gets AGY_CLI_DISABLE_AUTO_UPDATE=true (the literal "true"): otherwise agy may
        // start its updater, which can flash a console window.
        public static readonly Dictionary<string, string> AgyEnvironment = new Dictionary<string, string> { { "AGY_CLI_DISABLE_AUTO_UPDATE", "true" } };
        public static bool AgySupportsUsage(string versionOutput) {
            var m = System.Text.RegularExpressions.Regex.Match(versionOutput ?? "", @"(\d+)\.(\d+)\.(\d+)");
            if (!m.Success) return false;
            return Updates.Compare(m.Groups[1].Value + "." + m.Groups[2].Value + "." + m.Groups[3].Value, "1.1.11") >= 0;
        }
        // A failed read (not signed in: agy waits at "Select login method" until the timeout; too
        // old; no report) is not repeated for the spacing window.
        private static DateTime agyFailed = DateTime.MinValue;
        public static async Task<ProviderState> ReadAgy(DateTime nowUtc) {
            string agy = BackgroundCli.Find("agy"); if (agy == null) return null;
            lock (lastTry) if (nowUtc - agyFailed < Spacing) return null;
            ProviderState s = await ReadAgy(agy).ConfigureAwait(false);
            if (s == null) lock (lastTry) agyFailed = nowUtc;
            return s;
        }
        private static async Task<ProviderState> ReadAgy(string agy) {
            BackgroundCli.Result version = await BackgroundCli.Run("antigravity", agy, "--version", 8, AgyEnvironment).ConfigureAwait(false);
            if (version.ExitCode != 0 || !AgySupportsUsage(version.Output)) return null;
            BackgroundCli.Result report = await BackgroundCli.Run("antigravity", agy, "-p /usage --output-format json", 45, AgyEnvironment).ConfigureAwait(false);
            if (report.TimedOut || report.ExitCode != 0) return null;
            int at = report.Output.IndexOf('{'); if (at < 0) return null;
            object root; try { root = J.Parse(report.Output.Substring(at)); } catch { return null; }
            ProviderState s = ParseAgyUsage(root);
            return s.Quotas.Count > 0 ? s : null;
        }
        // The structured usage report: status SUCCESS, command "usage", groups of buckets with a
        // remaining fraction; the text response is never parsed.
        public static ProviderState ParseAgyUsage(object root) {
            var s = new ProviderState { Id = "antigravity", Plan = "Google AI" };
            if (J.Str(root, "status") != "SUCCESS" || J.Str(root, "command", "name") != "usage") return s;
            foreach (object group in J.Arr(J.Get(root, "command", "data", "groups"))) {
                string name = J.Str(group, "name");
                foreach (object b in J.Arr(J.Get(group, "buckets"))) {
                    if (J.Get(b, "disabled") is bool && (bool)J.Get(b, "disabled")) continue;
                    double? fraction = J.Num(b, "remaining_fraction"); if (!fraction.HasValue || fraction < 0 || fraction > 1) continue;
                    string window = J.Str(b, "window");
                    string label = (name + " · " + (window == "weekly" ? "每周" : window == "5h" ? "5 小时" : J.Str(b, "name"))).Trim(' ', '·');
                    Parsers.Add(s, label, (1 - fraction.Value) * 100, J.Get(b, "reset_time"), window == "weekly" ? 604800 : window == "5h" ? 18000 : 0);
                }
            }
            return s;
        }
    }
}
