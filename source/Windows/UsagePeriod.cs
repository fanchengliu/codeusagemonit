using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodeUsageMonit {
    public sealed class UsageRangeChoice {
        public string Preset = "30d", StartUtc = "", EndUtc = "";
        public bool FollowNow = true;
        public UsageRangeChoice Copy() { return new UsageRangeChoice { Preset = Preset, StartUtc = StartUtc, EndUtc = EndUtc, FollowNow = FollowNow }; }
    }
    public sealed class UsagePeriod {
        public DateTime StartUtc, EndUtc;
        public static readonly string[] Presets = { "today", "1d", "7d", "14d", "30d" };
        public static UsagePeriod Resolve(UsageRangeChoice choice, DateTime nowUtc, TimeZoneInfo zone) {
            nowUtc = nowUtc.ToUniversalTime(); DateTime start, end;
            if (choice.Preset != "custom" && Presets.Contains(choice.Preset) && choice.FollowNow) {
                start = choice.Preset == "today" ? LocalUtc(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date, zone) : nowUtc.AddDays(-Int32.Parse(choice.Preset.TrimEnd('d')));
                return new UsagePeriod { StartUtc = start, EndUtc = nowUtc };
            }
            if (!LogIndex.Parse(choice.StartUtc, out start)) throw new ArgumentException("请选择有效的开始日期与时间。");
            if (choice.FollowNow) end = nowUtc;
            else if (!LogIndex.Parse(choice.EndUtc, out end)) throw new ArgumentException("请选择有效的结束日期与时间。");
            if (end <= start) throw new ArgumentException("结束时间必须晚于开始时间。");
            if (start > nowUtc || end > nowUtc.AddMinutes(1)) throw new ArgumentException("不能选择未来的时间。");
            if (start < LocalUtc(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date.AddDays(-366), zone)) throw new ArgumentException("目前支持选择过去 366 天内的本机历史。");
            return new UsagePeriod { StartUtc = start, EndUtc = end > nowUtc ? nowUtc : end };
        }
        public static DateTime LocalUtc(DateTime local, TimeZoneInfo zone) {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) throw new ArgumentException("该时间因夏令时切换不存在，请选择相邻时间。");
            if (zone.IsAmbiguousTime(local)) throw new ArgumentException("该时间在夏令时切换时重复，请选择相邻时间。");
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
    }
    public sealed class PeriodUsage {
        public UsagePeriod Period;
        public List<DayUsage> Days = new List<DayUsage>();
        public bool Hourly, EstimatedTokens, ExcludedPartialDays, CoveragePartial, HasData, HasCost, CostComplete = true;
        public DateTime? AsOf;
        public double Tokens { get { return Days.Sum(d => d.Tokens); } }
        public double Cost { get { return Days.Where(d => d.CostKnown).Sum(d => d.Cost); } }
    }
    public static class PeriodCalculator {
        // Hour buckets use the scanned prefix for the current hour, never extrapolate it
        // to a complete hour. Partial boundaries assume uniform use and are labelled ≈.
        public static PeriodUsage Calculate(string id, UsagePeriod period, UsageHistory history, LogIndex logs, DateTime nowUtc, TimeZoneInfo zone, bool fetchedRange) {
            var result = new PeriodUsage { Period = period };
            var rows = history.Days.Where(d => d.Agent == id).ToList(); DateTime updated;
            bool hourly = logs != null && logs.Files.Count > 0 && logs.Covers(period.StartUtc) && LogIndex.Parse(logs.Updated, out updated) && updated > period.StartUtc;
            if (hourly) {
                LogIndex.Parse(logs.Updated, out updated); result.Hourly = true; result.HasData = true; result.AsOf = updated;
                var grouped = new Dictionary<string, DayUsage>();
                foreach (HourUsage hour in logs.Entries()) {
                    DateTime observedEnd = Min(hour.Hour.AddHours(1), Min(updated, nowUtc));
                    DateTime a = Max(hour.Hour, period.StartUtc), b = Min(observedEnd, period.EndUtc);
                    if (b <= a || observedEnd <= hour.Hour) continue;
                    // Split at local midnight as well (time zones can have fractional offsets).
                    for (DateTime part = a; part < b;) {
                        DateTime localDay = TimeZoneInfo.ConvertTimeFromUtc(part, zone).Date;
                        DateTime nextMidnight = Midnight(localDay.AddDays(1), zone), until = Min(nextMidnight, b);
                        double weight = (until - part).TotalSeconds / (observedEnd - hour.Hour).TotalSeconds;
                        if (a > hour.Hour || b < observedEnd) result.EstimatedTokens = true;
                        string key = HistoryService.DayKey(localDay); DayUsage day;
                        if (!grouped.TryGetValue(key, out day)) { day = new DayUsage { Agent = id, Day = key, CostKnown = true }; grouped[key] = day; }
                        double tokens = hour.Tokens * weight; day.Tokens += tokens;
                        var source = rows.Where(d => d.Day == key).ToList(); double sourceTokens = source.Sum(d => d.Tokens);
                        if (source.Count > 0 && source.All(d => d.CostKnown) && sourceTokens > 0) { day.Cost += tokens * source.Sum(d => d.Cost) / sourceTokens; result.HasCost = true; }
                        else if (tokens > 0) { day.CostKnown = false; result.CostComplete = false; }
                        if (!String.IsNullOrEmpty(hour.Model)) day.Models.Add(new ModelUsage { Model = hour.Model, Tokens = tokens });
                        part = until;
                    }
                }
                result.Days = grouped.Values.OrderBy(d => d.Day).ToList();
                if (result.Tokens == 0) result.HasCost = true;
            } else {
                DateTime stamp; bool stamped = LogIndex.Parse(history.Updated, out stamp); result.AsOf = stamped ? (DateTime?)stamp : null;
                result.HasData = stamped && rows.Count > 0;
                DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
                foreach (DayUsage row in rows) {
                    DateTime date; if (!DateTime.TryParseExact(row.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                    DateTime begin = Midnight(date, zone), end = Midnight(date.AddDays(1), zone);
                    DateTime observedEnd = date == localNow.Date ? Min(end, stamped ? Min(nowUtc, stamp) : nowUtc) : end;
                    if (begin >= period.EndUtc || observedEnd <= period.StartUtc) continue;
                    if (begin < period.StartUtc || observedEnd > period.EndUtc) { result.ExcludedPartialDays = true; continue; }
                    result.Days.Add(row);
                }
                result.HasCost = result.Days.Any(d => d.CostKnown) || (result.HasData && result.Days.Count == 0 && !result.ExcludedPartialDays);
                result.CostComplete = result.Days.All(d => d.CostKnown);
                if (!fetchedRange && period.StartUtc < Midnight(localNow.Date.AddDays(-29), zone)) result.CoveragePartial = true;
            }
            return result;
        }
        public static PeriodUsage Combine(UsagePeriod range, IEnumerable<PeriodUsage> parts) {
            var all = parts.ToList(); return new PeriodUsage { Period = range, Days = all.SelectMany(p => p.Days).ToList(), HasData = all.Any(p => p.HasData), HasCost = all.Any(p => p.HasCost), CostComplete = all.All(p => p.CostComplete), Hourly = all.Count > 0 && all.All(p => p.Hourly), EstimatedTokens = all.Any(p => p.EstimatedTokens), ExcludedPartialDays = all.Any(p => p.ExcludedPartialDays), CoveragePartial = all.Any(p => p.CoveragePartial), AsOf = all.Where(p => p.AsOf.HasValue).Select(p => p.AsOf).DefaultIfEmpty(null).Min() };
        }
        private static DateTime Midnight(DateTime day, TimeZoneInfo zone) { var local = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified); if (zone.IsInvalidTime(local)) local = local.AddHours(1); return TimeZoneInfo.ConvertTimeToUtc(local, zone); }
        private static DateTime Min(DateTime a, DateTime b) { return a < b ? a : b; }
        private static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }
    }
}
