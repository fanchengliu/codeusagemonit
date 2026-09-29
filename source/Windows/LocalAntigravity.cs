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
        private sealed class Server { public uint Pid; public string Csrf; }
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
                    string sid = WindowsIdentity.GetCurrent().User.Value;
                    var servers = new List<Server>();
                    using (var search = new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process WHERE Name LIKE 'language_server%' OR Name='agy.exe'")) {
                        foreach (ManagementObject process in search.Get()) using (process) {
                            string path = Convert.ToString(process["ExecutablePath"]), command = Convert.ToString(process["CommandLine"]);
                            if (path.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) < 0 && command.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) < 0 && !path.EndsWith(@"\agy.exe", StringComparison.OrdinalIgnoreCase)) continue;
                            using (ManagementBaseObject owner = process.InvokeMethod("GetOwnerSid", null, null)) { if (Convert.ToString(owner["Sid"]) != sid) continue; }
                            var match = Regex.Match(command, @"--(?:csrf_token|csrf-token)(?:=|\s+)""?([A-Za-z0-9._-]{8,256})");
                            if (!match.Success) continue;
                            servers.Add(new Server { Pid = Convert.ToUInt32(process["ProcessId"]), Csrf = match.Groups[1].Value });
                        }
                    }
                    foreach (Server server in servers.Take(2)) foreach (int port in ListenerPorts(server.Pid).Take(3)) {
                        foreach (string scheme in new[] { "https", "http" }) foreach (string method in new[] { "RetrieveUserQuotaSummary", "GetUserStatus", "GetCommandModelConfigs" }) {
                            if (clock.ElapsedMilliseconds > 7000) return null;
                            try {
                                object response = Query(port, scheme, method, server.Csrf);
                                ProviderState parsed = Parse(response);
                                if (parsed.Quotas.Count > 0) return parsed;
                            } catch { }
                        }
                    }
                } catch { }
                return null;
            });
        }
        // The only TLS exception is this authenticated, PID-owned loopback service's
        // self-signed certificate. External HTTPS requests retain normal validation.
        private static object Query(int port, string scheme, string method, string csrf) {
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
            object summary = J.Get(root, "quotaSummary") ?? J.Get(root, "userQuotaSummary") ?? root;
            ProviderState state = Parsers.Antigravity(summary);
            object user = J.Get(root, "userStatus");
            state.Account = J.Str(user, "email");
            if (state.Account.Length == 0) state.Account = J.Str(user, "userInfo", "email");
            string plan = J.Str(user, "planStatus", "planInfo", "planName");
            if (plan.Length == 0) plan = J.Str(user, "planStatus", "planInfo", "displayName");
            if (plan.Length > 0) state.Plan = plan;
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
        [StructLayout(LayoutKind.Sequential)] private struct TcpRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid; }
        [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int length, bool order, int family, int tableClass, uint reserved);
        private static List<int> ListenerPorts(uint pid) {
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
}
