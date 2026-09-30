using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodeUsageMonit {
    internal static class GrokTests {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileW(string path);
        private static void Check(bool condition) { if (!condition) throw new Exception("Feature assertion failed"); }
        internal static void Run(Action<string, Action> test) {
            test("Grok Bot transcript paths beyond MAX_PATH remain readable", () => {
                string folder = Path.Combine(Store.Root, "verification", "long-path-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, new string('t', 200) + ".blob"), extended = @"\\?\" + path;
                try {
                    using (var handle = CreateFileW(extended, 0x40000000, 7, IntPtr.Zero, 2, 0x80, IntPtr.Zero)) {
                        Check(!handle.IsInvalid && path.Length > 260);
                        using (var stream = new FileStream(handle, FileAccess.Write)) {
                            byte[] content = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"value\":{\"entries\":[]}}"); stream.Write(content, 0, content.Length);
                        }
                    }
                    Check(J.Num(GrokBotActivity.ReadTranscript(path), "schemaVersion") == 1);
                } finally { DeleteFileW(extended); Directory.Delete(folder); }
            });
            test("Grok Bot RPC failure preserves Cursor main quota", () => {
                var fake = new CursorHandler(true);
                using (var service = new ProviderService(new AppConfig { Proxy = "direct" }, fake)) {
                    var result = service.FetchCursorUsage("fixture", "fixture-user").GetAwaiter().GetResult();
                    Check(result.Quotas.Count == 1 && result.Quotas[0].Remaining == 70 && result.GrokBotError.Length > 0 && fake.ValidBotRequest);
                }
            });
            test("Grok Bot follows Cursor main quota with its independent reset", () => {
                var fake = new CursorHandler(false);
                using (var service = new ProviderService(new AppConfig { Proxy = "direct" }, fake)) {
                    var result = service.FetchCursorUsage("fixture", "fixture-user").GetAwaiter().GetResult();
                    Check(result.Quotas.Count == 2 && result.Quotas[1].Label.Contains("Grok Bot") && result.Quotas[1].Remaining == 92 && result.Quotas[0].Remaining == 70 && fake.ValidBotRequest);
                    Check(result.Quotas[1].ResetUtc == "2026-10-05T00:00:00.0000000Z" && result.Quotas[1].ResetUtc != result.Quotas[0].ResetUtc && result.Quotas[1].WindowSeconds == 604800);
                }
            });
            test("Grok Bot missing and pooled allowance never become a full personal quota", () => {
                foreach (string data in new[] { "{}", "{\"usagePercent\":null}", "{\"usagePercent\":-1}", "{\"usagePercent\":0,\"usesPooledEnterpriseAllowance\":true}", "{\"usagePercent\":0,\"hasNonZeroIncludedLimit\":false}", "{\"usagePercent\":0,\"includedLimitZero\":true}" })
                    Check(Parsers.CursorGrokBot(J.Parse(data)) == null);
                Check(Parsers.CursorGrokBot(J.Parse("{\"usagePercent\":0}")).Remaining == 100);
                Check(Parsers.CursorGrokBot(J.Parse("{\"usagePercent\":103}")).Remaining == 0);
            });
            test("Grok Bot timeout preserves the Cursor summary", () => {
                using (var service = new ProviderService(new AppConfig { Proxy = "direct" }, new CursorHandler(false) { CancelBot = true })) {
                    var result = service.FetchCursorUsage("fixture", "fixture-user").GetAwaiter().GetResult();
                    Check(result.Quotas.Count == 1 && result.Quotas[0].Remaining == 70 && result.GrokBotError.Length > 0);
                }
            });
            test("Cached Cursor quotas keep Grok Bot last without changing quota values", () => {
                var bot = new Quota { Label = "Grok Bot · 每周", Used = 8, ResetUtc = "2026-10-05T04:47:41Z" };
                var main = new Quota { Label = "套餐总量", Used = 30 };
                var auto = new Quota { Label = "Auto", Used = 20 };
                var api = new Quota { Label = "API / 手动模型", Used = 40 };
                var state = new ProviderState { Id = "cursor", Quotas = { bot, main, auto, api } };
                Parsers.NormalizeCachedState(state);
                Check(state.Quotas.SequenceEqual(new[] { main, auto, api, bot }) && bot.Remaining == 92 && bot.ResetUtc == "2026-10-05T04:47:41Z");
            });
            test("Grok product consumption stays separate from shared remaining quota", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":55},{\"product\":\"chat\",\"usagePercent\":8}]}}"));
                Check(s.Quotas.Count == 1 && s.Quotas[0].Remaining == 37);
                var shares = J.Arr(J.Get(J.Parse(J.Serializer().Serialize(s)), "ProductUsage")).ToList();
                Check(shares.Count == 2 && J.Num(shares[0], "UsedPercent") == 55 && J.Num(shares[1], "UsedPercent") == 8);
            });
            test("An inconsistent Grok product breakdown does not invent separate quotas", () => {
                var s = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":80}]}}"));
                Check(s.Quotas.Count == 1 && s.Quotas[0].Remaining == 37);
                Check(!J.Arr(J.Get(J.Parse(J.Serializer().Serialize(s)), "ProductUsage")).Any());
            });
            test("Legacy Grok cache removes the fake Build quota and keeps shared usage", () => {
                var state = J.Serializer().Deserialize<ProviderState>("{\"Id\":\"grok\",\"ProductUsage\":null,\"Quotas\":[{\"Label\":\"当前账期\",\"Used\":63},{\"Label\":\"Build 占比\",\"Used\":55}]}");
                Parsers.NormalizeCachedState(state);
                Check(state.Quotas.Count == 1 && state.Quotas[0].Remaining == 37 && state.ProductUsage.Count == 0);
                Parsers.NormalizeCachedState(state);
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
            test("Grok Bot activity excludes pending sends, duplicates and content", () => {
                object doc = J.Parse("{\"value\":{\"entries\":[{\"kind\":\"message\",\"role\":\"user\",\"id\":\"a\",\"timestampMs\":1790672400000,\"content\":\"DO-NOT-COPY\"},{\"kind\":\"message\",\"role\":\"user\",\"id\":\"a\",\"timestampMs\":1790672400000},{\"kind\":\"message\",\"role\":\"assistant\",\"id\":\"b\",\"timestampMs\":1790672400000},{\"kind\":\"send-message\",\"id\":\"pending\",\"timestampMs\":1790672400000}]}}");
                var result = GrokBotActivity.FromTranscripts(new[] { doc }, new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
                Check(result.Sessions == 1 && result.Days.Sum(d => d.UserMessages) == 1 && result.Days.Sum(d => d.AssistantMessages) == 1 && !J.Serializer().Serialize(result).Contains("DO-NOT-COPY"));
            });
            test("Grok Bot cache is scoped to the active account", () => {
                string fixture = Path.Combine(Store.Root, "verification", "bot-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
                try {
                    File.WriteAllText(Path.Combine(fixture, GrokBotActivity.Base32("sand.client.slice.client-meta.account-slot") + ".blob"), "{\"value\":\"test|active\"}");
                    string data = "{\"value\":{\"entries\":[{\"kind\":\"message\",\"role\":\"user\",\"id\":\"a\",\"timestampMs\":1790672400000}]}}";
                    foreach (string account in new[] { "test%7Cactive", "test%7Cother" }) File.WriteAllText(Path.Combine(fixture, GrokBotActivity.Base32("sand.client.slice.account." + account + ".transcript.replicas.session") + ".blob"), data);
                    var result = GrokBotActivity.ReadDirectory(fixture, new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc); Check(result.Sessions == 1 && result.Days.Single().UserMessages == 1);
                } finally { Directory.Delete(fixture, true); }
            });
        }
        private sealed class CursorHandler : HttpMessageHandler {
            private readonly bool fail; public bool ValidBotRequest, CancelBot;
            public CursorHandler(bool shouldFail) { fail = shouldFail; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) {
                if (request.RequestUri.Host == "api2.cursor.sh") {
                    ValidBotRequest = request.Method == HttpMethod.Post && request.Headers.Contains("Connect-Protocol-Version") && request.Headers.Authorization.Parameter == "fixture";
                    if (CancelBot) throw new TaskCanceledException();
                    return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = new StringContent("{\"usagePercent\":8,\"currentPeriodStart\":\"2026-09-28T00:00:00Z\",\"nextResetTimestampUtc\":\"2026-10-05T00:00:00Z\"}") });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"billingCycleEnd\":\"2026-10-31T00:00:00Z\",\"individualUsage\":{\"plan\":{\"totalPercentUsed\":30}}}") });
            }
        }
    }
}
