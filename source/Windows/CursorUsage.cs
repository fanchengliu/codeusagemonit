using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodeUsageMonit {
    // Cursor keeps no usage log on disk. Its dashboard lists every model call with token
    // counts and an API-equivalent charge in cents (cursor.com/api/dashboard/
    // get-filtered-usage-events), reached with the same editor session the quota query
    // uses. Calls are folded into the hourly index the log-based agents use
    // (cursor-logs.json), so charts, periods, models and the CLI treat Cursor like any
    // other agent. The events carry no request duration, so Cursor has no output speed.
    public static class CursorUsage {
        private const int PageSize = 100, MaxPages = 200;
        private const string FileName = "cursor-dashboard";

        public static LogIndex Scan(LogIndex index, DateTime nowUtc) {
            LogIndex fresh = LogReader.Fresh(index, nowUtc);
            LogFile file = fresh.Files.FirstOrDefault(f => f.Name == FileName);
            if (file == null) { file = new LogFile { Name = FileName }; fresh.Files.Add(file); }
            // Re-read two hours before the previous fetch: late events and the running hour.
            DateTime horizon = nowUtc.AddDays(-LogReader.HorizonDays), last, from = horizon;
            if (file.Stamp > 0) { last = new DateTime(file.Stamp, DateTimeKind.Utc).AddHours(-2); if (last > from) from = last; }
            try {
                using (var service = new ProviderService(Store.Read<AppConfig>("settings.json"))) {
                    for (int page = 1; page <= MaxPages; page++) {
                        object root = service.CursorDashboard("get-filtered-usage-events", Body(from, nowUtc, page)).GetAwaiter().GetResult();
                        List<object> events = Events(root).ToList();
                        foreach (object e in events) Add(fresh, file, e);
                        double total = J.Num(root, "totalUsageEventsCount") ?? 0;
                        if (events.Count < PageSize || page * PageSize >= total) break;
                        System.Threading.Thread.Sleep(150);
                    }
                }
            } catch {
                // Not signed in, offline or the endpoint changed: keep what was counted before.
                return index ?? fresh;
            }
            file.Stamp = nowUtc.Ticks;
            LogReader.Finish(fresh, nowUtc, null);
            return fresh;
        }
        internal static string Body(DateTime fromUtc, DateTime toUtc, int page) {
            return J.Serializer().Serialize(new Dictionary<string, object> {
                { "teamId", 0 }, { "startDate", LogReader.Millis(fromUtc).ToString(CultureInfo.InvariantCulture) },
                { "endDate", LogReader.Millis(toUtc).ToString(CultureInfo.InvariantCulture) }, { "page", page }, { "pageSize", PageSize } });
        }
        internal static IEnumerable<object> Events(object root) {
            foreach (string key in new[] { "usageEventsDisplay", "usageEvents", "events" }) {
                object list = J.Get(root, key); if (list != null) return J.Arr(list);
            }
            return new object[0];
        }
        // One dashboard event → one request in the hour it happened.
        internal static void Add(LogIndex index, LogFile file, object e) {
            double? ms = J.Num(e, "timestamp"); if (!ms.HasValue || ms.Value <= 0) return;
            DateTime when = LogReader.FromMillis(ms.Value > 1e11 ? ms.Value : ms.Value * 1000);
            object usage = J.Get(e, "tokenUsage");
            double input = LogReader.N(usage, "inputTokens"), output = LogReader.N(usage, "outputTokens");
            double read = LogReader.N(usage, "cacheReadTokens"), write = LogReader.N(usage, "cacheWriteTokens");
            // Request-based calls (kind …INCLUDED_IN_PRO) carry no tokenUsage: still one request.
            string model = J.Str(e, "model").Trim(); if (model.Length == 0 || model == "default") model = "auto";
            string key = "cu|" + LogReader.Millis(when) + "|" + model + "|" + input + "|" + output + "|" + read + "|" + write;
            if (index.Seen.ContainsKey(key)) return;
            LogIndex.See(index, key, when, file);
            // The dashboard's own figure (API list price of the call) wins; otherwise price it.
            double? cents = J.Num(usage, "totalCents");
            var bucket = new Bucket { I = input, C = read, W = write, O = output, R = 1 };
            if (cents.HasValue) bucket.D = cents.Value / 100;
            else {
                ModelPrice price = Pricing.Find(model);
                if (price != null) bucket.D = Pricing.Cost(price, new TokenUse { Input = input, Output = output, CacheRead = read, CacheWrite5m = write });
                else bucket.U = bucket.Tokens();
            }
            LogIndex.Add(file, when, "", model, bucket);
        }
        // Diagnostics (codeusage cursor-fields): the field names the dashboard returns and, over
        // every page of the scan horizon, how many events there are by kind. No values.
        public static string Fields(DateTime nowUtc) {
            using (var service = new ProviderService(Store.Read<AppConfig>("settings.json"))) {
                var keys = new SortedSet<string>(StringComparer.Ordinal); var nested = new SortedSet<string>(StringComparer.Ordinal);
                var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal); var seen = new HashSet<string>();
                int raw = 0, duplicates = 0; double total = 0;
                for (int page = 1; page <= MaxPages; page++) {
                    object root = service.CursorDashboard("get-filtered-usage-events", Body(nowUtc.AddDays(-LogReader.HorizonDays), nowUtc, page)).GetAwaiter().GetResult();
                    total = J.Num(root, "totalUsageEventsCount") ?? 0;
                    List<object> events = Events(root).ToList(); raw += events.Count;
                    foreach (object e in events) {
                        var d = e as IDictionary<string, object>; if (d == null) continue;
                        foreach (var p in d) { keys.Add(p.Key); var inner = p.Value as IDictionary<string, object>; if (inner != null) foreach (string k in inner.Keys) nested.Add(p.Key + "." + k); }
                        string kind = J.Str(e, "kind") + (J.Get(e, "tokenUsage") == null ? " (no tokenUsage)" : "");
                        int n; kinds.TryGetValue(kind, out n); kinds[kind] = n + 1;
                        if (!seen.Add(J.Str(e, "timestamp") + "|" + J.Str(e, "model") + "|" + J.Serializer().Serialize(J.Get(e, "tokenUsage")))) duplicates++;
                    }
                    if (events.Count < PageSize || page * PageSize >= total) break;
                    System.Threading.Thread.Sleep(150);
                }
                return "events (" + LogReader.HorizonDays + " days): " + raw + " fetched of " + total + ", identical repeats " + duplicates +
                    "\nkinds: " + String.Join(", ", kinds.Select(p => p.Key + " " + p.Value)) +
                    "\nevent fields: " + String.Join(", ", keys) + "\nnested: " + String.Join(", ", nested);
            }
        }
    }
}
