using System;
using System.Collections.Generic;
using System.IO;

namespace CodeUsageMonit {
    // Zstandard decompression (RFC 8878) for logs other tools write compressed: DeepSeek
    // Harness appends its sessions as zstd frames. .NET Framework has no zstd of its own.
    // Decoding only; no dictionaries (none of the logs use one); content checksums are skipped.
    public static class Zstd {
        private const uint FrameMagic = 0xFD2FB528, SkippableMagic = 0x184D2A50;

        public static byte[] Decode(byte[] src) { int complete; return Decode(src, 0, src.Length, out complete); }
        // Every whole frame in src[offset, end). complete = the offset after the last whole
        // frame, so a frame still being written is left for the next read.
        public static byte[] Decode(byte[] src, int offset, int end, out int complete) {
            var output = new MemoryStream();
            complete = offset;
            while (offset + 4 <= end) {
                uint magic = (uint)(src[offset] | src[offset + 1] << 8 | src[offset + 2] << 16 | src[offset + 3] << 24);
                if ((magic & 0xFFFFFFF0) == SkippableMagic) {
                    if (offset + 8 > end) break;
                    long size = (uint)(src[offset + 4] | src[offset + 5] << 8 | src[offset + 6] << 16 | src[offset + 7] << 24);
                    if (offset + 8 + size > end) break;
                    offset += 8 + (int)size; complete = offset; continue;
                }
                if (magic != FrameMagic) throw new InvalidDataException("not a zstd frame");
                var frame = new Frame(src, offset + 4, end);
                byte[] content;
                try { content = frame.Run(); } catch (Truncated) { break; }
                output.Write(content, 0, content.Length);
                offset = frame.End; complete = offset;
            }
            return output.ToArray();
        }
        private sealed class Truncated : Exception { }

