using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            test("Cursor Grok Bot weekly quota is appended after the plan meters", () => {
                var s = Parsers.Cursor(J.Parse("{\"membershipType\":\"pro\",\"billingCycleStart\":\"2026-09-02T03:15:00.000Z\",\"billingCycleEnd\":\"2026-10-02T03:15:00.000Z\",\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20.38,\"autoPercentUsed\":12.53,\"apiPercentUsed\":98.89}}}"));
                Parsers.CursorGrokBot(s, J.Parse("{\"currentPeriodStart\":\"2026-09-24T03:13:20.873Z\",\"nextResetTimestampUtc\":\"2026-10-01T03:13:20.873Z\",\"usagePercent\":69.163729,\"hasNonZeroIncludedLimit\":true,\"hasAvailableUsage\":true}"));
                Require(s.Quotas.Count == 4 && s.Quotas[0].Label == "套餐总量" && s.Quotas[1].Label == "Auto" && s.Quotas[2].Label == "API / 手动模型" && s.Quotas[3].Label == Parsers.CursorGrokLabel);
                Require(Math.Abs(s.Quotas[3].Used - 69.163729) < 1e-4 && Math.Abs(s.Quotas[3].Remaining - 30.836271) < 1e-4);
                Require(s.Quotas[3].ResetUtc.StartsWith("2026-10-01T03:13:20") && Math.Abs(s.Quotas[3].WindowSeconds - 604800) < 1);
                Require(UsageDetails.Primary(s).Label == "套餐总量" && UsageDetails.MainWindow(s).Label == "套餐总量");
                var noCycle = Parsers.Cursor(J.Parse("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20,\"autoPercentUsed\":5,\"apiPercentUsed\":99}}}"));
                Parsers.CursorGrokBot(noCycle, J.Parse("{\"usagePercent\":69.163729,\"hasNonZeroIncludedLimit\":true,\"currentPeriodStart\":\"2026-09-24T03:13:20.873Z\",\"nextResetTimestampUtc\":\"2026-10-01T03:13:20.873Z\"}"));
                Require(UsageDetails.Primary(noCycle).Label == "套餐总量" && UsageDetails.MainWindow(noCycle) == null);
            });
            test("Cursor Grok Bot with a zero included limit adds no quota", () => {
                var s = Parsers.Cursor(J.Parse("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20,\"autoPercentUsed\":5,\"apiPercentUsed\":99}}}"));
                Parsers.CursorGrokBot(s, J.Parse("{\"usagePercent\":69,\"hasNonZeroIncludedLimit\":false,\"hasAvailableUsage\":false,\"nextResetTimestampUtc\":\"2026-10-01T03:13:20.873Z\",\"currentPeriodStart\":\"2026-09-24T03:13:20.873Z\"}"));
                Require(s.Quotas.Count == 3 && s.Quotas.All(q => q.Label != Parsers.CursorGrokLabel) && s.Quotas[0].Remaining == 80);
            });
            test("Cursor Grok Bot missing fields add no quota", () => {
                var s = Parsers.Cursor(J.Parse("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":20}}}"));
                Parsers.CursorGrokBot(s, null);
                Parsers.CursorGrokBot(s, J.Parse("{}"));
                Parsers.CursorGrokBot(s, J.Parse("{\"hasNonZeroIncludedLimit\":true,\"nextResetTimestampUtc\":\"2026-10-01T03:13:20.873Z\"}"));
                Parsers.CursorGrokBot(s, J.Parse("{\"usagePercent\":69,\"nextResetTimestampUtc\":\"2026-10-01T03:13:20.873Z\"}"));
                Parsers.CursorGrokBot(s, J.Parse("{\"hasNonZeroIncludedLimit\":true,\"usagePercent\":\"nope\"}"));
                Require(s.Quotas.Count == 1 && s.Quotas[0].Label == "套餐总量" && s.Quotas[0].Remaining == 80);
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
            test("Grok product shares split the one allowance and are not quotas", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"GROK_BUILD\",\"usagePercent\":55},{\"product\":\"CHAT\",\"usagePercent\":8}]}}"));
                Require(s.Quotas.Count == 1 && s.Quotas[0].Remaining == 37 && s.ProductUsage.Count == 2 && s.ProductUsage[0].DisplayName == "Build" && s.ProductUsage[1].DisplayName == "Chat");
                var old = new ProviderState { Id = "grok" }; Parsers.Add(old, "当前账期", 63, null); Parsers.Add(old, "Build 占比", 55, null);
                Require(Parsers.Normalize(old).Quotas.Count == 1 && old.Quotas[0].Label == "当前账期" && old.Quotas[0].Remaining == 37 && old.ProductUsage.Count == 0);
            });
            GrokTests.Run(test);
            test("Percentages are bounded and nonnumeric values ignored", () => {
                var s = new ProviderState(); Parsers.Add(s, "one", -10, null); Parsers.Add(s, "two", 150, null); Parsers.Add(s, "bad", "NaN", null); Require(s.Quotas.Count == 2 && s.Quotas[0].Remaining == 100 && s.Quotas[1].Remaining == 0);
            });
            test("Daily history groups hourly buckets by local day and model", () => {
                var idx = new LogIndex(); idx.Files.Add(new LogFile { Hours = { { "2026010110||m1", new Bucket { I = 100, C = 900, O = 10, R = 1, D = 2 } }, { "2026010118||m2", new Bucket { I = 50, O = 5, R = 1, D = 1 } } } });
                // 10:00Z is 18:00 on Jan 1 in UTC+8; 18:00Z is 02:00 on Jan 2.
                var h = HistoryService.FromIndexes(new Dictionary<string, LogIndex> { { "codex", idx } }, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"), new DateTime(2026, 1, 2), 30);
                Require(h.Days.Count == 2 && h.Days[0].Day == "2026-01-01" && h.Days[0].Tokens == 1010 && h.Days[0].CachedTokens == 900 && h.Days[0].Cost == 2 && h.Days[1].Day == "2026-01-02" && h.Days[1].Models[0].Model == "m2" && h.Engine == HistoryService.Engine);
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
            test("Price sync accepts only a dated, complete catalogue", () => {
                string date; var many = String.Join(",", Enumerable.Range(0, 120).Select(i => "\"m" + i + "\":{\"in\":1,\"out\":2}"));
                Require(Pricing.Valid("{\"updated\":\"2026-10-01\",\"models\":{" + many + "}}", out date) && date == "2026-10-01");
                Require(!Pricing.Valid("{\"updated\":\"2026-10-01\",\"models\":{\"a\":{\"in\":1,\"out\":2}}}", out date));
                Require(!Pricing.Valid("{\"updated\":\"yesterday\",\"models\":{" + many + "}}", out date));
                Require(Pricing.Valid("{\"updated\":\"2026-10-01T02:17Z\",\"models\":{" + many + "}}", out date) && String.CompareOrdinal(date, "2026-10-01") > 0);
                Require(!Pricing.Valid("<html>rate limited</html>", out date));
                Require(Pricing.Valid(File.ReadAllText(Path.Combine(Store.Root, "pricing.json")), out date));
            });
            test("Pricing: cache writes, 1-hour writes, whole-request and marginal long-context tiers", () => {
                Pricing.LoadJson("{\"updated\":\"t\",\"models\":{\"t-model\":{\"in\":10,\"out\":50,\"cr\":1,\"cw\":12.5,\"long\":{\"at\":272000,\"in\":20,\"out\":75,\"cr\":2},\"fast\":2},\"c-model\":{\"in\":3,\"out\":15},\"m-model\":{\"in\":1,\"out\":2,\"long\":{\"at\":200000,\"marginal\":true,\"in\":2,\"out\":4}},\"g-5\":{\"in\":1,\"out\":1}}}");
                try {
                    ModelPrice t = Pricing.Find("t-model"), c = Pricing.Find("c-model"), m = Pricing.Find("m-model");
                    Require(Math.Abs(Pricing.CodexCost(t, 300000, 200000, 0, 1000) - 2.475) < 1e-9 && Math.Abs(Pricing.CodexCost(t, 1000, 900, 0, 10) - 0.0024) < 1e-12 && t.Fast == 2);
                    Require(Math.Abs(Pricing.Cost(c, new TokenUse { Input = 1000, Output = 100, CacheRead = 10000, CacheWrite5m = 2000, CacheWrite1h = 1000 }) - 0.021) < 1e-12);
                    Require(Math.Abs(Pricing.Cost(m, new TokenUse { Input = 300000 }) - 0.4) < 1e-12);
                    Require(Math.Abs(Pricing.Cost(t, new TokenUse { Input = 100000, CacheRead = 200000 }) - (100000 * 20e-6 + 200000 * 2e-6)) < 1e-9);
                    Require(Pricing.Find("t-model(xhigh)") == t && Pricing.Find("provider/t-model-20260101") == t && Pricing.Find("g-5-4") == null && Pricing.Find("g-5.4") == null && Pricing.Find("g-5-20260101") != null);
                } finally { Pricing.Load(Path.Combine(Store.Root, "pricing.json")); }
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
                var index = new LogIndex(); var state = new LogFile();
                long offset; using (var stream = new MemoryStream(bytes)) offset = CodexLogs.Read(index, state, "none", stream, 0);
                // No turn_context yet: the model falls back to gpt-5, as Codex itself does.
                Bucket first = state.Hours["2026092702|openai|gpt-5"];
                Require(state.Hours.Count == 1 && first.Tokens() == 1050 && first.I == 100 && first.C == 900 && first.R == 1 && offset == bytes.Length - 40);
                byte[] rest = System.Text.Encoding.UTF8.GetBytes(b + "\n");
                using (var stream = new MemoryStream(rest)) CodexLogs.Read(index, state, "none", stream, offset);
                Require(state.Hours["2026092703|openai|gpt-5"].Tokens() == 2030);
            });
            test("Codex buckets carry the session's provider and model; subscription windows skip relays", () => {
                string meta = "{\"timestamp\":\"2026-09-27T02:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"x\",\"model_provider\":\"custom\"}}";
                string turn = "{\"timestamp\":\"2026-09-27T02:00:01.000Z\",\"type\":\"turn_context\",\"payload\":{\"model\":\"demo-model\"}}";
                string tier = "{\"timestamp\":\"2026-09-27T02:00:02.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"thread_settings_applied\",\"thread_settings\":{\"service_tier\":\"priority\"}}}";
                string count = "{\"timestamp\":\"2026-09-27T02:05:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":500,\"output_tokens\":20},\"last_token_usage\":{\"input_tokens\":500,\"output_tokens\":20}}}}";
                var state = new LogFile();
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(meta + "\n" + turn + "\n" + tier + "\n" + count + "\n"))) CodexLogs.Read(new LogIndex(), state, "none", stream, 0);
                Require(state.Hours.ContainsKey("2026092702|custom|demo-model") && state.Hours["2026092702|custom|demo-model"].Tokens() == 520 && state.Tier == "fast");
                var index = new LogIndex { CoveredFrom = "2026-09-01T00:00:00Z" }; index.Files.Add(state);
                index.Files.Add(new LogFile { Hours = { { "2026092703|openai|demo-model", new Bucket { I = 100, R = 1 } } } });
                var q = new Quota { WindowSeconds = 604800, ResetUtc = "2026-10-01T00:00:00Z" };
                var usage = UsageDetails.Window(new DayUsage[0], q, new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), 0, index, TimeZoneInfo.Utc, CodexLogs.IsOfficial);
                Require(usage.Exact && Math.Abs(usage.Tokens - 100) < 1e-9);
            });
            test("Claude log entries are de-duplicated by message id + request id; the larger copy wins", () => {
                Func<string, string, string, int, string> line = (id, model, stamp, output) => "{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"requestId\":\"req_" + id + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":10,\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":1000}}}";
                string text = line("msg_a", "demo-claude", "2026-09-27T02:00:00Z", 5) + "\n" + line("msg_a", "demo-claude", "2026-09-27T02:00:01Z", 5) + "\n" + line("msg_b", "<synthetic>", "2026-09-27T02:01:00Z", 5) + "\n" + line("msg_c", "demo-claude", "2026-09-27T03:00:00Z", 5) + "\n" + line("msg_c", "demo-claude", "2026-09-27T03:00:02Z", 50) + "\n";
                var index = new LogIndex(); var state = new LogFile { Name = "s.jsonl" }; index.Files.Add(state);
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))) ClaudeLogs.Read(index, state, "s.jsonl", stream, 0);
                Require(state.Hours["2026092702||demo-claude"].Tokens() == 1115 && state.Hours["2026092702||demo-claude"].R == 1 && state.Hours["2026092702||"].Tokens() == 1115 && state.Hours["2026092703||demo-claude"].O == 50 && state.Hours["2026092703||demo-claude"].R == 1);
                // Without requestId: same id + same timestamp is a copy, a new timestamp is not.
                string bare = "{\"type\":\"assistant\",\"timestamp\":\"2026-09-27T04:00:00Z\",\"message\":{\"id\":\"msg_d\",\"model\":\"demo-claude\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}";
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(bare + "\n" + bare + "\n" + bare.Replace("04:00:00", "04:00:09") + "\n"))) ClaudeLogs.Read(index, state, "s.jsonl", stream, 0);
                Require(state.Hours["2026092704|" + ClaudeLogs.NoRequestId + "|demo-claude"].Tokens() == 4 && state.Hours["2026092704|" + ClaudeLogs.NoRequestId + "|demo-claude"].R == 2);
                // A null id is malformed and skipped; sub-agent progress lines count.
                string malformed = "{\"type\":\"assistant\",\"timestamp\":\"2026-09-27T05:00:00Z\",\"message\":{\"id\":null,\"model\":\"demo-claude\",\"usage\":{\"input_tokens\":7,\"output_tokens\":7}}}";
                string progress = "{\"type\":\"progress\",\"sessionId\":\"s1\",\"data\":{\"message\":{\"timestamp\":\"2026-09-27T05:10:00Z\",\"requestId\":\"req_p\",\"message\":{\"id\":\"msg_p\",\"model\":\"demo-claude\",\"usage\":{\"input_tokens\":3,\"output_tokens\":2}}}}}";
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(malformed + "\n" + progress + "\n"))) ClaudeLogs.Read(index, state, "s.jsonl", stream, 0);
                Require(state.Hours["2026092705||demo-claude"].Tokens() == 5);
            });
            test("Output speed (Codex): last tool output / prompt → last model item, across reads; short replies skipped", () => {
                Func<string, string, string> item = (stamp, payload) => "{\"timestamp\":\"2026-09-27T" + stamp + "Z\",\"type\":\"response_item\",\"payload\":{" + payload + "}}";
                Func<string, int, int, string> count = (stamp, total, last) => "{\"timestamp\":\"2026-09-27T" + stamp + "Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":" + (total * 10) + ",\"output_tokens\":" + total + "},\"last_token_usage\":{\"input_tokens\":" + (last * 10) + ",\"output_tokens\":" + last + "}}}}";
                string first = String.Join("\n", new[] {
                    item("10:00:00.000", "\"type\":\"message\",\"role\":\"user\",\"content\":[]"),
                    "{\"timestamp\":\"2026-09-27T10:00:00.500Z\",\"type\":\"turn_context\",\"payload\":{\"model\":\"demo-model\"}}",
                    item("10:00:12.000", "\"type\":\"reasoning\",\"summary\":[]") }) + "\n";
                string rest = String.Join("\n", new[] {
                    item("10:00:20.000", "\"type\":\"custom_tool_call\",\"name\":\"apply_patch\""),
                    item("10:00:22.000", "\"type\":\"custom_tool_call_output\",\"output\":\"ok\""),
                    count("10:00:22.010", 1000, 1000),
                    item("10:00:40.000", "\"type\":\"reasoning\",\"summary\":[]"),
                    item("10:00:47.000", "\"type\":\"message\",\"role\":\"assistant\",\"content\":[]"),
                    count("10:00:47.100", 1500, 500),
                    item("10:05:00.000", "\"type\":\"message\",\"role\":\"user\",\"content\":[]"),
                    item("10:05:03.000", "\"type\":\"message\",\"role\":\"assistant\",\"content\":[]"),
                    count("10:05:03.100", 1520, 20) }) + "\n";
                var index = new LogIndex(); var state = new LogFile();
                long offset; using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(first))) offset = CodexLogs.Read(index, state, "none", stream, 0);
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rest))) CodexLogs.Read(index, state, "none", stream, offset);
                Bucket b = state.Hours["2026092710|openai|demo-model"];
                // 20 s (10:00:00 → 10:00:20) + 25 s (10:00:22 → 10:00:47); the 20-token reply is not timed.
                Require(b.R == 3 && b.TN == 2 && b.TO == 1500 && Math.Abs(b.TS - 45) < 1e-9 && Math.Abs(b.Speed().Value - 1500 / 45.0) < 1e-9);
            });
            test("Output speed (Claude): parent entry → last block, across reads and files; quoted keys in text ignored", () => {
                Func<string, string, string, string, int, string> block = (id, uuid, parent, stamp, output) => "{\"parentUuid\":\"" + parent + "\",\"type\":\"assistant\",\"requestId\":\"req_" + id + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"demo-claude\",\"usage\":{\"input_tokens\":10,\"output_tokens\":" + output + "}},\"uuid\":\"" + uuid + "\",\"timestamp\":\"2026-09-27T" + stamp + "Z\"}";
                string user = "{\"parentUuid\":null,\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"hi\"},\"uuid\":\"u1\",\"timestamp\":\"2026-09-27T11:00:00.000Z\"}";
                string toolResult = "{\"parentUuid\":\"a2\",\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"content\":\"x \\\"uuid\\\":\\\"a1\\\",\\\"timestamp\\\":\\\"2026-09-27T09:00:00Z\\\"\"}]},\"uuid\":\"u2\",\"timestamp\":\"2026-09-27T11:00:30.000Z\"}";
                string system = "{\"parentUuid\":\"a3\",\"type\":\"system\",\"timestamp\":\"2026-09-27T11:01:00.000Z\",\"uuid\":\"s1\"}";
                string first = user + "\n" + block("m1", "a1", "u1", "11:00:06.000", 800) + "\n";
                string rest = String.Join("\n", new[] { block("m1", "a2", "a1", "11:00:10.000", 800), toolResult, block("m2", "a3", "u2", "11:00:45.000", 30), system, block("m3", "a4", "s1", "11:01:20.000", 400) }) + "\n";
                var index = new LogIndex(); var state = new LogFile { Name = "s.jsonl" }; index.Files.Add(state);
                long offset; using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(first))) offset = ClaudeLogs.Read(index, state, "s.jsonl", stream, 0);
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rest))) ClaudeLogs.Read(index, state, "s.jsonl", stream, offset);
                Bucket b = state.Hours["2026092711||demo-claude"];
                // m1: 11:00:00 → 11:00:10 = 10 s; m2 has 30 output tokens (skipped); m3: 11:01:00 → 11:01:20 = 20 s.
                Require(b.R == 3 && b.TN == 2 && b.TO == 1200 && Math.Abs(b.TS - 30) < 1e-9);
                // The same responses copied into another file are not timed again.
                var copy = new LogFile { Name = "t.jsonl" }; index.Files.Add(copy);
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(first + rest))) ClaudeLogs.Read(index, copy, "t.jsonl", stream, 0);
                Require(copy.Hours.Values.Sum(x => x.TN) == 0 && state.Hours["2026092711||demo-claude"].TN == 2);
            });
            test("Output speed (Grok, history): recorded API time per turn; timing reaches the daily rows", () => {
                string line = "{\"timestamp\":1750000000,\"params\":{\"sessionId\":\"sess-1\",\"update\":{\"sessionUpdate\":\"turn_completed\",\"usage\":{\"modelUsage\":{\"grok-demo\":{\"inputTokens\":100,\"outputTokens\":1000,\"modelCalls\":2,\"apiDurationMs\":20000},\"grok-mini\":{\"inputTokens\":10,\"outputTokens\":60,\"modelCalls\":2,\"apiDurationMs\":3000}}}},\"_meta\":{\"eventId\":\"evt-2\"}}}";
                var index = new LogIndex(); var file = new LogFile(); index.Files.Add(file);
                AgentLogs.GrokLine(index, file, line, "s", null);
                Bucket b = file.Hours["2025061515||grok-demo"], mini = file.Hours["2025061515||grok-mini"];
                Require(b.TN == 2 && b.TO == 1000 && b.TS == 20 && b.Speed() == 50 && mini.TN == 0);
                UsageHistory h = HistoryService.FromIndexes(new Dictionary<string, LogIndex> { { "grok", index } }, TimeZoneInfo.Utc, new DateTime(2025, 6, 15), 30);
                DayUsage row = h.Days.Single();
                Require(row.TimedRequests == 2 && row.TimedOutput == 1000 && row.TimedSeconds == 20 && row.Models.First(m => m.Model == "grok-demo").TimedSeconds == 20);
                Require(OutputTiming.Speed(1000, .5) == null && OutputTiming.Text(null) == "—" && OutputTiming.Text(50) == "50 t/s" && OutputTiming.Text(103.4) == "103 t/s" && OutputTiming.Text(9.46) == "9.5 t/s");
                Require(!OutputTiming.Accept(49, 10) && !OutputTiming.Accept(1000, 601) && OutputTiming.Accept(1000, 20) && OutputTiming.Supported("codex") && !OutputTiming.Supported("antigravity"));
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
                var claude = new LogIndex(); claude.Files.Add(new LogFile { Hours = { { "2026091810||m", new Bucket { I = 7, R = 1 } }, { "2026092210||m", new Bucket { I = 50, R = 1 } }, { "2026092610||m", new Bucket { I = 300, R = 2, D = 3 } }, { "2026092310|" + ClaudeLogs.NoRequestId + "|m", new Bucket { I = 20, R = 1 } }, { "2026091010|" + ClaudeLogs.NoRequestId + "|m", new Bucket { I = 5, R = 1 } } } });
                var codex = new LogIndex(); codex.Files.Add(new LogFile { Hours = { { "2026092610|custom|g", new Bucket { I = 40, R = 1, U = 40 } }, { "2026092610|openai|g", new Bucket { I = 900, R = 3 } } } });
                var report = ThirdPartyReport.Build(codex, claude, log, new UsageHistory(), new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
                var relay = report.Endpoints.First(e => e.App == "claude" && e.Host.Length > 0); var unknown = report.Endpoints.First(e => e.App == "codex");
                var bare = report.Endpoints.First(e => e.Key == ClaudeLogs.NoRequestId);
                Require(report.Endpoints.Count == 3 && relay.Current && relay.Month == 300 && relay.Requests == 2 && Math.Abs(relay.CostMonth - 3) < 1e-9 && relay.Week == 300 && relay.Today == 0);
                // No request-id: a relay even while settings.json looked official, and before tracking.
                Require(bare.Month == 25 && !bare.Current && unknown.Host == "" && unknown.Month == 40 && unknown.CostPartial && report.ClaudeUnattributed == 7 && report.AppMonth("claude") == 325);
            });
            test("The running hour counts in full up to now; past range edges still split by overlap", () => {
                DateTime hour = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc), now = hour.AddMinutes(15);
                var index = new LogIndex(); index.Files.Add(new LogFile { Name = "a.jsonl", Hours = { { "2026093010|openai|m", new Bucket { I = 1000, D = 4, R = 4 } }, { "2026093008|openai|m", new Bucket { I = 1000, D = 4, R = 4 } } } });
                // "Today until now" at 10:15: the 10:00 bucket already holds everything up to now.
                Bucket today = index.Usage(hour.AddHours(-10), now, null, null, now);
                Require(Math.Abs(today.Tokens() - 2000) < 1e-9 && Math.Abs(today.D - 8) < 1e-9 && Math.Abs(today.R - 8) < 1e-9);
                // A range ending at 08:15, asked at 10:15, takes a quarter of the 08:00 bucket.
                Require(Math.Abs(index.Usage(hour.AddHours(-3), hour.AddHours(-2).AddMinutes(15), null, null, now).Tokens() - 250) < 1e-9);
                Require(LogIndex.Weight(hour, hour.AddMinutes(-30), now, now) == 1 && LogIndex.Weight(hour, now, hour.AddHours(1), now) == 0 && LogIndex.Weight(hour.AddHours(-2), hour.AddHours(-2).AddMinutes(30), hour, now) == .5);
            });
            test("Switching relay inside an hour attributes each request to the endpoint in effect then", () => {
                var log = new EndpointLog();
                log.Marks.Add(new EndpointMark { App = "claude", Host = "a.example.com", Key = "aaaaaa", Name = "A", Since = "2026-09-30T09:00:00Z" });
                log.Marks.Add(new EndpointMark { App = "claude", Host = "b.example.com", Key = "bbbbbb", Name = "B", Since = "2026-09-30T10:30:00Z" });
                EndpointAttribution.Use(log);
                try {
                    // Switched A → B at 10:30; 900 tokens used on B at 10:40.
                    string line = "{\"type\":\"assistant\",\"timestamp\":\"2026-09-30T10:40:00Z\",\"requestId\":\"req_x\",\"message\":{\"id\":\"msg_x\",\"model\":\"demo-claude\",\"usage\":{\"input_tokens\":800,\"output_tokens\":100}}}";
                    var claude = new LogIndex(); var state = new LogFile { Name = "s.jsonl" }; claude.Files.Add(state);
                    using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(line + "\n"))) ClaudeLogs.Read(claude, state, "s.jsonl", stream, 0);
                    var report = ThirdPartyReport.Build(null, claude, log, new UsageHistory(), new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
                    var a = report.Endpoints.FirstOrDefault(e => e.Name == "A"); var b = report.Endpoints.First(e => e.Name == "B");
                    Require(b.Month == 900 && b.Current && (a == null || a.Month == 0));
                } finally { EndpointAttribution.Use(null); }
            });
            test("A truncated log is read again from the start and its surviving records count", () => {
                string dir = Path.Combine(Path.GetTempPath(), "codeusagemonit-trunc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
                try {
                    string path = Path.Combine(dir, "rollout.jsonl"); DateTime now = DateTime.UtcNow, at = now.AddHours(-1);
                    Func<int, int, string> count = (sec, total) => "{\"timestamp\":\"" + at.AddSeconds(sec).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":" + total + ",\"output_tokens\":0},\"last_token_usage\":{\"input_tokens\":200,\"output_tokens\":0}}}}";
                    Func<LogIndex, LogIndex> scan = idx => LogReader.Scan(idx, new[] { path }, info => info.Name, now, (i, st, p, s, off) => CodexLogs.Read(i, st, p, s, off));
                    File.WriteAllText(path, count(0, 200) + "\n" + count(10, 400) + "\n" + count(20, 600) + "\n");
                    LogIndex index = scan(null);
                    Require(Math.Abs(index.Entries().Sum(h => h.Tokens) - 600) < 1e-9);
                    // 600 tokens in three records → one 200-token record survives.
                    File.WriteAllText(path, count(0, 200) + "\n"); File.SetLastWriteTimeUtc(path, now.AddMinutes(-1));
                    index = scan(index);
                    Require(Math.Abs(index.Entries().Sum(h => h.Tokens) - 200) < 1e-9);
                } finally { try { Directory.Delete(dir, true); } catch { } }
            });
            test("Cursor dashboard events fold into hourly buckets per model, once each", () => {
                var index = new LogIndex { CoveredFrom = "2026-09-01T00:00:00Z" }; var file = new LogFile { Name = "cursor-dashboard" }; index.Files.Add(file);
                object page = J.Serializer().DeserializeObject("{\"totalUsageEventsCount\":3,\"usageEventsDisplay\":[" +
                    "{\"timestamp\":\"1790000000000\",\"model\":\"claude-x\",\"kind\":\"USAGE_EVENT_KIND_USAGE_BASED\",\"tokenUsage\":{\"inputTokens\":100,\"outputTokens\":20,\"cacheReadTokens\":300,\"cacheWriteTokens\":5,\"totalCents\":12.5}}," +
                    "{\"timestamp\":\"1790000060000\",\"model\":\"default\",\"tokenUsage\":{\"inputTokens\":10,\"outputTokens\":1}}," +
                    "{\"timestamp\":\"1790000120000\",\"model\":\"claude-x\",\"kind\":\"USAGE_EVENT_KIND_INCLUDED_IN_PRO\"}]}");
                List<object> events = CursorUsage.Events(page).ToList();
                Require(events.Count == 3);
                foreach (object e in events) CursorUsage.Add(index, file, e);
                foreach (object e in events) CursorUsage.Add(index, file, e); // a re-fetched page counts once
                List<HourUsage> hours = index.Entries().ToList();
                HourUsage x = hours.Single(h => h.Model == "claude-x"), auto = hours.Single(h => h.Model == "auto");
                Require(x.Hour == new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc) && x.Endpoint == null);
                Require(x.Requests == 2 && x.Input == 100 && x.Output == 20 && x.Cached == 300 && x.CacheWrite == 5 && Math.Abs(x.Cost - .125) < 1e-9);
                Require(auto.Requests == 1 && auto.Tokens == 11);
                Require(CursorUsage.Body(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), 2).Contains("\"startDate\":\"1788220800000\""));
            });
            test("Exact window clips hour buckets by overlap, tokens and cost alike", () => {
                var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                var index = new LogIndex { CoveredFrom = "2026-09-01T00:00:00Z", Updated = "2026-09-29T10:00:00Z" };
                // The window opens at 02:30Z, halfway through the 02:00Z bucket.
                index.Files.Add(new LogFile { Name = "a.jsonl", Hours = { { "2026092702|openai|m", new Bucket { I = 1000, D = 1 } }, { "2026092703|openai|m", new Bucket { I = 2000, D = 2 } }, { "2026092802|openai|m", new Bucket { I = 4000, D = 8 } } } });
                var q = new Quota { WindowSeconds = 604800, ResetUtc = "2026-10-04T02:30:00Z" };
                var usage = UsageDetails.Window(new DayUsage[0], q, new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), 0, index, zone);
                Require(usage.Exact && Math.Abs(usage.Tokens - 6500) < .001 && Math.Abs(usage.Cost - 10.5) < 1e-9 && usage.CostComplete);
                Require(!UsageDetails.Window(new DayUsage[0], q, new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), 4, index, zone).Exact);
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
                var idx = new LogIndex(); idx.Files.Add(new LogFile { Hours = { { "2026010102||x", new Bucket { I = 15, R = 1, U = 15 } } } });
                var h = HistoryService.FromIndexes(new Dictionary<string, LogIndex> { { "codex", idx } }, TimeZoneInfo.Utc, new DateTime(2026, 1, 1), 30);
                Require(h.Days.Count == 1 && !h.Days[0].CostKnown && h.Days[0].UnpricedTokens == 15);
            });
            test("Most-used model is ordered by recorded tokens including cache", () => {
                var day = new DayUsage { Day = "2026-01-01", Agent = "codex", Models = { new ModelUsage { Model = "test-a", Tokens = 91 }, new ModelUsage { Model = "test-b", Tokens = 10 } } };
                Require(UsageDetails.MainModel(new[] { day }) == "test-a");
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
            test("Display sizes: solid material becomes opaque acrylic, unknown sizes fall back to full", () => {
                var config = new AppConfig { UiVersion = 2, Material = "solid", SurfaceOpacity = .3, DisplaySize = "huge", CompactProvider = "" };
                WindowFrame.Normalize(config); Require(config.Material == "acrylic" && config.SurfaceOpacity == 1 && config.DisplaySize == "full" && config.CompactProvider == "overview");
                config.SurfaceOpacity = Double.NaN; WindowFrame.Normalize(config); Require(config.SurfaceOpacity == WindowFrame.DefaultOpacity);
            });
            test("Surface colour: hex parsing, HSV round trip, contrast, invalid config falls back", () => {
                System.Windows.Media.Color c;
                Require(MonitorPanel.TryParseHex("#0f1b2d", out c) && MonitorPanel.Hex(c) == "#0F1B2D");
                Require(MonitorPanel.TryParseHex("abc", out c) && MonitorPanel.Hex(c) == "#AABBCC");
                Require(!MonitorPanel.TryParseHex("#12345", out c) && !MonitorPanel.TryParseHex("red", out c) && !MonitorPanel.TryParseHex(null, out c));
                foreach (string hex in new[] { "#15171B", "#000000", "#FFFFFF", "#2A1218", "#0D2226", "#7F3FBF" }) {
                    double h, s, v; MonitorPanel.TryParseHex(hex, out c); MonitorPanel.ToHsv(c, out h, out s, out v);
                    Require(MonitorPanel.Hex(MonitorPanel.FromHsv(h, s, v)) == hex);
                }
                Require(MonitorPanel.Hex(MonitorPanel.FromHsv(360, 1, 1)) == "#FF0000" && MonitorPanel.Hex(MonitorPanel.FromHsv(120, 1, 1)) == "#00FF00");
                Require(Math.Abs(MonitorPanel.ContrastRatio(System.Windows.Media.Colors.White, System.Windows.Media.Colors.Black) - 21) < 1e-9);
                Require(MonitorPanel.ContrastRatio(System.Windows.Media.Color.FromRgb(0xF2, 0xF3, 0xF5), WindowFrame.DefaultTint) > 15);
                var config = new AppConfig { UiVersion = 2, SurfaceColor = "not a colour" };
                WindowFrame.Normalize(config); Require(config.SurfaceColor == "#15171B");
                config.SurfaceColor = "#abc"; WindowFrame.Normalize(config); Require(config.SurfaceColor == "#AABBCC");
            });
            test("Per-size geometry keeps valid compact layouts and drops broken ones", () => {
                var config = new AppConfig { UiVersion = 2 };
                config.Layouts["small"] = new WindowGeometry { Width = 200, Height = 200, Left = Double.NaN, Top = 40 };
                config.Layouts["full"] = new WindowGeometry { Width = 400, Height = 400 };
                config.Layouts["large"] = new WindowGeometry { Width = Double.PositiveInfinity, Height = 400 };
                WindowFrame.Normalize(config);
                Require(config.Layouts.Count == 1 && !config.Layouts["small"].Left.HasValue && config.Layouts["small"].Top == 40);
                config.Layouts = null; WindowFrame.Normalize(config); Require(config.Layouts != null);
            });
            test("Every display size's default fits between its resize limits", () => {
                foreach (string size in new[] { "small", "medium", "large", "full" }) {
                    System.Windows.Size min = WindowFrame.MinSize(size), def = WindowFrame.DefaultSize(size), max = WindowFrame.MaxSize(size);
                    Require(min.Width <= def.Width && def.Width <= max.Width && min.Height <= def.Height && def.Height <= max.Height);
                }
            });
            test("Grok turns: per-model usage, cached input split out, recorded cost wins, event ids dedupe", () => {
                string line = "{\"timestamp\":1750000000,\"params\":{\"sessionId\":\"sess-1\",\"update\":{\"sessionUpdate\":\"turn_completed\",\"usage\":{\"modelUsage\":{\"grok-4.5-build\":{\"inputTokens\":100,\"outputTokens\":20,\"cachedReadTokens\":40,\"reasoningTokens\":10,\"costUsdTicks\":12345678901}}}},\"_meta\":{\"eventId\":\"evt-1\"}}}";
                var index = new LogIndex(); var file = new LogFile();
                AgentLogs.GrokLine(index, file, line, "s", null); AgentLogs.GrokLine(index, file, line, "s", null);
                Bucket b = file.Hours["2025061515||grok-4.5-build"];
                Require(b.I == 60 && b.C == 40 && b.O == 20 && b.R == 1 && Math.Abs(b.D - 1.2345678901) < 1e-12);
            });
            test("Kimi wire records: old StatusUpdate and new turn-scoped usage.record", () => {
                var index = new LogIndex(); var file = new LogFile();
                AgentLogs.KimiLine(index, file, "{\"timestamp\":1770983427.123,\"message\":{\"type\":\"StatusUpdate\",\"payload\":{\"token_usage\":{\"input_other\":100,\"output\":50,\"input_cache_read\":10,\"input_cache_creation\":20},\"message_id\":\"msg-1\"}}}", "kimi-for-coding", "s", DateTime.UtcNow);
                AgentLogs.KimiLine(index, file, "{\"type\":\"usage.record\",\"model\":\"kimi-code/kimi-for-coding\",\"usage\":{\"inputOther\":3064,\"output\":76,\"inputCacheRead\":14848,\"inputCacheCreation\":0},\"usageScope\":\"turn\",\"time\":1782113184943}", "x", "s", DateTime.UtcNow);
                AgentLogs.KimiLine(index, file, "{\"type\":\"usage.record\",\"model\":\"kimi-code/kimi-for-coding\",\"usage\":{\"inputOther\":5000,\"output\":200},\"usageScope\":\"session\",\"time\":1782113185000}", "x", "s", DateTime.UtcNow);
                Bucket old = file.Hours["2026021311||kimi-for-coding"];
                Require(old.I == 100 && old.O == 50 && old.C == 10 && old.W == 20 && file.Hours.Count == 2 && file.Hours.Values.Sum(v => v.R) == 2);
            });
            test("Pi assistant messages: a total-only record becomes output, copies count once", () => {
                var index = new LogIndex(); var file = new LogFile();
                string line = "{\"type\":\"message\",\"timestamp\":\"2026-01-02T00:00:00.000Z\",\"message\":{\"role\":\"assistant\",\"model\":\"gpt-5\",\"usage\":{\"totalTokens\":333}}}";
                MoreAgentLogs.PiLine(index, file, line); MoreAgentLogs.PiLine(index, file, line);
                Bucket b = file.Hours["2026010200||gpt-5"]; Require(b.O == 333 && b.R == 1);
            });
            test("Antigravity desktop response parses measured model quotas", () => {
                var state = LocalAntigravity.Parse(J.Parse("{\"userStatus\":{\"cascadeModelConfigData\":{\"clientModelConfigs\":[{\"label\":\"test-model\",\"quotaInfo\":{\"remainingFraction\":0.6,\"resetTime\":\"2026-01-20T00:00:00Z\"}}]}}}")); Require(state.Quotas.Count == 1 && state.Quotas[0].Remaining == 60);
            });
            test("Installed copies use the per-user directory; zip copies and portable.txt stay beside the exe", () => {
                string root = Path.Combine(Path.GetTempPath(), "codeusagemonit-data-" + Guid.NewGuid().ToString("N"));
                string user = Path.Combine(root, "userdir");
                Directory.CreateDirectory(root);
                try {
                    Require(Store.PathsEqual(Store.UserDataDirectory(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "codeusagemonit")));
                    Require(!Store.PathsEqual(null, root) && !Store.PathsEqual("  ", root));
                    Require(Store.PathsEqual(root + Path.DirectorySeparatorChar, root));
                    Require(Store.NormalizeDirectory("C:\\") == "C:\\");
                    Require(!Store.TryMigratePortableData(Path.Combine(user, "inside"), user));
                    string beside = Path.Combine(root, "data");
                    Require(Store.PathsEqual(Store.ResolveData(root, "", null, false, user), beside));
                    Require(Store.PathsEqual(Store.ResolveData(root, Path.Combine(root, "other"), null, false, user), beside));
                    Require(Store.PathsEqual(Store.ResolveData(root, root + Path.DirectorySeparatorChar, null, false, user), user));
                    Require(Store.PathsEqual(Store.ResolveData(root, "", null, true, user), user));
                    Require(Store.PathsEqual(Store.ResolveData(root, "", Path.Combine(root, "override"), true, user), Path.Combine(root, "override")));
                    File.WriteAllText(Path.Combine(root, "portable.txt"), "portable");
                    Require(Store.PathsEqual(Store.ResolveData(root, root, Path.Combine(root, "override"), true, user), beside));
                    File.Delete(Path.Combine(root, "portable.txt"));
                    Directory.CreateDirectory(beside);
                    File.WriteAllText(Path.Combine(beside, "settings.json"), "{\"kept\":true}");
                    Directory.CreateDirectory(Path.Combine(beside, "nested"));
                    File.WriteAllText(Path.Combine(beside, "nested", "a.txt"), "a");
                    File.WriteAllText(Path.Combine(root, "installed.txt"), "installed");
                    string chosen = Store.ChooseDataFor(root, "", null, user);
                    Require(Store.PathsEqual(chosen, user));
                    Require(File.ReadAllText(Path.Combine(user, "settings.json")) == "{\"kept\":true}");
                    Require(File.ReadAllText(Path.Combine(user, "nested", "a.txt")) == "a");
                    Require(!Directory.Exists(beside));
                    Directory.CreateDirectory(beside);
                    File.WriteAllText(Path.Combine(beside, "settings.json"), "{\"new\":true}");
                    Require(!Store.TryMigratePortableData(beside, user));
                    Require(File.ReadAllText(Path.Combine(user, "settings.json")) == "{\"kept\":true}");
                } finally { try { Directory.Delete(root, true); } catch { } }
            });
            test("A linked data directory is copied and the link is left in place", () => {
                string root = Path.Combine(Path.GetTempPath(), "codeusagemonit-link-" + Guid.NewGuid().ToString("N"));
                string real = Path.Combine(root, "real"); string link = Path.Combine(root, "data"); string user = Path.Combine(root, "user");
                Directory.CreateDirectory(real);
                File.WriteAllText(Path.Combine(real, "settings.json"), "{}");
                Process linkProcess = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + real + "\"") { UseShellExecute = false, CreateNoWindow = true });
                linkProcess.WaitForExit();
                Require(linkProcess.ExitCode == 0);
                try {
                    Require(Store.TryMigratePortableData(link, user));
                    Require(File.ReadAllText(Path.Combine(user, "settings.json")) == "{}");
                    Require(Directory.Exists(link) && File.Exists(Path.Combine(real, "settings.json")));
                } finally {
                    try {
                        Process remove = Process.Start(new ProcessStartInfo("cmd.exe", "/c rmdir \"" + link + "\"") { UseShellExecute = false, CreateNoWindow = true });
                        if (remove != null) remove.WaitForExit();
                        Directory.Delete(root, true);
                    } catch { }
                }
            });
            Directory.CreateDirectory(Path.Combine(Store.Root, "verification"));
            File.WriteAllText(Path.Combine(Store.Root, "verification", "tests.json"), J.Serializer().Serialize(new { passed = passed.Count, failed = failed.Count, checks = passed, errors = failed }));
            return failed.Count == 0 ? 0 : 1;
        }
        private static void Require(bool value) { if (!value) throw new Exception("Assertion failed"); }
    }
}
