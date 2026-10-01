using System;
using System.Linq;

namespace CodeUsageMonit {
    // Shared-quota regressions ported from xing-skyline/codeusagemonit PR #1.
    // Cursor Grok Bot coverage lives with the plan meters and is not repeated here.
    internal static class GrokTests {
        private static void Check(bool condition) { if (!condition) throw new Exception("Feature assertion failed"); }
        internal static void Run(Action<string, Action> test) {
            test("Grok product consumption stays separate from shared remaining quota", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":55},{\"product\":\"chat\",\"usagePercent\":8}]}}"));
                Check(s.Quotas.Count == 1 && s.Quotas[0].Remaining == 37);
                var shares = J.Arr(J.Get(J.Parse(J.Serializer().Serialize(s)), "ProductUsage")).ToList();
                Check(shares.Count == 2 && J.Num(shares[0], "UsedPercent") == 55 && J.Num(shares[1], "UsedPercent") == 8);
                Check(!J.Serializer().Serialize(s.Quotas).Contains("Build 占比"));
            });
            test("An inconsistent Grok product breakdown does not invent separate quotas", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":80}]}}"));
                Check(s.Quotas.Count == 1 && s.Quotas[0].Remaining == 37);
                Check(!J.Arr(J.Get(J.Parse(J.Serializer().Serialize(s)), "ProductUsage")).Any());
            });
            test("Legacy Grok cache removes the fake Build quota and keeps shared usage", () => {
                var state = J.Serializer().Deserialize<ProviderState>("{\"Id\":\"grok\",\"ProductUsage\":null,\"Quotas\":[{\"Label\":\"当前账期\",\"Used\":63},{\"Label\":\"Build 占比\",\"Used\":55}]}");
                Parsers.Normalize(state);
                Check(state.Quotas.Count == 1 && state.Quotas[0].Remaining == 37 && state.ProductUsage.Count == 0);
                Parsers.Normalize(state);
                Check(state.Quotas.Count == 1);
            });
            test("Malformed and duplicate Grok shares never replace the shared allowance", () => {
                foreach (string rows in new[] { "null", "[{\"product\":\"build\",\"usagePercent\":null}]", "[{\"product\":\"build\",\"usagePercent\":-2}]", "[{\"product\":\"build\",\"usagePercent\":30},{\"product\":\"BUILD\",\"usagePercent\":33}]" }) {
                    var state = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":" + rows + "}}"));
                    Check(state.Quotas.Count == 1 && state.Quotas[0].Remaining == 37 && state.ProductUsage.Count == 0);
                }
                var rounded = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":55},{\"product\":\"chat\",\"usagePercent\":8.5}]}}"));
                Check(rounded.ProductUsage.Count == 2);
                var restored = J.Serializer().Deserialize<ProviderState>(J.Serializer().Serialize(rounded));
                Check(restored.ProductUsage.Count == 2 && restored.ProductUsage[0].UsedPercent == 55 && restored.Quotas.Count == 1);
            });
            test("Grok CLI text and JSON keep consumption off the remaining-quota list", () => {
                var state = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":55},{\"product\":\"chat\",\"usagePercent\":8}]}}"));
                Parsers.Normalize(state);
                string build = CliProgram.ProductLine(state.ProductUsage[0]);
                string chat = CliProgram.ProductLine(state.ProductUsage[1]);
                Check(build == "Build · 已消耗总额度 55%（共用当前账期额度）" && chat == "Chat · 已消耗总额度 8%（共用当前账期额度）");
                Check(build.IndexOf("剩余", StringComparison.Ordinal) < 0 && chat.IndexOf("剩余", StringComparison.Ordinal) < 0);
                string json = J.Serializer().Serialize(new {
                    windows = state.Quotas.Select(q => new { label = q.Label, remainingPercent = Math.Round(q.Remaining, 2) }),
                    productUsage = CliProgram.ProductJson(state)
                });
                Check(json.Contains("\"label\":\"当前账期\"") && json.Contains("\"remainingPercent\":37") && json.Contains("\"product\":\"build\"") && json.Contains("\"usedPercent\":55") && json.Contains("\"product\":\"chat\"") && json.Contains("\"usedPercent\":8"));
                Check(json.IndexOf("Build 占比", StringComparison.Ordinal) < 0 && json.IndexOf("45", StringComparison.Ordinal) < 0);
                var legacy = J.Serializer().Deserialize<ProviderState>("{\"Id\":\"grok\",\"ProductUsage\":null,\"Quotas\":[{\"Label\":\"当前账期\",\"Used\":63},{\"Label\":\"Build 占比\",\"Used\":55}]}");
                Parsers.Normalize(legacy);
                string legacyJson = J.Serializer().Serialize(new {
                    windows = legacy.Quotas.Select(q => new { label = q.Label, remainingPercent = Math.Round(q.Remaining, 2) }),
                    productUsage = CliProgram.ProductJson(legacy)
                });
                Check(legacy.Quotas.Count == 1 && legacyJson.Contains("\"remainingPercent\":37") && legacyJson.Contains("\"productUsage\":[]") && legacyJson.IndexOf("Build 占比", StringComparison.Ordinal) < 0);
            });
            test("A failed Grok refresh keeps the shared quota and the consumption breakdown", () => {
                var old = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":55},{\"product\":\"chat\",\"usagePercent\":8}]}}"));
                old.Status = "ready"; old.LastSuccess = "2026-09-30T00:00:00Z";
                var incoming = new ProviderState { Id = "grok", Status = "error", Message = "synthetic timeout" };
                Parsers.RetainLastGood(incoming, old);
                Check(incoming.Stale && incoming.Quotas.Count == 1 && incoming.Quotas[0].Remaining == 37 && incoming.ProductUsage.Count == 2 && incoming.ProductUsage[0].UsedPercent == 55 && incoming.LastSuccess == old.LastSuccess);
                var fresh = new ProviderState { Id = "grok", Status = "ready" };
                Parsers.RetainLastGood(fresh, old);
                Check(fresh.Quotas.Count == 0 && fresh.ProductUsage.Count == 0);
            });
        }
    }
}
