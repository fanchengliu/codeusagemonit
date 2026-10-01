using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // How often each platform is read. By default every platform follows the global interval
    // (设置 › 高级 › 自动刷新); a platform can have its own, and one whose client is writing
    // its local session logs right now ("in use", e.g. a Claude Code conversation) is read
    // more often, quotas and local usage alike. A 30-second tick reads whatever is due.
    public static class Pacing {
        public static readonly TimeSpan InUseWindow = TimeSpan.FromMinutes(10);
        public static readonly int[] OwnChoices = { 0, 1, 2, 3, 5, 10, 15, 30, 60 };   // 0 = follow the global interval
        public static readonly int[] InUseChoices = { 1, 2, 3, 5 };

        // Minutes between reads of one platform.
        public static int Minutes(AppConfig config, string id, bool inUse) {
            int own; int minutes = config.ProviderMinutes != null && config.ProviderMinutes.TryGetValue(id, out own) && own > 0 ? own : config.RefreshMinutes;
            if (config.FastInUse && inUse) minutes = Math.Min(minutes, Math.Max(1, config.InUseMinutes));
            return Math.Max(1, minutes);
        }
        public static bool Due(DateTime lastUtc, int minutes, DateTime nowUtc) {
            // A little early rather than a whole tick late.
            return lastUtc == DateTime.MinValue || nowUtc - lastUtc >= TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(20);
        }
        // Folders whose files change while a platform's client is working.
        public static IEnumerable<string> ActivityFolders(string id) {
            string home = LogReader.Home;
            Func<string, string, string> env = (variable, fallback) => { string v = Environment.GetEnvironmentVariable(variable); return String.IsNullOrWhiteSpace(v) ? fallback : v.Trim(); };
            switch (id) {
                case "codex": return new[] { Path.Combine(ProviderService.CodexHome(), "sessions") };
                case "claude": return ClaudeLogs.ConfigDirs().Select(d => Path.Combine(d, "projects"));
                case "cursor": return new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage") };
                case "antigravity": return new[] { Path.Combine(home, ".gemini") };
                case "deepseek": return new[] { Path.Combine(HarnessLogs.Root, "sessions") };
                case "grok": return new[] { Path.Combine(env("GROK_HOME", Path.Combine(home, ".grok")), "sessions") };
                case "kimi": return new[] { Path.Combine(home, ".kimi"), Path.Combine(home, ".kimi-code") };
                case "zcode": return new[] { env("ZCODE_HOME", Path.Combine(home, ".zcode")) };
                case "opencode": return new[] { Path.Combine(home, ".local", "share", "opencode") };
                case "pi": return new[] { Path.Combine(home, ".pi", "agent", "sessions") };
                case "copilot": return new[] { env("COPILOT_HOME", Path.Combine(home, ".copilot")) };
                default: return new string[0];
            }
        }
        // Whether a changed file in a watched folder means work (not, say, a config file).
        public static bool CountsAsWork(string id, string path) {
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (id == "cursor") return name.StartsWith("state.vscdb", StringComparison.Ordinal);
            // Antigravity: its conversation databases, not the app's caches and logs.
            if (id == "antigravity") return path.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0 && path.IndexOf("conversations", StringComparison.OrdinalIgnoreCase) >= 0;
            return name.EndsWith(".jsonl") || name.Contains(".jsonl.") || name.EndsWith(".db") || name.EndsWith(".sqlite") || name.EndsWith(".json") || name.Contains("-wal");
        }
    }

    public sealed partial class MonitorPanel {
        private readonly Dictionary<string, DateTime> lastFetched = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> lastActivity = new Dictionary<string, DateTime>();
        private DateTime lastQuickScan = DateTime.MinValue;
        private bool pacing;

        private bool InUse(string id) { lock (lastActivity) { DateTime at; return lastActivity.TryGetValue(id, out at) && DateTime.UtcNow - at < Pacing.InUseWindow; } }
        private List<string> InUseIds() { return EnabledIds().Where(InUse).ToList(); }
        private void Fetched(string id) { lastFetched[id] = DateTime.UtcNow; }

        // One watcher per platform folder; a write marks the platform as in use.
        private void WatchActivity() {
            foreach (string id in ProviderCatalog.Ids) {
                foreach (string folder in Pacing.ActivityFolders(id)) {
                    try {
                        if (!Directory.Exists(folder)) continue;
                        string captured = id;
                        var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, InternalBufferSize = 16384 };
                        FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e) {
                            if (!Pacing.CountsAsWork(captured, e.FullPath)) return;
                            lock (lastActivity) lastActivity[captured] = DateTime.UtcNow;
                        };
                        watcher.Changed += changed; watcher.Created += changed;
                        watcher.EnableRaisingEvents = true; watchers.Add(watcher);
                    } catch { }
                }
            }
        }
        // The 30-second tick: read the platforms that are due, quietly (no spinner), and rescan
        // local usage more often while something is in use.
        private async Task RefreshDue() {
            if (demo || pacing || refreshing) return;
            pacing = true;
            try {
                DateTime now = DateTime.UtcNow;
                List<string> due = config.Enabled.Where(ProviderCatalog.IsKnown).Where(id => {
                    DateTime last; if (!lastFetched.TryGetValue(id, out last)) last = DateTime.MinValue;
                    return Pacing.Due(last, Pacing.Minutes(config, id, InUse(id)), now);
                }).ToList();
                if (due.Count > 0) {
                    using (var service = new ProviderService(config)) {
                        var jobs = due.Select(async id => {
                            Fetched(id); int serial = BeginFetch(id);
                            try { ProviderState result = await Task.Run(() => service.Fetch(id)); if (Latest(id, serial)) Accept(id, result); } catch { }
                        }).ToArray();
                        await Task.WhenAll(jobs);
                    }
                    lastRefresh = DateTime.Now;
                    try { Store.Write("quota-cache.json", states.Values.ToList()); } catch { }
                    UpdateTray(); Render();
                }
                List<string> busy = InUseIds();
                bool quick = config.FastInUse && busy.Count > 0 && now - lastQuickScan >= TimeSpan.FromMinutes(Math.Max(1, config.InUseMinutes));
                DateTime stamp; bool stale = !DateTime.TryParse(history.Updated, out stamp) || now - stamp.ToUniversalTime() > TimeSpan.FromMinutes(15);
                if (!scanning && (quick || (due.Count > 0 && stale))) { lastQuickScan = now; await ScanHistory(); }
            } finally { pacing = false; }
        }
    }
}
