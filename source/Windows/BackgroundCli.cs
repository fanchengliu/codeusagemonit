using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeUsageMonit {
    // Runs a platform's official CLI out of sight so that the CLI itself keeps its sign-in fresh
    // (Claude Code, Grok, Antigravity's agy): this program never refreshes or rewrites another
    // client's tokens. Each run is hidden, time-limited and placed in a Windows job, so the CLI
    // and anything it starts end with it; runs for one platform never overlap.
    public static class BackgroundCli {
        public sealed class Result { public int ExitCode = -1; public string Output = ""; public bool TimedOut; }
        private static readonly Dictionary<string, SemaphoreSlim> gates = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private static SemaphoreSlim Gate(string platform) { lock (gates) { SemaphoreSlim g; if (!gates.TryGetValue(platform, out g)) gates[platform] = g = new SemaphoreSlim(1, 1); return g; } }
        private const int MaxOutput = 1 << 20;

        // An executable on PATH (with PATHEXT), e.g. "claude" → C:\…\claude.cmd. Null when absent.
        public static string Find(string name) {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';').Where(e => e.Length > 0).ToArray();
            foreach (string dir in path.Split(';')) {
                string d = dir.Trim().Trim('"'); if (d.Length == 0) continue;
                foreach (string ext in new[] { ".exe", ".cmd", ".bat" }.Concat(extensions.Select(e => e.ToLowerInvariant())).Distinct()) {
                    try { string candidate = Path.Combine(d, name + ext); if (File.Exists(candidate)) return candidate; } catch { }
                }
            }
            return null;
        }
        private static ProcessStartInfo Info(string executable, string arguments) {
            bool script = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            return script ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + executable + "\" " + arguments + "\"")
                          : new ProcessStartInfo(executable, arguments);
        }

        // Runs to completion (or the timeout) with output captured: `grok models`, `agy -p /usage`.
        // The working directory is a fresh empty folder; standard input is closed.
        public static async Task<Result> Run(string platform, string executable, string arguments, int timeoutSeconds, IDictionary<string, string> environment) {
            SemaphoreSlim gate = Gate(platform);
            await gate.WaitAsync().ConfigureAwait(false);
            string scratch = Path.Combine(Path.GetTempPath(), "codeusagemonit-cli-" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(scratch);
                return await Task.Run(() => {
                    ProcessStartInfo info = Info(executable, arguments);
                    info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden; info.WorkingDirectory = scratch;
                    info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
                    info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
                    // Set for this child only, never in our own environment.
                    if (environment != null) foreach (var item in environment) info.EnvironmentVariables[item.Key] = item.Value;
                    var result = new Result(); var output = new StringBuilder();
                    using (var job = new Job())
                    using (var process = new Process { StartInfo = info }) {
                        DataReceivedEventHandler collect = (s, e) => { if (e.Data == null) return; lock (output) { if (output.Length < MaxOutput) output.AppendLine(e.Data); } };
                        process.OutputDataReceived += collect; process.ErrorDataReceived += collect;
                        process.Start(); job.Add(process);
                        try { process.StandardInput.Close(); } catch { }
                        process.BeginOutputReadLine(); process.BeginErrorReadLine();
                        if (!process.WaitForExit(timeoutSeconds * 1000)) { result.TimedOut = true; job.Dispose(); try { process.Kill(); } catch { } }
                        else { process.WaitForExit(); result.ExitCode = process.ExitCode; }
                    }
                    lock (output) result.Output = output.ToString();
                    return result;
                }).ConfigureAwait(false);
            } finally {
                gate.Release();
                try { Directory.Delete(scratch, true); } catch { }
            }
        }
        // Starts an interactive CLI in a hidden console (some CLIs refresh their sign-in only
        // when started interactively) and ends it as soon as done() is true or the timeout passes.
        // Nothing is typed into it. Returns whether done() became true.
        public static async Task<bool> RunUntil(string platform, string executable, string arguments, string workingDirectory, Func<bool> done, int timeoutSeconds) {
            SemaphoreSlim gate = Gate(platform);
            await gate.WaitAsync().ConfigureAwait(false);
            try {
                return await Task.Run(() => {
                    ProcessStartInfo info = Info(executable, arguments);
                    info.UseShellExecute = true; info.WindowStyle = ProcessWindowStyle.Hidden; info.WorkingDirectory = workingDirectory;
                    using (var job = new Job())
                    using (Process process = Process.Start(info)) {
                        if (process == null) return false;
                        job.Add(process);
                        var clock = Stopwatch.StartNew();
                        bool ok = false;
                        while (clock.Elapsed.TotalSeconds < timeoutSeconds) {
                            if (done()) { ok = true; break; }
                            if (process.HasExited) { ok = done(); break; }
                            Thread.Sleep(400);
                        }
                        // A moment for the CLI to finish writing, then end it and everything it started.
                        if (ok) Thread.Sleep(800);
                        job.Dispose(); try { if (!process.HasExited) process.Kill(); } catch { }
                        return ok;
                    }
                }).ConfigureAwait(false);
            } finally { gate.Release(); }
        }

        // A Windows job that ends its processes when it is closed.
        private sealed class Job : IDisposable {
            private IntPtr handle;
            public Job() {
                handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == IntPtr.Zero) return;
                var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)); IntPtr buffer = Marshal.AllocHGlobal(size);
                try { Marshal.StructureToPtr(limits, buffer, false); SetInformationJobObject(handle, 9, buffer, (uint)size); } finally { Marshal.FreeHGlobal(buffer); }
            }
            public void Add(Process process) { if (handle != IntPtr.Zero) { try { AssignProcessToJobObject(handle, process.Handle); } catch (Win32Exception) { } catch (InvalidOperationException) { } } }
            public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
            [StructLayout(LayoutKind.Sequential)] private struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
            [StructLayout(LayoutKind.Sequential)] private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
            [StructLayout(LayoutKind.Sequential)] private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
