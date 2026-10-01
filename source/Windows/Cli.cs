using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // codeusage.exe — the command-line companion. Built from the same sources as the
    // desktop app (build.ps1, /main:CodeUsageMonit.CliProgram) and sharing its data folder,
    // so settings, saved keys and caches are the same.
    public static class CliProgram {
        private const string Version = AppInfo.ShortVersion;
        private static bool color, json;
        private static readonly List<string> only = new List<string>();

        public static int Main(string[] args) {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            var rest = new List<string>(); var raw = new List<string>(); int days = 7; bool all = false, refresh = false;
            color = !Console.IsOutputRedirected && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) && EnableAnsi();
            for (int i = 0; i < args.Length; i++) {
                string a = args[i];
                if (a == "--json") json = true;
                else if (a == "--no-color") color = false;
                else if (a == "--all") all = true;
                else if (a == "--refresh") refresh = true;
                else if ((a == "-p" || a == "--provider") && i + 1 < args.Length) only.AddRange(args[++i].Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0));
                else if (a == "--days" && i + 1 < args.Length) { if (!Int32.TryParse(args[++i], out days) || days < 1 || days > 30) return Fail("--days 应为 1–30"); }
                else if (a == "-h" || a == "--help" || a == "/?") rest.Insert(0, "help");
                else if (a == "-v" || a == "--version") rest.Insert(0, "version");
                else if (a.StartsWith("-")) return Fail("未知参数：" + a + "（codeusage help 查看用法）");
                else { rest.Add(a.ToLowerInvariant()); raw.Add(a); }
            }
            string command = rest.Count > 0 ? rest[0] : "status";
            var config = Store.Read<AppConfig>("settings.json");
            if (config.Enabled == null || config.Enabled.Length == 0) config.Enabled = ProviderCatalog.DefaultEnabled;
            foreach (CustomProvider custom in CustomProviders.Load()) ProviderCatalog.Custom[custom.Id] = custom;
            foreach (string id in only) if (!ProviderCatalog.IsKnown(id)) return Fail("未知平台：" + id + "（codeusage providers 查看全部）");
            List<string> ids = only.Count > 0 ? only.Distinct().ToList() : ProviderCatalog.All.Where(id => all || config.Enabled.Contains(id)).ToList();
            try {
                switch (command) {
                    case "status": return Status(config, ids);
                    case "usage": return Usage(config, ids).GetAwaiter().GetResult();
                    case "cost": return Cost(ids, days, refresh);
                    case "thirdparty": case "third-party": return ThirdParty();
                    case "providers": return Providers(config);
                    case "export": return Export(raw.Count > 1 ? raw[1] : null);
                    case "import": return raw.Count > 1 ? Import(raw[1]) : Fail("用法：codeusage import <文件.sql>");
                    case "backup": return Backup(config);
                    case "sync": return Sync(config);
                    // Diagnostics: field names of Cursor's usage events (no values are printed).
                    case "cursor-fields": Console.WriteLine(CursorUsage.Fields(DateTime.UtcNow)); return 0;
                    case "version": Console.WriteLine("codeusage " + Version + "（codeusagemonit 命令行）"); return 0;
                    case "help": Help(); return 0;
                    default: return Fail("未知命令：" + command + "（codeusage help 查看用法）");
                }
            } catch (Exception e) { return Fail(e is ProviderException || e is ArgumentException ? e.Message : e.GetType().Name + "：" + e.Message); }
        }

        private static void Help() {
            Console.WriteLine(Bold("codeusage") + " " + Version + " — codeusagemonit 的命令行版，与桌面版共用数据目录（设置、密钥、缓存）");
            Console.WriteLine("数据目录：" + Store.Data);
            Console.WriteLine();
            Console.WriteLine("用法：codeusage [命令] [选项]");
            Console.WriteLine();
            Console.WriteLine("命令：");
            Row2("  status", "各平台额度与本机用量（默认；读取桌面版缓存，不联网）");
            Row2("  usage", "实时查询额度（联网，使用与桌面版相同的登录和密钥）");
            Row2("  cost", "按天列出本机 Token 与 API 等价费用");
            Row2("  thirdparty", "第三方 API（中转站）用量");
            Row2("  providers", "平台列表、启用状态与凭据来源");
            Row2("  export [文件]", "把本机（及已导入设备）的用量导出为 SQL");
            Row2("  import <文件>", "导入另一台设备导出的 SQL");
            Row2("  backup", "立即备份设置和用量索引");
            Row2("  sync", "按设置里的 WebDAV 同步各设备的用量");
            Row2("  help | version", "帮助 / 版本");
            Console.WriteLine();
            Console.WriteLine("选项：");
            Row2("  -p, --provider <id>", "只看指定平台，可用逗号分隔，例如 -p codex,claude");
            Row2("  --all", "包括未启用的平台");
            Row2("  --days <n>", "cost 的天数，1–30，默认 7");
            Row2("  --refresh", "cost 先重新扫描本机日志");
            Row2("  --json", "输出 JSON");
            Row2("  --no-color", "关闭颜色（也支持 NO_COLOR 环境变量）");
            Console.WriteLine();
            Console.WriteLine("示例：codeusage · codeusage usage -p codex · codeusage cost --days 30 --json");
        }
        private static void Row2(string left, string right) { Console.WriteLine(Pad(left, 24) + Dim(right)); }

        // ── status / usage ────────────────────────────────────────────────
        private static int Status(AppConfig config, List<string> ids) {
            string cache = Path.Combine(Store.Data, "quota-cache.json");
            var cached = Store.Read<List<ProviderState>>("quota-cache.json").Where(s => s != null && s.Id != null).Select(Parsers.Normalize).ToDictionary(s => s.Id, s => s);
            var states = ids.Select(id => { ProviderState s; return cached.TryGetValue(id, out s) ? s : new ProviderState { Id = id, Status = ProviderCatalog.LocalOnly(id) ? "ready" : "loading", Message = ProviderCatalog.LocalOnly(id) ? "仅本机用量" : "缓存中没有此平台，运行 codeusage usage 实时查询" }; }).ToList();
            string age = File.Exists(cache) ? Ago(File.GetLastWriteTimeUtc(cache)) : "无缓存";
            return Print(states, "缓存 · " + age);
        }
        private static async Task<int> Usage(AppConfig config, List<string> ids) {
            if (!json) Console.Error.WriteLine(Dim("正在查询 " + ids.Count + " 个平台…"));
            List<ProviderState> states;
            using (var service = new ProviderService(config)) states = (await Task.WhenAll(ids.Select(id => service.Fetch(id))).ConfigureAwait(false)).ToList();
            int code = Print(states, "实时");
            return states.Any(s => s.Status == "ready") ? code : 1;
        }
        internal static string ProductLine(ProductConsumption product) {
            return product.DisplayName + " · 已消耗总额度 " + product.UsedPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%（共用当前账期额度）";
        }
        internal static object ProductJson(ProviderState state) {
            return (state.ProductUsage ?? new List<ProductConsumption>()).Select(p => new { product = p.Product, usedPercent = p.UsedPercent }).ToList();
        }
        private static int Print(List<ProviderState> states, string source) {
            foreach (ProviderState state in states) Parsers.Normalize(state);
            UsageHistory history = Store.Read<UsageHistory>("history.json");
            var config = Store.Read<AppConfig>("settings.json");
            if (json) {
                Console.WriteLine(J.Serializer().Serialize(states.Select(s => {
                    var days = history.Days.Where(d => d.Agent == s.Id).ToList();
                    return new {
                        id = s.Id, name = ProviderCatalog.Name(s.Id), status = s.Status, message = s.Status == "ready" ? "" : s.Message, plan = s.Plan, account = Mask(s.Account, config.HideAccounts), updated = s.LastSuccess,
                        windows = s.Quotas.Select(q => new { label = q.Label, remainingPercent = Math.Round(q.Remaining, 2), usedPercent = Math.Round(q.Used, 2), resetsAt = q.ResetUtc, windowSeconds = q.WindowSeconds }),
                        balances = s.Balances.Select(b => new { currency = b.Currency, amount = b.Amount }),
                        productUsage = ProductJson(s),
                        spend = days.Count == 0 ? null : new { todayUsd = Round(days.Where(d => d.Day == Today() && d.CostKnown).Sum(d => d.Cost)), last30Usd = Round(days.Where(d => d.CostKnown).Sum(d => d.Cost)), last30Tokens = days.Sum(d => d.Tokens) }
                    };
                })));
                return 0;
            }
            Pricing.EnsureLoaded();
            Console.WriteLine(Bold("codeusagemonit") + Dim(" V" + Version + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · " + source + (Pricing.Day.Length > 0 ? " · 价目 " + Pricing.Day : "")));
            foreach (ProviderState s in states) {
                Console.WriteLine();
                string name = Paint(ProviderCatalog.Name(s.Id), ProviderCatalog.Color(s.Id), true);
                string plan = s.Plan.Length > 0 && s.Plan != ProviderCatalog.Name(s.Id) ? "  " + Dim(s.Plan) : "";
                string account = Mask(s.Account, config.HideAccounts);
                Console.WriteLine(name + plan + (account.Length > 0 ? "  " + Dim(account) : ""));
                if (s.Status != "ready" && s.Status != "cached" && s.Quotas.Count == 0) Console.WriteLine("  " + Paint(Short(s.Message), "#F2B36B", false));
                int labelWidth = Math.Max(8, s.Quotas.Select(q => Width(q.Label)).DefaultIfEmpty(0).Max() + 2);
                foreach (Quota q in s.Quotas) {
                    string pct = Pad(q.Remaining.ToString("0.#", CultureInfo.InvariantCulture) + "%", 6, true);
                    Console.WriteLine("  " + Pad(q.Label, labelWidth) + Bold(pct) + " 剩余  " + Bar(q.Remaining, ProviderCatalog.Color(s.Id)) + "  " + Dim(Countdown(q.ResetUtc)));
                    PaceInfo pace = UsageDetails.Pace(q, DateTime.UtcNow);
                    if (pace != null) {
                        string keyword = Math.Abs(pace.Reserve) < 1 ? "进度均衡" : pace.Reserve >= 0 ? "余量 " + pace.Reserve.ToString("0") + "%" : "超前消耗 " + (-pace.Reserve).ToString("0") + "%";
                        string tail = pace.Lasts ? " · 按当前速度可持续到重置" : pace.SecondsUntilEmpty.HasValue ? " · 预计 " + Duration(pace.SecondsUntilEmpty.Value) + "后用尽" : "";
                        Console.WriteLine("  " + new string(' ', labelWidth) + Paint(keyword, Math.Abs(pace.Reserve) < 1 ? "#A7ADB7" : pace.Reserve >= 0 ? "#7AD3A8" : "#F2B36B", false) + Dim(tail));
                    }
                }
                foreach (ProductConsumption product in s.ProductUsage) Console.WriteLine("  " + ProductLine(product));
                foreach (Balance b in s.Balances) Console.WriteLine("  " + Pad("可用余额", labelWidth) + Bold(b.Currency + " " + b.Amount.ToString("N2", CultureInfo.InvariantCulture)));
                if (s.ResetCreditsAvailable.HasValue) Console.WriteLine("  " + Pad("限额重置", labelWidth) + s.ResetCreditsAvailable + " 次可用");
                var days = history.Days.Where(d => d.Agent == s.Id).ToList();
                if (days.Count > 0) Console.WriteLine("  " + Dim("今日 " + Money(days.Where(d => d.Day == Today())) + " · 30 天 " + Money(days) + " · " + Compact(days.Sum(d => d.Tokens)) + " Token（API 等价）"));
            }
            return 0;
        }

        // ── cost ──────────────────────────────────────────────────────────
        private static int Cost(List<string> ids, int days, bool refresh) {
            UsageHistory history = Store.Read<UsageHistory>("history.json");
            if (refresh) {
                // Same scan the desktop app runs: every agent's local logs → hourly index → days.
                var indexes = Devices.Attach(UsageScanner.ScanAll(UsageScanner.Agents.Select(a => a.Id), DateTime.UtcNow, true), Devices.Imported());
                UsageHistory next = HistoryService.FromIndexes(indexes, TimeZoneInfo.Local, DateTime.Today, 30);
                history = HistoryService.Merge(history, next, HistoryService.DayKey(DateTime.Today.AddDays(-29)), HistoryService.DayKey(DateTime.Today));
                try { Store.Write("history.json", history); } catch { }
            }
            if (history.Error.Length > 0) return Fail(history.Error);
            var range = Enumerable.Range(0, days).Select(i => HistoryService.DayKey(DateTime.Today.AddDays(i - days + 1))).ToList();
            var rows = history.Days.Where(d => range.Contains(d.Day) && (only.Count > 0 ? only.Contains(d.Agent) : ids.Contains(d.Agent))).ToList();
            List<string> agents = ProviderCatalog.Ids.Where(id => rows.Any(r => r.Agent == id)).ToList();
            if (json) {
                Console.WriteLine(J.Serializer().Serialize(new {
                    days = range.Select(day => new { date = day, providers = rows.Where(r => r.Day == day).Select(r => new { id = r.Agent, costUsd = r.CostKnown ? (double?)Round(r.Cost) : null, tokens = r.Tokens, inputTokens = r.InputTokens, outputTokens = r.OutputTokens, cacheReadTokens = r.CachedTokens, cacheWriteTokens = r.CacheCreationTokens, requests = r.Requests, unpricedTokens = r.UnpricedTokens, outputTokensPerSecond = Tps(r.TimedOutput, r.TimedSeconds), timedRequests = r.TimedRequests, models = r.Models.Select(m => new { model = m.Model, tokens = m.Tokens, costUsd = Round(m.Cost), outputTokensPerSecond = Tps(m.TimedOutput, m.TimedSeconds) }) }) }),
                    outputTokensPerSecond = agents.ToDictionary(id => id, id => Tps(rows.Where(r => r.Agent == id).Sum(r => r.TimedOutput), rows.Where(r => r.Agent == id).Sum(r => r.TimedSeconds))),
                    speedNote = "output tokens (incl. reasoning) / seconds from request sent to last output, per agent; requests with >= 50 output tokens",
                    totalUsd = Round(rows.Where(r => r.CostKnown).Sum(r => r.Cost)), totalTokens = rows.Sum(r => r.Tokens), note = "API-equivalent estimate from local logs, not a subscription bill"
                }));
                return 0;
            }
            Console.WriteLine(Bold("近 " + days + " 天 · 本机用量") + Dim("（本机日志 × 官方 API 价目估算，不是订阅账单）"));
            if (rows.Count == 0) { Console.WriteLine(Dim("这段时间没有本机记录。")); return 0; }
            List<string> columns = agents.Take(5).ToList(); bool other = agents.Count > 5;
            var header = new StringBuilder(Pad("日期", 8));
            foreach (string id in columns) header.Append(Pad(ProviderCatalog.Name(id), 12, true));
            if (other) header.Append(Pad("其他", 12, true));
            header.Append(Pad("合计", 12, true)).Append(Pad("Token", 10, true));
            Console.WriteLine(Dim(header.ToString()));
            foreach (string day in range) {
                var dayRows = rows.Where(r => r.Day == day).ToList();
                var line = new StringBuilder(Pad(day.Substring(5), 8));
                foreach (string id in columns) line.Append(Pad(Cell(dayRows.Where(r => r.Agent == id)), 12, true));
                if (other) line.Append(Pad(Cell(dayRows.Where(r => !columns.Contains(r.Agent))), 12, true));
                line.Append(Bold(Pad(Cell(dayRows), 12, true))).Append(Dim(Pad(dayRows.Count == 0 ? "—" : Compact(dayRows.Sum(r => r.Tokens)), 10, true)));
                Console.WriteLine(line.ToString());
            }
            var total = new StringBuilder(Pad("合计", 8));
            foreach (string id in columns) total.Append(Pad(Cell(rows.Where(r => r.Agent == id)), 12, true));
            if (other) total.Append(Pad(Cell(rows.Where(r => !columns.Contains(r.Agent))), 12, true));
            total.Append(Pad(Cell(rows), 12, true)).Append(Pad(Compact(rows.Sum(r => r.Tokens)), 10, true));
            Console.WriteLine(Bold(total.ToString()));
            // Output speed per agent (never averaged across agents).
            var speeds = agents.Select(id => new { Id = id, Speed = OutputTiming.Speed(rows.Where(r => r.Agent == id).Sum(r => r.TimedOutput), rows.Where(r => r.Agent == id).Sum(r => r.TimedSeconds)) }).Where(x => x.Speed.HasValue).ToList();
            if (speeds.Count > 0) Console.WriteLine(Dim("输出速度  ") + String.Join(Dim(" · "), speeds.Select(x => ProviderCatalog.Name(x.Id) + " " + Bold(OutputTiming.Text(x.Speed)))) + Dim("（发出请求 → 最后一段输出，含首字延迟）"));
            return 0;
        }
        private static double? Tps(double output, double seconds) { double? v = OutputTiming.Speed(output, seconds); return v.HasValue ? (double?)Math.Round(v.Value, 1) : null; }
        private static string Cell(IEnumerable<DayUsage> rows) { var list = rows.ToList(); return list.Count == 0 ? "—" : Money(list); }

        // ── data: export / import / backup / sync (DataSync.cs) ──────────
        private static int Export(string path) {
            DeviceIdentity self = Devices.Self();
            if (String.IsNullOrWhiteSpace(path)) path = "codeusagemonit-" + new String(self.Name.Select(c => Char.IsLetterOrDigit(c) || c == (char)45 ? c : (char)95).ToArray()) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".sql";
            File.WriteAllText(path, Devices.ExportSql(DateTime.UtcNow, true), new UTF8Encoding(false));
            Console.WriteLine("已导出到 " + Path.GetFullPath(path)); return 0;
        }
        private static int Import(string path) {
            if (!File.Exists(path)) return Fail("找不到文件：" + path);
            Console.WriteLine(Devices.ImportSql(File.ReadAllText(path), DateTime.UtcNow).Summary()); return 0;
        }
        private static int Backup(AppConfig config) {
            string path = Backups.Create(config, false, DateTime.UtcNow);
            Console.WriteLine("已备份到 " + path); return 0;
        }
        private static int Sync(AppConfig config) {
            if (String.IsNullOrWhiteSpace(config.WebDavUrl)) return Fail("还没有设置 WebDAV（设置 › 数据 › 云同步）");
            try {
                string message = WebDavSync.Run(config, config.WebDavUrl, config.WebDavUser, Store.SavedKey(WebDavSync.KeyId), DateTime.UtcNow).GetAwaiter().GetResult();
                WebDavSync.Record(true, message, DateTime.UtcNow); Console.WriteLine(message); return 0;
            } catch (Exception e) { string message = e is ProviderException ? e.Message : "同步失败：" + e.Message; WebDavSync.Record(false, message, DateTime.UtcNow); return Fail(message); }
        }

        // ── thirdparty ────────────────────────────────────────────────────
        private static int ThirdParty() {
            var indexes = Devices.Attach(new Dictionary<string, LogIndex> { { "codex", Store.Read<LogIndex>("codex-logs.json") }, { "claude", Store.Read<LogIndex>("claude-logs.json") } }, Devices.Imported());
            ThirdPartySummary report = ThirdPartyReport.Build(indexes["codex"], indexes["claude"], Store.Read<EndpointLog>("endpoints.json"), Store.Read<UsageHistory>("history.json"), DateTime.UtcNow, TimeZoneInfo.Local);
            if (json) {
                Console.WriteLine(J.Serializer().Serialize(new {
                    endpoints = report.Endpoints.Select(e => new { app = e.App, name = e.Title, host = e.Host, current = e.Current, todayTokens = e.Today, weekTokens = e.Week, monthTokens = e.Month, requests = e.Requests, outputTokensPerSecond = Tps(e.TimedOutput, e.TimedSeconds), officialPriceUsd30 = Round(e.CostMonth), lastUsed = e.LastUsed, mainModel = e.MainModel }),
                    claudeUnattributedTokens = report.ClaudeUnattributed, note = "Relays set their own limits; only usage is shown."
                }));
                return 0;
            }
            Console.WriteLine(Bold("第三方 API 用量") + Dim("（本机日志；服务商的周/月限额无法得知）"));
            if (report.Endpoints.Count == 0) { Console.WriteLine(Dim("近 30 天没有经第三方接口的用量。数据由桌面版扫描生成，请先运行一次桌面版。")); return 0; }
            foreach (EndpointUsage e in report.Endpoints) {
                Console.WriteLine();
                Console.WriteLine(Paint(e.Title, ProviderCatalog.Color(e.App), true) + (e.Current ? Paint("  使用中", "#5CC8E0", false) : "") + "  " + Dim((e.App == "claude" ? "Claude Code" : "Codex") + (e.Host.Length > 0 ? " · " + e.Host : "")));
                Console.WriteLine("  今日 " + Bold(Compact(e.Today)) + "  近 7 天 " + Bold(Compact(e.Week)) + "  近 30 天 " + Bold(Compact(e.Month)) + " Token" + Dim("  · " + e.Requests.ToString("N0") + " 次请求" + (e.Speed.HasValue ? " · " + OutputTiming.Text(e.Speed) : "") + " · 官方价参考 ≈$" + e.CostMonth.ToString("N2", CultureInfo.InvariantCulture)));
            }
            return 0;
        }

        // ── providers ─────────────────────────────────────────────────────
        private static int Providers(AppConfig config) {
            var rows = ProviderCatalog.All.Select(id => new { id = id, name = ProviderCatalog.Name(id), enabled = config.Enabled.Contains(id), credential = Credential(id, config) }).ToList();
            if (json) { Console.WriteLine(J.Serializer().Serialize(rows)); return 0; }
            foreach (var row in rows) Console.WriteLine((row.enabled ? Paint("●", "#7AD3A8", false) : Dim("○")) + " " + Pad(row.id, 22) + Pad(row.name, 16) + Dim(row.credential));
            Console.WriteLine(Dim("● 已启用 · 在桌面版 设置 → 显示的平台 中切换"));
            return 0;
        }
        private static string Credential(string id, AppConfig config) {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch (id) {
                case "codex": return File.Exists(Path.Combine(ProviderService.CodexHome(), "auth.json")) ? "Codex 登录（auth.json）" : "未登录 Codex";
                case "claude": return ClaudeLogs.ConfigDirs().Any(d => File.Exists(Path.Combine(d, ".credentials.json"))) ? "Claude Code 登录" : "未登录 Claude Code";
                case "cursor": return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Cursor\User\globalStorage\state.vscdb")) ? "Cursor 编辑器登录" : "未发现 Cursor";
                case "antigravity": return "Antigravity 桌面应用 / 凭据管理器";
                case "grok": return File.Exists(Path.Combine(home, ".grok", "auth.json")) ? "Grok CLI 登录" : "未登录 Grok CLI";
                case "copilot": return Store.HasSavedKey("copilot") ? "GitHub 授权（本程序保存）" : ProviderService.CopilotToken().Length > 0 ? "Copilot 客户端的 GitHub 授权" : "未登录（桌面版 设置 → 使用 GitHub 登录）";
                case "pi": return "不需要（只统计本机日志）";
                default: {
                    string env = Store.KeyEnvironmentName(id, config);
                    return env.Length > 0 ? "环境变量 " + env : Store.HasSavedKey(id) ? "已保存的密钥（DPAPI）" : ProviderCatalog.Custom.ContainsKey(id) && ProviderCatalog.Custom[id].Auth == "none" ? "无需密钥" : "未设置密钥";
                }
            }
        }

        // ── Formatting ────────────────────────────────────────────────────
        // The user's segmented meter in text form: 24 cells, filled = remaining.
        private static string Bar(double remaining, string hex) {
            int filled = (int)Math.Round(Math.Max(0, Math.Min(100, remaining)) / 100 * 24);
            string on = new string('▮', filled), off = new string('▯', 24 - filled);
            return Paint(on, remaining < 10 ? "#E6A083" : hex, false) + Dim(off);
        }
        private static string Paint(string text, string hex, bool bold) {
            if (!color || text.Length == 0) return text;
            int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
            return "\u001b[" + (bold ? "1;" : "") + "38;2;" + r + ";" + g + ";" + b + "m" + text + "\u001b[0m";
        }
        private static string Bold(string text) { return color ? "\u001b[1m" + text + "\u001b[0m" : text; }
        private static string Dim(string text) { return color ? "\u001b[38;2;125;131;141m" + text + "\u001b[0m" : text; }
        // Terminal width: CJK and full-width characters take two columns.
        private static int Width(string text) { return text.Sum(c => (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6) ? 2 : 1); }
        private static string Pad(string text, int width, bool right = false) { int pad = Math.Max(1, width - Width(text)); return right ? new string(' ', pad) + text : text + new string(' ', pad); }
        private static string Mask(string account, bool hide) { if (String.IsNullOrEmpty(account)) return ""; int at = account.IndexOf('@'); return hide && at > 0 ? account.Substring(0, Math.Min(2, at)) + "•••" + account.Substring(at) : account; }
        private static string Short(string text) { return text.Length > 70 ? text.Substring(0, 70) + "…" : text; }
        private static string Today() { return HistoryService.DayKey(DateTime.Today); }
        private static double Round(double value) { return Math.Round(value, 2); }
        private static string Money(IEnumerable<DayUsage> days) { var rows = days.Where(d => d.CostKnown).ToList(); return rows.Count == 0 ? "$0.00" : "$" + rows.Sum(d => d.Cost).ToString("N2", CultureInfo.InvariantCulture); }
        private static string Compact(double n) { var c = CultureInfo.InvariantCulture; return n >= 1e9 ? (n / 1e9).ToString("0.##", c) + "B" : n >= 1e6 ? (n / 1e6).ToString("0.##", c) + "M" : n >= 1e3 ? (n / 1e3).ToString("0.#", c) + "K" : n.ToString("N0", c); }
        private static string Countdown(string iso) { DateTimeOffset date; if (!DateTimeOffset.TryParse(iso, out date)) return "重置时间未知"; var span = date - DateTimeOffset.UtcNow; if (span.TotalSeconds <= 0) return "等待新窗口"; return (span.TotalDays >= 1 ? (int)span.TotalDays + "天" + span.Hours + "小时" : span.TotalHours >= 1 ? (int)span.TotalHours + "小时" + span.Minutes + "分" : Math.Max(1, (int)span.TotalMinutes) + "分钟") + "后重置"; }
        private static string Duration(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalDays >= 1 ? (int)t.TotalDays + "天" + t.Hours + "小时" : t.TotalHours >= 1 ? (int)t.TotalHours + "小时" + t.Minutes + "分" : Math.Max(1, (int)t.TotalMinutes) + "分钟"; }
        private static string Ago(DateTime utc) { double m = (DateTime.UtcNow - utc).TotalMinutes; return m < 1 ? "刚刚" : m < 60 ? (int)m + " 分钟前" : m < 1440 ? (int)(m / 60) + " 小时前" : utc.ToLocalTime().ToString("MM-dd HH:mm"); }
        private static int Fail(string message) { Console.Error.WriteLine(Paint("错误：", "#F08A7E", true) + message); return 2; }
        private static bool EnableAnsi() {
            try { IntPtr handle = GetStdHandle(-11); uint mode; if (!GetConsoleMode(handle, out mode)) return false; return (mode & 4) != 0 || SetConsoleMode(handle, mode | 4); } catch { return false; }
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
