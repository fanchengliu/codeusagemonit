using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeUsageMonit {
    public static class PeriodTests {
        private static void Require(bool b) { if (!b) throw new Exception("Assertion failed"); }
        private static DateTime U(int day, int hour, int minute = 0) { return new DateTime(2026, 9, day, hour, minute, 0, DateTimeKind.Utc); }
        private static UsageHistory History(DateTime stamp, params DayUsage[] rows) { return new UsageHistory { Updated = stamp.ToString("o"), Zone = "UTC", Days = rows.ToList() }; }
        private static DayUsage Row(string day, double tokens, double cost) { return new DayUsage { Day = day, Agent = "codex", Tokens = tokens, Cost = cost }; }
        private static LogIndex Index(DateTime updated) { return new LogIndex { CoveredFrom = U(1, 0).ToString("o"), Updated = updated.ToString("o"), Files = new List<LogFile> { new LogFile { Name = "fixture" } } }; }
        public static void Add(Action<string, Action> test) {
            test("Calendar today uses local midnight while 1d means the last 24 hours", () => {
                var zone = TimeZoneInfo.CreateCustomTimeZone("fixture+8", TimeSpan.FromHours(8), "fixture", "fixture");
                var today = UsagePeriod.Resolve(new UsageRangeChoice { Preset = "today" }, U(29, 3), zone);
                var day = UsagePeriod.Resolve(new UsageRangeChoice { Preset = "1d" }, U(29, 3), zone);
                Require(today.StartUtc == U(28, 16) && day.StartUtc == U(28, 3) && today.EndUtc == day.EndUtc);
            });
            test("Following current time preserves the custom start and advances only its end", () => {
                var c = new UsageRangeChoice { Preset = "custom", StartUtc = U(28, 9, 15).ToString("o"), FollowNow = true };
                var a = UsagePeriod.Resolve(c, U(29, 10), TimeZoneInfo.Utc); var b = UsagePeriod.Resolve(c, U(29, 11), TimeZoneInfo.Utc);
                Require(a.StartUtc == b.StartUtc && b.EndUtc - a.EndUtc == TimeSpan.FromHours(1));
            });
            test("Invalid reversed future and excessively old ranges are rejected", () => {
                foreach (var c in new[] {
                    new UsageRangeChoice { Preset = "custom", StartUtc = "bad", EndUtc = U(29, 8).ToString("o"), FollowNow = false },
                    new UsageRangeChoice { Preset = "custom", StartUtc = U(29, 9).ToString("o"), EndUtc = U(29, 8).ToString("o"), FollowNow = false },
                    new UsageRangeChoice { Preset = "custom", StartUtc = U(29, 9).ToString("o"), EndUtc = U(30, 8).ToString("o"), FollowNow = false },
                    new UsageRangeChoice { Preset = "custom", StartUtc = "2020-01-01T00:00:00Z", FollowNow = true } }) {
                    bool rejected = false; try { UsagePeriod.Resolve(c, U(29, 12), TimeZoneInfo.Utc); } catch (ArgumentException) { rejected = true; } Require(rejected);
                }
            });
            test("Hour boundaries are weighted and retain the model of the selected hours", () => {
                var logs = Index(U(29, 12)); LogIndex.Add(logs.Files[0], U(29, 10), "openai", "model-a", 120); LogIndex.Add(logs.Files[0], U(29, 11), "relay", "model-b", 80);
                var result = PeriodCalculator.Calculate("codex", new UsagePeriod { StartUtc = U(29, 10, 30), EndUtc = U(29, 11, 15) }, History(U(29, 12), Row("2026-09-29", 200, 2)), logs, U(29, 12), TimeZoneInfo.Utc, true);
                Require(result.EstimatedTokens && result.Hourly && Math.Abs(result.Tokens - 80) < .001 && Math.Abs(result.Cost - .8) < .001 && UsageDetails.MainModel(result.Days) == "model-a");
            });
            test("The current scanned hour is not divided by a full hour", () => {
                var logs = Index(U(29, 10, 15)); LogIndex.Add(logs.Files[0], U(29, 10), "openai", "test", 120);
                var r = PeriodCalculator.Calculate("codex", new UsagePeriod { StartUtc = U(29, 0), EndUtc = U(29, 10, 30) }, History(U(29, 10, 15), Row("2026-09-29", 120, 1.2)), logs, U(29, 10, 30), TimeZoneInfo.Utc, true);
                Require(r.Tokens == 120 && !r.EstimatedTokens && r.AsOf == U(29, 10, 15));
            });
            test("End timestamps are exclusive and future hourly buckets are omitted", () => {
                var logs = Index(U(29, 12)); LogIndex.Add(logs.Files[0], U(29, 10), "openai", "test", 100); LogIndex.Add(logs.Files[0], U(29, 11), "openai", "test", 900);
                var r = PeriodCalculator.Calculate("codex", new UsagePeriod { StartUtc = U(29, 10), EndUtc = U(29, 11) }, History(U(29, 12), Row("2026-09-29", 1000, 10)), logs, U(29, 12), TimeZoneInfo.Utc, true);
                Require(r.Tokens == 100 && r.Cost == 1 && !r.EstimatedTokens);
            });
            test("Daily sources never report an entire day's usage for a partial time selection", () => {
                var h = History(U(29, 12), Row("2026-09-28", 100, 1), Row("2026-09-29", 30, .3));
                var r = PeriodCalculator.Calculate("codex", new UsagePeriod { StartUtc = U(28, 9), EndUtc = U(29, 12) }, h, null, U(29, 12), TimeZoneInfo.Utc, true);
                Require(r.ExcludedPartialDays && r.Tokens == 30 && r.Days.Count == 1);
                var narrow = PeriodCalculator.Calculate("codex", new UsagePeriod { StartUtc = U(28, 9), EndUtc = U(28, 10) }, h, null, U(29, 12), TimeZoneInfo.Utc, true);
                Require(narrow.ExcludedPartialDays && narrow.Days.Count == 0);
            });
            test("Missing provider history stays unavailable rather than a zero usage claim", () => {
                var r = PeriodCalculator.Calculate("claude", new UsagePeriod { StartUtc = U(28, 0), EndUtc = U(29, 12) }, History(U(29, 12), Row("2026-09-28", 100, 1)), null, U(29, 12), TimeZoneInfo.Utc, true);
                Require(!r.HasData && !r.HasCost);
            });
            test("Fractional time zones split hourly usage across local midnight for pricing", () => {
                var zone = TimeZoneInfo.CreateCustomTimeZone("fixture+530", TimeSpan.FromMinutes(330), "fixture", "fixture");
                var logs = Index(U(29, 20)); LogIndex.Add(logs.Files[0], U(29, 18), "openai", "test", 100);
                var r = PeriodCalculator.Calculate("codex", new UsagePeriod { StartUtc = U(29, 18), EndUtc = U(29, 19) }, History(U(29, 20), Row("2026-09-29", 50, .5), Row("2026-09-30", 50, 1)), logs, U(29, 20), zone, true);
                Require(r.Days.Count == 2 && r.Tokens == 100 && Math.Abs(r.Cost - 1.5) < .001);
            });
            test("Extended scans widen coverage and invalidate cached hourly totals", () => {
                var old = Index(U(29, 12)); old.CoveredFrom = U(29, 12).AddDays(-31).ToString("o"); LogIndex.Add(old.Files[0], U(29, 10), "openai", "test", 10); old.Entries(); LogIndex.Add(old.Files[0], U(29, 10), "openai", "test", 20);
                var updated = LogReader.Scan(old, new string[0], f => f.Name, U(29, 12), (index, file, stream, offset) => offset);
                Require(updated.Entries().Sum(h => h.Tokens) == 30);
                var longer = LogReader.Scan(updated, new string[0], f => f.Name, U(29, 12), (index, file, stream, offset) => offset, 90);
                Require(longer.Covers(U(29, 12).AddDays(-89)));
            });
        }
    }
}
