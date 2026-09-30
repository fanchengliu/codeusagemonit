using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // A user-defined provider: one GET request to a JSON endpoint, plus dot-paths that
    // map the response to quota windows, a balance and identity. Deliberately narrow (the
    // same boundary CodexBar draws for declarative providers): GET only, fixed auth header
    // forms, the secret never inline (DPAPI file), no redirects, bounded response.
    public sealed class CustomWindow {
        public string Label = "", UsedPercent = "", RemainingPercent = "", Used = "", Limit = "", Remaining = "", ResetsAt = "", ResetInSeconds = "";
        public bool Ratio;
        public double WindowHours;
    }
    public sealed class CustomProvider {
        public string Id = "", Name = "", Color = "", Url = "", Auth = "none", Header = "";
        public List<CustomWindow> Windows = new List<CustomWindow>();
        public string Balance = "", Currency = "", CurrencyPath = "", Plan = "", Account = "";
        public string Source = "";
        public string Host { get { Uri uri; return Uri.TryCreate(Url, UriKind.Absolute, out uri) ? uri.Authority : ""; } }
    }
    public sealed class CustomProviderFile { public List<string> Definitions = new List<string>(); }

    public static class CustomProviders {
        public const int MaxResponseBytes = 1 << 20, MaxWindows = 6;
        private static readonly Regex PathPattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_\-]*(\.[A-Za-z_][A-Za-z0-9_\-]*|\[\d{1,4}\])*$");
        private static readonly string[] ForbiddenHeaders = { "host", "cookie", "content-length", "content-type", "transfer-encoding", "connection", "proxy-authorization" };

        public const string Template = "{\n  \"name\": \"我的中转站\",\n  \"url\": \"https://api.example.com/api/usage\",\n  \"auth\": \"bearer\",\n  \"windows\": [\n    { \"label\": \"每日\", \"used\": \"data.daily.used\", \"limit\": \"data.daily.limit\", \"resetsAt\": \"data.daily.reset_at\", \"windowHours\": 24 },\n    { \"label\": \"每月\", \"usedPercent\": \"data.monthly.percent\", \"resetInSeconds\": \"data.monthly.reset_in\" }\n  ],\n  \"balance\": { \"amount\": \"data.balance\", \"currency\": \"USD\" },\n  \"plan\": \"data.plan_name\",\n  \"account\": \"data.email\"\n}";

        public static List<CustomProvider> Load() {
            var result = new List<CustomProvider>();
            foreach (string text in Store.Read<CustomProviderFile>("custom-providers.json").Definitions) { try { result.Add(Parse(text)); } catch { } }
            return result;
        }
        public static void Save(IEnumerable<CustomProvider> providers) { Store.Write("custom-providers.json", new CustomProviderFile { Definitions = providers.Select(p => p.Source).ToList() }); }

        // Parses and validates a definition; ArgumentException messages are shown to the user.
        public static CustomProvider Parse(string json) {
            object root;
            try { root = J.Parse(json ?? ""); } catch (Exception e) { throw new ArgumentException("JSON 格式错误：" + e.Message); }
            if (!(root is Dictionary<string, object>)) throw new ArgumentException("定义必须是一个 JSON 对象");
            var known = new HashSet<string> { "id", "name", "color", "url", "auth", "header", "windows", "balance", "plan", "account" };
            foreach (string key in J.Dict(root).Keys) if (!known.Contains(key)) throw new ArgumentException("未知字段：" + key);
            var p = new CustomProvider { Source = json.Trim() };
            p.Name = J.Str(root, "name").Trim();
            if (p.Name.Length == 0 || p.Name.Length > 40) throw new ArgumentException("name 为 1–40 个字符");
            string id = J.Str(root, "id").Trim().ToLowerInvariant();
            if (id.Length == 0) id = Slug(p.Name);
            if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,31}$")) throw new ArgumentException("id 只能包含小写字母、数字和连字符（最多 32 个）");
            p.Id = "custom-" + id;
            p.Color = J.Str(root, "color").Trim();
            if (p.Color.Length > 0 && !Regex.IsMatch(p.Color, "^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("color 应为 #RRGGBB");
            p.Url = J.Str(root, "url").Trim();
            string urlError = UrlError(p.Url); if (urlError != null) throw new ArgumentException(urlError);
            p.Auth = J.Str(root, "auth").Trim().ToLowerInvariant(); if (p.Auth.Length == 0) p.Auth = "none";
            if (!new[] { "none", "bearer", "x-api-key", "token", "header" }.Contains(p.Auth)) throw new ArgumentException("auth 只能是 none、bearer、x-api-key、token 或 header");
            p.Header = J.Str(root, "header").Trim();
            if (p.Auth == "header" && (!Regex.IsMatch(p.Header, "^[A-Za-z0-9-]{1,64}$") || ForbiddenHeaders.Contains(p.Header.ToLowerInvariant()))) throw new ArgumentException("auth 为 header 时需要合法的 header 名称，例如 X-Api-Key");
            foreach (object item in J.Arr(J.Get(root, "windows"))) {
                var w = new CustomWindow {
                    Label = J.Str(item, "label").Trim(), UsedPercent = PathOf(item, "usedPercent"), RemainingPercent = PathOf(item, "remainingPercent"),
                    Used = PathOf(item, "used"), Limit = PathOf(item, "limit"), Remaining = PathOf(item, "remaining"),
                    ResetsAt = PathOf(item, "resetsAt"), ResetInSeconds = PathOf(item, "resetInSeconds"),
                    Ratio = J.Get(item, "ratio") is bool && (bool)J.Get(item, "ratio"), WindowHours = J.Num(item, "windowHours") ?? 0
                };
                if (w.Label.Length == 0 || w.Label.Length > 30) throw new ArgumentException("每个窗口都需要 1–30 个字符的 label");
                int forms = (w.UsedPercent.Length > 0 ? 1 : 0) + (w.RemainingPercent.Length > 0 ? 1 : 0) + (w.Used.Length > 0 || w.Remaining.Length > 0 ? 1 : 0);
                if (forms != 1) throw new ArgumentException("窗口“" + w.Label + "”需要且只能用一种写法：usedPercent、remainingPercent，或 used/remaining + limit");
                if ((w.Used.Length > 0 || w.Remaining.Length > 0) && w.Limit.Length == 0) throw new ArgumentException("窗口“" + w.Label + "”使用 used/remaining 时必须提供 limit");
                if (w.WindowHours < 0 || w.WindowHours > 24 * 366) throw new ArgumentException("windowHours 超出范围");
                p.Windows.Add(w);
            }
            if (p.Windows.Count > MaxWindows) throw new ArgumentException("最多 " + MaxWindows + " 个窗口");
            object balance = J.Get(root, "balance");
            if (balance != null) {
                p.Balance = PathOf(balance, "amount"); p.CurrencyPath = PathOf(balance, "currencyPath");
                p.Currency = J.Str(balance, "currency").Trim().ToUpperInvariant();
                if (p.Balance.Length == 0) throw new ArgumentException("balance 需要 amount 路径");
                if (p.Currency.Length > 0 && !Regex.IsMatch(p.Currency, "^[A-Z]{3}$")) throw new ArgumentException("currency 应为三位货币代码，例如 USD、CNY");
            }
            p.Plan = PathOf(root, "plan"); p.Account = PathOf(root, "account");
            if (p.Windows.Count == 0 && p.Balance.Length == 0) throw new ArgumentException("至少需要一个 windows 窗口或 balance");
            return p;
        }
        private static string PathOf(object item, string key) {
            string path = J.Str(item, key).Trim();
            if (path.Length == 0) return "";
            if (path.Length > 256 || path.Count(c => c == '.' || c == '[') > 31 || !PathPattern.IsMatch(path)) throw new ArgumentException("路径“" + path + "”不合法：只支持 a.b.c 和 [0] 形式");
            return path;
        }
        private static string Slug(string name) {
            string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length > 24) slug = slug.Substring(0, 24).Trim('-');
            if (slug.Length == 0) using (var sha = System.Security.Cryptography.SHA256.Create()) slug = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(name)), 0, 3).Replace("-", "").ToLowerInvariant();
            return slug;
        }
        // Public origins must use HTTPS. Plain HTTP is allowed only for this machine or a
        // private network (self-hosted gateways). No user info or fragments.
        public static string UrlError(string url) {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return "url 必须是完整地址，例如 https://api.example.com/usage";
            if (uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return "url 不能包含用户名、密码或 # 片段";
            if (uri.Scheme == Uri.UriSchemeHttps) return null;
            if (uri.Scheme == Uri.UriSchemeHttp && IsPrivateHost(uri)) return null;
            return "公网地址必须使用 https；http 只允许本机或局域网地址";
        }
        public static bool IsPrivateHost(Uri uri) {
            if (uri.IsLoopback || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress ip; if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out ip)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 127;
        }
        // Dot-path lookup: "data.items[0].used". Missing segments return null.
        public static object Resolve(object root, string path) {
            if (String.IsNullOrEmpty(path)) return null;
            object current = root;
            foreach (Match part in Regex.Matches(path, @"[A-Za-z_][A-Za-z0-9_\-]*|\[\d+\]")) {
                if (current == null) return null;
                if (part.Value.StartsWith("[")) {
                    int index = Int32.Parse(part.Value.Trim('[', ']'), CultureInfo.InvariantCulture);
                    var list = J.Arr(current).ToList(); current = index < list.Count ? list[index] : null;
                } else current = J.Get(current, part.Value);
            }
            return current;
        }
        public static ProviderState Map(CustomProvider p, object root, DateTime nowUtc) {
            var s = new ProviderState { Id = p.Id, Plan = Text(Resolve(root, p.Plan)), Account = Text(Resolve(root, p.Account)) };
            if (s.Plan.Length == 0) s.Plan = "自定义";
            foreach (CustomWindow w in p.Windows) {
                double scale = w.Ratio ? 100 : 1; double? used = null;
                if (w.UsedPercent.Length > 0) { double? v = J.Num(Resolve(root, w.UsedPercent)); if (v.HasValue) used = v * scale; }
                else if (w.RemainingPercent.Length > 0) { double? v = J.Num(Resolve(root, w.RemainingPercent)); if (v.HasValue) used = 100 - v * scale; }
                else {
                    double? limit = J.Num(Resolve(root, w.Limit)), usedCount = w.Used.Length > 0 ? J.Num(Resolve(root, w.Used)) : null, left = w.Remaining.Length > 0 ? J.Num(Resolve(root, w.Remaining)) : null;
                    if (!usedCount.HasValue && left.HasValue && limit.HasValue) usedCount = limit - left;
                    if (limit > 0 && usedCount.HasValue) used = usedCount.Value / limit.Value * 100;
                }
                if (!used.HasValue) continue; // a missing value stays unknown, never 0%
                string reset = w.ResetsAt.Length > 0 ? J.Iso(Resolve(root, w.ResetsAt)) : "";
                if (reset.Length == 0 && w.ResetInSeconds.Length > 0) { double? seconds = J.Num(Resolve(root, w.ResetInSeconds)); if (seconds >= 0) reset = nowUtc.AddSeconds(seconds.Value).ToString("o"); }
                Parsers.Add(s, w.Label, used.Value, reset, w.WindowHours * 3600);
            }
            if (p.Balance.Length > 0) {
                double? amount = J.Num(Resolve(root, p.Balance));
                string currency = p.CurrencyPath.Length > 0 ? Text(Resolve(root, p.CurrencyPath)).ToUpperInvariant() : p.Currency;
                if (!Regex.IsMatch(currency, "^[A-Z]{3}$")) currency = p.Currency.Length > 0 ? p.Currency : "USD";
                if (amount.HasValue) s.Balances.Add(new Balance { Currency = currency, Amount = amount.Value });
            }
            if (s.Quotas.Count == 0 && s.Balances.Count == 0) throw new ProviderException("接口返回中找不到定义的字段，请检查路径（没有把缺失数据当作 0）", "error");
            return s;
        }
        private static string Text(object value) {
            if (value == null || value is IDictionary<string, object> || (value is System.Collections.IEnumerable && !(value is string))) return "";
            string text = Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
            return text.Length > 60 ? text.Substring(0, 60) : text;
        }
        public static async Task<ProviderState> Fetch(HttpClient client, CustomProvider p, string secret, DateTime nowUtc) {
            string error = UrlError(p.Url); if (error != null) throw new ProviderException(error, "setup");
            if (p.Auth != "none" && String.IsNullOrWhiteSpace(secret)) throw new ProviderException("此自定义平台需要密钥：在设置 → 自定义平台 中编辑并填写。", "setup");
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, p.Url)) {
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
                // The secret goes only into the declared header form, never the URL or logs.
                if (p.Auth == "bearer") request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + secret.Trim());
                else if (p.Auth == "token") request.Headers.TryAddWithoutValidation("Authorization", "token " + secret.Trim());
                else if (p.Auth == "x-api-key") request.Headers.TryAddWithoutValidation("X-API-Key", secret.Trim());
                else if (p.Auth == "header") request.Headers.TryAddWithoutValidation(p.Header, secret.Trim());
                using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false)) {
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400) throw new ProviderException("接口返回了重定向（HTTP " + status + "）；为防止密钥被转发，不跟随重定向", "error");
                    if (status == 401 || status == 403) throw new ProviderException("密钥无效或无权限（HTTP " + status + "）", "expired");
                    if (!response.IsSuccessStatusCode) throw new ProviderException("接口返回 HTTP " + status, "error");
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) {
                        var buffer = new MemoryStream(); byte[] chunk = new byte[65536]; int read;
                        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, deadline.Token).ConfigureAwait(false)) > 0) {
                            buffer.Write(chunk, 0, read);
                            if (buffer.Length > MaxResponseBytes) throw new ProviderException("接口返回超过 1 MB，已中止", "error");
                        }
                        object root;
                        try { root = J.Parse(Encoding.UTF8.GetString(buffer.ToArray())); } catch { throw new ProviderException("接口返回的不是 JSON", "error"); }
                        return Map(p, root, nowUtc);
                    }
                }
            }
        }
    }
}
