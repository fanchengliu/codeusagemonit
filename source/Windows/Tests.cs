using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeUsageMonit {
    public static class SelfTests {
        public static int Run() {
            var passed = new List<string>(); var failed = new List<string>();
            Action<string, Action> test = (name, check) => { try { check(); passed.Add(name); } catch (Exception e) { failed.Add(name + ": " + e.Message); } };
            test("Codex weekly-only response does not invent a 5h window", () => {
                var s = Parsers.Codex(J.Parse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":38,\"limit_window_seconds\":604800,\"reset_at\":1900000000},\"secondary_window\":null}}"));
                Require(s.Quotas.Count == 1 && s.Quotas[0].Label == "每周" && s.Quotas[0].Remaining == 62);
            });
            test("Window classification uses duration, not primary/secondary position", () => {
                var s = Parsers.Codex(J.Parse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":10,\"limit_window_seconds\":604800},\"secondary_window\":{\"used_percent\":70,\"limit_window_seconds\":18000}}}"));
                Require(s.Quotas[0].Label == "每周" && s.Quotas[1].Label == "5 小时" && s.Quotas[1].Remaining == 30);
            });
            test("Absent utilization stays unknown instead of becoming zero", () => {
                Require(Parsers.Claude(J.Parse("{\"five_hour\":{\"resets_at\":null},\"seven_day\":null}")).Quotas.Count == 0);
                Require(Parsers.Codex(J.Parse("{\"rate_limit\":{\"primary_window\":{\"limit_window_seconds\":18000}}}")).Quotas.Count == 0);
            });
            test("Explicit zero usage is valid data", () => {
                var s = Parsers.Claude(J.Parse("{\"five_hour\":{\"utilization\":0},\"seven_day\":{\"utilization\":3}}"));
                Require(s.Quotas.Count == 2 && s.Quotas[0].Remaining == 100 && s.Quotas[1].Remaining == 97);
            });
            test("Malformed optional Codex limits do not discard primary usage", () => {
                var s = Parsers.Codex(J.Parse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":25,\"limit_window_seconds\":18000}},\"additional_rate_limits\":[null,{}, {\"limit_name\":\"test-model\",\"rate_limit\":{\"primary_window\":{\"used_percent\":50,\"limit_window_seconds\":604800}}}]}"));
                Require(s.Quotas.Count == 2 && s.Quotas[0].Remaining == 75);
            });
            test("Cursor total, Auto and API retain separate metrics", () => {
                var s = Parsers.Cursor(J.Parse("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20,\"autoPercentUsed\":5,\"apiPercentUsed\":99}}}"));
                Require(s.Quotas.Count == 3 && s.Quotas[0].Remaining == 80 && s.Quotas[1].Remaining == 95 && s.Quotas[2].Remaining == 1);
            });
            test("Cursor cycle duration is used only when both API boundaries exist", () => {
                var state = Parsers.Cursor(J.Parse("{\"membershipType\":\"pro\",\"billingCycleStart\":\"2026-01-01T00:00:00Z\",\"billingCycleEnd\":\"2026-01-31T00:00:00Z\",\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20}}}"));
                Require(state.Plan == "Cursor Pro" && state.Quotas[0].WindowSeconds == 2592000);
                var missing = Parsers.Cursor(J.Parse("{\"billingCycleEnd\":\"2026-01-31T00:00:00Z\",\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20}}}")); Require(missing.Quotas[0].WindowSeconds == 0);
            });
            test("DeepSeek currencies are never summed or treated as percentages", () => {
                var s = Parsers.DeepSeek(J.Parse("{\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"12.34\"},{\"currency\":\"USD\",\"total_balance\":\"2.50\"}]}"));
                Require(s.Quotas.Count == 0 && s.Balances.Count == 2 && s.Balances[0].Amount == 12.34);
            });
            test("Antigravity fractions map correctly and null buckets stay absent", () => {
                var s = Parsers.Antigravity(J.Parse("{\"groups\":[{\"displayName\":\"Gemini\",\"buckets\":[{\"window\":\"5h\",\"remainingFraction\":0.7},{\"window\":\"weekly\",\"remainingFraction\":null}]}]}"));
                Require(s.Quotas.Count == 1 && Math.Abs(s.Quotas[0].Remaining - 70) < 0.0001);
            });
            test("Antigravity model fallback preserves each model's quota", () => {
                var s = Parsers.Antigravity(J.Parse("{\"models\":{\"model-a\":{\"quotaInfo\":{\"remainingFraction\":0}},\"model-b\":{\"quotaInfo\":{\"remainingFraction\":1}}}}"));
                Require(s.Quotas.Count == 2 && s.Quotas.Sum(q => q.Remaining) == 100);
            });
            test("Grok zero spend is a full allowance, not missing data", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":0}}")); Require(s.Quotas.Count == 1 && s.Quotas[0].Remaining == 100);
            });
            test("Percentages are bounded and nonnumeric values ignored", () => {
                var s = new ProviderState(); Parsers.Add(s, "one", -10, null); Parsers.Add(s, "two", 150, null); Parsers.Add(s, "bad", "NaN", null); Require(s.Quotas.Count == 2 && s.Quotas[0].Remaining == 100 && s.Quotas[1].Remaining == 0);
            });
            test("History uses by-agent rows exactly once and includes cache tokens", () => {
                var h = HistoryService.Parse(J.Parse("{\"daily\":[{\"period\":\"2026-01-01\",\"totalCost\":9,\"agents\":[{\"agent\":\"codex\",\"totalCost\":2,\"totalTokens\":1000,\"cacheReadTokens\":900},{\"agent\":\"claude\",\"totalCost\":7,\"totalTokens\":500}]}]}")); Require(h.Days.Count == 2 && h.Days.Sum(d => d.Cost) == 9 && h.Days[0].CachedTokens == 900);
            });
            test("JWT display parsing fails closed on malformed tokens", () => { Require(J.Jwt("bad") == null && J.Jwt("a.!.c") == null); });
            test("Direct proxy selection does not inherit ambient SOCKS settings", () => { Require(ProviderService.ResolveProxy("direct") == ""); });
            test("Explicit HTTP proxy is preserved and unsupported schemes rejected", () => {
                Require(ProviderService.ResolveProxy("http://127.0.0.1:7890") == "http://127.0.0.1:7890"); bool rejected = false; try { ProviderService.ResolveProxy("socks5://127.0.0.1:7890"); } catch (ArgumentException) { rejected = true; } Require(rejected);
            });
            test("Plan formatting matches the upstream Pro tiers", () => {
                Require(UsageDetails.PlanName("pro") == "Pro 20x" && UsageDetails.PlanName("pro_lite") == "Pro 5x" && UsageDetails.PlanName("custom-plan") == "custom-plan");
            });
            test("Pace reserve and headroom are derived from elapsed time", () => {
                DateTime now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
                var q = new Quota { Used = 66, WindowSeconds = 604800, ResetUtc = now.AddHours(12).ToString("o") };
                var p = UsageDetails.Pace(q, now);
                Require(p != null && Math.Abs(p.Reserve - 26.857142857) < .0001 && p.Lasts && p.SpeedMultiplier > 1.5);
            });
            test("Pace predicts depletion for a deficit and rejects invalid windows", () => {
                DateTime now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
                var q = new Quota { Used = 70, WindowSeconds = 604800, ResetUtc = now.AddDays(5).ToString("o") };
                var p = UsageDetails.Pace(q, now); Require(p != null && p.Reserve < 0 && !p.Lasts && p.SecondsUntilEmpty.HasValue);
                q.WindowSeconds = 0; Require(UsageDetails.Pace(q, now) == null);
                q.WindowSeconds = 604800; q.ResetUtc = now.AddDays(8).ToString("o"); Require(UsageDetails.Pace(q, now) == null);
            });
            test("Reset credit inventory excludes expired and redeemed expiry entries", () => {
                var state = new ProviderState(); var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
                UsageDetails.ParseResetCredits(state, J.Parse("{\"available_count\":2,\"credits\":[{\"status\":\"available\",\"expires_at\":\"2026-01-20T00:00:00Z\"},{\"status\":\"redeemed\",\"expires_at\":\"2026-01-25T00:00:00Z\"},{\"status\":\"available\",\"expires_at\":\"2026-01-01T00:00:00Z\"}]}"), now);
                Require(state.ResetCreditsAvailable == 2 && state.ResetCreditExpiries.Count == 1);
            });
            test("No reset credits is different from an unavailable reset endpoint", () => {
                var state = new ProviderState(); UsageDetails.ParseResetCredits(state, J.Parse("{}"), DateTime.UtcNow); Require(!state.ResetCreditsAvailable.HasValue);
                UsageDetails.ParseResetCredits(state, J.Parse("{\"available_count\":0,\"credits\":[]}"), DateTime.UtcNow); Require(state.ResetCreditsAvailable == 0);
            });
            test("Current-window estimates exclude the partial opening day and future rows", () => {
                DateTime now = new DateTime(2026, 1, 17, 4, 0, 0, DateTimeKind.Utc);
                var q = new Quota { WindowSeconds = 604800, ResetUtc = new DateTime(2026, 1, 20, 4, 0, 0, DateTimeKind.Utc).ToString("o") };
                var days = new[] { new DayUsage { Day = "2026-01-13", Tokens = 100 }, new DayUsage { Day = "2026-01-14", Tokens = 200 }, new DayUsage { Day = "2026-01-17", Tokens = 300 }, new DayUsage { Day = "2026-01-19", Tokens = 500 } };
                var usage = UsageDetails.Window(days, q, now, 0, null, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")); Require(!usage.Exact && usage.Days.Count == 2 && usage.Tokens == 500);
            });
            test("History keeps token composition fields from ccusage", () => {
                var h = HistoryService.Parse(J.Parse("{\"daily\":[{\"period\":\"2026-09-29\",\"agents\":[{\"agent\":\"codex\",\"inputTokens\":1795411,\"outputTokens\":556773,\"cacheReadTokens\":143120640,\"cacheCreationTokens\":0,\"totalTokens\":145472824,\"totalCost\":294.522551}]}]}"));
                var d = h.Days[0]; Require(d.InputTokens == 1795411 && d.OutputTokens == 556773 && d.CachedTokens == 143120640 && d.InputTokens + d.OutputTokens + d.CachedTokens == d.Tokens);
            });
            test("History merge never shrinks a past day and always replaces today", () => {
                var old = new UsageHistory { Zone = "Z", Days = { new DayUsage { Day = "2026-09-27", Agent = "codex", Tokens = 12000 }, new DayUsage { Day = "2026-09-28", Agent = "codex", Tokens = 10 }, new DayUsage { Day = "2026-09-29", Agent = "codex", Tokens = 999 }, new DayUsage { Day = "2026-08-01", Agent = "codex", Tokens = 5 } } };
                var next = new UsageHistory { Zone = "Z", Days = { new DayUsage { Day = "2026-09-27", Agent = "codex", Tokens = 11000 }, new DayUsage { Day = "2026-09-28", Agent = "codex", Tokens = 20 }, new DayUsage { Day = "2026-09-29", Agent = "codex", Tokens = 50 } } };
                var merged = HistoryService.Merge(old, next, "2026-08-31", "2026-09-29");
                Require(merged.Days.Count == 3 && merged.Days.First(d => d.Day == "2026-09-27").Tokens == 12000 && merged.Days.First(d => d.Day == "2026-09-28").Tokens == 20 && merged.Days.First(d => d.Day == "2026-09-29").Tokens == 50);
                var otherZone = new UsageHistory { Zone = "Y", Days = { new DayUsage { Day = "2026-09-27", Agent = "codex", Tokens = 1 } } };
                Require(HistoryService.Merge(old, otherZone, "2026-08-31", "2026-09-29").Days.Count == 1);
            });
            test("Codex log reader counts token_count once, skips repeats, keeps an unfinished tail", () => {
                string a = "{\"timestamp\":\"2026-09-27T02:10:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":1000,\"cached_input_tokens\":900,\"output_tokens\":50,\"reasoning_output_tokens\":10,\"total_tokens\":1050},\"last_token_usage\":{\"input_tokens\":1000,\"cached_input_tokens\":900,\"output_tokens\":50,\"total_tokens\":1050}}}}";
                string repeat = a.Replace("02:10:00", "02:11:00");
                string noise = "{\"timestamp\":\"2026-09-27T02:12:00.000Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"text\":\"" + new string('x', 300000) + "\"}}";
                string nullInfo = "{\"timestamp\":\"2026-09-27T02:13:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}";
                string b = "{\"timestamp\":\"2026-09-27T03:05:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":3000,\"cached_input_tokens\":2700,\"output_tokens\":80,\"reasoning_output_tokens\":10,\"total_tokens\":3080},\"last_token_usage\":{\"input_tokens\":2000,\"cached_input_tokens\":1800,\"output_tokens\":30,\"total_tokens\":2030}}}}";
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(a + "\n" + repeat + "\n" + noise + "\n" + nullInfo + "\n" + b.Substring(0, 40));
                var state = new LogFile();
                long offset; using (var stream = new MemoryStream(bytes)) offset = CodexLogs.Read(state, stream, 0);
                Require(state.Hours.Count == 1 && state.Hours["2026092702|openai|"].T == 1050 && state.Hours["2026092702|openai|"].R == 1 && offset == bytes.Length - 40);
                byte[] rest = System.Text.Encoding.UTF8.GetBytes(b + "\n");
                using (var stream = new MemoryStream(rest)) CodexLogs.Read(state, stream, offset);
                Require(state.Hours["2026092703|openai|"].T == 2030);
            });
            test("Codex buckets carry the session's provider and model; subscription windows skip relays", () => {
                string meta = "{\"timestamp\":\"2026-09-27T02:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"model_provider\":\"custom\"}}";
                string turn = "{\"timestamp\":\"2026-09-27T02:00:01.000Z\",\"type\":\"turn_context\",\"payload\":{\"model\":\"demo-model\"}}";
                string count = "{\"timestamp\":\"2026-09-27T02:05:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":500,\"output_tokens\":20},\"last_token_usage\":{\"input_tokens\":500,\"output_tokens\":20}}}}";
                var state = new LogFile();
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(meta + "\n" + turn + "\n" + count + "\n"))) CodexLogs.Read(state, stream, 0);
                Require(state.Hours.ContainsKey("2026092702|custom|demo-model") && state.Hours["2026092702|custom|demo-model"].T == 520);
                var index = new LogIndex { CoveredFrom = "2026-09-01T00:00:00Z" }; index.Files.Add(state);
                index.Files.Add(new LogFile { Hours = { { "2026092703|openai|demo-model", new Bucket { T = 100, R = 1 } } } });
                var q = new Quota { WindowSeconds = 604800, ResetUtc = "2026-10-01T00:00:00Z" };
                var usage = UsageDetails.Window(new DayUsage[0], q, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), 0, index, TimeZoneInfo.Utc, CodexLogs.IsOfficial);
                Require(usage.Exact && Math.Abs(usage.Tokens - 100) < 1e-9);
            });
            test("Claude log entries are de-duplicated by message id + request id", () => {
                Func<string, string, string, string> line = (id, model, stamp) => "{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"requestId\":\"req_1\",\"message\":{\"id\":\"" + id + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":1000}}}";
                string text = line("msg_a", "demo-claude", "2026-09-27T02:00:00Z") + "\n" + line("msg_a", "demo-claude", "2026-09-27T02:00:01Z") + "\n" + line("msg_b", "<synthetic>", "2026-09-27T02:01:00Z") + "\n" + line("msg_c", "demo-claude", "2026-09-27T03:00:00Z") + "\n";
                var index = new LogIndex(); var state = new LogFile();
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))) ClaudeLogs.Read(index, state, stream, 0);
                Require(state.Hours["2026092702||demo-claude"].T == 1115 && state.Hours["2026092702||demo-claude"].R == 1 && state.Hours["2026092703||demo-claude"].T == 1115 && index.Seen.Count == 2);
                // Without requestId: same id + same timestamp is a copy, a new timestamp is not.
                string bare = "{\"type\":\"assistant\",\"timestamp\":\"2026-09-27T04:00:00Z\",\"message\":{\"id\":\"msg_d\",\"model\":\"demo-claude\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}";
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(bare + "\n" + bare + "\n" + bare.Replace("04:00:00", "04:00:09") + "\n"))) ClaudeLogs.Read(index, state, stream, 0);
                Require(state.Hours["2026092704|" + ClaudeLogs.NoRequestId + "|demo-claude"].T == 4 && state.Hours["2026092704|" + ClaudeLogs.NoRequestId + "|demo-claude"].R == 2);
            });
            test("Codex endpoint is read from model_provider, profiles and provider tables", () => {
                string toml = "model = \"m\"\nmodel_provider = \"custom\"\n[projects.'D:\\x.y']\ntrust_level = \"trusted\"\n[model_providers.\"custom\"]\nname = \"custom\"\nbase_url = \"https://Relay.Example.com/v1\" # comment\n";
                Require(Endpoints.ActiveCodexProvider(toml) == "custom" && Endpoints.Host(Endpoints.Toml(toml, "model_providers.custom", "base_url")) == "relay.example.com");
                Require(Endpoints.ActiveCodexProvider("profile = \"p\"\n[profiles.p]\nmodel_provider = \"alt\"\n") == "alt" && Endpoints.ActiveCodexProvider("model = \"m\"\n") == "openai");
                Require(Endpoints.Host("http://127.0.0.1:15721") == "127.0.0.1:15721" && Endpoints.Host("not a url") == "");
                string fingerprint = Endpoints.Fingerprint("sk-test-123456");
                Require(fingerprint.Length == 6 && fingerprint == Endpoints.Fingerprint(" sk-test-123456 ") && Endpoints.Fingerprint("") == "");
            });
            test("Third-party usage follows the switch timeline; history before tracking stays unattributed", () => {
                var log = new EndpointLog();
                log.Marks.Add(new EndpointMark { App = "claude", Official = true, Since = "2026-09-20T00:00:00Z" });
                log.Marks.Add(new EndpointMark { App = "claude", Host = "relay.example.com", Key = "abc123", Name = "Relay", Since = "2026-09-25T00:00:00Z" });
                var claude = new LogIndex(); claude.Files.Add(new LogFile { Hours = { { "2026091810||m", new Bucket { T = 7, R = 1 } }, { "2026092210||m", new Bucket { T = 50, R = 1 } }, { "2026092610||m", new Bucket { T = 300, R = 2 } }, { "2026092310|" + ClaudeLogs.NoRequestId + "|m", new Bucket { T = 20, R = 1 } }, { "2026091010|" + ClaudeLogs.NoRequestId + "|m", new Bucket { T = 5, R = 1 } } } });
                var codex = new LogIndex(); codex.Files.Add(new LogFile { Hours = { { "2026092610|custom|g", new Bucket { T = 40, R = 1 } }, { "2026092610|openai|g", new Bucket { T = 900, R = 3 } } } });
                var history = new UsageHistory { Days = { new DayUsage { Day = "2026-09-26", Agent = "claude", Tokens = 350, Cost = 3.5, Models = { new ModelUsage { Model = "m", Tokens = 350, Cost = 3.5 } } } } };
                var report = ThirdPartyReport.Build(codex, claude, log, history, new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
                var relay = report.Endpoints.First(e => e.App == "claude" && e.Host.Length > 0); var unknown = report.Endpoints.First(e => e.App == "codex");
                var bare = report.Endpoints.First(e => e.Key == ClaudeLogs.NoRequestId);
                Require(report.Endpoints.Count == 3 && relay.Current && relay.Month == 300 && relay.Requests == 2 && Math.Abs(relay.CostMonth - 3) < 1e-9 && relay.Week == 300 && relay.Today == 0);
                // No request-id: a relay even while settings.json looked official, and before tracking.
                Require(bare.Month == 25 && !bare.Current && unknown.Host == "" && unknown.Month == 40 && unknown.CostPartial && report.ClaudeUnattributed == 7 && report.AppMonth("claude") == 325);
            });
            test("Exact window clips hour buckets and prices each day by its own rate", () => {
                var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                var index = new LogIndex { CoveredFrom = "2026-09-01T00:00:00Z", Updated = "2026-09-29T10:00:00Z" };
                // 2026-09-27 02:00Z is 10:00 local; the window opens at 02:30Z (10:30 local).
                index.Files.Add(new LogFile { Name = "a.jsonl", Hours = { { "2026092702|openai|m", new Bucket { T = 1000 } }, { "2026092703|openai|m", new Bucket { T = 2000 } }, { "2026092802|openai|m", new Bucket { T = 4000 } } } });
                var days = new[] { new DayUsage { Day = "2026-09-27", Tokens = 10000, Cost = 10 }, new DayUsage { Day = "2026-09-28", Tokens = 4000, Cost = 8 } };
                var q = new Quota { WindowSeconds = 604800, ResetUtc = "2026-10-04T02:30:00Z" };
                var usage = UsageDetails.Window(days, q, new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), 0, index, zone);
                Require(usage.Exact && Math.Abs(usage.Tokens - 6500) < .001 && Math.Abs(usage.Cost - (2500 * .001 + 4000 * .002)) < 1e-9);
                Require(!UsageDetails.Window(days, q, new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), 4, index, zone).Exact);
            });
            test("Copilot maps percent_remaining, skips placeholders and unlimited pools", () => {
                var s = Parsers.Copilot(J.Parse("{\"copilot_plan\":\"individual_pro\",\"quota_reset_date\":\"2026-10-01\",\"quota_snapshots\":{\"premium_interactions\":{\"entitlement\":300,\"remaining\":120,\"percent_remaining\":40,\"unlimited\":false},\"chat\":{\"unlimited\":true},\"completions\":{\"entitlement\":0,\"remaining\":0,\"percent_remaining\":0}}}"));
                Require(s.Quotas.Count == 1 && s.Quotas[0].Label == "高级请求" && s.Quotas[0].Remaining == 40 && s.Quotas[0].ResetUtc.StartsWith("2026-10-01") && s.Quotas[0].WindowSeconds > 2000000 && s.Plan == "Copilot Individual Pro");
                var derived = Parsers.Copilot(J.Parse("{\"quota_snapshots\":{\"premium_interactions\":{\"entitlement\":50,\"remaining\":10}}}"));
                Require(derived.Quotas.Count == 1 && Math.Abs(derived.Quotas[0].Remaining - 20) < 1e-9);
            });
            test("Kimi prefers ratio pools and falls back to counts and rate-limit windows", () => {
                var s = Parsers.Kimi(J.Parse("{\"user\":{\"membership\":{\"level\":\"LEVEL_BASIC\"}},\"usages\":{\"limit_7d\":{\"used_ratio\":0.25,\"reset_time\":\"2026-10-03T00:00:00Z\"}},\"usage\":{\"limit\":\"2048\",\"used\":\"214\"},\"limits\":[{\"window\":{\"duration\":300,\"timeUnit\":\"TIME_UNIT_MINUTE\"},\"detail\":{\"limit\":\"200\",\"used\":\"139\",\"resetTime\":\"2026-10-01T05:00:00Z\"}}]}"));
                Require(s.Plan == "Kimi Moderato" && s.Quotas.Count == 2 && s.Quotas.First(q => q.Label == "每周").Used == 25 && Math.Abs(s.Quotas.First(q => q.Label == "5 小时").Used - 69.5) < 1e-9);
                var legacy = Parsers.Kimi(J.Parse("{\"usage\":{\"limit\":\"2048\",\"remaining\":\"1834\",\"resetTime\":\"2026-10-03T00:00:00Z\"}}"));
                Require(legacy.Quotas.Count == 1 && Math.Abs(legacy.Quotas[0].Used - 214.0 / 2048 * 100) < 1e-9 && legacy.Quotas[0].WindowSeconds == 604800);
            });
            test("OpenCode Go reads percent aliases and relative resets", () => {
                var now = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
                var s = Parsers.OpenCode(J.Parse("{\"usage\":{\"rolling\":{\"percent\":12.5,\"resetInSec\":3600},\"weekly\":{\"usagePercent\":40,\"resetAt\":\"2026-10-03T00:00:00Z\"},\"monthly\":null}}"), now);
                Require(s.Quotas.Count == 2 && s.Quotas[0].Used == 12.5 && s.Quotas[0].ResetUtc.StartsWith("2026-09-29T01:00") && s.Quotas[1].Used == 40);
                bool threw = false; try { Parsers.OpenCode(J.Parse("{\"usage\":{}}"), now); } catch (ProviderException) { threw = true; } Require(threw);
            });
            test("GLM Coding Plan (ZCode) maps units, counts and the MCP allowance", () => {
                var s = Parsers.Zai(J.Parse("{\"success\":true,\"code\":200,\"data\":{\"planName\":\"Pro\",\"limits\":[{\"type\":\"TOKENS_LIMIT\",\"unit\":3,\"number\":5,\"percentage\":10,\"usage\":1000,\"currentValue\":300,\"remaining\":700,\"nextResetTime\":1790675446000},{\"type\":\"TOKENS_LIMIT\",\"unit\":6,\"number\":1,\"percentage\":7},{\"type\":\"TIME_LIMIT\",\"unit\":5,\"number\":1,\"percentage\":2},{\"type\":\"OTHER\",\"unit\":1,\"number\":1,\"percentage\":50}]}}"));
                Require(s.Plan == "GLM Pro" && s.Quotas.Count == 3 && s.Quotas[0].Label == "5 小时" && s.Quotas[0].Used == 30 && s.Quotas[0].WindowSeconds == 18000 && s.Quotas[0].ResetUtc.StartsWith("2026-09-29"));
                Require(s.Quotas[1].Label == "每周" && s.Quotas[1].Used == 7 && s.Quotas[2].Label.StartsWith("MCP") && s.Quotas[2].WindowSeconds == 0);
                bool threw = false; try { Parsers.Zai(J.Parse("{\"success\":false,\"msg\":\"token invalid\"}")); } catch (ProviderException) { threw = true; } Require(threw);
            });
            test("Custom provider definitions are validated before any request", () => {
                var p = CustomProviders.Parse("{\"name\":\"My Gateway\",\"url\":\"https://api.example.com/usage\",\"auth\":\"bearer\",\"windows\":[{\"label\":\"每日\",\"used\":\"d.used\",\"limit\":\"d.limit\"}]}");
                Require(p.Id == "custom-my-gateway" && p.Host == "api.example.com" && p.Windows.Count == 1);
                Func<string, bool> rejected = json => { try { CustomProviders.Parse(json); return false; } catch (ArgumentException) { return true; } };
                Require(rejected("{\"name\":\"x\",\"url\":\"http://api.example.com/u\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"p\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://user:pw@api.example.com/u\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"p\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://a.example.com\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"p\",\"remainingPercent\":\"q\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://a.example.com\",\"windows\":[{\"label\":\"a\",\"used\":\"u\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://a.example.com\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"a..b\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://a.example.com\",\"auth\":\"header\",\"header\":\"Cookie\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"p\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://a.example.com\",\"script\":\"x\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"p\"}]}"));
                Require(rejected("{\"name\":\"x\",\"url\":\"https://a.example.com\"}"));
                Require(!rejected("{\"name\":\"lan\",\"url\":\"http://192.168.1.20:3000/api/usage\",\"windows\":[{\"label\":\"a\",\"usedPercent\":\"p\"}]}") && !rejected("{\"name\":\"loop\",\"url\":\"http://127.0.0.1:8080/u\",\"balance\":{\"amount\":\"b\"}}"));
            });
            test("Custom provider mapping: paths, ratios, counts, resets and missing fields", () => {
                var p = CustomProviders.Parse("{\"name\":\"g\",\"url\":\"https://a.example.com\",\"plan\":\"data.plan\",\"windows\":[{\"label\":\"日\",\"used\":\"data.items[1].used\",\"limit\":\"data.items[1].limit\",\"resetInSeconds\":\"data.reset\",\"windowHours\":24},{\"label\":\"月\",\"remainingPercent\":\"data.left\",\"ratio\":true,\"resetsAt\":\"data.month_end\"},{\"label\":\"缺\",\"usedPercent\":\"data.nope\"}],\"balance\":{\"amount\":\"data.balance\",\"currencyPath\":\"data.cur\"}}");
                var now = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
                var s = CustomProviders.Map(p, J.Parse("{\"data\":{\"plan\":\"Team\",\"items\":[{\"used\":1},{\"used\":\"30\",\"limit\":120}],\"reset\":7200,\"left\":0.8,\"month_end\":1790675446,\"balance\":\"12.5\",\"cur\":\"cny\"}}"), now);
                Require(s.Plan == "Team" && s.Quotas.Count == 2 && s.Quotas[0].Used == 25 && s.Quotas[0].ResetUtc.StartsWith("2026-09-29T02:00") && s.Quotas[0].WindowSeconds == 86400);
                Require(Math.Abs(s.Quotas[1].Used - 20) < 1e-9 && s.Quotas[1].ResetUtc.StartsWith("2026-09-29") && s.Balances.Count == 1 && s.Balances[0].Currency == "CNY" && s.Balances[0].Amount == 12.5);
                bool threw = false; try { CustomProviders.Map(p, J.Parse("{\"other\":1}"), now); } catch (ProviderException) { threw = true; } Require(threw);
                Require(CustomProviders.Resolve(J.Parse("{\"a\":[{\"b\":5}]}"), "a[0].b").ToString() == "5" && CustomProviders.Resolve(J.Parse("{\"a\":[]}"), "a[3].b") == null);
            });
            test("Epoch milliseconds are recognised as timestamps", () => {
                Require(J.Iso(1790675446000.0).StartsWith("2026-09-29") && J.Iso(1790675446).StartsWith("2026-09-29"));
            });
            test("Quota cache entries round-trip through the serializer", () => {
                var state = new ProviderState { Id = "codex", Status = "ready" }; state.Quotas.Add(new Quota { Label = "每周", Used = 47, WindowSeconds = 604800, ResetUtc = "2026-10-04T02:33:45Z" });
                var back = J.Serializer().Deserialize<List<ProviderState>>(J.Serializer().Serialize(new List<ProviderState> { state }));
                Require(back.Count == 1 && back[0].Quotas[0].Remaining == 53 && back[0].Quotas[0].Label == "每周");
            });
            test("Unpriced local usage remains unknown instead of becoming a zero-dollar claim", () => {
                var h = HistoryService.Parse(J.Parse("{\"daily\":[{\"period\":\"2026-01-01\",\"agents\":[{\"agent\":\"codex\",\"totalTokens\":15}]}]}")); Require(h.Days.Count == 1 && !h.Days[0].CostKnown);
            });
            test("Most-used model is ordered by recorded tokens including cache", () => {
                var h = HistoryService.Parse(J.Parse("{\"daily\":[{\"period\":\"2026-01-01\",\"agents\":[{\"agent\":\"codex\",\"totalTokens\":101,\"modelBreakdowns\":[{\"modelName\":\"test-a\",\"inputTokens\":1,\"cacheReadTokens\":90},{\"modelName\":\"test-b\",\"inputTokens\":10}]}]}]}")); Require(UsageDetails.MainModel(h.Days) == "test-a");
            });
            test("Window migration turns off legacy always-on-top and preserves provider selection", () => {
                var config = new AppConfig { UiVersion = 0, PinWindow = true, AlwaysOnTop = true, Enabled = new[] { "codex" } };
                WindowFrame.Normalize(config); Require(!config.AlwaysOnTop && config.UiVersion == 2 && config.Enabled.Length == 1);
                config.WindowWidth = 560; WindowFrame.Normalize(config); Require(config.WindowWidth == 560);
            });
            test("Window geometry and zoom reject nonfinite values", () => {
                var config = new AppConfig { UiVersion = 2, WindowWidth = Double.NaN, WindowHeight = -10, UiScale = Double.PositiveInfinity, WindowLeft = Double.NaN };
                WindowFrame.Normalize(config); Require(config.WindowWidth == 420 && config.WindowHeight == 460 && config.UiScale == 1 && !config.WindowLeft.HasValue);
            });
            test("Antigravity desktop response parses measured model quotas", () => {
                var state = LocalAntigravity.Parse(J.Parse("{\"userStatus\":{\"cascadeModelConfigData\":{\"clientModelConfigs\":[{\"label\":\"test-model\",\"quotaInfo\":{\"remainingFraction\":0.6,\"resetTime\":\"2026-01-20T00:00:00Z\"}}]}}}")); Require(state.Quotas.Count == 1 && state.Quotas[0].Remaining == 60);
            });
            test("Legacy opaque settings migrate to acrylic with zero transparency", () => {
                var config = J.Serializer().Deserialize<AppConfig>("{\"UiVersion\":2,\"Material\":\"solid\",\"SurfaceOpacity\":0.4,\"Widgets\":[{\"Size\":\"small\"}],\"Enabled\":[\"codex\"]}");
                WindowFrame.Normalize(config);
                Require(config.Material == "acrylic" && config.SurfaceOpacity == 1 && config.DisplaySize == "full" && config.Enabled.SequenceEqual(new[] { "codex" }));
            });
            test("Transparency accepts both endpoints and rejects invalid alpha", () => {
                Require(WindowFrame.ClampOpacity(0) == 0 && WindowFrame.ClampOpacity(1) == 1);
                Require(WindowFrame.ClampOpacity(-.5) == 0 && WindowFrame.ClampOpacity(2) == 1 && WindowFrame.ClampOpacity(Double.NaN) == WindowFrame.DefaultOpacity);
            });
            test("Display size and compact geometry survive valid settings and reject invalid values", () => {
                foreach (string size in MonitorPanel.DisplaySizes) {
                    var config = new AppConfig { UiVersion = 2, DisplaySize = size, CompactLeft = 45, CompactTop = 90 };
                    WindowFrame.Normalize(config); Require(config.DisplaySize == size && config.CompactLeft == 45 && config.CompactTop == 90);
                }
                var bad = new AppConfig { UiVersion = 2, DisplaySize = "widget", CompactLeft = Double.NaN, CompactTop = Double.PositiveInfinity };
                WindowFrame.Normalize(bad); Require(bad.DisplaySize == "full" && !bad.CompactLeft.HasValue && !bad.CompactTop.HasValue);
            });
            test("Mica migrates to acrylic without changing transparency or layout geometry", () => {
                var c = new AppConfig { UiVersion = 2, Material = "mica", SurfaceOpacity = .35, DisplaySize = "small" };
                c.Layouts["small"] = new PanelPlacement { Width = 310, Height = 430, Left = 90, Top = 80 };
                WindowFrame.Normalize(c); Require(c.Material == "acrylic" && c.SurfaceOpacity == .35 && c.Layouts["small"].Width == 310 && c.Layouts["small"].Top == 80);
                c.Layouts["medium"] = new PanelPlacement { Width = Double.NaN, Height = Double.PositiveInfinity, Left = Double.NaN };
                WindowFrame.Normalize(c); Require(c.Layouts["medium"].Width == WindowFrame.DefaultSize("medium").Width && !c.Layouts["medium"].Left.HasValue);
            });
            test("Local provider refresh preserves other providers and retained past totals", () => {
                var other = new DayUsage { Agent = "codex", Day = "2026-01-02", Tokens = 70 };
                var old = new UsageHistory { Zone = "test", Days = new List<DayUsage> { other, new DayUsage { Agent = "pi", Day = "2026-01-01", Tokens = 100 } } };
                var next = new UsageHistory { Zone = "test", Days = new List<DayUsage> { new DayUsage { Agent = "codex", Day = "2026-01-02", Tokens = 999 }, new DayUsage { Agent = "pi", Day = "2026-01-01", Tokens = 20 }, new DayUsage { Agent = "pi", Day = "2026-01-02", Tokens = 30 } } };
                var result = HistoryService.MergeProvider(old, next, "pi", "2026-01-01", "2026-01-02");
                Require(Object.ReferenceEquals(result.Days.Single(d => d.Agent == "codex"), other) && result.Days.Where(d => d.Agent == "pi").Sum(d => d.Tokens) == 130);
            });
            PeriodTests.Add(test);
            test("Restored compact defaults migrate once while custom and zoomed geometry remain", () => {
                var c = new AppConfig { UiVersion = 2 }; c.Layouts["small"] = new PanelPlacement { Width = 260, Height = 320 }; c.Layouts["medium"] = new PanelPlacement { Width = 405, Height = 285 };
                WindowFrame.Normalize(c); Require(c.Layouts["small"].Width == 172 && c.Layouts["small"].Height == 172 && c.Layouts["medium"].Width == 405);
                c.UiScale = .8; c.Layouts["small"].Width = 150; c.Layouts["small"].Height = 150; WindowFrame.Normalize(c); Require(c.Layouts["small"].Width == 150 && c.LayoutStyleVersion == 8);
            });
            Directory.CreateDirectory(Path.Combine(Store.Root, "verification"));
            File.WriteAllText(Path.Combine(Store.Root, "verification", "tests.json"), J.Serializer().Serialize(new { passed = passed.Count, failed = failed.Count, checks = passed, errors = failed }));
            return failed.Count == 0 ? 0 : 1;
        }
        private static void Require(bool value) { if (!value) throw new Exception("Assertion failed"); }
    }
}
