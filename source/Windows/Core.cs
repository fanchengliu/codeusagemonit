// Windows provider adapters, written for this project after studying how CodexBar (MIT)
// and CodeZeno/Claude-Code-Usage-Monitor (MIT) talk to each provider; no code is shared.
// See THIRD-PARTY-NOTICES.md. Credentials remain in their owning applications.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodeUsageMonit {
    public sealed class Quota {
        public string Label, ResetUtc;
        public double WindowSeconds;
        public double Used;
        public double Remaining { get { return Math.Max(0, 100 - Used); } }
    }
    public sealed class Balance { public string Currency; public double Amount; }
    public sealed class ProductConsumption {
        public string Product;
        // Percentage points taken from the shared allowance, not a quota of its own.
        public double UsedPercent;
        [ScriptIgnore]
        public string DisplayName {
            get {
                if (String.IsNullOrEmpty(Product)) return "";
                if (Product.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0) return "Build";
                if (Product.IndexOf("chat", StringComparison.OrdinalIgnoreCase) >= 0) return "Chat";
                return Product;
            }
        }
    }
    public sealed class ProviderState {
        public string Id, Status = "loading", Message = "等待首次读取", Plan = "", Account = "", LastSuccess = "", LastAttempt = "";
        public List<Quota> Quotas = new List<Quota>();
        public List<Balance> Balances = new List<Balance>();
        public List<ProductConsumption> ProductUsage = new List<ProductConsumption>();
        public bool Stale;
        public int? ResetCreditsAvailable;
        public List<string> ResetCreditExpiries = new List<string>();
        public string ResetCreditsUpdated = "", ResetCreditsError = "";
    }
    public sealed class ModelUsage { public string Model; public double Tokens, Cost, TimedOutput, TimedSeconds; }
    public sealed class DayUsage {
        public string Day, Agent;
        // Tokens = fresh input + output + cache read + cache write (+ other counted tokens).
        // CachedTokens is the cache-read subset; InputTokens excludes cached input.
        public double Cost, Tokens, CachedTokens, InputTokens, OutputTokens, CacheCreationTokens, Requests, UnpricedTokens;
        // Requests with a measured duration: output tokens / seconds = output speed (OutputTiming).
        public double TimedRequests, TimedOutput, TimedSeconds;
        public bool CostKnown = true;
        public List<ModelUsage> Models = new List<ModelUsage>();
    }
    public sealed class UsageHistory {
        public string Updated = "", Error = "", Zone = "", Engine = "";
        public List<DayUsage> Days = new List<DayUsage>();
    }
    public sealed class AppConfig {
        public string Proxy = "auto";
        public int RefreshMinutes = 5;
        public bool PinWindow = false;
        public bool HideAccounts = true;
        public string[] Enabled = ProviderCatalog.DefaultEnabled;
        public int UiVersion;
        public bool AlwaysOnTop = false;
        public bool HideOnDeactivate = false;
        public double? WindowLeft, WindowTop;
        public double WindowWidth = 420, WindowHeight = 790, UiScale = 1;
        public string Material = "acrylic";
        public bool ShowThirdParty = true;
        // Fetch the repository's pricing.json once a day (Pricing.SyncAsync).
        public bool PriceSync = true;
        public string KimiRegion = "china", ZaiRegion = "china";
        // Alpha of the smoke layer over Acrylic/Mica (0–1). Settings show it as
        // transparency = 1 − alpha, so 0% transparency is fully opaque.
        public double SurfaceOpacity = .66;
        // Colour of that layer (#RRGGBB); text stays light, so dark colours read best.
        public string SurfaceColor = "#15171B";
        // Optional background picture: a file in data/ (background.*), how it fills the
        // window (fill | fit | tile) and how much it is dimmed for legibility (0–0.85).
        public string BackgroundImage = "", BackgroundFit = "fill";
        public double BackgroundDim = .35;
        // One window, four sizes: "small" | "medium" | "large" | "full" (the panel).
        public string DisplaySize = "full";
        // Page shown by the compact sizes: "overview" or a provider id.
        public string CompactProvider = "overview";
        // Position of the compact sizes before each size kept its own geometry (read once).
        public double? CompactLeft, CompactTop;
        // Size and position of each compact size; the full panel uses WindowLeft/Top/Width/Height.
        public Dictionary<string, WindowGeometry> Layouts = new Dictionary<string, WindowGeometry>();
        // Interface language: "auto" (follows Windows) | "zh" | "en".
        public string Language = "auto";
        // Automatic backups (Backups in DataSync.cs): every BackupHours (0 = off), keeping the
        // newest BackupKeep automatic ones, in BackupFolder (empty = data\backups).
        public int BackupHours = 24, BackupKeep = 7;
        public string BackupFolder = "";
        // WebDAV sync of usage between devices: folder URL, user name (the password is a
        // DPAPI key, "webdav"), and how often to sync automatically (0 = only on demand).
        public string WebDavUrl = "", WebDavUser = "";
        public int SyncMinutes = 0;
        // Look for a newer release on GitHub once a day.
        public bool UpdateCheck = true;
    }
    public sealed class WindowGeometry { public double Width, Height; public double? Left, Top; }
    public static class AppInfo {
        public const string ShortVersion = "1.2";
        public const string UserAgent = "codeusagemonit/" + ShortVersion;
        // Full version from the assembly ("1.3.0").
        public static string Version { get { System.Version v = typeof(AppInfo).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); } }
    }
    public static class ProviderCatalog {
        public static readonly string[] Ids = { "codex", "claude", "cursor", "antigravity", "deepseek", "grok", "copilot", "kimi", "opencode", "zcode", "pi" };
        // New installs start with the original six; the rest are opt-in in Settings.
        public static readonly string[] DefaultEnabled = { "codex", "claude", "cursor", "antigravity", "deepseek", "grok" };
        // Not a quota provider: a page of usage that went through third-party endpoints.
        public const string ThirdParty = "thirdparty";
        // User-defined HTTP JSON providers (CustomProviders.cs), keyed by "custom-…" id.
        public static readonly Dictionary<string, CustomProvider> Custom = new Dictionary<string, CustomProvider>();
        public static IEnumerable<string> All { get { return Ids.Concat(Custom.Keys.OrderBy(k => k, StringComparer.Ordinal)); } }
        public static bool IsKnown(string id) { return Ids.Contains(id) || Custom.ContainsKey(id); }
        // Providers without an account quota: only local session logs.
        public static bool LocalOnly(string id) { return id == "pi"; }
        // Usage read from the account (Cursor's dashboard) rather than this machine's logs:
        // it covers every device signed in to the account.
        public static bool AccountUsage(string id) { return id == "cursor"; }
        // The local client a provider's usage is read from, when it is not the obvious one.
        public static string UsageSource(string id) { return id == "deepseek" ? "DeepSeek Harness" : null; }
        public static string UsageTitle(string id) { return AccountUsage(id) ? "账户用量" : "本机用量"; }
        public static string Name(string id) {
            CustomProvider custom; if (id != null && Custom.TryGetValue(id, out custom)) return custom.Name;
            switch (id) { case "codex": return "Codex"; case "claude": return "Claude"; case "cursor": return "Cursor"; case "antigravity": return "Antigravity"; case "deepseek": return "DeepSeek"; case "grok": return "Grok"; case "copilot": return "Copilot"; case "kimi": return "Kimi"; case "opencode": return "OpenCode"; case "zcode": return "ZCode"; case "pi": return "Pi"; case ThirdParty: return "第三方"; default: return "概览"; }
        }
        public static string Color(string id) {
            CustomProvider custom; if (id != null && Custom.TryGetValue(id, out custom)) return custom.Color.Length > 0 ? custom.Color : "#A9B4C6";
            switch (id) { case "codex": return "#63D5E5"; case "claude": return "#E8AB8C"; case "cursor": return "#80DDAB"; case "antigravity": return "#B8A0F5"; case "deepseek": return "#7FA9FF"; case "grok": return "#D8DEE9"; case "copilot": return "#E58FD0"; case "kimi": return "#F4C95D"; case "opencode": return "#A6D96A"; case "zcode": return "#F2874E"; case "pi": return "#A9B4C6"; case ThirdParty: return "#9DB4CF"; default: return "#8CB9FF"; }
        }
        public static string Help(string id) {
            if (id != null && Custom.ContainsKey(id)) return "检查接口地址、字段路径和密钥（设置 → 自定义平台），然后刷新。";
            switch (id) {
                case "codex": return "使用 Codex 应用或 CLI 登录后，点击刷新。";
                case "claude": return "使用 Claude Code 登录后，点击刷新。";
                case "cursor": return "使用 Cursor 编辑器登录后，点击刷新。";
                case "antigravity": return "打开 Antigravity 并完成登录，保持应用运行后刷新。也可使用已登录的 agy CLI。";
                case "deepseek": return "填写 DeepSeek API Key 即可查询 API 账户余额（卡片或设置中都可以填写），也可设置环境变量 DEEPSEEK_API_KEY。";
                case "grok": return "使用 Grok Build CLI 执行 grok login，完成登录后刷新。";
                case "copilot": return "使用 GitHub 设备码登录：在浏览器输入一次性代码授权，完成后自动连接。";
                case "kimi": return "填写 Kimi Code API Key（在 kimi.com/code/console 创建），或设置环境变量 KIMI_CODE_API_KEY。";
                case "opencode": return "OpenCode Go 订阅：填写 OpenCode API Key，或设置环境变量 OPENCODE_API_KEY。";
                case "zcode": return "填写智谱 / Z.ai 的 API Key（GLM 编码套餐），或设置环境变量 Z_AI_API_KEY。";
                case "pi": return "Pi 没有账户额度，这里只统计本机日志中的用量。";
                default: return "";
            }
        }
        // Environment variables checked before the key saved in Settings.
        public static string[] KeyEnv(string id, AppConfig config) {
            switch (id) {
                case "deepseek": return new[] { "DEEPSEEK_API_KEY" };
                case "kimi": return new[] { "KIMI_CODE_API_KEY" };
                case "opencode": return new[] { "OPENCODE_API_KEY" };
                case "zcode": return config != null && config.ZaiRegion == "global" ? new[] { "Z_AI_API_KEY" } : new[] { "Z_AI_API_KEY", "BIGMODEL_API_KEY", "ZHIPU_API_KEY", "ZHIPUAI_API_KEY", "GLM_API_KEY" };
                default: return new string[0];
            }
        }
    }
    public static class J {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 }; }
        public static object Parse(string text) { return Serializer().DeserializeObject(text); }
        public static object Get(object obj, params string[] keys) {
            foreach (string key in keys) { var d = obj as IDictionary<string, object>; if (d == null || !d.TryGetValue(key, out obj)) return null; }
            return obj;
        }
        public static string Str(object obj, params string[] keys) { object v = keys.Length == 0 ? obj : Get(obj, keys); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double? Num(object obj, params string[] keys) {
            object v = keys.Length == 0 ? obj : Get(obj, keys); if (v == null || v is bool) return null;
            double n; return Double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) ? (double?)n : null;
        }
        public static IEnumerable<object> Arr(object obj) { var a = obj as IEnumerable; if (a == null || obj is string || obj is IDictionary) yield break; foreach (object v in a) yield return v; }
        public static Dictionary<string, object> Dict(object obj) { return obj as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object File(string path) { if (!System.IO.File.Exists(path)) return null; return Parse(System.IO.File.ReadAllText(path)); }
        public static string Iso(object value) {
            double? seconds = Num(value);
            if (seconds.HasValue && seconds > 100000000) {
                // Some endpoints return epoch milliseconds; treat 12+ digit values as ms.
                double s = seconds.Value > 100000000000 ? seconds.Value / 1000 : seconds.Value;
                try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToString("o"); } catch { return ""; }
            }
            DateTimeOffset date; return DateTimeOffset.TryParse(Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date) ? date.UtcDateTime.ToString("o") : "";
        }
        public static object Jwt(string token) { try { string[] p = token.Split('.'); if (p.Length < 2) return null; string s = p[1].Replace('-', '+').Replace('_', '/'); s += new string('=', (4 - s.Length % 4) % 4); return Parse(Encoding.UTF8.GetString(Convert.FromBase64String(s))); } catch { return null; } }
    }
    public static class Store {
        // Keep in sync with source/Windows/installer/codeusagemonit.iss.
        public const string InstallRegistryKey = @"Software\codeusagemonit";
        public const string InstallRegistryValue = "InstallPath";
        public static readonly string Root = NormalizeDirectory(AppDomain.CurrentDomain.BaseDirectory);
        // Chosen on first read. EnableDemoMode assigns this before anything reads it, so
        // --demo never migrates or opens the real data directory.
        private static string dataPath;
        public static string Data {
            get {
                if (dataPath == null) dataPath = ChooseDataFor(Root, ReadInstallPath(), Environment.GetEnvironmentVariable("CODEUSAGEMONIT_DATA"), UserDataDirectory());
                return dataPath;
            }
            set { dataPath = value; }
        }
        public static void EnableDemoMode() { Data = Path.Combine(Root, "verification", "demo-data"); }
        // Self-tests point the data directory at a temporary folder and back, without choosing
        // (or migrating) the real one.
        internal static string SwapData(string path) { string old = dataPath; dataPath = path; return old; }
        // Zip, Scoop and install.ps1 keep using <exe>\data. setup.exe writes installed.txt
        // (and HKCU\Software\codeusagemonit\InstallPath); those copies use
        // %LOCALAPPDATA%\codeusagemonit so Program Files can stay read-only.
        // portable.txt next to the exe forces <exe>\data even for an installed copy.
        // CODEUSAGEMONIT_DATA overrides the directory unless portable.txt is present.
        public static string UserDataDirectory() {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "codeusagemonit");
        }
        public static string ResolveData(string root, string installPath, string envData, bool installedMarker, string userData) {
            string beside = Path.Combine(root ?? "", "data");
            try { if (File.Exists(Path.Combine(root ?? "", "portable.txt"))) return beside; } catch { }
            if (!String.IsNullOrWhiteSpace(envData)) { try { return Path.GetFullPath(envData.Trim()); } catch { } }
            if (installedMarker || PathsEqual(installPath, root)) return String.IsNullOrEmpty(userData) ? beside : userData;
            return beside;
        }
        public static string ChooseDataFor(string root, string installPath, string envData, string userData) {
            bool marker = false;
            try { marker = File.Exists(Path.Combine(root, "installed.txt")); } catch { }
            string chosen = ResolveData(root, installPath, envData, marker, userData);
            if (!String.IsNullOrEmpty(userData) && PathsEqual(chosen, userData)) TryMigratePortableData(Path.Combine(root, "data"), chosen);
            return chosen;
        }
        public static string ReadInstallPath() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InstallRegistryKey)) {
                    if (key == null) return "";
                    object value = key.GetValue(InstallRegistryValue);
                    return value == null ? "" : Convert.ToString(value);
                }
            } catch { return ""; }
        }
        public static bool PathsEqual(string a, string b) {
            if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b)) return false;
            try { return String.Equals(NormalizeDirectory(a), NormalizeDirectory(b), StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        // First launch of an installed copy: if the per-user folder is still empty and a
        // real data directory sits beside the exe (zip or the PowerShell installer), copy it
        // across and remove the old folder. A junction (Scoop's persist link) is copied and left in place.
        public static bool TryMigratePortableData(string from, string to) {
            string staging = null;
            try {
                if (String.IsNullOrEmpty(from) || String.IsNullOrEmpty(to) || PathsEqual(from, to)) return false;
                if (IsNested(from, to) || IsNested(to, from)) return false;
                if (!Directory.Exists(from)) return false;
                DirectoryInfo source = new DirectoryInfo(from);
                bool link = (source.Attributes & FileAttributes.ReparsePoint) != 0;
                if (Directory.GetFileSystemEntries(from).Length == 0) return false;
                if (Directory.Exists(to) && Directory.GetFileSystemEntries(to).Length > 0) return false;
                string parent = Path.GetDirectoryName(NormalizeDirectory(to));
                if (String.IsNullOrEmpty(parent)) return false;
                Directory.CreateDirectory(parent);
                staging = Path.Combine(parent, Path.GetFileName(NormalizeDirectory(to).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".migrating");
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                CopyAll(from, staging);
                if (Directory.Exists(to)) Directory.Delete(to, true);
                Directory.Move(staging, to);
                staging = null;
                if (!link) { try { Directory.Delete(from, true); } catch { } }
                return true;
            } catch {
                if (staging != null && Directory.Exists(staging)) { try { Directory.Delete(staging, true); } catch { } }
                return false;
            }
        }
        private static bool IsNested(string parent, string child) {
            try {
                string p = NormalizeDirectory(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string c = NormalizeDirectory(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return c.StartsWith(p, StringComparison.OrdinalIgnoreCase) && !PathsEqual(parent, child);
            } catch { return false; }
        }
        private static void CopyAll(string from, string to) {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
            foreach (string dir in Directory.GetDirectories(from)) {
                DirectoryInfo info = new DirectoryInfo(dir);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                CopyAll(dir, Path.Combine(to, info.Name));
            }
        }
        public static string NormalizeDirectory(string path) {
            if (String.IsNullOrEmpty(path)) return "";
            try { path = Path.GetFullPath(path); } catch { }
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 2 && trimmed[1] == ':') return trimmed + Path.DirectorySeparatorChar;
            return trimmed.Length == 0 ? path : trimmed;
        }
        public static void Write(string name, object value) {
            Directory.CreateDirectory(Data); string path = Path.Combine(Data, name); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, J.Serializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static T Read<T>(string name) where T : new() { try { string path = Path.Combine(Data, name); return File.Exists(path) ? J.Serializer().Deserialize<T>(File.ReadAllText(path)) : new T(); } catch { return new T(); } }
        // Secrets live in data/<id>.key, encrypted with DPAPI for the current Windows user.
        // Environment variables (ProviderCatalog.KeyEnv) take precedence over saved keys.
        public static string ProviderKey(string id, AppConfig config = null) {
            foreach (string name in ProviderCatalog.KeyEnv(id, config)) { string fromEnv = Environment.GetEnvironmentVariable(name); if (!String.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim(); }
            return SavedKey(id);
        }
        public static string SavedKey(string id) {
            try { string file = KeyPath(id); if (!File.Exists(file)) return ""; return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)); } catch { return ""; }
        }
        public static bool HasSavedKey(string id) { return File.Exists(KeyPath(id)); }
        public static string KeyEnvironmentName(string id, AppConfig config) { return ProviderCatalog.KeyEnv(id, config).FirstOrDefault(n => !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n))) ?? ""; }
        public static void SetProviderKey(string id, string key) {
            Directory.CreateDirectory(Data); string path = KeyPath(id);
            if (String.IsNullOrWhiteSpace(key)) { if (File.Exists(path)) File.Delete(path); return; }
            File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
        }
        private static string KeyPath(string id) {
            if (String.IsNullOrEmpty(id) || id.Any(c => !(Char.IsLetterOrDigit(c) && c < 128) && c != '-')) throw new ArgumentException("invalid key id");
            return Path.Combine(Data, id + ".key");
        }
        public static string DeepSeekKey() { return ProviderKey("deepseek"); }
        public static void SetDeepSeekKey(string key) { SetProviderKey("deepseek", key); }
    }
    public sealed class ProviderException : Exception {
        public string State;
        public ProviderException(string message, string state) : base(message) { State = state; }
    }
    public static class Parsers {
        public static void Add(ProviderState s, string label, object used, object reset, double windowSeconds = 0) {
            double? n = J.Num(used); if (!n.HasValue) return;
            s.Quotas.Add(new Quota { Label = label, Used = Math.Max(0, Math.Min(100, n.Value)), ResetUtc = J.Iso(reset), WindowSeconds = windowSeconds });
        }
        public static string WindowLabel(double seconds) { if (seconds >= 2500000) return "每月"; if (seconds >= 432000) return "每周"; if (seconds >= 86400) return "每日"; if (seconds > 0) return (seconds / 3600).ToString("0.#") + " 小时"; return "当前窗口"; }
        public static ProviderState Codex(object root) {
            var s = new ProviderState { Id = "codex", Plan = UsageDetails.PlanName(J.Str(root, "plan_type")) };
            foreach (string key in new[] { "primary_window", "secondary_window" }) { object w = J.Get(root, "rate_limit", key); double seconds = J.Num(w, "limit_window_seconds") ?? 0; if (w != null) Add(s, WindowLabel(seconds), J.Get(w, "used_percent"), J.Get(w, "reset_at"), seconds); }
            foreach (object extra in J.Arr(J.Get(root, "additional_rate_limits"))) {
                string model = J.Str(extra, "limit_name"); if (model.Length == 0) model = J.Str(extra, "metered_feature");
                foreach (string key in new[] { "primary_window", "secondary_window" }) { object w = J.Get(extra, "rate_limit", key); double seconds = J.Num(w, "limit_window_seconds") ?? 0; if (w != null) Add(s, model + " · " + WindowLabel(seconds), J.Get(w, "used_percent"), J.Get(w, "reset_at"), seconds); }
            }
            return s;
        }
        public static ProviderState Claude(object root) {
            var s = new ProviderState { Id = "claude", Plan = "Claude Code" };
            foreach (var pair in new[] { new[] { "five_hour", "5 小时" }, new[] { "seven_day", "每周" }, new[] { "seven_day_sonnet", "Sonnet · 每周" }, new[] { "seven_day_opus", "Opus · 每周" } }) {
                object w = J.Get(root, pair[0]); if (w != null) Add(s, pair[1], J.Get(w, "utilization"), J.Get(w, "resets_at"), pair[0] == "five_hour" ? 18000 : 604800);
            }
            foreach (object w in J.Arr(J.Get(root, "limits"))) {
                string kind = J.Str(w, "kind"), model = J.Str(w, "model"); string label = J.Str(w, "display_name");
                if (label.Length == 0) label = kind.Contains("weekly") ? (model.Length == 0 ? "每周" : model + " · 每周") : kind.Contains("five") || kind.Contains("session") ? "5 小时" : kind;
                object used = J.Get(w, "utilization") ?? J.Get(w, "used_percent");
                if (!s.Quotas.Any(q => q.Label == label)) Add(s, label, used, J.Get(w, "resets_at") ?? J.Get(w, "reset_at"));
            }
            return s;
        }
        public static ProviderState Cursor(object root) {
            var s = new ProviderState { Id = "cursor", Plan = J.Str(root, "membershipType") };
            object p = J.Get(root, "individualUsage", "plan"); object reset = J.Get(root, "billingCycleEnd");
            DateTimeOffset cycleStart, cycleEnd; double duration = 0;
            if (DateTimeOffset.TryParse(J.Str(root, "billingCycleStart"), out cycleStart) && DateTimeOffset.TryParse(J.Str(root, "billingCycleEnd"), out cycleEnd) && cycleEnd > cycleStart) duration = (cycleEnd - cycleStart).TotalSeconds;
            Add(s, "套餐总量", J.Get(p, "totalPercentUsed"), reset, duration); Add(s, "Auto", J.Get(p, "autoPercentUsed"), reset, duration); Add(s, "API / 手动模型", J.Get(p, "apiPercentUsed"), reset, duration);
            s.Plan = s.Plan.Length == 0 ? "Cursor" : "Cursor " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Plan); return s;
        }
        // Shown under the plan meters. Not labeled "每周" on its own, so headline pickers
        // that look for that exact label keep 套餐总量.
        public const string CursorGrokLabel = "Grok Bot · 每周";
        // POST cursor.com/api/dashboard/get-sand-usage-status. Appended only when the
        // account has a non-zero included Grok Bot allowance and a numeric usage percent.
        public static void CursorGrokBot(ProviderState state, object root) {
            if (state == null || root == null) return;
            object included = J.Get(root, "hasNonZeroIncludedLimit");
            if (!(included is bool) || !(bool)included) return;
            if (!J.Num(root, "usagePercent").HasValue) return;
            object reset = J.Get(root, "nextResetTimestampUtc");
            double window = 604800;
            DateTimeOffset start, end;
            if (DateTimeOffset.TryParse(J.Str(root, "currentPeriodStart"), out start) && DateTimeOffset.TryParse(J.Str(reset), out end) && end > start) window = (end - start).TotalSeconds;
            Add(state, CursorGrokLabel, J.Get(root, "usagePercent"), reset, window);
        }
        public static ProviderState DeepSeek(object root) {
            var s = new ProviderState { Id = "deepseek", Plan = "API 余额" };
            foreach (object b in J.Arr(J.Get(root, "balance_infos"))) { double? n = J.Num(b, "total_balance"); string c = J.Str(b, "currency"); if (n.HasValue && c.Length > 0) s.Balances.Add(new Balance { Currency = c, Amount = n.Value }); }
            return s;
        }
        public static ProviderState Grok(object root) {
            var s = new ProviderState { Id = "grok", Plan = J.Str(root, "subscriptionTier") }; object c = J.Get(root, "config");
            object end = J.Get(c, "currentPeriod", "end"); if (J.Get(end, "seconds") != null) end = J.Get(end, "seconds");
            // One shared allowance. productUsage (Build / Chat) splits what was spent from it;
            // a product's share is not a quota of its own, so it gets no remaining meter.
            Add(s, "当前账期", J.Get(c, "creditUsagePercent"), end);
            double? sharedUsed = J.Num(c, "creditUsagePercent");
            var products = new List<ProductConsumption>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool valid = sharedUsed.HasValue && sharedUsed >= 0 && sharedUsed <= 100;
            foreach (object product in J.Arr(J.Get(c, "productUsage"))) {
                string name = J.Str(product, "product").Trim(); double? used = J.Num(product, "usagePercent");
                if (name.Length == 0 || !used.HasValue || used < 0 || used > 100 || !names.Add(name)) { valid = false; break; }
                products.Add(new ProductConsumption { Product = name, UsedPercent = used.Value });
            }
            // Allow one percentage point for independently rounded product shares.
            if (valid && products.Count > 0 && Math.Abs(products.Sum(p => p.UsedPercent) - sharedUsed.Value) <= 1)
                s.ProductUsage = products;
            if (s.Plan.Length == 0) s.Plan = "Grok Build"; return s;
        }
        // Readings cached by older versions: before 1.2 Grok's Build share was saved as a quota.
        // Its complement (for example "Build 剩余 45%") is not spendable allowance.
        public static ProviderState Normalize(ProviderState s) {
            if (s == null) return null;
            if (s.ProductUsage == null) s.ProductUsage = new List<ProductConsumption>();
            if (s.Id == "grok" && s.Quotas != null) s.Quotas.RemoveAll(q => q.Label == "Build 占比");
            return s;
        }
        // A failed refresh keeps the last good reading, including the consumption breakdown.
        public static void RetainLastGood(ProviderState incoming, ProviderState old) {
            if (incoming == null || (incoming.Status != "error" && incoming.Status != "expired")) return;
            if (old == null) old = new ProviderState();
            incoming.Quotas = old.Quotas; incoming.Balances = old.Balances; incoming.ProductUsage = old.ProductUsage ?? new List<ProductConsumption>();
            incoming.LastSuccess = old.LastSuccess; incoming.Plan = old.Plan; incoming.Account = old.Account;
            incoming.ResetCreditsAvailable = old.ResetCreditsAvailable; incoming.ResetCreditExpiries = old.ResetCreditExpiries; incoming.ResetCreditsUpdated = old.ResetCreditsUpdated;
            incoming.Stale = (old.Quotas != null && old.Quotas.Count > 0) || (old.Balances != null && old.Balances.Count > 0);
        }
        // GET api.github.com/copilot_internal/user. Snapshots report percent_remaining; the
        // monthly quota resets on quota_reset_date. Unlimited pools have no meter.
        public static ProviderState Copilot(object root) {
            string plan = J.Str(root, "copilot_plan");
            var s = new ProviderState { Id = "copilot", Plan = plan.Length == 0 ? "Copilot" : "Copilot " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(plan.Replace('_', ' ')) };
            string reset = J.Iso(J.Get(root, "quota_reset_date")); DateTime resetAt; double window = 0;
            if (DateTime.TryParse(reset, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out resetAt)) window = (resetAt - resetAt.AddMonths(-1)).TotalSeconds;
            object snapshots = J.Get(root, "quota_snapshots");
            var unlimited = new List<string>();
            foreach (var pair in new[] { new[] { "premium_interactions", "高级请求" }, new[] { "chat", "对话" }, new[] { "completions", "代码补全" } }) {
                object snap = J.Get(snapshots, pair[0]); if (snap == null) continue;
                if (J.Get(snap, "unlimited") is bool && (bool)J.Get(snap, "unlimited")) { unlimited.Add(pair[1]); continue; }
                double? remaining = J.Num(snap, "percent_remaining"), entitlement = J.Num(snap, "entitlement"), left = J.Num(snap, "remaining");
                if (!remaining.HasValue && entitlement > 0 && left.HasValue) remaining = left.Value / entitlement.Value * 100;
                // entitlement 0 + remaining 0 + percent 0 is a placeholder, not an exhausted quota.
                if (!remaining.HasValue || (entitlement == 0 && left == 0 && remaining == 0)) continue;
                Add(s, pair[1], 100 - remaining.Value, reset, window);
            }
            if (s.Quotas.Count == 0 && unlimited.Count > 0) s.Plan += " · " + String.Join("、", unlimited) + "不限量";
            return s;
        }
        // GET api.kimi.com/coding/v1/usages (Kimi Code). Ratio pools (0–1) take precedence;
        // legacy count responses are mapped when no ratio is present for that window.
        public static ProviderState Kimi(object root) {
            var s = new ProviderState { Id = "kimi", Plan = KimiLevel(J.Str(root, "user", "membership", "level")) };
            object pools = J.Get(root, "usages");
            foreach (var pool in new[] { new { Key = "limit_5h", Label = "5 小时", Seconds = 18000.0 }, new { Key = "limit_7d", Label = "每周", Seconds = 604800.0 }, new { Key = "limit_month_total", Label = "每月总量", Seconds = 0.0 } }) {
                object item = J.Get(pools, pool.Key); double? ratio = J.Num(item, "used_ratio");
                if (ratio.HasValue && ratio >= 0) Add(s, pool.Label, Math.Min(1, ratio.Value) * 100, KimiReset(item), pool.Seconds);
            }
            object usage = J.Get(root, "usage");
            if (usage != null && !s.Quotas.Any(q => q.Label == "每周")) AddCount(s, "每周", usage, 604800);
            foreach (object limit in J.Arr(J.Get(root, "limits"))) {
                double duration = J.Num(limit, "window", "duration") ?? 0; string unit = J.Str(limit, "window", "timeUnit");
                double seconds = duration * (unit.Contains("MINUTE") ? 60 : unit.Contains("HOUR") ? 3600 : unit.Contains("DAY") ? 86400 : 0);
                string label = seconds == 18000 ? "5 小时" : seconds > 0 ? WindowLabel(seconds) : "速率限制";
                if (!s.Quotas.Any(q => q.Label == label)) AddCount(s, label, J.Get(limit, "detail"), seconds);
            }
            return s;
        }
        private static void AddCount(ProviderState s, string label, object detail, double seconds) {
            double? limit = J.Num(detail, "limit"), used = J.Num(detail, "used"), remaining = J.Num(detail, "remaining");
            if (!used.HasValue && limit.HasValue && remaining.HasValue) used = limit - remaining;
            if (limit > 0 && used.HasValue) Add(s, label, used.Value / limit.Value * 100, KimiReset(detail), seconds);
        }
        private static object KimiReset(object item) { return J.Get(item, "resetTime") ?? J.Get(item, "resetAt") ?? J.Get(item, "reset_time") ?? J.Get(item, "reset_at"); }
        private static string KimiLevel(string level) {
            switch (level) { case "LEVEL_FREE": return "Kimi Adagio"; case "LEVEL_TRIAL": return "Kimi Andante"; case "LEVEL_BASIC": return "Kimi Moderato"; case "LEVEL_INTERMEDIATE": return "Kimi Allegretto"; case "LEVEL_ADVANCED": return "Kimi Allegro"; default: return "Kimi Code"; }
        }
        // GET opencode.ai/zen/go/v1/usage (OpenCode Go). Percentages are 0–100; resets are
        // absolute (resetAt…) or relative (resetInSec…).
        public static ProviderState OpenCode(object root, DateTime nowUtc) {
            var s = new ProviderState { Id = "opencode", Plan = "OpenCode Go" };
            object usage = J.Get(root, "usage");
            if (J.Get(usage, "rolling") == null) throw new ProviderException("OpenCode 接口未返回用量字段", "error");
            foreach (var window in new[] { new { Key = "rolling", Label = "5 小时", Seconds = 18000.0 }, new { Key = "weekly", Label = "每周", Seconds = 604800.0 }, new { Key = "monthly", Label = "每月", Seconds = 0.0 } }) {
                object item = J.Get(usage, window.Key); if (item == null) continue;
                object percent = new[] { "usagePercent", "usedPercent", "percentUsed", "percent", "usage_percent", "used_percent", "utilization" }.Select(k => J.Get(item, k)).FirstOrDefault(v => J.Num(v).HasValue);
                if (percent == null) continue;
                object reset = new[] { "resetAt", "resetsAt", "reset_at", "resets_at", "nextReset", "next_reset" }.Select(k => J.Get(item, k)).FirstOrDefault(v => v != null);
                string resetIso = reset != null ? J.Iso(reset) : "";
                if (resetIso.Length == 0) { double? inSeconds = new[] { "resetInSec", "resetInSeconds", "resetSeconds", "reset_in_sec", "resetIn" }.Select(k => J.Num(item, k)).FirstOrDefault(v => v.HasValue); if (inSeconds.HasValue) resetIso = nowUtc.AddSeconds(inSeconds.Value).ToString("o"); }
                Add(s, window.Label, percent, resetIso, window.Seconds);
            }
            return s;
        }
        // GET {api.z.ai | open.bigmodel.cn}/api/monitor/usage/quota/limit (GLM Coding Plan,
        // used by ZCode). unit: 1 day, 3 hour, 5 minute, 6 week; nextResetTime is epoch ms.
        // TIME_LIMIT is the monthly MCP tool allowance, not a time window.
        public static ProviderState Zai(object root) {
            if (J.Get(root, "success") is bool && !(bool)J.Get(root, "success")) throw new ProviderException("智谱接口返回错误：" + J.Str(root, "msg"), "error");
            object data = J.Get(root, "data");
            string plan = new[] { "planName", "plan", "plan_type", "packageName", "level" }.Select(k => J.Str(data, k)).FirstOrDefault(v => v.Length > 0) ?? "";
            var s = new ProviderState { Id = "zcode", Plan = plan.Length > 0 ? "GLM " + plan : "GLM 编码套餐" };
            foreach (object limit in J.Arr(J.Get(data, "limits"))) {
                string type = J.Str(limit, "type");
                if (type != "TOKENS_LIMIT" && type != "CREDIT_LIMIT" && type != "TIME_LIMIT") continue;
                double? percent = J.Num(limit, "percentage"), usage = J.Num(limit, "usage"), current = J.Num(limit, "currentValue"), remaining = J.Num(limit, "remaining");
                if (usage > 0) {
                    double? used = remaining.HasValue ? Math.Max(usage.Value - remaining.Value, current ?? usage.Value - remaining.Value) : current;
                    if (used.HasValue) percent = Math.Max(0, Math.Min(usage.Value, used.Value)) / usage.Value * 100;
                }
                if (!percent.HasValue) continue;
                int unit = (int)(J.Num(limit, "unit") ?? 0); double number = J.Num(limit, "number") ?? 0;
                double minutes = number * (unit == 1 ? 1440 : unit == 3 ? 60 : unit == 5 ? 1 : unit == 6 ? 10080 : 0);
                string label = type == "TIME_LIMIT" ? "MCP 工具调用（每月）" : minutes == 300 ? "5 小时" : minutes > 0 ? WindowLabel(minutes * 60) : "编码套餐";
                if (type == "CREDIT_LIMIT") label += " · 积分";
                Add(s, label, percent, J.Get(limit, "nextResetTime"), type == "TIME_LIMIT" ? 0 : minutes * 60);
            }
            return s;
        }
        public static ProviderState Antigravity(object root) {
            var s = new ProviderState { Id = "antigravity", Plan = "Google AI" };
            foreach (object group in J.Arr(J.Get(root, "groups"))) {
                string name = J.Str(group, "displayName");
                foreach (object b in J.Arr(J.Get(group, "buckets"))) { double? fraction = J.Num(b, "remainingFraction"); string window = J.Str(b, "window"); string label = name + " · " + (window == "weekly" ? "每周" : window == "5h" ? "5 小时" : J.Str(b, "displayName")); if (fraction.HasValue) Add(s, label.Trim(' ', '·'), (1 - fraction.Value) * 100, J.Get(b, "resetTime"), window == "weekly" ? 604800 : window == "5h" ? 18000 : 0); }
            }
            if (s.Quotas.Count == 0) foreach (var model in J.Dict(J.Get(root, "models"))) { double? fraction = J.Num(model.Value, "quotaInfo", "remainingFraction"); if (fraction.HasValue) Add(s, model.Key, (1 - fraction.Value) * 100, J.Get(model.Value, "quotaInfo", "resetTime")); }
            return s;
        }
    }
    public sealed class ProviderService : IDisposable {
        private readonly HttpClient client;
        private readonly AppConfig config;
        public ProviderService(AppConfig config) {
            this.config = config ?? new AppConfig();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; ServicePointManager.DefaultConnectionLimit = 12;
            var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, UseCookies = false };
            string proxy = ResolveProxy(config.Proxy); handler.UseProxy = proxy.Length > 0; if (handler.UseProxy) handler.Proxy = new WebProxy(proxy);
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        }
        public static string ResolveProxy(string setting) {
            if (setting == "direct") return "";
            if (setting != "auto" && !String.IsNullOrWhiteSpace(setting)) { Uri u; if (!Uri.TryCreate(setting, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) throw new ArgumentException("代理地址应为 http://主机:端口"); return setting; }
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) { if (k != null && Convert.ToInt32(k.GetValue("ProxyEnable", 0)) != 0) { string server = Convert.ToString(k.GetValue("ProxyServer", "")); if (server.Contains("=")) { string choice = server.Split(';').FirstOrDefault(p => p.StartsWith("https=", StringComparison.OrdinalIgnoreCase)) ?? server.Split(';').FirstOrDefault(p => p.StartsWith("http=", StringComparison.OrdinalIgnoreCase)); server = choice == null ? "" : choice.Substring(choice.IndexOf('=') + 1); } if (server.Length > 0) return server.Contains("://") ? server : "http://" + server; } } } catch { }
            foreach (string key in new[] { "HTTPS_PROXY", "HTTP_PROXY" }) { string value = Environment.GetEnvironmentVariable(key); Uri u; if (Uri.TryCreate(value, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https")) return value; }
            return "";
        }
        private async Task<object> Request(string url, string token, string cookie, string body, Dictionary<string, string> extra, int timeoutSeconds = 18) {
            using (var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url)) {
                request.Headers.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
                if (!String.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                if (!String.IsNullOrWhiteSpace(cookie)) request.Headers.TryAddWithoutValidation("Cookie", "WorkosCursorSessionToken=" + cookie);
                if (extra != null) foreach (var h in extra) { request.Headers.Remove(h.Key); request.Headers.TryAddWithoutValidation(h.Key, h.Value); }
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request, deadline.Token).ConfigureAwait(false)) {
                    int status = (int)response.StatusCode;
                    if (status == 401) throw new ProviderException("登录已过期，请在原应用中重新登录", "expired");
                    if (status == 403) throw new ProviderException("接口拒绝访问（403），请检查代理或登录状态", "error");
                    if (status == 429) throw new ProviderException("查询过于频繁，稍后自动重试", "error");
                    if (!response.IsSuccessStatusCode) throw new ProviderException("服务暂时不可用（HTTP " + status + "）", "error");
                    return J.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }
        private static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        private static string ConfigPath(string variable, string fallback, string file) { string path = Environment.GetEnvironmentVariable(variable); if (String.IsNullOrWhiteSpace(path)) path = Path.Combine(Home, fallback); path = Environment.ExpandEnvironmentVariables(path); if (path.StartsWith("~")) path = Home + path.Substring(1); return Path.Combine(path, file); }
        public static string CodexHome() { return Path.GetDirectoryName(ConfigPath("CODEX_HOME", ".codex", "auth.json")); }
        public async Task<ProviderState> Fetch(string id) {
            if (ProviderCatalog.LocalOnly(id)) {
                string now = DateTime.UtcNow.ToString("o");
                return new ProviderState { Id = id, Status = "ready", Message = "仅本机用量", Plan = "本机日志", LastSuccess = now, LastAttempt = now };
            }
            try {
                ProviderState s;
                switch (id) {
                    case "codex": {
                        object a = J.File(ConfigPath("CODEX_HOME", ".codex", "auth.json")); string token = J.Str(a, "tokens", "access_token"); Need(token, id);
                        var headers = new Dictionary<string, string> { { "User-Agent", "codex-cli" } }; string account = J.Str(a, "tokens", "account_id"); if (account.Length > 0) headers.Add("ChatGPT-Account-Id", account);
                        object usage;
                        try { usage = await Request("https://chatgpt.com/backend-api/wham/usage", token, null, null, headers).ConfigureAwait(false); }
                        catch (ProviderException e) { if (e.State == "expired") throw new ProviderException("Codex 登录令牌已过期。打开 Codex 应用或运行一次 codex 即可自动续期，然后刷新。", "expired"); throw; }
                        s = Parsers.Codex(usage); s.Account = J.Str(J.Jwt(J.Str(a, "tokens", "id_token")), "email");
                        try {
                            var resetHeaders = new Dictionary<string, string>(headers) { { "OpenAI-Beta", "codex-1" }, { "originator", "Codex Desktop" }, { "Accept", "application/json" } };
                            object credits = await Request("https://chatgpt.com/backend-api/wham/rate-limit-reset-credits", token, null, null, resetHeaders, 4).ConfigureAwait(false);
                            UsageDetails.ParseResetCredits(s, credits, DateTime.UtcNow);
                        } catch { s.ResetCreditsError = "重置额度暂未返回"; }
                        break;
                    }
                    case "claude": {
                        object a = J.File(ConfigPath("CLAUDE_CONFIG_DIR", ".claude", ".credentials.json")); string token = J.Str(a, "claudeAiOauth", "accessToken"); Need(token, id);
                        object usage;
                        try { usage = await Request("https://api.anthropic.com/api/oauth/usage", token, null, null, new Dictionary<string, string> { { "anthropic-beta", "oauth-2025-04-20" } }).ConfigureAwait(false); }
                        catch (ProviderException e) { if (e.State == "expired") throw new ProviderException("Claude Code 的登录令牌已过期。运行一次 Claude Code 即可自动续期，然后刷新。", "expired"); throw; }
                        s = Parsers.Claude(usage);
                        string plan = J.Str(a, "claudeAiOauth", "subscriptionType"); if (plan.Length > 0) s.Plan = "Claude " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(plan);
                        try { s.Account = J.Str(J.File(Path.Combine(Home, ".claude.json")), "oauthAccount", "emailAddress"); } catch { } break;
                    }
                    case "cursor": {
                        string db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Cursor\User\globalStorage\state.vscdb");
                        string cookie = CursorCookie(); Need(cookie, id);
                        var headers = new Dictionary<string, string> { { "User-Agent", "Mozilla/5.0" } };
                        s = Parsers.Cursor(await Request("https://cursor.com/api/usage-summary", null, cookie, null, headers).ConfigureAwait(false));
                        // Grok Bot is a separate weekly allowance. A missing or rejected call
                        // must not turn the whole Cursor provider into an error.
                        try { Parsers.CursorGrokBot(s, await CursorDashboard("get-sand-usage-status", "{}").ConfigureAwait(false)); } catch { }
                        s.Account = NativeCredentials.SqliteText(db, "cursorAuth/cachedEmail"); break;
                    }
                    case "deepseek": {
                        string key = Store.DeepSeekKey(); Need(key, id); s = Parsers.DeepSeek(await Request("https://api.deepseek.com/user/balance", key, null, null, null).ConfigureAwait(false)); break;
                    }
                    case "grok": {
                        object auth = J.File(ConfigPath("GROK_HOME", ".grok", "auth.json")); object entry = J.Dict(auth).Where(p => p.Key == "https://accounts.x.ai/sign-in" || p.Key.StartsWith("https://auth.x.ai::", StringComparison.Ordinal)).Where(p => J.Str(p.Value, "key").Length > 0).OrderByDescending(p => J.Str(p.Value, "expires_at")).Select(p => p.Value).FirstOrDefault();
                        string token = J.Str(entry, "key"); Need(token, id); var headers = new Dictionary<string, string> { { "X-XAI-Token-Auth", "xai-grok-cli" }, { "x-grok-client-mode", "cli" }, { "x-grok-client-version", "1.0.0" } }; string userId = J.Str(entry, "user_id"); if (userId.Length > 0) headers.Add("x-userid", userId);
                        s = Parsers.Grok(await Request("https://cli-chat-proxy.grok.com/v1/billing?format=credits", token, null, null, headers).ConfigureAwait(false)); break;
                    }
                    case "antigravity": {
                        ProviderState local = await LocalAntigravity.TryRead().ConfigureAwait(false);
                        if (local != null) { s = local; break; }
                        string credential = NativeCredentials.GenericCredential("gemini:antigravity"); Need(credential, id); object auth = J.Parse(credential); string token = J.Str(auth, "token", "access_token"); Need(token, id);
                        const string endpoint = "https://cloudcode-pa.googleapis.com/v1internal:"; var headers = new Dictionary<string, string> { { "User-Agent", "antigravity" } };
                        object loaded = await Request(endpoint + "loadCodeAssist", token, null, "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\"}}", headers).ConfigureAwait(false);
                        string project = J.Str(loaded, "cloudaicompanionProject"); string body = project.Length == 0 ? "{}" : J.Serializer().Serialize(new Dictionary<string, string> { { "project", project } });
                        s = new ProviderState { Id = id };
                        if (project.Length > 0) { try { s = Parsers.Antigravity(await Request(endpoint + "retrieveUserQuotaSummary", token, null, body, headers).ConfigureAwait(false)); } catch (ProviderException e) { if (e.State == "expired") throw; } }
                        if (s.Quotas.Count == 0) {
                            s = Parsers.Antigravity(await Request(endpoint + "fetchAvailableModels", token, null, body, headers).ConfigureAwait(false));
                            if (s.Quotas.Count > 0 && s.Quotas.All(q => q.Used == 0)) throw new ProviderException("仅检测到模型可用性，尚未取得真实额度。请打开 Antigravity 后刷新。", "setup");
                        }
                        break;
                    }
                    case "copilot": {
                        string token = CopilotToken(); Need(token, id);
                        var headers = new Dictionary<string, string> { { "Authorization", "token " + token }, { "Editor-Version", "vscode/1.96.2" }, { "Editor-Plugin-Version", "copilot-chat/0.26.7" }, { "User-Agent", "GitHubCopilotChat/0.26.7" }, { "X-Github-Api-Version", "2025-04-01" } };
                        s = Parsers.Copilot(await KeyedRequest(id, "https://api.github.com/copilot_internal/user", null, headers).ConfigureAwait(false));
                        try { s.Account = J.Str(await Request("https://api.github.com/user", null, null, null, new Dictionary<string, string> { { "Authorization", "token " + token }, { "Accept", "application/vnd.github+json" } }, 6).ConfigureAwait(false), "login"); } catch { }
                        break;
                    }
                    case "kimi": {
                        string key = Store.ProviderKey(id, config); Need(key, id);
                        string host = config.KimiRegion == "international" ? "https://api.kimi.ai" : "https://api.kimi.com";
                        s = Parsers.Kimi(await KeyedRequest(id, host + "/coding/v1/usages", key, null).ConfigureAwait(false)); break;
                    }
                    case "opencode": {
                        string key = Store.ProviderKey(id, config); Need(key, id);
                        s = Parsers.OpenCode(await KeyedRequest(id, "https://opencode.ai/zen/go/v1/usage", key, null).ConfigureAwait(false), DateTime.UtcNow); break;
                    }
                    case "zcode": {
                        string key = Store.ProviderKey(id, config); Need(key, id);
                        string host = config.ZaiRegion == "global" ? "https://api.z.ai" : "https://open.bigmodel.cn";
                        s = Parsers.Zai(await KeyedRequest(id, host + "/api/monitor/usage/quota/limit", key, null).ConfigureAwait(false)); break;
                    }
                    default: {
                        CustomProvider custom;
                        if (!ProviderCatalog.Custom.TryGetValue(id, out custom)) throw new ProviderException("不支持的平台", "setup");
                        s = await CustomProviders.Fetch(client, custom, Store.SavedKey(id), DateTime.UtcNow).ConfigureAwait(false); break;
                    }
                }
                if (s.Quotas.Count == 0 && s.Balances.Count == 0) throw new ProviderException("接口未返回可识别的额度；没有将缺失数据当作零", "error");
                s.Status = "ready"; s.Message = "已连接"; s.LastSuccess = DateTime.UtcNow.ToString("o"); s.LastAttempt = s.LastSuccess; return s;
            } catch (ProviderException e) { return new ProviderState { Id = id, Status = e.State, Message = e.Message, LastAttempt = DateTime.UtcNow.ToString("o") }; }
            catch (TaskCanceledException) { return Failure(id, "查询超时，请检查代理连接"); }
            catch (HttpRequestException) { return Failure(id, "网络连接失败，请检查代理设置"); }
            catch (UnauthorizedAccessException) { return Failure(id, "无法读取本机登录文件，请检查文件权限"); }
            catch (Exception) { return Failure(id, "无法读取此平台的数据格式，请在原应用登录后重试"); }
        }
        // A public JSON document (the GitHub releases API for the update check).
        public Task<object> GetJson(string url) { return Request(url, null, null, null, new Dictionary<string, string> { { "Accept", "application/vnd.github+json" } }); }
        // Cursor: the editor's own session (state.vscdb), sent the way cursor.com expects it.
        private static string CursorDb { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb"); } }
        public static string CursorCookie() {
            string token = NativeCredentials.SqliteText(CursorDb, "cursorAuth/accessToken"); if (String.IsNullOrWhiteSpace(token)) return "";
            string sub = J.Str(J.Jwt(token), "sub"); if (sub.Contains("|")) sub = sub.Substring(sub.LastIndexOf('|') + 1);
            return sub.Length == 0 ? "" : sub + "%3A%3A" + token;
        }
        // POST cursor.com/api/dashboard/<endpoint> with the editor session.
        public Task<object> CursorDashboard(string endpoint, string body) {
            string cookie = CursorCookie(); if (cookie.Length == 0) throw new ProviderException(ProviderCatalog.Help("cursor"), "setup");
            var headers = new Dictionary<string, string> { { "User-Agent", "Mozilla/5.0" }, { "Origin", "https://cursor.com" }, { "Referer", "https://cursor.com/dashboard" }, { "Accept", "application/json" } };
            return Request("https://cursor.com/api/dashboard/" + endpoint, null, cookie, body, headers, 25);
        }
        public Task<ProviderState> FetchCustom(CustomProvider provider, string secret) { return CustomProviders.Fetch(client, provider, secret, DateTime.UtcNow); }
        // API-key providers: a rejected key is a settings problem, not an expired app login.
        private async Task<object> KeyedRequest(string id, string url, string key, Dictionary<string, string> headers) {
            try { return await Request(url, key, null, null, headers).ConfigureAwait(false); }
            catch (ProviderException e) {
                if (e.State != "expired") throw;
                throw new ProviderException(id == "copilot" ? "GitHub 授权已失效，请重新使用 GitHub 登录。" : ProviderCatalog.Name(id) + " 的 API Key 无效或已过期，请重新填写。", "expired");
            }
        }
        // Copilot: the token from the in-app GitHub device login, else one already saved by
        // an official Copilot client (copilot.vim / JetBrains / Copilot CLI) — read-only.
        public static string CopilotToken() {
            string saved = Store.SavedKey("copilot"); if (saved.Length > 0) return saved;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string path in new[] { Path.Combine(local, "github-copilot", "apps.json"), Path.Combine(local, "github-copilot", "hosts.json"), Path.Combine(Home, ".config", "github-copilot", "apps.json"), Path.Combine(Home, ".config", "github-copilot", "hosts.json") }) {
                try {
                    foreach (var entry in J.Dict(J.File(path))) if (entry.Key.StartsWith("github.com", StringComparison.OrdinalIgnoreCase)) { string token = J.Str(entry.Value, "oauth_token"); if (token.Length > 0) return token; }
                } catch { }
            }
            return "";
        }
        // GitHub OAuth device flow with the public VS Code Copilot client ID (read:user).
        public sealed class DeviceLogin { public string DeviceCode = "", UserCode = "", VerificationUri = ""; public int Interval = 5, ExpiresIn = 900; }
        private const string CopilotClientId = "Iv1.b507a08c87ecfe98";
        public async Task<DeviceLogin> StartCopilotLogin() {
            object reply = await PostForm("https://github.com/login/device/code", new Dictionary<string, string> { { "client_id", CopilotClientId }, { "scope", "read:user" } }).ConfigureAwait(false);
            var login = new DeviceLogin { DeviceCode = J.Str(reply, "device_code"), UserCode = J.Str(reply, "user_code"), VerificationUri = J.Str(reply, "verification_uri"), Interval = (int)(J.Num(reply, "interval") ?? 5), ExpiresIn = (int)(J.Num(reply, "expires_in") ?? 900) };
            if (login.DeviceCode.Length == 0 || login.UserCode.Length == 0) throw new ProviderException("GitHub 没有返回设备码", "error");
            if (!login.VerificationUri.StartsWith("https://github.com/", StringComparison.Ordinal)) login.VerificationUri = "https://github.com/login/device";
            return login;
        }
        // Returns the token, or null while authorization is still pending.
        public async Task<string> PollCopilotLogin(DeviceLogin login) {
            object reply = await PostForm("https://github.com/login/oauth/access_token", new Dictionary<string, string> { { "client_id", CopilotClientId }, { "device_code", login.DeviceCode }, { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" } }).ConfigureAwait(false);
            string token = J.Str(reply, "access_token"), error = J.Str(reply, "error");
            if (token.Length > 0) return token;
            if (error == "authorization_pending") return null;
            if (error == "slow_down") { login.Interval += 5; return null; }
            if (error == "expired_token") throw new ProviderException("设备码已过期，请重新登录", "error");
            if (error == "access_denied") throw new ProviderException("已在 GitHub 取消授权", "error");
            throw new ProviderException("GitHub 登录失败：" + (error.Length > 0 ? error : "未知错误"), "error");
        }
        private async Task<object> PostForm(string url, Dictionary<string, string> form) {
            using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                request.Headers.TryAddWithoutValidation("Accept", "application/json"); request.Headers.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
                request.Content = new FormUrlEncodedContent(form);
                using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode) throw new ProviderException("GitHub 暂时不可用（HTTP " + (int)response.StatusCode + "）", "error");
                    return J.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }
        private static ProviderState Failure(string id, string message) { return new ProviderState { Id = id, Status = "error", Message = message, LastAttempt = DateTime.UtcNow.ToString("o") }; }
        private static void Need(string value, string id) { if (String.IsNullOrWhiteSpace(value)) throw new ProviderException(ProviderCatalog.Help(id), "setup"); }
        public void Dispose() { client.Dispose(); }
    }
    internal static class NativeCredentials {
        [StructLayout(LayoutKind.Sequential)] private struct Credential { public uint Flags, Type; public IntPtr Target, Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint Size; public IntPtr Blob; public uint Persist, AttributeCount; public IntPtr Attributes, Alias, User; }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
        public static string GenericCredential(string target) { IntPtr p; if (!CredRead(target, 1, 0, out p)) return ""; try { var c = (Credential)Marshal.PtrToStructure(p, typeof(Credential)); if (c.Size > 1024 * 1024 || c.Blob == IntPtr.Zero) return ""; byte[] b = new byte[c.Size]; Marshal.Copy(c.Blob, b, 0, b.Length); return Encoding.UTF8.GetString(b).TrimEnd('\0'); } finally { CredFree(p); } }
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr stmt, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr stmt);
        // Read-only query returning every column as text; empty on any failure.
        public static List<string[]> SqliteRows(string path, string query) {
            var rows = new List<string[]>(); if (!File.Exists(path)) return rows; IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
            try {
                if (sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 1, IntPtr.Zero) != 0) return rows; sqlite3_busy_timeout(db, 1500);
                byte[] sql = Encoding.UTF8.GetBytes(query + "\0"); if (sqlite3_prepare_v2(db, sql, sql.Length, out stmt, IntPtr.Zero) != 0) return rows;
                int columns = sqlite3_column_count(stmt);
                while (rows.Count < 5000 && sqlite3_step(stmt) == 100) {
                    var row = new string[columns];
                    for (int c = 0; c < columns; c++) {
                        IntPtr text = sqlite3_column_text(stmt, c); int count = sqlite3_column_bytes(stmt, c);
                        if (text == IntPtr.Zero || count <= 0 || count > 4 * 1024 * 1024) { row[c] = ""; continue; }
                        byte[] b = new byte[count]; Marshal.Copy(text, b, 0, count); row[c] = Encoding.UTF8.GetString(b);
                    }
                    rows.Add(row);
                }
            } catch { }
            finally { if (stmt != IntPtr.Zero) sqlite3_finalize(stmt); if (db != IntPtr.Zero) sqlite3_close(db); }
            return rows;
        }
        public static string SqliteText(string path, string key) {
            if (!File.Exists(path)) return ""; IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
            try { if (sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 1, IntPtr.Zero) != 0) return ""; sqlite3_busy_timeout(db, 1500); string query = "SELECT value FROM ItemTable WHERE key='" + key.Replace("'", "''") + "' LIMIT 1"; byte[] sql = Encoding.UTF8.GetBytes(query + "\0"); if (sqlite3_prepare_v2(db, sql, sql.Length, out stmt, IntPtr.Zero) != 0 || sqlite3_step(stmt) != 100) return ""; int count = sqlite3_column_bytes(stmt, 0); if (count < 1 || count > 1024 * 1024) return ""; byte[] b = new byte[count]; Marshal.Copy(sqlite3_column_text(stmt, 0), b, 0, count); return Encoding.UTF8.GetString(b); }
            finally { if (stmt != IntPtr.Zero) sqlite3_finalize(stmt); if (db != IntPtr.Zero) sqlite3_close(db); }
        }
    }
    public static partial class HistoryService {
        public static string DayKey(DateTime day) { return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        // Clients may delete or prune old session files, and a rebuilt index cannot count
        // them any more. Past days therefore keep the largest total ever observed. Today is
        // always replaced (it is still growing); a time-zone or engine change starts over.
        public static UsageHistory Merge(UsageHistory previous, UsageHistory next, string oldestDay, string today) {
            if (previous == null || next == null || previous.Days.Count == 0 || previous.Zone != next.Zone || previous.Engine != next.Engine) return next;
            foreach (DayUsage old in previous.Days) {
                if (old.Day == null || String.CompareOrdinal(old.Day, today) >= 0 || String.CompareOrdinal(old.Day, oldestDay) < 0) continue;
                int index = next.Days.FindIndex(d => d.Day == old.Day && d.Agent == old.Agent);
                if (index < 0) next.Days.Add(old); else if (next.Days[index].Tokens < old.Tokens) next.Days[index] = old;
            }
            return next;
        }
    }
}
