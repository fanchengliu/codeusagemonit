using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Scans every supported agent's local history into its hourly index (data/*-logs.json)
    // and derives the 30-day daily history the panel and CLI show.
    public static class UsageScanner {
        public sealed class Agent { public string Id, File; public Func<LogIndex, DateTime, LogIndex> Scan; }
        public static readonly Agent[] Agents = {
            new Agent { Id = "codex", File = "codex-logs.json", Scan = CodexLogs.Scan },
            new Agent { Id = "claude", File = "claude-logs.json", Scan = ClaudeLogs.Scan },
            new Agent { Id = "antigravity", File = "antigravity-logs.json", Scan = AgentLogs.ScanAntigravity },
            new Agent { Id = "zcode", File = "zcode-logs.json", Scan = AgentLogs.ScanZcode },
            new Agent { Id = "grok", File = "grok-logs.json", Scan = AgentLogs.ScanGrok },
            new Agent { Id = "kimi", File = "kimi-logs.json", Scan = AgentLogs.ScanKimi },
            new Agent { Id = "pi", File = "pi-logs.json", Scan = MoreAgentLogs.ScanPi },
            new Agent { Id = "copilot", File = "copilot-logs.json", Scan = MoreAgentLogs.ScanCopilot },
            new Agent { Id = "opencode", File = "opencode-logs.json", Scan = MoreAgentLogs.ScanOpenCode }
        };
        // Scans the given agents in parallel; each index is read from and written to data/.
        public static Dictionary<string, LogIndex> ScanAll(IEnumerable<string> ids, DateTime nowUtc, bool persist) {
            Pricing.EnsureLoaded();
            var wanted = new HashSet<string>(ids);
            var tasks = Agents.Where(a => wanted.Contains(a.Id)).Select(a => Task.Run(() => {
                LogIndex index = null;
                try { index = a.Scan(persist ? Store.Read<LogIndex>(a.File) : null, nowUtc); if (persist) Store.Write(a.File, index); } catch { index = null; }
                return new KeyValuePair<string, LogIndex>(a.Id, index);
            })).ToArray();
            Task.WaitAll(tasks);
            return tasks.Select(t => t.Result).Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value);
        }
        // Reads the saved indexes without scanning (the CLI's offline commands).
        public static Dictionary<string, LogIndex> Load(IEnumerable<string> ids) {
            var result = new Dictionary<string, LogIndex>();
            foreach (Agent a in Agents.Where(a => ids.Contains(a.Id))) { try { LogIndex index = Store.Read<LogIndex>(a.File); if (index != null && index.Version == LogIndex.CurrentVersion) result[a.Id] = index; } catch { } }
            return result;
        }
    }

    public static partial class HistoryService {
        public const string Engine = "native-1";
        // Daily rows per agent in the given time zone, from the hourly indexes. Hours are
        // UTC buckets, so zones with a half-hour offset split a day at the nearest hour.
        public static UsageHistory FromIndexes(IDictionary<string, LogIndex> indexes, TimeZoneInfo zone, DateTime today, int days) {
            var history = new UsageHistory { Updated = DateTime.UtcNow.ToString("o"), Zone = zone.Id, Engine = Engine };
            string oldest = DayKey(today.AddDays(-(days - 1))), newest = DayKey(today);
            foreach (var pair in indexes) {
                var byDay = new Dictionary<string, DayUsage>();
                var models = new Dictionary<string, ModelUsage>();
                foreach (HourUsage hour in pair.Value.Entries()) {
                    string day = DayKey(TimeZoneInfo.ConvertTimeFromUtc(hour.Hour, zone).Date);
                    if (String.CompareOrdinal(day, oldest) < 0 || String.CompareOrdinal(day, newest) > 0) continue;
                    DayUsage row; if (!byDay.TryGetValue(day, out row)) { row = new DayUsage { Day = day, Agent = pair.Key, CostKnown = true }; byDay[day] = row; }
                    row.Cost += hour.Cost; row.Tokens += hour.Tokens; row.CachedTokens += hour.Cached; row.InputTokens += hour.Input; row.OutputTokens += hour.Output; row.CacheCreationTokens += hour.CacheWrite; row.Requests += hour.Requests; row.UnpricedTokens += hour.Unpriced;
                    row.TimedRequests += hour.TimedRequests; row.TimedOutput += hour.TimedOutput; row.TimedSeconds += hour.TimedSeconds;
                    if (hour.Model.Length == 0) continue;
                    ModelUsage model; string key = day + "|" + hour.Model;
                    if (!models.TryGetValue(key, out model)) { model = new ModelUsage { Model = hour.Model }; models[key] = model; row.Models.Add(model); }
                    model.Tokens += hour.Input + hour.Output + hour.Cached + hour.CacheWrite; model.Cost += hour.Cost; model.TimedOutput += hour.TimedOutput; model.TimedSeconds += hour.TimedSeconds;
                }
                foreach (DayUsage row in byDay.Values.OrderBy(r => r.Day, StringComparer.Ordinal)) {
                    if (row.Tokens <= 0 && row.Requests <= 0) continue;
                    row.Models = row.Models.OrderByDescending(m => m.Cost).ThenByDescending(m => m.Tokens).ToList();
                    row.CostKnown = row.Cost > 0 || row.UnpricedTokens < row.Tokens;
                    history.Days.Add(row);
                }
            }
            history.Days = history.Days.OrderBy(d => d.Day, StringComparer.Ordinal).ThenBy(d => d.Agent, StringComparer.Ordinal).ToList();
            return history;
        }
    }
}
