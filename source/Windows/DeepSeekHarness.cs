using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeUsageMonit {
    // DeepSeek Harness (dsh, @deepseek-ai/dsh-*): each session appends zstd frames of JSON
    // lines to ~/.dsh/sessions/<workspace>/<session>/session.v4.jsonl.zstd. A model call is
    // a "request/header" (send time, model) followed by an "assistant/message" (usage, time
    // of the last output); its tokens count under DeepSeek, its duration gives the output
    // speed. Only numbers are kept; message text is never stored.
    public static class HarnessLogs {
        public static string Root { get { return Path.Combine(LogReader.Home, ".dsh"); } }
        // DeepSeek Harness has been used on this computer.
        public static bool Present { get { try { return Directory.Exists(Path.Combine(Root, "sessions")); } catch { return false; } } }

        public static LogIndex Scan(LogIndex index, DateTime nowUtc) {
            var paths = LogReader.Files(Path.Combine(Root, "sessions"), "session.v*.jsonl*")
                .Where(p => p.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jsonl.zstd", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase)).ToList();
            return LogReader.Scan(index, paths, info => info.FullName.ToLowerInvariant(), nowUtc, Read);
        }
        internal static long Read(LogIndex index, LogFile state, string path, Stream stream, long offset) {
            string session = Path.GetFileName(Path.GetDirectoryName(path));
            if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                return LogReader.Read(stream, offset, 16 << 20, (bytes, length) => true, line => Line(index, state, line, session));
            // Appended frames: everything after the last whole frame read so far. A frame still
            // being written is left for the next scan.
            long available = stream.Length - offset;
            if (available <= 0 || available > 512L << 20) return offset;
            var tail = new byte[available]; int read = 0;
            while (read < tail.Length) { int n = stream.Read(tail, read, tail.Length - read); if (n <= 0) break; read += n; }
            int complete; byte[] text;
            try { text = Zstd.Decode(tail, 0, read, out complete); } catch (Exception) { return offset; }
            foreach (string line in Encoding.UTF8.GetString(text).Split('\n')) if (line.Length > 1) Line(index, state, line, session);
            return offset + complete;
        }
        // state.StartMs / state.Model: the pending request (its header came, its answer not yet).
        internal static void Line(LogIndex index, LogFile state, string line, string session) {
            if (line.IndexOf("\"request/header\"", StringComparison.Ordinal) < 0 && line.IndexOf("\"assistant/message\"", StringComparison.Ordinal) < 0) return;
            object root; try { root = J.Parse(line); } catch { return; }
            string type = J.Str(root, "type"); double? time = J.Num(root, "time");
            if (type == "request/header") {
                state.StartMs = time.HasValue ? (long)time.Value : 0;
                string requested = J.Str(root, "data", "header", "config", "model").Trim(); if (requested.Length > 0) state.Model = requested;
                return;
            }
            if (type != "assistant/message" || !time.HasValue) return;
            object usage = J.Get(root, "data", "usage"); if (usage == null) return;
            double input = LogReader.N(usage, "inputTokens"), output = LogReader.N(usage, "outputTokens");
            double read = LogReader.N(usage, "cacheReadTokens"), write = LogReader.N(usage, "cacheWriteTokens"), total = LogReader.N(usage, "totalTokens");
            // Input is counted without the cache hits; should a record carry them inside
            // inputTokens (total = input + output), they are taken out.
            if (read > 0 && total > 0 && Math.Abs(total - (input + output)) < .5 && Math.Abs(total - (input + output + read + write)) >= .5) input = Math.Max(0, input - read);
            if (input + output + read + write <= 0) return;
            DateTime when = LogReader.FromMillis(time.Value);
            string key = "dsh|" + session + "|" + (J.Num(root, "seq") ?? time.Value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (index.Seen.ContainsKey(key)) return;
            LogIndex.See(index, key, when, state);
            string model = state.Model.Length > 0 ? state.Model : "deepseek";
            var bucket = new Bucket { I = input, C = read, W = write, O = output, R = 1 };
            ModelPrice price = Pricing.Find(model);
            if (price != null) bucket.D = Pricing.Cost(price, new TokenUse { Input = input, Output = output, CacheRead = read, CacheWrite5m = write });
            else bucket.U = bucket.Tokens();
            // Request sent (header) → last output written (this message).
            double seconds = state.StartMs > 0 ? (time.Value - state.StartMs) / 1000.0 : 0;
            if (OutputTiming.Accept(output, seconds)) { bucket.TN = 1; bucket.TO = output; bucket.TS = seconds; }
            state.StartMs = 0;
            LogIndex.Add(state, when, "", model, bucket);
        }
    }
}
