using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Updating from inside the app (Updates.cs): release notes, checksums, how each kind of copy
    // updates, the portable swap and its rollback, and downloads from a feed on this computer.
    internal static class UpdateTests {
        private static void Check(bool condition) { if (!condition) throw new Exception("Feature assertion failed"); }
        private static string NewFolder() { string d = Path.Combine(Path.GetTempPath(), "codeusagemonit-updatetest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
        private static void Put(string root, string relative, string text) { string p = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, text); }
        private static string Zip(Dictionary<string, string> files) {
            string stage = NewFolder(), zip = Path.Combine(NewFolder(), "update.zip");
            foreach (var f in files) Put(stage, f.Key, f.Value);
            ZipFile.CreateFromDirectory(stage, zip); Directory.Delete(stage, true);
            return zip;
        }
        internal static void Run(Action<string, Action> test) {
            test("Update notes: only the release's 本版更新 section, as plain lines", () => {
                string body = "# codeusagemonit 1.0.2\r\n\r\n## 本版更新\r\n\r\n- **软件内一键更新**：自动下载\r\n- 用 `scoop update` 更新\r\n\r\n## 简介\r\n\r\n- 不该出现";
                string notes = Updates.NotesFrom(body);
                Check(notes == "· 软件内一键更新：自动下载\n· 用 scoop update 更新");
                Check(Updates.NotesFrom("# codeusagemonit 1.0.1\n\n## 主要功能\n\n- x") == "" && Updates.NotesFrom(null) == "");
                Check(Updates.NotesFrom("## What's new\n* a\n# next") == "· a");
            });
            test("SHA256SUMS.txt: the hash listed for exactly that file name", () => {
                string a = new string('a', 64), b = new string('B', 64);
                string sums = a + "  codeusagemonit-1.0.2-win-x64.zip\n" + b + " *codeusagemonit-setup-1.0.2.exe\r\nxyz  codeusagemonit-setup-9.exe\n";
                Check(Updates.HashFor(sums, "codeusagemonit-1.0.2-win-x64.zip") == a);
                Check(Updates.HashFor(sums, "codeusagemonit-setup-1.0.2.exe") == b.ToLowerInvariant());
                Check(Updates.HashFor(sums, "codeusagemonit-setup-9.exe") == null && Updates.HashFor(sums, "codeusagemonit-1.0.2-win-x64") == null && Updates.HashFor("", "x") == null);
            });
            test("Release file names and URLs follow the release workflow", () => {
                Check(Updates.SetupName("1.0.2") == "codeusagemonit-setup-1.0.2.exe" && Updates.ZipName("1.0.2") == "codeusagemonit-1.0.2-win-x64.zip");
                Check(Updates.DownloadUrl("1.0.2", "SHA256SUMS.txt") == "https://github.com/fanchengliu/codeusagemonit/releases/download/v1.0.2/SHA256SUMS.txt");
            });
            test("A test feed is only accepted on this computer, over plain http", () => {
                try {
                    Environment.SetEnvironmentVariable(Updates.FeedVariable, "http://127.0.0.1:9/");
                    Check(Updates.ApiUrl == "http://127.0.0.1:9/releases/latest" && Updates.DownloadUrl("1.0.2", "a.zip") == "http://127.0.0.1:9/releases/download/v1.0.2/a.zip");
                    foreach (string bad in new[] { "http://example.com/", "https://127.0.0.1:9/", "file:///C:/feed/", "not a url" }) {
                        Environment.SetEnvironmentVariable(Updates.FeedVariable, bad);
                        Check(Updates.ApiUrl == Updates.Api && Updates.DownloadUrl("1.0.2", "a.zip").StartsWith(Updates.DownloadBase));
                    }
                } finally { Environment.SetEnvironmentVariable(Updates.FeedVariable, null); }
            });
            test("A skipped version is not offered again; a newer one is", () => {
                var config = new AppConfig();
                var state = new UpdateState { Latest = "99.0.0" };
                Check(Updates.Newer(state) && Updates.Offer(state, config));
                config.SkippedVersion = "99.0.0"; Check(!Updates.Offer(state, config) && Updates.Newer(state));
                state.Latest = "99.0.1"; Check(Updates.Offer(state, config));
                state.Latest = AppInfo.Version; Check(!Updates.Newer(state) && !Updates.Offer(state, new AppConfig()));
                Check(J.Serializer().Deserialize<AppConfig>("{\"UpdateCheck\":true}").SkippedVersion == "");
            });
            test("How a copy updates: setup install, portable folder or Scoop", () => {
                string installed = NewFolder(), portable = NewFolder();
                try {
                    File.WriteAllText(Path.Combine(installed, "installed.txt"), "");
                    Check(Updates.InstallKind(installed) == "setup" && Updates.InstallKind(portable) == "portable");
                    Check(Updates.InstallKind(@"C:\Users\u\scoop\apps\codeusagemonit\current") == "scoop" && Updates.InstallKind(@"D:\Scoop\Apps\codeusagemonit\1.0.1\") == "scoop");
                    Check(!Directory.GetFiles(portable, ".update-probe-*").Any());
                } finally { Directory.Delete(installed, true); Directory.Delete(portable, true); }
            });
            test("Portable update replaces program files, keeps data and moves the old files aside", () => {
                string root = NewFolder();
                string zip = Zip(new Dictionary<string, string> { { "codeusagemonit.exe", "new exe" }, { "Panel.xaml", "new panel" }, { @"icons\devin.svg", "new icon" }, { @"data\settings.json", "from zip" }, { "installed.txt", "" }, { "portable.txt", "from zip" } });
                try {
                    Put(root, "codeusagemonit.exe", "old exe"); Put(root, "Panel.xaml", "old panel"); Put(root, @"data\settings.json", "mine"); Put(root, "portable.txt", "mine");
                    Updates.ApplyPortable(zip, root);
                    Check(File.ReadAllText(Path.Combine(root, "codeusagemonit.exe")) == "new exe" && File.ReadAllText(Path.Combine(root, "Panel.xaml")) == "new panel" && File.ReadAllText(Path.Combine(root, @"icons\devin.svg")) == "new icon");
                    Check(File.ReadAllText(Path.Combine(root, @"data\settings.json")) == "mine" && File.ReadAllText(Path.Combine(root, "portable.txt")) == "mine" && !File.Exists(Path.Combine(root, "installed.txt")));
                    Check(File.ReadAllText(Path.Combine(root, "codeusagemonit.exe" + Updates.OldSuffix)) == "old exe");
                    Put(root, @"data\notes" + Updates.OldSuffix, "mine too");
                    Updates.CleanUp(root);
                    Check(!File.Exists(Path.Combine(root, "codeusagemonit.exe" + Updates.OldSuffix)) && !File.Exists(Path.Combine(root, "Panel.xaml" + Updates.OldSuffix)) && File.Exists(Path.Combine(root, @"data\notes" + Updates.OldSuffix)));
                } finally { Directory.Delete(root, true); Directory.Delete(Path.GetDirectoryName(zip), true); }
            });
            test("Unchanged files are left alone, even while another reader holds them open", () => {
                string root = NewFolder();
                string zip = Zip(new Dictionary<string, string> { { "codeusagemonit.exe", "new exe" }, { "app.ico", "same icon" } });
                try {
                    Put(root, "codeusagemonit.exe", "old exe"); Put(root, "app.ico", "same icon");
                    // Opened as a BitmapImage opens it: shared for reading only, so it cannot be moved.
                    using (new FileStream(Path.Combine(root, "app.ico"), FileMode.Open, FileAccess.Read, FileShare.Read)) Updates.ApplyPortable(zip, root);
                    Check(File.ReadAllText(Path.Combine(root, "codeusagemonit.exe")) == "new exe" && !File.Exists(Path.Combine(root, "app.ico" + Updates.OldSuffix)));
                } finally { Directory.Delete(root, true); Directory.Delete(Path.GetDirectoryName(zip), true); }
            });
            test("A running exe can be moved aside for the portable swap", () => {
                string root = NewFolder();
                string exe = Path.Combine(root, "codeusagemonit.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);
                string zip = Zip(new Dictionary<string, string> { { "codeusagemonit.exe", "new exe" } });
                var running = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-n 6 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                try {
                    Thread.Sleep(300);
                    Updates.ApplyPortable(zip, root);
                    Check(File.ReadAllText(exe) == "new exe" && File.Exists(exe + Updates.OldSuffix) && !running.HasExited);
                } finally {
                    try { running.Kill(); running.WaitForExit(3000); } catch { }
                    running.Dispose();
                    Directory.Delete(root, true); Directory.Delete(Path.GetDirectoryName(zip), true);
                }
            });
            test("A portable update that fails part-way leaves the folder as it was", () => {
                string root = NewFolder();
                // A folder where the zip has a file makes that copy fail.
                string zip = Zip(new Dictionary<string, string> { { "codeusagemonit.exe", "new exe" }, { "Panel.xaml", "new panel" }, { "pricing.json", "new prices" } });
                string notExe = Zip(new Dictionary<string, string> { { "Panel.xaml", "new panel" } });
                try {
                    Put(root, "codeusagemonit.exe", "old exe"); Put(root, "Panel.xaml", "old panel"); Directory.CreateDirectory(Path.Combine(root, "pricing.json"));
                    bool failed = false; try { Updates.ApplyPortable(zip, root); } catch { failed = true; }
                    Check(failed && File.ReadAllText(Path.Combine(root, "codeusagemonit.exe")) == "old exe" && File.ReadAllText(Path.Combine(root, "Panel.xaml")) == "old panel");
                    Check(!Directory.GetFiles(root, "*" + Updates.OldSuffix, SearchOption.AllDirectories).Any());
                    failed = false; try { Updates.ApplyPortable(notExe, root); } catch (ProviderException) { failed = true; }
                    Check(failed && File.ReadAllText(Path.Combine(root, "Panel.xaml")) == "old panel");
                } finally { Directory.Delete(root, true); Directory.Delete(Path.GetDirectoryName(zip), true); Directory.Delete(Path.GetDirectoryName(notExe), true); }
            });
            test("Downloads are checked against SHA256SUMS.txt; a mismatch is deleted", () => {
                byte[] good = Encoding.UTF8.GetBytes(new string('x', 300000)), bad = Encoding.UTF8.GetBytes("tampered");
                string goodHash = Hash(good);
                var files = new Dictionary<string, byte[]> {
                    { "/releases/download/v98.0.0/SHA256SUMS.txt", Encoding.UTF8.GetBytes(goodHash + "  codeusagemonit-98.0.0-win-x64.zip\n" + goodHash + "  codeusagemonit-setup-98.0.0.exe\n") },
                    { "/releases/download/v98.0.0/codeusagemonit-98.0.0-win-x64.zip", good },
                    { "/releases/download/v98.0.0/codeusagemonit-setup-98.0.0.exe", bad }
                };
                using (var feed = new LocalFeed(files)) {
                    try {
                        Environment.SetEnvironmentVariable(Updates.FeedVariable, feed.Url);
                        var seen = new List<int>(); var progress = new SyncProgress(seen.Add);
                        string path = Updates.Download(new AppConfig { Proxy = "direct" }, "98.0.0", "portable", progress, CancellationToken.None).Result;
                        Check(File.Exists(path) && Updates.Sha256(path) == goodHash && seen.Count > 0 && seen.Last() == 100);
                        bool rejected = false;
                        try { Updates.Download(new AppConfig { Proxy = "direct" }, "98.0.0", "setup", null, CancellationToken.None).Wait(); }
                        catch (AggregateException e) { rejected = e.InnerException is ProviderException; }
                        Check(rejected && !Directory.GetFiles(Path.Combine(Updates.WorkFolder, "98.0.0"), "codeusagemonit-setup-*").Any());
                    } finally {
                        Environment.SetEnvironmentVariable(Updates.FeedVariable, null);
                        try { Directory.Delete(Path.Combine(Updates.WorkFolder, "98.0.0"), true); } catch { }
                    }
                }
            });
        }
        private static string Hash(byte[] data) { using (var sha = System.Security.Cryptography.SHA256.Create()) return String.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2"))); }
        private sealed class SyncProgress : IProgress<int> { private readonly Action<int> report; public SyncProgress(Action<int> r) { report = r; } public void Report(int value) { lock (this) report(value); } }
        // A minimal HTTP/1.1 file server on 127.0.0.1 (one request per connection).
        private sealed class LocalFeed : IDisposable {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Dictionary<string, byte[]> files; private volatile bool stopped;
            public string Url { get { return "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/"; } }
            public LocalFeed(Dictionary<string, byte[]> files) { this.files = files; listener.Start(); Task.Run((Action)Serve); }
            private void Serve() {
                while (!stopped) {
                    TcpClient client; try { client = listener.AcceptTcpClient(); } catch { return; }
                    Task.Run(() => {
                        using (client) using (NetworkStream stream = client.GetStream()) {
                            var reader = new StreamReader(stream, Encoding.ASCII); string first = reader.ReadLine() ?? ""; string line;
                            while (!String.IsNullOrEmpty(line = reader.ReadLine())) { }
                            string[] parts = first.Split(' '); byte[] body;
                            bool found = parts.Length > 1 && files.TryGetValue(parts[1], out body);
                            if (!found) body = Encoding.ASCII.GetBytes("not found");
                            else body = files[parts[1]];
                            byte[] head = Encoding.ASCII.GetBytes((found ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") + "\r\nContent-Length: " + body.Length + "\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                            stream.Write(head, 0, head.Length); stream.Write(body, 0, body.Length); stream.Flush();
                        }
                    });
                }
            }
            public void Dispose() { stopped = true; listener.Stop(); }
        }
    }
}