        // ── Tables (RFC 8878 §3.1.1.3.2.1–2) ─────────────────────────────
        private static readonly int[] LiteralBase = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 40, 48, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 };
        private static readonly int[] LiteralExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        private static readonly int[] MatchBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 37, 39, 41, 43, 47, 51, 59, 67, 83, 99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539 };
        private static readonly int[] MatchExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        private static readonly Fse LiteralDefault = Fse.Build(new[] { 4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1 }, 36, 6);
        private static readonly Fse MatchDefault = Fse.Build(new[] { 1, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1, -1 }, 53, 6);
        private static readonly Fse OffsetDefault = Fse.Build(new[] { 1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1 }, 29, 5);

        private static int HighBit(long v) { int n = -1; while (v != 0) { v >>= 1; n++; } return n; }

        // A finite-state-entropy decoding table.
        private sealed class Fse {
            public byte[] Symbol; public byte[] Bits; public int[] Base; public int Log;
            public static Fse Rle(int symbol) { return new Fse { Symbol = new[] { (byte)symbol }, Bits = new byte[1], Base = new int[1], Log = 0 }; }
            public static Fse Build(int[] counts, int symbols, int log) {
                int size = 1 << log, high = size - 1;
                var t = new Fse { Symbol = new byte[size], Bits = new byte[size], Base = new int[size], Log = log };
                var next = new int[symbols];
                for (int s = 0; s < symbols; s++) { if (counts[s] == -1) { t.Symbol[high--] = (byte)s; next[s] = 1; } else next[s] = counts[s]; }
                int step = (size >> 1) + (size >> 3) + 3, mask = size - 1, position = 0;
                for (int s = 0; s < symbols; s++)
                    for (int i = 0; i < counts[s]; i++) { t.Symbol[position] = (byte)s; do position = (position + step) & mask; while (position > high); }
                if (position != 0) throw new InvalidDataException("zstd: bad FSE distribution");
                for (int u = 0; u < size; u++) {
                    int state = next[t.Symbol[u]]++, bits = log - HighBit(state);
                    t.Bits[u] = (byte)bits; t.Base[u] = (state << bits) - size;
                }
                return t;
            }
            // Table description (RFC 8878 §4.1.1), read forwards from src[p]; p moves past it.
            public static Fse Read(byte[] src, ref int p, int end, int maxLog, int maxSymbol) {
                var bits = new ForwardBits(src, p, end);
                int log = bits.Read(4) + 5; if (log > maxLog) throw new InvalidDataException("zstd: FSE accuracy too large");
                int remaining = (1 << log) + 1, threshold = 1 << log, width = log + 1, symbol = 0;
                var counts = new int[maxSymbol + 1]; bool previousZero = false;
                while (remaining > 1 && symbol <= maxSymbol) {
                    if (previousZero) {
                        int repeat;
                        do { repeat = bits.Read(2); for (int i = 0; i < repeat; i++) { if (symbol > maxSymbol) throw new InvalidDataException("zstd: FSE symbols overflow"); counts[symbol++] = 0; } } while (repeat == 3);
                        if (symbol > maxSymbol) throw new InvalidDataException("zstd: FSE symbols overflow");
                    }
                    int max = (2 * threshold - 1) - remaining, count, low = bits.Peek(width - 1) & (threshold - 1);
                    if (low < max) { count = low; bits.Skip(width - 1); }
                    else { count = bits.Peek(width) & (2 * threshold - 1); if (count >= threshold) count -= max; bits.Skip(width); }
                    count--;
                    remaining -= count < 0 ? -count : count;
                    counts[symbol++] = count;
                    previousZero = count == 0;
                    while (remaining < threshold) { width--; threshold >>= 1; }
                }
                if (remaining != 1) throw new InvalidDataException("zstd: bad FSE description");
                p += (bits.Used + 7) / 8;
                if (p > end) throw new Truncated();
                return Build(counts, symbol, log);
            }
        }

        // Bits read least significant first, from the start (table descriptions).
        private sealed class ForwardBits {
            private readonly byte[] d; private readonly int start, end; public int Used;
            public ForwardBits(byte[] d, int start, int end) { this.d = d; this.start = start; this.end = end; }
            public int Peek(int n) {
                long v = 0; int first = (start * 8 + Used) >> 3, last = (start * 8 + Used + n - 1) >> 3;
                for (int b = last; b >= first; b--) v = (v << 8) | (uint)(b < end ? d[b] : 0);
                return (int)((v >> ((start * 8 + Used) & 7)) & ((1L << n) - 1));
            }
            public void Skip(int n) { Used += n; }
            public int Read(int n) { int v = Peek(n); Used += n; return v; }
        }
        // Bits read from the end backwards (Huffman streams, FSE-coded data): the last byte's
        // highest set bit marks where the data starts. Past the beginning, bits read as zero.
        private sealed class BackBits {
            private readonly byte[] d; private readonly int start; private long pos;
            public BackBits(byte[] d, int start, int end) {
                if (end <= start || d[end - 1] == 0) throw new InvalidDataException("zstd: bad bitstream");
                this.d = d; this.start = start; pos = (end - start - 1) * 8L + HighBit(d[end - 1]);
            }
            public bool Overflow { get { return pos < 0; } }
            public int Read(int n) { if (n == 0) return 0; pos -= n; return (int)Extract(pos, n); }
            public int Peek(int n) { return (int)Extract(pos - n, n); }
            public void Skip(int n) { pos -= n; }
            private ulong Extract(long at, int n) {
                if (n <= 0) return 0;
                if (at < 0) { int keep = n + (int)at; return keep <= 0 ? 0 : Extract(0, keep) << (int)-at; }
                int first = (int)(at >> 3), last = (int)((at + n - 1) >> 3); ulong v = 0;
                for (int b = last; b >= first; b--) v = (v << 8) | d[start + b];
                return (v >> (int)(at & 7)) & ((1UL << n) - 1);
            }
        }

        private sealed class Frame {
            private readonly byte[] src; private readonly int limit; private int pos;
            public int End;
            private byte[] output = new byte[1 << 16]; private int length;
            private readonly long[] repeat = { 1, 4, 8 };
            private Fse literalTable, offsetTable, matchTable;
            private byte[] huffmanSymbol, huffmanBits; private int huffmanMax;
            public Frame(byte[] src, int pos, int limit) { this.src = src; this.pos = pos; this.limit = limit; }

            private void Need(int p, int n) { if (p + n > limit) throw new Truncated(); }
            public byte[] Run() {
                Need(pos, 1);
                int descriptor = src[pos++];
                if ((descriptor & 8) != 0) throw new InvalidDataException("zstd: reserved frame bit");
                bool single = (descriptor & 0x20) != 0, checksum = (descriptor & 4) != 0;
                int dictionaryBytes = new[] { 0, 1, 2, 4 }[descriptor & 3], sizeFlag = descriptor >> 6;
                int sizeBytes = sizeFlag == 0 ? (single ? 1 : 0) : sizeFlag == 1 ? 2 : sizeFlag == 2 ? 4 : 8;
                Need(pos, (single ? 0 : 1) + dictionaryBytes + sizeBytes);
                if (!single) pos++;
                long dictionary = 0; for (int i = 0; i < dictionaryBytes; i++) dictionary |= (long)src[pos++] << (8 * i);
                if (dictionary != 0) throw new NotSupportedException("zstd: dictionaries are not supported");
                long contentSize = 0; for (int i = 0; i < sizeBytes; i++) contentSize |= (long)src[pos++] << (8 * i);
                if (sizeBytes == 2) contentSize += 256;
                if (sizeBytes > 0 && contentSize > 0 && contentSize < 1L << 30) output = new byte[Math.Max(16, (int)contentSize)];
                while (true) {
                    Need(pos, 3);
                    int header = src[pos] | src[pos + 1] << 8 | src[pos + 2] << 16; pos += 3;
                    bool last = (header & 1) != 0; int type = (header >> 1) & 3, size = header >> 3;
                    if (type == 0) { Need(pos, size); Append(src, pos, size); pos += size; }
                    else if (type == 1) { Need(pos, 1); Ensure(size); for (int i = 0; i < size; i++) output[length++] = src[pos]; pos += 1; }
                    else if (type == 2) { Need(pos, size); Compressed(pos, pos + size); pos += size; }
                    else throw new InvalidDataException("zstd: reserved block type");
                    if (last) break;
                }
                if (checksum) { Need(pos, 4); pos += 4; }
                End = pos;
                var result = new byte[length]; Buffer.BlockCopy(output, 0, result, 0, length); return result;
            }
            private void Ensure(int extra) {
                if (length + extra <= output.Length) return;
                long size = output.Length; while (size < length + (long)extra) size *= 2;
                if (size > int.MaxValue - 64) throw new InvalidDataException("zstd: frame too large");
                Array.Resize(ref output, (int)size);
            }
            private void Append(byte[] data, int at, int count) { if (count <= 0) return; Ensure(count); Buffer.BlockCopy(data, at, output, length, count); length += count; }

            private void Compressed(int p, int end) {
                byte[] literals; p = Literals(p, end, out literals);
                if (p >= end) throw new InvalidDataException("zstd: missing sequences section");
                int count = src[p++];
                if (count == 0) { Append(literals, 0, literals.Length); return; }
                if (count >= 128) {
                    if (count < 255) count = ((count - 128) << 8) + src[p++];
                    else { count = src[p] + (src[p + 1] << 8) + 0x7F00; p += 2; }
                }
                int modes = src[p++];
                literalTable = Table(modes >> 6, ref p, end, LiteralDefault, literalTable, 9, 35);
                offsetTable = Table((modes >> 4) & 3, ref p, end, OffsetDefault, offsetTable, 8, 31);
                matchTable = Table((modes >> 2) & 3, ref p, end, MatchDefault, matchTable, 9, 52);
                var bits = new BackBits(src, p, end);
                int literalState = bits.Read(literalTable.Log), offsetState = bits.Read(offsetTable.Log), matchState = bits.Read(matchTable.Log);
                int used = 0;
                for (int i = 0; i < count; i++) {
                    int offsetCode = offsetTable.Symbol[offsetState], literalCode = literalTable.Symbol[literalState], matchCode = matchTable.Symbol[matchState];
                    if (offsetCode > 31 || literalCode > 35 || matchCode > 52) throw new InvalidDataException("zstd: bad sequence code");
                    long offsetValue = (1L << offsetCode) + (uint)bits.Read(offsetCode);
                    int match = MatchBase[matchCode] + bits.Read(MatchExtra[matchCode]);
                    int literal = LiteralBase[literalCode] + bits.Read(LiteralExtra[literalCode]);
                    if (i < count - 1) {
                        literalState = literalTable.Base[literalState] + bits.Read(literalTable.Bits[literalState]);
                        matchState = matchTable.Base[matchState] + bits.Read(matchTable.Bits[matchState]);
                        offsetState = offsetTable.Base[offsetState] + bits.Read(offsetTable.Bits[offsetState]);
                    }
                    long offset;
                    if (offsetValue > 3) { offset = offsetValue - 3; repeat[2] = repeat[1]; repeat[1] = repeat[0]; repeat[0] = offset; }
                    else {
                        int index = (int)offsetValue - 1 + (literal == 0 ? 1 : 0);
                        if (index == 0) offset = repeat[0];
                        else {
                            offset = index == 1 ? repeat[1] : index == 2 ? repeat[2] : repeat[0] - 1;
                            if (index != 1) repeat[2] = repeat[1];
                            repeat[1] = repeat[0]; repeat[0] = offset;
                        }
                    }
                    if (used + literal > literals.Length) throw new InvalidDataException("zstd: literals overrun");
                    Append(literals, used, literal); used += literal;
                    if (offset <= 0 || offset > length) throw new InvalidDataException("zstd: offset out of range");
                    Ensure(match);
                    int from = length - (int)offset;
                    for (int k = 0; k < match; k++) output[length++] = output[from + k];
                }
                Append(literals, used, literals.Length - used);
            }
            private Fse Table(int mode, ref int p, int end, Fse predefined, Fse previous, int maxLog, int maxSymbol) {
                if (mode == 0) return predefined;
                if (mode == 1) { if (p >= end) throw new InvalidDataException("zstd: missing RLE symbol"); return Fse.Rle(src[p++]); }
                if (mode == 2) return Fse.Read(src, ref p, end, maxLog, maxSymbol);
                if (previous == null) throw new InvalidDataException("zstd: repeat table without a previous one");
                return previous;
            }

            // Literals section (RFC 8878 §3.1.1.3.1).
            private int Literals(int p, int end, out byte[] literals) {
                int first = src[p], type = first & 3, format = (first >> 2) & 3;
                if (type < 2) {
                    int size;
                    if ((format & 1) == 0) { size = first >> 3; p += 1; }
                    else if (format == 1) { size = (first >> 4) + (src[p + 1] << 4); p += 2; }
                    else { size = (first >> 4) + (src[p + 1] << 4) + (src[p + 2] << 12); p += 3; }
                    literals = new byte[size];
                    if (type == 0) { if (p + size > end) throw new InvalidDataException("zstd: literals overrun"); Buffer.BlockCopy(src, p, literals, 0, size); p += size; }
                    else { for (int i = 0; i < size; i++) literals[i] = src[p]; p += 1; }
                    return p;
                }
                int regenerated, compressed;
                if (format < 2) { int v = first | src[p + 1] << 8 | src[p + 2] << 16; regenerated = (v >> 4) & 0x3FF; compressed = (v >> 14) & 0x3FF; p += 3; }
                else if (format == 2) { long v = (uint)(first | src[p + 1] << 8 | src[p + 2] << 16) | (long)src[p + 3] << 24; regenerated = (int)((v >> 4) & 0x3FFF); compressed = (int)((v >> 18) & 0x3FFF); p += 4; }
                else { long v = (uint)(first | src[p + 1] << 8 | src[p + 2] << 16) | (long)src[p + 3] << 24 | (long)src[p + 4] << 32; regenerated = (int)((v >> 4) & 0x3FFFF); compressed = (int)((v >> 22) & 0x3FFFF); p += 5; }
                int stop = p + compressed; if (stop > end) throw new InvalidDataException("zstd: literals overrun");
                if (type == 2) p = Huffman(p, stop);
                else if (huffmanSymbol == null) throw new InvalidDataException("zstd: treeless literals without a table");
                literals = new byte[regenerated];
                if (format == 0) Stream(p, stop, literals, 0, regenerated);
                else {
                    int s1 = src[p] | src[p + 1] << 8, s2 = src[p + 2] | src[p + 3] << 8, s3 = src[p + 4] | src[p + 5] << 8; p += 6;
                    int segment = (regenerated + 3) / 4, rest = regenerated - 3 * segment;
                    if (p + s1 + s2 + s3 >= stop || rest < 0) throw new InvalidDataException("zstd: bad literal streams");
                    Stream(p, p + s1, literals, 0, segment); p += s1;
                    Stream(p, p + s2, literals, segment, segment); p += s2;
                    Stream(p, p + s3, literals, 2 * segment, segment); p += s3;
                    Stream(p, stop, literals, 3 * segment, rest);
                }
                return stop;
            }
            // Huffman tree description (RFC 8878 §4.2.1): weights, direct or FSE-compressed.
            private int Huffman(int p, int end) {
                int header = src[p++]; var weights = new List<int>();
                if (header >= 128) {
                    int n = header - 127; if (p + (n + 1) / 2 > end) throw new InvalidDataException("zstd: bad Huffman weights");
                    for (int i = 0; i < n; i++) { int b = src[p + i / 2]; weights.Add(i % 2 == 0 ? b >> 4 : b & 15); }
                    p += (n + 1) / 2;
                } else {
                    int stop = p + header; if (stop > end) throw new InvalidDataException("zstd: bad Huffman weights");
                    Fse table = Fse.Read(src, ref p, stop, 6, 255);
                    var bits = new BackBits(src, p, stop);
                    int a = bits.Read(table.Log), b = bits.Read(table.Log);
                    while (true) {
                        weights.Add(table.Symbol[a]); a = table.Base[a] + bits.Read(table.Bits[a]);
                        if (bits.Overflow) { weights.Add(table.Symbol[b]); break; }
                        weights.Add(table.Symbol[b]); b = table.Base[b] + bits.Read(table.Bits[b]);
                        if (bits.Overflow) { weights.Add(table.Symbol[a]); break; }
                        if (weights.Count > 255) throw new InvalidDataException("zstd: too many Huffman weights");
                    }
                    p = stop;
                }
                long sum = 0; foreach (int w in weights) { if (w > 11) throw new InvalidDataException("zstd: bad Huffman weight"); if (w > 0) sum += 1L << (w - 1); }
                if (sum == 0) throw new InvalidDataException("zstd: empty Huffman tree");
                int maxBits = HighBit(sum) + 1; long rest = (1L << maxBits) - sum;
                if ((rest & (rest - 1)) != 0 || maxBits > 11 || weights.Count > 255) throw new InvalidDataException("zstd: bad Huffman tree");
                weights.Add(HighBit(rest) + 1);
                huffmanMax = maxBits; huffmanSymbol = new byte[1 << maxBits]; huffmanBits = new byte[1 << maxBits];
                int position = 0;
                for (int w = 1; w <= maxBits; w++)
                    for (int s = 0; s < weights.Count; s++) {
                        if (weights[s] != w) continue;
                        for (int i = 0; i < 1 << (w - 1); i++) { huffmanSymbol[position] = (byte)s; huffmanBits[position] = (byte)(maxBits + 1 - w); position++; }
                    }
                return p;
            }
            private void Stream(int start, int end, byte[] target, int at, int count) {
                if (count == 0) return;
                var bits = new BackBits(src, start, end);
                for (int i = 0; i < count; i++) { int index = bits.Peek(huffmanMax); target[at + i] = huffmanSymbol[index]; bits.Skip(huffmanBits[index]); }
            }
        }
    }
}
