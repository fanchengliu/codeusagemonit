using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CodeUsageMonit {
    // API list prices for estimating what local usage would cost on the vendors' APIs.
    // The table (pricing.json next to the executable, USD per million tokens) is compiled
    // from the public LiteLLM and models.dev catalogs; nothing here calls another tool.
    public sealed class ModelPrice {
        public string Key = "";
        public double In, Out, CacheRead, CacheWrite, Fast = 1;
        public bool CacheReadExplicit, CacheWriteExplicit;
        // Long-context tier. Marginal: only tokens beyond the threshold, per token bucket
        // (LiteLLM *_above_200k). Otherwise the whole request moves to the tier once its
        // context exceeds the threshold (vendor "context" tiers, e.g. OpenAI 272K).
        public long LongAt; public bool LongMarginal;
        public double? LongIn, LongOut, LongCacheRead, LongCacheWrite;
        public bool HasLong { get { return LongAt > 0 && (LongIn.HasValue || LongOut.HasValue || LongCacheRead.HasValue || LongCacheWrite.HasValue); } }
    }
    // One request's tokens in the Claude shape: input excludes cache reads and writes.
    public struct TokenUse {
        public double Input, Output, CacheRead, CacheWrite5m, CacheWrite1h;
        public double CacheWrite { get { return CacheWrite5m + CacheWrite1h; } }
        public double Total { get { return Input + Output + CacheRead + CacheWrite; } }
    }
    public static class Pricing {
        private static Dictionary<string, ModelPrice> table = new Dictionary<string, ModelPrice>();
        private static Dictionary<string, string> normalized = new Dictionary<string, string>();
        private static readonly Dictionary<string, ModelPrice> cache = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
        private static readonly object gate = new object();
        public static string Version = "";
        public static int Count { get { return table.Count; } }
        private static bool loaded;

        public static void EnsureLoaded() { if (!loaded) Load(Path.Combine(Store.Root, "pricing.json")); }
        public static void Load(string path) {
            lock (gate) {
                loaded = true;
                try { if (File.Exists(path)) LoadJson(File.ReadAllText(path)); } catch { }
            }
        }
        public static void LoadJson(string json) {
            loaded = true;
            lock (gate) {
                object root = J.Parse(json);
                var next = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
                foreach (var pair in J.Dict(J.Get(root, "models"))) {
                    object v = pair.Value; double? input = J.Num(v, "in"), output = J.Num(v, "out");
                    if (!input.HasValue || !output.HasValue) continue;
                    double m = 1e-6;
                    var p = new ModelPrice { Key = pair.Key.ToLowerInvariant(), In = input.Value * m, Out = output.Value * m };
                    double? cr = J.Num(v, "cr"), cw = J.Num(v, "cw"), fast = J.Num(v, "fast");
                    p.CacheReadExplicit = cr.HasValue; p.CacheRead = cr.HasValue ? cr.Value * m : p.In * 0.1;
                    p.CacheWriteExplicit = cw.HasValue; p.CacheWrite = cw.HasValue ? cw.Value * m : p.In * 1.25;
                    if (fast.HasValue && fast.Value > 0) p.Fast = fast.Value;
                    object longRate = J.Get(v, "long");
                    if (longRate != null) {
                        double? at = J.Num(longRate, "at"); p.LongAt = at.HasValue ? (long)at.Value : 0;
                        p.LongMarginal = J.Get(longRate, "marginal") is bool && (bool)J.Get(longRate, "marginal");
                        p.LongIn = Scale(J.Num(longRate, "in")); p.LongOut = Scale(J.Num(longRate, "out"));
                        p.LongCacheRead = Scale(J.Num(longRate, "cr")); p.LongCacheWrite = Scale(J.Num(longRate, "cw"));
                    }
                    next[p.Key] = p;
                }
                table = next;
                normalized = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string key in next.Keys) { string n = NormalizeKey(key); if (!normalized.ContainsKey(n)) normalized[n] = key; }
                cache.Clear();
                // The content hash makes a hand-edited price rebuild the indexes too.
                string hash; using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json)), 0, 4).Replace("-", "").ToLowerInvariant();
                string updated = J.Str(root, "updated"); Version = updated + "/" + next.Count + "/" + hash;
            }
        }
        private static double? Scale(double? perMillion) { return perMillion.HasValue ? perMillion.Value * 1e-6 : (double?)null; }

        // ── Model lookup ──────────────────────────────────────────────────
        // Exact id first, then without the provider prefix or release date, then with
        // '.'/'@' read as '-', and finally the longest catalogue id that the name contains
        // at word boundaries (so "gpt-5.4(xhigh)" prices as gpt-5.4, never gpt-5).
        public static ModelPrice Find(string model) {
            if (String.IsNullOrWhiteSpace(model)) return null;
            EnsureLoaded();
            lock (gate) {
                ModelPrice hit; if (cache.TryGetValue(model, out hit)) return hit;
                hit = Lookup(model.Trim().ToLowerInvariant());
                cache[model] = hit; return hit;
            }
        }
        // Exact match only (after case folding); used where a fuzzy hit would be wrong.
        public static ModelPrice FindExact(string model) {
            if (String.IsNullOrWhiteSpace(model)) return null;
            EnsureLoaded();
            lock (gate) { ModelPrice p; return table.TryGetValue(model.Trim().ToLowerInvariant(), out p) ? p : null; }
        }
        private static ModelPrice Lookup(string model) {
            ModelPrice p;
            foreach (string candidate in Candidates(model)) {
                if (table.TryGetValue(candidate, out p)) return p;
                string key; if (normalized.TryGetValue(NormalizeKey(candidate), out key)) return table[key];
            }
            string best = null; string target = NormalizeKey(model);
            foreach (var pair in normalized) {
                if (pair.Key.Length < 3 || (best != null && pair.Key.Length <= best.Length)) continue;
                if (ContainsKey(target, pair.Key)) best = pair.Key;
            }
            return best == null ? null : table[normalized[best]];
        }
        private static IEnumerable<string> Candidates(string model) {
            yield return model;
            string bare = model.Contains('/') ? model.Substring(model.LastIndexOf('/') + 1) : model;
            if (bare != model) yield return bare;
            string undated = WithoutDate(bare);
            if (undated != bare) yield return undated;
        }
        public static string WithoutDate(string model) {
            if (model.Length > 11) {
                string s = model.Substring(model.Length - 11);
                if (s[0] == '-' && s.Substring(1, 4).All(Char.IsDigit) && s[5] == '-' && s.Substring(6, 2).All(Char.IsDigit) && s[8] == '-' && s.Substring(9).All(Char.IsDigit)) return model.Substring(0, model.Length - 11);
            }
            if (model.Length > 9) {
                string s = model.Substring(model.Length - 9);
                if (s[0] == '-' && s.Substring(1).All(Char.IsDigit)) return model.Substring(0, model.Length - 9);
            }
            return model;
        }
        private static string NormalizeKey(string value) { return value.Replace('.', '-').Replace('@', '-'); }
        // key appears in value with non-alphanumeric boundaries, and is not followed by a
        // further version number ("gpt-5" must not match "gpt-5-4").
        private static bool ContainsKey(string value, string key) {
            int index = -1;
            while ((index = value.IndexOf(key, index + 1, StringComparison.Ordinal)) >= 0) {
                if (index > 0 && Char.IsLetterOrDigit(value[index - 1])) continue;
                int end = index + key.Length;
                if (end == value.Length) return true;
                char next = value[end];
                if (Char.IsLetterOrDigit(next)) continue;
                if (Char.IsDigit(key[key.Length - 1]) && next == '-' && end + 1 < value.Length && Char.IsDigit(value[end + 1])) {
                    int digits = 0; while (end + 1 + digits < value.Length && Char.IsDigit(value[end + 1 + digits])) digits++;
                    bool dateSuffix = digits == 8 && (end + 1 + digits == value.Length || !Char.IsLetterOrDigit(value[end + 1 + digits]));
                    if (!dateSuffix) continue;
                }
                return true;
            }
            return false;
        }

        // ── Cost of one request ───────────────────────────────────────────
        // Claude-shaped usage (Claude Code, Antigravity, ZCode, Grok, Kimi, …). A 1-hour
        // cache write costs twice the input rate.
        public static double Cost(ModelPrice p, TokenUse u) {
            if (p == null) return 0;
            double write1h = p.In * 2, write1hLong = p.LongIn.HasValue ? p.LongIn.Value * 2 : write1h;
            if (p.HasLong && !p.LongMarginal) {
                bool longContext = u.Input + u.CacheRead + u.CacheWrite > p.LongAt;
                Func<double, double?, double> rate = (b, a) => longContext && a.HasValue ? a.Value : b;
                return u.Input * rate(p.In, p.LongIn) + u.Output * rate(p.Out, p.LongOut) + u.CacheWrite5m * rate(p.CacheWrite, p.LongCacheWrite)
                    + u.CacheWrite1h * (longContext && p.LongIn.HasValue ? write1hLong : write1h) + u.CacheRead * rate(p.CacheRead, p.LongCacheRead);
            }
            double at = p.HasLong ? p.LongAt : 200000;
            return Tiered(u.Input, p.In, p.LongIn, at) + Tiered(u.Output, p.Out, p.LongOut, at) + Tiered(u.CacheWrite5m, p.CacheWrite, p.LongCacheWrite, at)
                + Tiered(u.CacheWrite1h, write1h, p.LongIn.HasValue ? write1hLong : (double?)null, at) + Tiered(u.CacheRead, p.CacheRead, p.LongCacheRead, at);
        }
        private static double Tiered(double tokens, double rate, double? above, double at) {
            if (tokens <= 0) return 0;
            if (above.HasValue && tokens > at) return at * rate + (tokens - at) * above.Value;
            return tokens * rate;
        }
        // OpenAI-shaped request (Codex): input includes cached input. A request whose input
        // exceeds the model's context threshold (200K when none is published) is billed
        // entirely at the long-context rates; cached input without its own rate costs the
        // full input rate.
        public static double CodexCost(ModelPrice p, double input, double cached, double cacheWrite, double output) {
            if (p == null) return 0;
            double cachedRate = p.CacheReadExplicit ? p.CacheRead : p.In;
            bool longContext = input > (p.LongAt > 0 ? p.LongAt : 200000);
            double inRate = p.In, outRate = p.Out, writeRate = p.CacheWrite;
            if (longContext) {
                inRate = p.LongIn ?? p.In; outRate = p.LongOut ?? p.Out; writeRate = p.LongCacheWrite ?? p.CacheWrite;
                cachedRate = p.CacheReadExplicit ? (p.LongCacheRead ?? p.CacheRead) : inRate;
            }
            cached = Math.Min(cached, input); cacheWrite = Math.Min(cacheWrite, input - cached);
            return (input - cached - cacheWrite) * inRate + cached * cachedRate + cacheWrite * writeRate + output * outRate;
        }
    }
}
