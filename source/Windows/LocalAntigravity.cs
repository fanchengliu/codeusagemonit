using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    internal static class LocalAntigravity {
        internal sealed class Server { public uint Pid; public string Csrf; }
        public static string FindApplication() {
            string[] paths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\antigravity\Antigravity.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Antigravity\Antigravity.exe")
            };
            return paths.FirstOrDefault(File.Exists) ?? "";
        }
        public static Task<ProviderState> TryRead() {
            return Task.Run(() => {
                var clock = Stopwatch.StartNew();
                try {
                    List<Server> servers = FindServers((path, command) => path.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0 || command.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith(@"\agy.exe", StringComparison.OrdinalIgnoreCase));
                    foreach (Server server in servers.Take(2)) foreach (int port in ListenerPorts(server.Pid).Take(3)) {
                        foreach (string scheme in new[] { "https", "http" }) foreach (string method in new[] { "RetrieveUserQuotaSummary", "GetUserStatus", "GetCommandModelConfigs" }) {
                            if (clock.ElapsedMilliseconds > 7000) return null;
                            try {
                                object response = Query(port, scheme, method, server.Csrf);
                                ProviderState parsed = Parse(response);
                                if (parsed.Quotas.Count == 0) continue;
                                // The quota summary has no subscription or model names; GetUserStatus has both.
                                object status = method == "GetUserStatus" ? response : null;
                                if (status == null && clock.ElapsedMilliseconds < 7000) { try { status = Query(port, scheme, "GetUserStatus", server.Csrf); } catch { } }
                                if (status != null) { ApplyUserStatus(parsed, status); AntigravityModels.Learn(status); }
                                return parsed;
                            } catch { }
                        }
                    }
                } catch { }
                return null;
            });
        }
        // Language servers (Codeium-based: Antigravity, Devin / Windsurf) of the current user whose
        // executable path or command line passes accept, with the CSRF token they were started with.
        internal static List<Server> FindServers(Func<string, string, bool> accept) {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            var servers = new List<Server>();
            // Handle is Win32_Process's key: without it the results have no WMI path and
            // GetOwnerSid cannot be invoked on them.
            using (var search = new ManagementObjectSearcher("SELECT Handle,ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process WHERE Name LIKE 'language_server%' OR Name='agy.exe'")) {
                foreach (ManagementObject process in search.Get()) using (process) {
                    string path = Convert.ToString(process["ExecutablePath"]), command = Convert.ToString(process["CommandLine"]);
                    if (!accept(path, command)) continue;
                    using (ManagementBaseObject owner = process.InvokeMethod("GetOwnerSid", null, null)) { if (Convert.ToString(owner["Sid"]) != sid) continue; }
                    var match = Regex.Match(command, @"--(?:csrf_token|csrf-token)(?:=|\s+)""?([A-Za-z0-9._-]{8,256})");
                    if (!match.Success) continue;
                    servers.Add(new Server { Pid = Convert.ToUInt32(process["ProcessId"]), Csrf = match.Groups[1].Value });
                }
            }
            return servers;
        }
        // The only TLS exception is this authenticated, PID-owned loopback service's
        // self-signed certificate. External HTTPS requests retain normal validation.
        internal static object Query(int port, string scheme, string method, string csrf) {
            var uri = new Uri(scheme + "://127.0.0.1:" + port + "/exa.language_server_pb.LanguageServerService/" + method);
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = null; request.AllowAutoRedirect = false;
            request.Method = "POST"; request.ContentType = "application/json"; request.Accept = "application/json";
            request.Timeout = 1500; request.ReadWriteTimeout = 1500;
            request.Headers["X-Codeium-Csrf-Token"] = csrf;
            request.Headers["Connect-Protocol-Version"] = "1";
            if (scheme == "https") request.ServerCertificateValidationCallback = (sender, cert, chain, errors) => {
                var local = sender as HttpWebRequest;
                return local != null && local.RequestUri.Host == "127.0.0.1" && local.RequestUri.Port == port;
            };
            byte[] body = Encoding.UTF8.GetBytes("{}"); request.ContentLength = body.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            using (var response = (HttpWebResponse)request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) {
                string json = reader.ReadToEnd(); if (json.Length > 1024 * 1024) return null;
                return J.Parse(json);
            }
        }
        public static ProviderState Parse(object root) {
            // RetrieveUserQuotaSummary answers locally as {"response":{"groups":[…]}}.
            object summary = J.Get(root, "quotaSummary") ?? J.Get(root, "userQuotaSummary") ?? J.Get(root, "response") ?? root;
            ProviderState state = Parsers.Antigravity(summary);
            object user = J.Get(root, "userStatus");
            ApplyUserStatus(state, root);
            if (state.Quotas.Count == 0) {
                object models = J.Get(user, "cascadeModelConfigData", "clientModelConfigs") ?? J.Get(root, "clientModelConfigs");
                foreach (object model in J.Arr(models)) {
                    double? remaining = J.Num(model, "quotaInfo", "remainingFraction");
                    string name = J.Str(model, "label"); if (name.Length == 0) name = J.Str(model, "modelId");
                    if (remaining.HasValue) Parsers.Add(state, name.Length > 0 ? name : "模型额度", (1 - remaining.Value) * 100, J.Get(model, "quotaInfo", "resetTime"));
                }
            }
            return state;
        }
        // Account and subscription from a GetUserStatus answer. The subscription is userTier
        // ("Google AI Pro", "Google AI Ultra"); planStatus.planInfo.planName is an older plan field
        // that says "Pro" whatever the subscription, so it is only a fallback.
        public static void ApplyUserStatus(ProviderState state, object root) {
            object user = J.Get(root, "userStatus"); if (user == null) return;
            string account = J.Str(user, "email"); if (account.Length == 0) account = J.Str(user, "userInfo", "email");
            if (account.Length > 0) state.Account = account;
            string plan = J.Str(user, "userTier", "name");
            if (plan.Length == 0) plan = J.Str(user, "planStatus", "planInfo", "planName");
            if (plan.Length == 0) plan = J.Str(user, "planStatus", "planInfo", "displayName");
            if (plan.Length > 0) state.Plan = plan;
        }
        [StructLayout(LayoutKind.Sequential)] private struct TcpRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid; }
        [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int length, bool order, int family, int tableClass, uint reserved);
        internal static List<int> ListenerPorts(uint pid) {
            var result = new List<int>(); int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            if (size < 4 || size > 16 * 1024 * 1024) return result;
            IntPtr memory = Marshal.AllocHGlobal(size);
            try {
                if (GetExtendedTcpTable(memory, ref size, false, 2, 3, 0) != 0) return result;
                int rows = Marshal.ReadInt32(memory), stride = Marshal.SizeOf(typeof(TcpRow));
                for (int i = 0; i < rows && 4 + (i + 1) * stride <= size; i++) {
                    var row = (TcpRow)Marshal.PtrToStructure(IntPtr.Add(memory, 4 + i * stride), typeof(TcpRow));
                    if (row.Pid != pid || row.State != 2) continue;
                    result.Add((ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort));
                }
            } finally { Marshal.FreeHGlobal(memory); }
            return result.Distinct().ToList();
        }
    }
    // Model names as the running language server lists them (GetUserStatus →
    // cascadeModelConfigData.clientModelConfigs: MODEL_PLACEHOLDER_M417 → "Gemini 4 Argon (High)").
    // Antigravity's logs record only the numeric id, so a model newer than this program showed up
    // as model_placeholder_m417; the names are kept in data\antigravity-models.json for the log scan.
    internal static class AntigravityModels {
        private const string FileName = "antigravity-models.json";
        private static readonly object gate = new object();
        private static Dictionary<string, string> known;
        public static Dictionary<string, string> Extract(object root) {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            object models = J.Get(root, "userStatus", "cascadeModelConfigData", "clientModelConfigs") ?? J.Get(root, "clientModelConfigs");
            foreach (object model in J.Arr(models)) {
                string id = J.Str(model, "modelOrAlias", "model").Trim().ToLowerInvariant(), label = J.Str(model, "label").Trim();
                if (id.StartsWith("model_", StringComparison.Ordinal) && id.Length <= 80 && label.Length > 0 && label.Length <= 80 && !label.StartsWith("model_", StringComparison.OrdinalIgnoreCase)) found[id] = label;
            }
            return found;
        }
        public static void Learn(object root) {
            Dictionary<string, string> found = Extract(root); if (found.Count == 0) return;
            lock (gate) {
                Dictionary<string, string> all = Load(); bool changed = false;
                foreach (var pair in found) { string old; if (!all.TryGetValue(pair.Key, out old) || old != pair.Value) { all[pair.Key] = pair.Value; changed = true; } }
                if (changed) try { Store.Write(FileName, all); } catch { }
            }
        }
        // "model_placeholder_m417" → "Gemini 4 Argon (High)", or null.
        public static string Label(string id) { lock (gate) { string label; return Load().TryGetValue(id ?? "", out label) ? label : null; } }
        private static Dictionary<string, string> Load() {
            if (known == null) known = new Dictionary<string, string>(Store.Read<Dictionary<string, string>>(FileName) ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            return known;
        }
        internal static void Reset() { lock (gate) known = null; }
        // Self-tests: names in memory only, nothing written.
        internal static void Use(Dictionary<string, string> labels) { lock (gate) known = new Dictionary<string, string>(labels, StringComparer.Ordinal); }
    }
}
