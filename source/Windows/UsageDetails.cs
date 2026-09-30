using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodeUsageMonit {
    public sealed class PaceInfo {
        public double ExpectedUsed, Reserve, SpeedMultiplier;
        public double? SecondsUntilEmpty;
        public bool Lasts;
    }
    public sealed class WindowUsage {
        public DateTime StartUtc, EndUtc;
        public List<DayUsage> Days = new List<DayUsage>();
        // Exact: tokens come from hourly session-log buckets clipped to the window.
        // Otherwise only whole local days inside the window are counted (lower bound).
        public bool Exact, HasCost, CostComplete = true;
        public double Tokens, Cost;
        public string MainModel { get { return UsageDetails.MainModel(Days); } }
    }
    public static class UsageDetails {
        public static string PlanName(string raw) {
            switch ((raw ?? "").Trim().ToLowerInvariant()) {
                case "pro": return "Pro 20x";
                case "prolite": case "pro_lite": case "pro-lite": case "pro lite": return "Pro 5x";
                case "plus": return "Plus";
                case "team": return "Team";
                case "business": return "Business";
                case "enterprise": return "Enterprise";
                case "free": return "Free";
                default: return raw ?? "";
            }
        }
        public static void ParseResetCredits(ProviderState state, object payload, DateTime now) {
            double? count = J.Num(payload, "available_count");
            if (!count.HasValue || count < 0 || count != Math.Floor(count.Value)) return;
            state.ResetCreditsAvailable = (int)count.Value;
            state.ResetCreditExpiries.Clear();
            foreach (object credit in J.Arr(J.Get(payload, "credits"))) {
                string status = J.Str(credit, "status").ToLowerInvariant();
                string expiry = J.Iso(J.Get(credit, "expires_at")); DateTime until;
                if ((status == "available" || status == "granted") && DateTime.TryParse(expiry, null, DateTimeStyles.RoundtripKind, out until) && until.ToUniversalTime() > now.ToUniversalTime()) state.ResetCreditExpiries.Add(expiry);
            }
            state.ResetCreditExpiries.Sort(StringComparer.Ordinal);
            state.ResetCreditsUpdated = now.ToUniversalTime().ToString("o");
            state.ResetCreditsError = "";
        }
        public static PaceInfo Pace(Quota quota, DateTime now) {
            DateTime reset;
            if (quota.WindowSeconds <= 0 || !DateTime.TryParse(quota.ResetUtc, null, DateTimeStyles.RoundtripKind, out reset)) return null;
            double remainingTime = (reset.ToUniversalTime() - now.ToUniversalTime()).TotalSeconds;
            if (remainingTime <= 0 || remainingTime > quota.WindowSeconds) return null;
            double elapsed = quota.WindowSeconds - remainingTime;
            if (elapsed <= 0 && quota.Used > 0) return null;
            double expected = elapsed / quota.WindowSeconds * 100;
            double projected = elapsed > 0 ? quota.Used * remainingTime / elapsed : 0;
            var p = new PaceInfo { ExpectedUsed = expected, Reserve = expected - quota.Used, Lasts = quota.Used == 0 && elapsed > 0, SpeedMultiplier = projected > 0 ? quota.Remaining / projected : 0 };
            if (quota.Used >= 100) p.SecondsUntilEmpty = 0;
            else if (quota.Used > 0 && elapsed > 0) {
                double eta = quota.Remaining * elapsed / quota.Used;
                p.Lasts = eta >= remainingTime;
                if (!p.Lasts) p.SecondsUntilEmpty = eta;
            }
            return p;
        }
        public static string MainModel(IEnumerable<DayUsage> records) {
            var top = records.SelectMany(d => d.Models ?? new List<ModelUsage>()).Where(m => !String.IsNullOrEmpty(m.Model)).GroupBy(m => m.Model).OrderByDescending(g => g.Sum(m => m.Tokens)).FirstOrDefault();
            return top == null ? "" : top.Key;
        }
        public static WindowUsage Window(IEnumerable<DayUsage> source, Quota quota, DateTime now, int previous) { return Window(source, quota, now, previous, null, TimeZoneInfo.Local); }
        // `provider` filters log buckets, e.g. a subscription window counts only Codex's
        // official provider and leaves out third-party endpoints.
        public static WindowUsage Window(IEnumerable<DayUsage> source, Quota quota, DateTime now, int previous, LogIndex logs, TimeZoneInfo zone, Func<string, bool> provider = null) {
            DateTime reset;
            if (quota == null || quota.WindowSeconds <= 0 || !DateTime.TryParse(quota.ResetUtc, null, DateTimeStyles.RoundtripKind, out reset)) return null;
            var result = new WindowUsage { EndUtc = reset.ToUniversalTime().AddSeconds(-quota.WindowSeconds * previous), StartUtc = reset.ToUniversalTime().AddSeconds(-quota.WindowSeconds * (previous + 1)) };
            DateTime nowUtc = now.ToUniversalTime();
            List<DayUsage> rows = source.ToList();
            if (logs != null && logs.Covers(result.StartUtc)) {
                // Hour-accurate tokens and cost from the local session index (each request was
                // priced when it was read, so long-context tiers are exact per request).
                result.Exact = true;
                DateTime until = result.EndUtc < nowUtc ? result.EndUtc : nowUtc;
                Bucket total = logs.Usage(result.StartUtc, until, provider);
                result.Tokens = total.Tokens(); result.Cost = total.D;
                result.HasCost = total.D > 0 || (result.Tokens > 0 && total.U < result.Tokens);
                result.CostComplete = total.U <= 0;
                for (DateTime day = TimeZoneInfo.ConvertTimeFromUtc(result.StartUtc, zone).Date; LocalMidnightUtc(day, zone) < until; day = day.AddDays(1)) {
                    string key = HistoryService.DayKey(day); DayUsage row = rows.FirstOrDefault(d => d.Day == key); if (row != null) result.Days.Add(row);
                }
                return result;
            }
            // Daily rows have no intra-day timestamps. Include only days wholly
            // contained in the window, plus the still-open current day. This gives a
            // conservative recorded subtotal, never a falsely exact hourly estimate.
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
            foreach (DayUsage day in rows) {
                DateTime date;
                if (!DateTime.TryParseExact(day.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                DateTime begin = LocalMidnightUtc(date, zone), end = LocalMidnightUtc(date.AddDays(1), zone);
                bool openToday = date.Date == localNow.Date && result.EndUtc >= nowUtc;
                if (begin >= result.StartUtc && begin < result.EndUtc && begin < nowUtc && (end <= result.EndUtc || openToday)) result.Days.Add(day);
            }
            result.Tokens = result.Days.Sum(d => d.Tokens);
            result.Cost = result.Days.Where(d => d.CostKnown).Sum(d => d.Cost);
            result.HasCost = result.Days.Any(d => d.CostKnown);
            result.CostComplete = result.Days.All(d => d.CostKnown);
            return result;
        }
        private static DateTime LocalMidnightUtc(DateTime date, TimeZoneInfo zone) {
            DateTime local = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            // A DST jump at midnight makes 00:00 invalid in a few zones; use 01:00 then.
            if (zone.IsInvalidTime(local)) local = local.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        public static Quota MainWindow(ProviderState state) {
            return state.Quotas.FirstOrDefault(q => q.Label == "每周" && q.WindowSeconds > 0) ?? state.Quotas.FirstOrDefault(q => q.WindowSeconds > 0);
        }
    }
}
