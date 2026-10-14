using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Session recovery (SessionRecovery, BackgroundCli), Antigravity plan and model names, and the
    // Devin provider.
    internal static class RecoveryTests {
        private static void Check(bool condition) { if (!condition) throw new Exception("Feature assertion failed"); }
        private static readonly DateTime Now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        private static double Unix(DateTime utc) { return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
        internal static void Run(Action<string, Action> test) {
            // ── Claude Code ──
            test("Claude token expiry is read from expiresAt (ms) with a minute of margin", () => {
                Check(SessionRecovery.ClaudeExpired(J.Parse("{\"claudeAiOauth\":{\"expiresAt\":" + (Unix(Now.AddSeconds(30)) * 1000) + "}}"), Now));
                Check(!SessionRecovery.ClaudeExpired(J.Parse("{\"claudeAiOauth\":{\"expiresAt\":" + (Unix(Now.AddHours(3)) * 1000) + "}}"), Now));
                // No expiry recorded: not treated as expired (the usage request decides).
                Check(!SessionRecovery.ClaudeExpired(J.Parse("{\"claudeAiOauth\":{\"accessToken\":\"x\"}}"), Now));
            });
            test("Renewal is not repeated within its spacing window", () => {
                // (The gate itself: a real renewal would start the user's own claude CLI.)
                SessionRecovery.ResetSpacing();
                Check(SessionRecovery.MayTry("selftest", Now) && !SessionRecovery.MayTry("selftest", Now.AddMinutes(5)) && SessionRecovery.MayTry("selftest", Now.AddMinutes(11)));
                Check(SessionRecovery.MayTry("selftest-other", Now.AddMinutes(5)));
                SessionRecovery.ResetSpacing();
            });
            test("Renewal is on by default, also for settings saved before it existed", () => {
                Check(new AppConfig().RenewSessions);
                Check(J.Serializer().Deserialize<AppConfig>("{\"RefreshMinutes\":5}").RenewSessions);
                Check(!J.Serializer().Deserialize<AppConfig>("{\"RenewSessions\":false}").RenewSessions);
            });

            // ── Grok (#17) ──
            test("Grok uses the newest sign-in entry and reads its expiry", () => {
                var c = SessionRecovery.ReadGrok(J.Parse("{\"https://accounts.x.ai/sign-in\":{\"key\":\"old\",\"expires_at\":\"2026-10-13T00:00:00Z\"},\"https://auth.x.ai::abc\":{\"key\":\"new\",\"user_id\":\"u1\",\"expires_at\":\"2026-10-14T18:00:00Z\"},\"other\":{\"key\":\"x\",\"expires_at\":\"2027-01-01T00:00:00Z\"}}"));
                Check(c.Key == "new" && c.UserId == "u1" && !SessionRecovery.GrokExpired(c, Now));
                Check(SessionRecovery.GrokExpired(new SessionRecovery.GrokCredential { Key = "k", ExpiresAt = "2026-10-14T11:59:30Z" }, Now));
                Check(!SessionRecovery.GrokExpired(new SessionRecovery.GrokCredential { Key = "k" }, Now));
                Check(SessionRecovery.ReadGrok(J.Parse("{}")).Key.Length == 0 && SessionRecovery.ReadGrok((object)null).Key.Length == 0);
            });
            test("Grok credits: explicit percentage is used, an omitted one only for a current known period", () => {
                var s = new ProviderState { Id = "grok" };
                SessionRecovery.AddGrokCredits(s, J.Parse("{\"config\":{\"creditUsagePercent\":12.5,\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":\"2026-10-10T00:00:00Z\",\"end\":\"2026-10-17T00:00:00Z\"}}}"), Now);
                Check(s.Quotas.Count == 1 && s.Quotas[0].Used == 12.5 && s.Quotas[0].ResetUtc.StartsWith("2026-10-17"));
                var zero = new ProviderState { Id = "grok" };
                SessionRecovery.AddGrokCredits(zero, J.Parse("{\"config\":{\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":{\"seconds\":\"" + Unix(Now.AddDays(-2)) + "\"},\"end\":{\"seconds\":\"" + Unix(Now.AddDays(5)) + "\"}},\"onDemandCap\":{\"val\":0}}}"), Now);
                Check(zero.Quotas.Count == 1 && zero.Quotas[0].Used == 0 && zero.Quotas[0].ResetUtc.Length > 0);
            });
            test("Grok credits: empty, unknown, future, past or out-of-range answers give no quota", () => {
                foreach (string json in new[] {
                    "{}", "{\"config\":{}}",
                    "{\"config\":{\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_UNSPECIFIED\",\"start\":\"2026-10-10T00:00:00Z\",\"end\":\"2026-10-17T00:00:00Z\"}}}",
                    "{\"config\":{\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":\"2026-10-15T00:00:00Z\",\"end\":\"2026-10-22T00:00:00Z\"}}}",
                    "{\"config\":{\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":\"2026-10-01T00:00:00Z\",\"end\":\"2026-10-08T00:00:00Z\"}}}",
                    "{\"config\":{\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":\"2026-10-10T00:00:00Z\"}}}",
                    "{\"config\":{\"creditUsagePercent\":140,\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":\"2026-10-10T00:00:00Z\",\"end\":\"2026-10-17T00:00:00Z\"}}}" }) {
                    var s = new ProviderState { Id = "grok" }; SessionRecovery.AddGrokCredits(s, J.Parse(json), Now); Check(s.Quotas.Count == 0);
                }
            });
            test("Grok billing with a period but no percentage is not read as 0 % by itself", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"currentPeriod\":{\"type\":\"USAGE_PERIOD_TYPE_WEEKLY\",\"start\":\"2026-10-01T00:00:00Z\",\"end\":\"2026-10-08T00:00:00Z\"},\"onDemandCap\":{\"val\":0},\"onDemandUsed\":{\"val\":0}}}"));
                Check(s.Quotas.Count == 0);
            });

            // ── Antigravity CLI (agy) ──
            test("agy usage report: weekly and 5-hour buckets, disabled and invalid ones skipped", () => {
                var s = SessionRecovery.ParseAgyUsage(J.Parse("{\"status\":\"SUCCESS\",\"command\":{\"name\":\"usage\",\"data\":{\"groups\":[{\"name\":\"Gemini\",\"buckets\":[{\"window\":\"weekly\",\"remaining_fraction\":0.75,\"reset_time\":\"2026-10-20T00:00:00Z\"},{\"window\":\"5h\",\"remaining_fraction\":1},{\"window\":\"5h\",\"remaining_fraction\":0.2,\"disabled\":true},{\"window\":\"weekly\",\"remaining_fraction\":1.7}]},{\"name\":\"Claude\",\"buckets\":[{\"window\":\"weekly\",\"remaining_fraction\":null}]}]}}}"));
                Check(s.Quotas.Count == 2 && s.Quotas[0].Label == "Gemini · 每周" && s.Quotas[0].Remaining == 75 && s.Quotas[0].WindowSeconds == 604800 && s.Quotas[1].Label == "Gemini · 5 小时" && s.Quotas[1].Used == 0);
            });
            test("agy: only a successful usage report counts, and only from 1.1.11 on", () => {
                Check(SessionRecovery.ParseAgyUsage(J.Parse("{\"status\":\"ERROR\",\"command\":{\"name\":\"usage\",\"data\":{\"groups\":[{\"name\":\"G\",\"buckets\":[{\"window\":\"weekly\",\"remaining_fraction\":0.5}]}]}}}")).Quotas.Count == 0);
                Check(SessionRecovery.ParseAgyUsage(J.Parse("{\"status\":\"SUCCESS\",\"command\":{\"name\":\"help\"}}")).Quotas.Count == 0);
                Check(SessionRecovery.AgySupportsUsage("agy 1.2.7") && SessionRecovery.AgySupportsUsage("1.1.11") && !SessionRecovery.AgySupportsUsage("agy version 1.1.10") && !SessionRecovery.AgySupportsUsage(""));
                Check(SessionRecovery.AgyEnvironment["AGY_CLI_DISABLE_AUTO_UPDATE"] == "true");
            });

            // ── Background CLI runs ──
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            test("A background CLI gets its own variables only, an empty folder and no input", () => {
                var r = BackgroundCli.Run("selftest-env", cmd, "/d /c \"echo [%AGY_CLI_DISABLE_AUTO_UPDATE%] & cd & set /p x=\"", 10, SessionRecovery.AgyEnvironment).Result;
                Check(!r.TimedOut && r.Output.Contains("[true]") && r.Output.Contains("codeusagemonit-cli-"));
                Check(Environment.GetEnvironmentVariable("AGY_CLI_DISABLE_AUTO_UPDATE") == null);
                Check(!Directory.GetDirectories(Path.GetTempPath(), "codeusagemonit-cli-*").Any(d => Directory.GetLastWriteTimeUtc(d) > DateTime.UtcNow.AddSeconds(-30)));
            });
            test("A background CLI that overruns is ended with everything it started", () => {
                string marker = Path.Combine(Path.GetTempPath(), "codeusagemonit-selftest-" + Guid.NewGuid().ToString("N") + ".txt");
                var clock = Stopwatch.StartNew();
                var r = BackgroundCli.Run("selftest-timeout", cmd, "/d /c \"ping -n 3 127.0.0.1 >nul & echo late> \"" + marker + "\"\"", 1, null).Result;
                Check(r.TimedOut && clock.ElapsedMilliseconds < 2500);
                System.Threading.Thread.Sleep(2600);
                bool written = File.Exists(marker); try { File.Delete(marker); } catch { }
                Check(!written);
            });
            test("Background runs for one platform never overlap", () => {
                var clock = Stopwatch.StartNew();
                Task<BackgroundCli.Result> a = BackgroundCli.Run("selftest-serial", cmd, "/d /c ping -n 2 127.0.0.1 >nul", 10, null), b = BackgroundCli.Run("selftest-serial", cmd, "/d /c ping -n 2 127.0.0.1 >nul", 10, null);
                Task.WaitAll(a, b);
                Check(a.Result.ExitCode == 0 && b.Result.ExitCode == 0 && clock.ElapsedMilliseconds >= 1800);
            });
            test("Executables are found on PATH with their extension", () => {
                string found = BackgroundCli.Find("cmd"); Check(found != null && found.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase));
                Check(BackgroundCli.Find("codeusagemonit-no-such-cli") == null);
            });

            // ── Antigravity plan and model names ──
            test("Antigravity plan is the Google AI tier, the old plan field only a fallback", () => {
                var s = new ProviderState { Id = "antigravity", Plan = "Google AI" };
                LocalAntigravity.ApplyUserStatus(s, J.Parse("{\"userStatus\":{\"userTier\":{\"id\":\"g1-ultra-tier\",\"name\":\"Google AI Ultra\"},\"planStatus\":{\"planInfo\":{\"planName\":\"Pro\"}}}}"));
                Check(s.Plan == "Google AI Ultra");
                var old = new ProviderState { Id = "antigravity", Plan = "Google AI" };
                LocalAntigravity.ApplyUserStatus(old, J.Parse("{\"userStatus\":{\"planStatus\":{\"planInfo\":{\"planName\":\"Pro\"}}}}"));
                Check(old.Plan == "Pro");
                var none = new ProviderState { Id = "antigravity", Plan = "Google AI" }; LocalAntigravity.ApplyUserStatus(none, J.Parse("{}")); Check(none.Plan == "Google AI");
            });
            test("Antigravity's local quota summary (wrapped in response) gives the group windows", () => {
                var s = LocalAntigravity.Parse(J.Parse("{\"response\":{\"groups\":[{\"displayName\":\"Gemini Models\",\"buckets\":[{\"window\":\"weekly\",\"remainingFraction\":0.33,\"resetTime\":\"2026-10-17T12:06:19Z\"},{\"window\":\"5h\",\"remainingFraction\":0.96}]},{\"displayName\":\"Claude and GPT models\",\"buckets\":[{\"window\":\"weekly\",\"remainingFraction\":0.97}]}]}}"));
                Check(s.Quotas.Count == 3 && s.Quotas[0].Label == "Gemini Models · 每周" && s.Quotas[2].Label == "Claude and GPT models · 每周");
            });
            test("Language servers are found without WMI errors (Handle selected for GetOwnerSid)", () => {
                Check(LocalAntigravity.FindServers((path, command) => true) != null);
            });
            test("Antigravity model names come from the language server's model list", () => {
                var found = AntigravityModels.Extract(J.Parse("{\"userStatus\":{\"cascadeModelConfigData\":{\"clientModelConfigs\":[{\"label\":\"Gemini 4 Argon (High)\",\"modelOrAlias\":{\"model\":\"MODEL_PLACEHOLDER_M417\"}},{\"label\":\"Claude Opus 5.5 (Medium)\",\"modelOrAlias\":{\"model\":\"MODEL_PLACEHOLDER_M401\"}},{\"label\":\"\",\"modelOrAlias\":{\"model\":\"MODEL_PLACEHOLDER_M9\"}},{\"label\":\"x\",\"modelOrAlias\":{\"alias\":\"A\"}}]}}}"));
                Check(found.Count == 2 && found["model_placeholder_m417"] == "Gemini 4 Argon (High)" && found["model_placeholder_m401"] == "Claude Opus 5.5 (Medium)");
            });
            test("Antigravity names: new models keep their effort, older ones stay as before", () => {
                try {
                    AntigravityModels.Use(new Dictionary<string, string> { { "model_placeholder_m401", "Claude Opus 5.5 (Medium)" }, { "model_placeholder_m417", "Gemini 4 Argon (High)" }, { "model_placeholder_m318", "Something Else" } });
                    Check(AgentLogs.NormalizeAg("model_placeholder_m401") == "claude-opus-5-5-medium");
                    Check(AgentLogs.NormalizeAg("model_placeholder_m417") == "gemini-4-argon-high");
                    Check(AgentLogs.NormalizeAg("MODEL_PLACEHOLDER_M318") == "gemini-3.8-flash-high");
                    Check(AgentLogs.NormalizeAg("model_placeholder_m999") == "model_placeholder_m999");
                    Check(AgentLogs.NormalizeAg("Claude Sonnet 5.5 (High)") == "claude-sonnet-5-5-high");
                    Check(AgentLogs.NormalizeAg("Gemini 3.8 Flash (Low)") == "gemini-3.8-flash-low");
                    Check(AgentLogs.NormalizeAg("Gemini 3 Pro (High)") == "gemini-3-pro" && AgentLogs.NormalizeAg("Claude 3.7 Sonnet (Thinking)") == "claude-3-7-sonnet");
                    Check(AgentLogs.NormalizeAg("GPT-OSS 120B (Medium)") == "gpt-oss-120b-medium");
                } finally { AntigravityModels.Reset(); }
            });
            test("Antigravity effort variants are priced as their model, exactly or not at all", () => {
                ModelPrice opus = Pricing.FindExact("claude-opus-5-5");
                if (opus != null) Check(AgentLogs.AntigravityPrice("claude-opus-5-5-medium", null) == opus);
                Check(AgentLogs.AntigravityPrice("gemini-3.8-flash-high", 3) == (Pricing.FindExact("gemini-3.8-flash") ?? Pricing.FindExact("gemini/gemini-3.8-flash")));
                Check(AgentLogs.AntigravityPrice("gemini-4-argon-high", 3) == null || Pricing.FindExact("gemini-4-argon") != null);
            });

            // ── Devin ──
            test("Devin language server: daily and weekly quota from planStatus", () => {
                var s = LocalDevin.ParseStatus(J.Parse("{\"userStatus\":{\"planStatus\":{\"planInfo\":{\"planName\":\"Pro\"},\"dailyQuotaRemainingPercent\":64,\"weeklyQuotaRemainingPercent\":81.5,\"dailyQuotaResetAtUnix\":\"" + Unix(Now.AddHours(6)) + "\",\"weeklyQuotaResetAtUnix\":\"" + Unix(Now.AddDays(4)) + "\"}}}"), Now);
                Check(s.Plan == "Devin Pro" && s.Quotas.Count == 2 && s.Quotas[0].Label == "每日" && s.Quotas[0].Remaining == 64 && s.Quotas[0].WindowSeconds == 86400 && s.Quotas[1].Label == "每周" && s.Quotas[1].Remaining == 81.5);
                var snake = LocalDevin.ParseStatus(J.Parse("{\"plan_status\":{\"plan_info\":{\"plan_name\":\"Devin Max\"},\"daily_quota_remaining_percent\":10}}"), Now);
                Check(snake.Plan == "Devin Max" && snake.Quotas.Count == 1 && snake.Quotas[0].Remaining == 10);
            });
            test("Devin: a missing percentage stays unknown, a passed reset drops a cached window", () => {
                Check(LocalDevin.ParseStatus(J.Parse("{\"userStatus\":{\"planStatus\":{\"planInfo\":{\"planName\":\"Pro\"},\"dailyQuotaResetAtUnix\":\"" + Unix(Now.AddHours(6)) + "\"}}}"), Now).Quotas.Count == 0);
                var cached = LocalDevin.ParseCache(J.Parse("{\"planName\":\"Pro\",\"quotaUsage\":{\"dailyRemainingPercent\":20,\"weeklyRemainingPercent\":70,\"dailyResetAtUnix\":" + Unix(Now.AddHours(-2)) + ",\"weeklyResetAtUnix\":" + Unix(Now.AddDays(2)) + "}}"), Now);
                Check(cached.Plan == "Devin Pro（本机缓存）" && cached.Quotas.Count == 1 && cached.Quotas[0].Label == "每周" && cached.Quotas[0].Remaining == 70);
            });
            test("Devin cache: credit plans fall back to message and flow-action counts in the current period", () => {
                double end = Unix(Now.AddDays(10)) * 1000;
                var s = LocalDevin.ParseCache(J.Parse("{\"planName\":\"Teams\",\"endTimestamp\":" + end + ",\"usage\":{\"messages\":500,\"usedMessages\":125,\"flowActions\":1000,\"remainingFlowActions\":900}}"), Now);
                Check(s.Quotas.Count == 2 && s.Quotas[0].Label == "消息" && s.Quotas[0].Remaining == 75 && s.Quotas[1].Label == "Flow 操作" && s.Quotas[1].Remaining == 90 && s.Quotas[0].ResetUtc.Length > 0);
                var ended = LocalDevin.ParseCache(J.Parse("{\"endTimestamp\":" + (Unix(Now.AddDays(-1)) * 1000) + ",\"usage\":{\"messages\":500,\"usedMessages\":125}}"), Now);
                Check(ended.Quotas.Count == 0);
            });
            test("Devin cache values decode from UTF-8 or UTF-16 bytes, anything else is ignored", () => {
                string json = "{\"planName\":\"Pro\"}";
                Check(J.Str(LocalDevin.DecodeJson(Encoding.UTF8.GetBytes(json)), "planName") == "Pro");
                Check(J.Str(LocalDevin.DecodeJson(Encoding.Unicode.GetBytes(json)), "planName") == "Pro");
                Check(LocalDevin.DecodeJson(Encoding.UTF8.GetBytes("not json")) == null && LocalDevin.DecodeJson(null) == null);
            });
            test("Devin is listed and opt-in like the other added platforms", () => {
                Check(ProviderCatalog.Ids.Contains("devin") && !ProviderCatalog.DefaultEnabled.Contains("devin") && ProviderCatalog.Name("devin") == "Devin" && ProviderCatalog.Help("devin").Length > 0);
            });
        }
    }
}
