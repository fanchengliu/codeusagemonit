using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Updates from the GitHub releases: is there a newer one (one GET to the public releases API,
    // no account, nothing uploaded), and installing it from inside the app. The release's
    // SHA256SUMS.txt is the check on every download; a file that does not match is deleted.
    //   setup    (installed with setup.exe): run the new setup.exe silently over this install;
    //            it keeps the folder, tasks and data and reopens the app (/restart=1).
    //   portable (zip / install.ps1): replace this folder's program files with the new zip's,
    //            data\ untouched; the running exe is renamed aside, then the new one starts.
    //   scoop    : Scoop owns the folder; it is updated with `scoop update codeusagemonit`.
    //   manual   : a folder this user cannot write to; the release page opens instead.
    public sealed class UpdateState { public string Checked = "", Latest = "", Url = "", Published = "", Error = "", Notes = ""; }
    public static class Updates {
        public const string Api = "https://api.github.com/repos/fanchengliu/codeusagemonit/releases/latest";
        public const string Releases = "https://github.com/fanchengliu/codeusagemonit/releases/latest";
        public const string DownloadBase = "https://github.com/fanchengliu/codeusagemonit/releases/download/";
        // Self-tests of the whole update path point this at a feed on this computer
        // (loopback http only: "http://127.0.0.1:<port>/"), shaped like the GitHub URLs.
        public const string FeedVariable = "CODEUSAGEMONIT_UPDATE_FEED";
        private static string Feed {
            get {
                string feed = (Environment.GetEnvironmentVariable(FeedVariable) ?? "").Trim(); Uri uri;
                return Uri.TryCreate(feed, UriKind.Absolute, out uri) && uri.Scheme == "http" && uri.IsLoopback ? feed.TrimEnd('/') + "/" : null;
            }
        }
        public static string ApiUrl { get { return Feed != null ? Feed + "releases/latest" : Api; } }
        public static string DownloadUrl(string version, string name) { return (Feed != null ? Feed + "releases/download/" : DownloadBase) + "v" + version + "/" + name; }
        public static string SetupName(string version) { return "codeusagemonit-setup-" + version + ".exe"; }
        public static string ZipName(string version) { return "codeusagemonit-" + version + "-win-x64.zip"; }

        public static UpdateState State() { return Store.Read<UpdateState>("update-check.json"); }
        public static bool Newer(UpdateState state) { return state != null && state.Latest.Length > 0 && Compare(state.Latest, AppInfo.Version) > 0; }
        // Worth telling about unprompted: newer, and not a version the user chose to skip.
        public static bool Offer(UpdateState state, AppConfig config) { return Newer(state) && (config.SkippedVersion ?? "") != state.Latest; }
        public static bool Due(AppConfig config, DateTime nowUtc) { return Due(config, nowUtc, TimeSpan.FromHours(24)); }
        // At start-up the release is asked again unless it was asked within the hour.
        public static bool DueOnStart(AppConfig config, DateTime nowUtc) { return Due(config, nowUtc, TimeSpan.FromHours(1)); }
        private static bool Due(AppConfig config, DateTime nowUtc, TimeSpan every) {
            if (!config.UpdateCheck) return false;
            DateTime last; return !LogIndex.Parse(State().Checked, out last) || nowUtc - last >= every;
        }
        public static async Task<UpdateState> Check(AppConfig config, DateTime nowUtc) {
            var state = new UpdateState { Checked = nowUtc.ToString("o") };
            try {
                object release;
                using (var service = new ProviderService(config)) release = await service.GetJson(ApiUrl).ConfigureAwait(false);
                string tag = J.Str(release, "tag_name").Trim();
                if (tag.Length == 0) throw new ProviderException(I18n.T("GitHub 没有返回版本号。"), "error");
                state.Latest = tag.TrimStart('v', 'V');
                string url = J.Str(release, "html_url"); state.Url = url.StartsWith("https://github.com/fanchengliu/codeusagemonit/", StringComparison.Ordinal) ? url : Releases;
                state.Published = J.Str(release, "published_at");
                state.Notes = NotesFrom(J.Str(release, "body"));
            } catch (Exception e) {
                // Keep the last known release.
                UpdateState previous = State(); state.Latest = previous.Latest; state.Url = previous.Url; state.Published = previous.Published; state.Notes = previous.Notes;
                state.Error = e is ProviderException ? e.Message : I18n.T("无法连接 GitHub，请检查网络或代理设置。");
            }
            try { Store.Write("update-check.json", state); } catch { }
            return state;
        }
        // The release notes' "本版更新" (What's new) section as plain lines; "" when there is none.
        public static string NotesFrom(string body) {
            var lines = new List<string>(); bool inside = false;
            foreach (string raw in (body ?? "").Replace("\r\n", "\n").Split('\n')) {
                string line = raw.Trim();
                if (line.StartsWith("#")) {
                    if (inside) break;
                    inside = line.TrimStart('#').Trim().StartsWith("本版更新", StringComparison.Ordinal) || line.TrimStart('#').Trim().StartsWith("What's new", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inside) continue;
                if (line.StartsWith("- ") || line.StartsWith("* ")) line = "· " + line.Substring(2).Trim();
                line = line.Replace("**", "").Replace("`", "");
                if (line.Length > 0 || (lines.Count > 0 && lines[lines.Count - 1].Length > 0)) lines.Add(line);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            string text = String.Join("\n", lines);
            return text.Length > 2000 ? text.Substring(0, 2000) + "…" : text;
        }
        // "1.10.0" > "1.9.2"; missing parts count as 0; anything after "-" is ignored.
        public static int Compare(string a, string b) {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++) {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }
        private static int[] Parts(string version) {
            string core = (version ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
            return core.Split('.').Select(s => { int n; return Int32.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : 0; }).ToArray();
        }

        // ── Installing ────────────────────────────────────────────────────
        public static string InstallKind(string root) {
            string folder = (root ?? "").TrimEnd('\\') + "\\";
            if (folder.IndexOf(@"\scoop\apps\", StringComparison.OrdinalIgnoreCase) >= 0) return "scoop";
            if (File.Exists(Path.Combine(root, "installed.txt"))) return "setup";
            return CanWrite(root) ? "portable" : "manual";
        }
        private static bool CanWrite(string folder) {
            try { string probe = Path.Combine(folder, ".update-probe-" + Guid.NewGuid().ToString("N")); File.WriteAllText(probe, ""); File.Delete(probe); return true; } catch { return false; }
        }
        public static string WorkFolder { get { return Path.Combine(Path.GetTempPath(), "codeusagemonit-update"); } }
        // The SHA-256 that SHA256SUMS.txt lists for a file name ("<hex>  <name>"), or null.
        public static string HashFor(string sums, string name) {
            foreach (string raw in (sums ?? "").Replace("\r\n", "\n").Split('\n')) {
                string[] parts = raw.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].Trim().TrimStart('*') == name && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit)) return parts[0].ToLowerInvariant();
            }
            return null;
        }
        public static string Sha256(string path) {
            using (var sha = SHA256.Create()) using (FileStream stream = File.OpenRead(path)) return String.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }
        // GitHub sends release files from its own hosts; the test feed is on this computer.
        private static bool TrustedHost(Uri uri) {
            if (uri == null) return false;
            if (Feed != null) return uri.IsLoopback;
            string host = uri.Host.ToLowerInvariant();
            return uri.Scheme == "https" && (host == "github.com" || host.EndsWith(".github.com") || host.EndsWith(".githubusercontent.com"));
        }
        private static HttpClient Client(AppConfig config) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, UseCookies = false };
            string proxy = ProviderService.ResolveProxy(config.Proxy); handler.UseProxy = proxy.Length > 0; if (handler.UseProxy) handler.Proxy = new WebProxy(proxy);
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            return client;
        }
        // Downloads the file this install needs (setup.exe or the zip) and checks it against the
        // release's SHA256SUMS.txt. progress: 0–100, or -1 while the size is unknown.
        public static async Task<string> Download(AppConfig config, string version, string kind, IProgress<int> progress, CancellationToken cancel) {
            string name = kind == "setup" ? SetupName(version) : ZipName(version);
            string folder = Path.Combine(WorkFolder, version); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name), part = path + ".part";
            using (HttpClient client = Client(config)) {
                string sums;
                using (HttpResponseMessage response = await client.GetAsync(DownloadUrl(version, "SHA256SUMS.txt"), cancel).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode || !TrustedHost(response.RequestMessage.RequestUri)) throw new ProviderException(I18n.T("没有取到这个版本的校验文件（SHA256SUMS.txt）。"), "error");
                    sums = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                string expected = HashFor(sums, name);
                if (expected == null) throw new ProviderException(I18n.T("这个版本的校验文件里没有 {0}。", name), "error");
                using (HttpResponseMessage response = await client.GetAsync(DownloadUrl(version, name), HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode || !TrustedHost(response.RequestMessage.RequestUri)) throw new ProviderException(I18n.T("下载失败（HTTP {0}）。", (int)response.StatusCode), "error");
                    long total = response.Content.Headers.ContentLength ?? -1;
                    const long Limit = 200L * 1024 * 1024;
                    if (total > Limit) throw new ProviderException(I18n.T("下载的文件大小异常，已停止。"), "error");
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = File.Create(part)) {
                        var buffer = new byte[81920]; long read = 0; int n, last = -2;
                        while ((n = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0) {
                            output.Write(buffer, 0, n); read += n;
                            if (read > Limit) throw new ProviderException(I18n.T("下载的文件大小异常，已停止。"), "error");
                            int percent = total > 0 ? (int)(read * 100 / total) : -1;
                            if (percent != last && progress != null) { progress.Report(percent); last = percent; }
                        }
                    }
                }
                if (Sha256(part) != expected) { try { File.Delete(part); } catch { } throw new ProviderException(I18n.T("下载的文件与发布的校验值不一致，已删除。"), "error"); }
                if (File.Exists(path)) File.Delete(path);
                File.Move(part, path);
                return path;
            }
        }
        // setup: the new setup.exe upgrades this install in place with no pages, then starts the app
        // again (/restart=1; /sidebyside=1 keeps a side-by-side copy side by side). The app quits
        // right after this so setup can replace its files.
        public static void LaunchSetup(string setupPath, bool sideBySide) {
            string log = Path.Combine(Path.GetDirectoryName(setupPath), "setup.log");
            string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /restart=1" + (sideBySide ? " /sidebyside=1" : "") + " /LOG=\"" + log + "\"";
            Process.Start(new ProcessStartInfo(setupPath, arguments) { UseShellExecute = true });
        }
        // Files a portable folder keeps whatever the zip holds.
        private static bool Keep(string relative) {
            string r = relative.Replace('/', '\\');
            return r.StartsWith("data\\", StringComparison.OrdinalIgnoreCase) || r.Equals("portable.txt", StringComparison.OrdinalIgnoreCase) || r.Equals("installed.txt", StringComparison.OrdinalIgnoreCase);
        }
        public const string OldSuffix = ".update-old";
        // portable: unpack the zip beside, then for each of its files move the current one aside
        // (<name>.update-old; a running exe can be renamed though not overwritten) and copy the
        // new one in. Any failure moves everything back, so the folder is either all old or all new.
        public static void ApplyPortable(string zipPath, string root) {
            string stage = Path.Combine(WorkFolder, "stage-" + Guid.NewGuid().ToString("N"));
            var moved = new List<string>(); var created = new List<string>();
            try {
                ZipFile.ExtractToDirectory(zipPath, stage);
                List<string> files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Select(f => f.Substring(stage.Length).TrimStart('\\')).Where(f => !Keep(f)).ToList();
                if (!files.Any(f => f.Equals("codeusagemonit.exe", StringComparison.OrdinalIgnoreCase))) throw new ProviderException(I18n.T("下载的压缩包里没有 codeusagemonit.exe。"), "error");
                try {
                    foreach (string relative in files) {
                        string target = Path.Combine(root, relative), staged = Path.Combine(stage, relative);
                        // Unchanged files stay as they are (and cannot hold the update up).
                        if (SameContent(target, staged)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target)) {
                            string aside = target + OldSuffix;
                            if (File.Exists(aside)) File.Delete(aside);
                            File.Move(target, aside); moved.Add(target);
                        } else created.Add(target);
                        File.Copy(staged, target);
                    }
                } catch {
                    foreach (string target in created) { try { File.Delete(target); } catch { } }
                    foreach (string target in moved) { try { if (File.Exists(target)) File.Delete(target); File.Move(target + OldSuffix, target); } catch { } }
                    throw;
                }
            } finally { try { Directory.Delete(stage, true); } catch { } }
        }
        private static bool SameContent(string a, string b) {
            try {
                if (!File.Exists(a) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
                using (var sha = SHA256.Create()) {
                    byte[] x, y;
                    using (var s = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) x = sha.ComputeHash(s);
                    using (var s = File.OpenRead(b)) y = sha.ComputeHash(s);
                    return x.SequenceEqual(y);
                }
            } catch { return false; }
        }
        // After an update: the previous program files (the old exe was still running when it was
        // moved aside)…
        public static void CleanUp(string root) {
            try {
                foreach (string file in Directory.EnumerateFiles(root, "*" + OldSuffix, SearchOption.AllDirectories).ToList()) {
                    if (file.Substring(root.Length).TrimStart('\\').StartsWith("data\\", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
            } catch { }
        }
        // …and the downloads.
        public static void ClearDownloads() { try { if (Directory.Exists(WorkFolder)) Directory.Delete(WorkFolder, true); } catch { } }
    }
}
