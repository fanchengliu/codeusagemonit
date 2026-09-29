// Local fixture used only by verify-regression.ps1 in its isolated output directory.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
[assembly: AssemblyTitle("codeusagemonit offline history fixture")]
public static class FakeHistory {
    public static void Main(string[] args) {
        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-calls.txt"), String.Join(" ", args) + Environment.NewLine);
        Thread.Sleep(120);
        int start = Array.IndexOf(args, "--since"); string day = start >= 0 ? args[start + 1] : "2026-01-01";
        Console.WriteLine(new JavaScriptSerializer().Serialize(new { daily = new[] { new { period = day, agents = new[] { new { agent = "pi", totalTokens = 100, totalCost = 1.0 } } } } }));
    }
}
