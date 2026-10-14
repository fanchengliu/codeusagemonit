using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Devin Desktop (formerly Windsurf), asked for in GitHub issue #16. Read-only and
    // local, from the app's own sign-in:
    // 1. its running language server, GetUserStatus → userStatus.planStatus: the daily and weekly
    //    quota (remaining percent and reset time) and the plan name;
    // 2. otherwise the plan the app caches in state.vscdb (windsurf.settings.cachedPlanInfo), which it
    //    refreshes only when it starts, so the plan is marked as cached.
    // Devin's web API (app.devin.ai / windsurf.com GetPlanStatus) needs browser session tokens and
    // is not used. Field names follow CodexBar's Windsurf provider (MIT) and are not yet confirmed on
    // Windows.
    internal static class LocalDevin {
        public static string FindApplication() {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] paths = {
                Path.Combine(local, @"Devin\Devin.exe"), Path.Combine(local, @"devin\Devin.exe"), Path.Combine(programs, @"Devin\Devin.exe"),
                Path.Combine(local, @"Windsurf\Windsurf.exe"), Path.Combine(programs, @"Windsurf\Windsurf.exe")
            };
            return paths.FirstOrDefault(File.Exists) ?? "";
        }
        public static IEnumerable<string> StateDatabases() {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new[] { "Devin", "Windsurf", "Windsurf - Next" }.Select(app => Path.Combine(roaming, app, "User", "globalStorage", "state.vscdb")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc);
        }
        public static bool Installed { get { return FindApplication().Length > 0 || StateDatabases().Any(); } }

        public static Task<ProviderState> TryRead(DateTime nowUtc) {
            return Task.Run(() => {
                ProviderState live = TryServer(nowUtc);
                if (live != null) return live;
                bool cacheFound = false; ProviderState cached = null;
                foreach (string db in StateDatabases()) {
                    byte[] value = NativeCredentials.SqliteBytes(db, "SELECT value FROM ItemTable WHERE key LIKE '%settings.cachedPlanInfo' ORDER BY key = 'windsurf.settings.cachedPlanInfo' DESC LIMIT 1");
                    object info = DecodeJson(value); if (info == null) continue;
                    cacheFound = true; cached = ParseCache(info, nowUtc);
                    if (cached.Quotas.Count > 0) return cached;
                }
                if (cacheFound) throw new ProviderException("Devin 本机缓存的额度窗口已经重置过，缓存只在应用启动时更新。打开 Devin Desktop 后刷新。", "error");
                if (StateDatabases().Any()) throw new ProviderException("找到了 Devin 的本机数据，但还没有套餐信息。登录 Devin Desktop 后刷新。", "setup");
                throw new ProviderException(ProviderCatalog.Help("devin"), "setup");
            });
        }
        private static ProviderState TryServer(DateTime nowUtc) {
            var clock = Stopwatch.StartNew();
            try {
                var servers = LocalAntigravity.FindServers((path, command) => {
                    string both = path + " " + command;
                    return both.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) < 0 && (both.IndexOf("devin", StringComparison.OrdinalIgnoreCase) >= 0 || both.IndexOf("windsurf", StringComparison.OrdinalIgnoreCase) >= 0);
                });
                foreach (LocalAntigravity.Server server in servers.Take(2)) foreach (int port in LocalAntigravity.ListenerPorts(server.Pid).Take(3)) foreach (string scheme in new[] { "https", "http" }) {
                    if (clock.ElapsedMilliseconds > 6000) return null;
                    try { ProviderState s = ParseStatus(LocalAntigravity.Query(port, scheme, "GetUserStatus", server.Csrf), nowUtc); if (s.Quotas.Count > 0) return s; } catch { }
                }
            } catch { }
            return null;
        }
        // state.vscdb values may be UTF-8 or UTF-16 (LE) bytes; only a decoding that parses is used.
        public static object DecodeJson(byte[] value) {
            if (value == null) return null;
            foreach (Encoding encoding in new[] { Encoding.UTF8, Encoding.Unicode }) {
                string text = encoding.GetString(value).Trim('\0', ' ', '\r', '\n', '﻿');
                if (!text.StartsWith("{")) continue;
                try { object root = J.Parse(text); if (root is IDictionary<string, object>) return root; } catch { }
            }
            return null;
        }
        // GetUserStatus (proto JSON, camelCase) or GetPlanStatus (snake_case).
        public static ProviderState ParseStatus(object root, DateTime nowUtc) {
            object plan = J.Get(root, "userStatus", "planStatus") ?? J.Get(root, "planStatus") ?? J.Get(root, "plan_status");
            var s = new ProviderState { Id = "devin", Plan = PlanName(Pick(plan, "planInfo", "plan_info"), "planName", "plan_name") };
            Window(s, "每日", Num(plan, "dailyQuotaRemainingPercent", "daily_quota_remaining_percent"), Num(plan, "dailyQuotaResetAtUnix", "daily_quota_reset_at_unix"), 86400, nowUtc);
            Window(s, "每周", Num(plan, "weeklyQuotaRemainingPercent", "weekly_quota_remaining_percent"), Num(plan, "weeklyQuotaResetAtUnix", "weekly_quota_reset_at_unix"), 604800, nowUtc);
            return s;
        }
        // windsurf.settings.cachedPlanInfo: quotaUsage (daily / weekly) on quota plans; older credit
        // plans have only message and flow-action counts for the billing period.
        public static ProviderState ParseCache(object info, DateTime nowUtc) {
            var s = new ProviderState { Id = "devin", Plan = PlanName(info, "planName") };
            s.Plan = (s.Plan.Length > 0 ? s.Plan : "Devin") + "（本机缓存）";
            object quota = J.Get(info, "quotaUsage");
            Window(s, "每日", J.Num(quota, "dailyRemainingPercent"), J.Num(quota, "dailyResetAtUnix"), 86400, nowUtc);
            Window(s, "每周", J.Num(quota, "weeklyRemainingPercent"), J.Num(quota, "weeklyResetAtUnix"), 604800, nowUtc);
            if (s.Quotas.Count > 0) return s;
            double? end = J.Num(info, "endTimestamp");
            // The billing period has ended: its counts are history, not the current allowance.
            if (end.HasValue && LogReader.FromMillis(end.Value) <= nowUtc) return s;
            object usage = J.Get(info, "usage");
            Count(s, "消息", J.Num(usage, "messages"), J.Num(usage, "usedMessages"), J.Num(usage, "remainingMessages"), end);
            Count(s, "Flow 操作", J.Num(usage, "flowActions"), J.Num(usage, "usedFlowActions"), J.Num(usage, "remainingFlowActions"), end);
            return s;
        }
        // A remaining percentage that is absent is not read as 0 or 100 %; a cached window whose
        // reset time has passed says nothing about the current one and is left out.
        private static void Window(ProviderState s, string label, double? remaining, double? resetUnix, double seconds, DateTime nowUtc) {
            if (!remaining.HasValue || remaining < 0 || remaining > 100) return;
            if (resetUnix.HasValue && resetUnix > 0 && LogReader.FromMillis(resetUnix.Value * 1000) <= nowUtc) return;
            Parsers.Add(s, label, 100 - remaining.Value, resetUnix.HasValue && resetUnix > 0 ? (object)resetUnix.Value : null, seconds);
        }
        private static void Count(ProviderState s, string label, double? total, double? used, double? remaining, double? endMillis) {
            if (!total.HasValue || total <= 0) return;
            double? spent = used ?? (remaining.HasValue ? Math.Max(0, total.Value - remaining.Value) : (double?)null);
            if (!spent.HasValue) return;
            Parsers.Add(s, label, Math.Max(0, Math.Min(total.Value, spent.Value)) / total.Value * 100, endMillis.HasValue ? (object)(endMillis.Value / 1000) : null);
        }
        private static object Pick(object obj, params string[] keys) { foreach (string k in keys) { object v = J.Get(obj, k); if (v != null) return v; } return null; }
        private static double? Num(object obj, params string[] keys) { foreach (string k in keys) { double? v = J.Num(obj, k); if (v.HasValue) return v; } return null; }
        private static string PlanName(object obj, params string[] keys) { foreach (string k in keys) { string v = J.Str(obj, k).Trim(); if (v.Length > 0 && v.Length <= 40) return v.StartsWith("Devin", StringComparison.OrdinalIgnoreCase) || v.StartsWith("Windsurf", StringComparison.OrdinalIgnoreCase) ? v : "Devin " + v; } return ""; }
    }
}
