using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Minimal UnityFS bundle unpacker: header -> LZ4 blocksInfo -> data blocks -> raw dump.
// We do not parse the SerializedFile type tree; the JSON payloads appear as plain text
// in the decompressed stream, which is enough to extract the config tables.
static class BundleDump
{
    static int Lz4Decode(byte[] src, int sOff, int sLen, byte[] dst, int dOff)
    {
        int sEnd = sOff + sLen;
        int d = dOff;
        while (sOff < sEnd)
        {
            int token = src[sOff++];
            int lit = token >> 4;
            if (lit == 15)
            {
                int b;
                do { b = src[sOff++]; lit += b; } while (b == 255);
            }
            for (int i = 0; i < lit; i++) dst[d++] = src[sOff++];
            if (sOff >= sEnd) break;
            int lo = src[sOff++];
            int hi = src[sOff++];
            int offset = lo | (hi << 8);
            int mlen = token & 15;
            if (mlen == 15)
            {
                int b;
                do { b = src[sOff++]; mlen += b; } while (b == 255);
            }
            mlen += 4;
            int m = d - offset;
            for (int i = 0; i < mlen; i++) dst[d++] = dst[m++];
        }
        return d - dOff;
    }

    static long ReadI64(byte[] b, ref int p)
    {
        long v = 0;
        for (int i = 0; i < 8; i++) v = (v << 8) | b[p++];
        return v;
    }
    static int ReadI32(byte[] b, ref int p)
    {
        int v = 0;
        for (int i = 0; i < 4; i++) v = (v << 8) | b[p++];
        return v;
    }
    static int ReadI16(byte[] b, ref int p)
    {
        int v = (b[p] << 8) | b[p + 1];
        p += 2;
        return v;
    }
    static string ReadCStr(byte[] b, ref int p)
    {
        int s = p;
        while (b[p] != 0) p++;
        string r = Encoding.UTF8.GetString(b, s, p - s);
        p++;
        return r;
    }

    static void Main(string[] args)
    {
        string inPath = args[0];
        string outPath = args[1];
        byte[] f = File.ReadAllBytes(inPath);
        int p = 0;

        string sig = ReadCStr(f, ref p);
        int version = ReadI32(f, ref p);
        string unityVer = ReadCStr(f, ref p);
        string unityRev = ReadCStr(f, ref p);
        long size = ReadI64(f, ref p);
        int ciSize = ReadI32(f, ref p);
        int uiSize = ReadI32(f, ref p);
        int flags = ReadI32(f, ref p);

        Console.WriteLine("sig=" + sig + " version=" + version + " unity=" + unityRev);
        Console.WriteLine("size=" + size + " compressedBlocksInfo=" + ciSize + " uncompressed=" + uiSize + " flags=0x" + flags.ToString("X"));

        if (version >= 7) { while (p % 16 != 0) p++; }
        if ((flags & 0x80) != 0) throw new Exception("blocksInfo at end not handled");

        byte[] bi = new byte[uiSize];
        int comp = flags & 0x3F;
        if (comp == 0) Array.Copy(f, p, bi, 0, uiSize);
        else if (comp == 2 || comp == 3) Lz4Decode(f, p, ciSize, bi, 0);
        else throw new Exception("blocksInfo compression " + comp + " not handled");
        p += ciSize;

        int q = 16; // skip uncompressedDataHash
        int blockCount = ReadI32(bi, ref q);
        Console.WriteLine("blocks=" + blockCount);
        var bUnc = new int[blockCount];
        var bCmp = new int[blockCount];
        var bFlg = new int[blockCount];
        long total = 0;
        for (int i = 0; i < blockCount; i++)
        {
            bUnc[i] = ReadI32(bi, ref q);
            bCmp[i] = ReadI32(bi, ref q);
            bFlg[i] = ReadI16(bi, ref q);
            total += bUnc[i];
        }
        long sumCmp = 0;
        for (int i = 0; i < blockCount; i++) sumCmp += bCmp[i];
        Console.WriteLine("sumCompressed=" + sumCmp + " fileSize=" + f.Length
            + " => impliedDataStart=" + (f.Length - sumCmp) + "  (currentP=" + p + ")");
        p = (int)(f.Length - sumCmp);

        int nodeCount = ReadI32(bi, ref q);
        Console.WriteLine("nodes=" + nodeCount + " totalUncompressed=" + total);
        for (int i = 0; i < nodeCount; i++)
        {
            long off = ReadI64(bi, ref q);
            long sz = ReadI64(bi, ref q);
            int fl = ReadI32(bi, ref q);
            string path = ReadCStr(bi, ref q);
            Console.WriteLine("  node[" + i + "] off=" + off + " size=" + sz + " path=" + path);
        }

        byte[] outBuf = new byte[total];
        int d = 0;
        var seenFlags = new Dictionary<int, int>();
        for (int i = 0; i < blockCount; i++)
        {
            int c = bFlg[i] & 0x3F;
            if (!seenFlags.ContainsKey(c)) seenFlags[c] = 0;
            seenFlags[c]++;
            try
            {
                if (c == 0) { Array.Copy(f, p, outBuf, d, bUnc[i]); d += bUnc[i]; }
                else if (c == 2 || c == 3)
                {
                    byte[] tmp = new byte[bUnc[i] + 64];
                    int n = Lz4Decode(f, p, bCmp[i], tmp, 0);
                    if (n != bUnc[i]) Console.WriteLine("  WARN block " + i + " got " + n + " expected " + bUnc[i]);
                    int copy = Math.Min(n, bUnc[i]);
                    Array.Copy(tmp, 0, outBuf, d, copy);
                    d += bUnc[i];
                }
                else { Console.WriteLine("  SKIP block " + i + " compression=" + c); d += bUnc[i]; }
            }
            catch (Exception e)
            {
                Console.WriteLine("  FAIL block " + i + " comp=" + c + " cmp=" + bCmp[i] + " unc=" + bUnc[i] + " : " + e.GetType().Name);
                d += bUnc[i];
            }
            p += bCmp[i];
        }
        foreach (var kv in seenFlags) Console.WriteLine("blockCompression " + kv.Key + " count=" + kv.Value);
        File.WriteAllBytes(outPath, outBuf);
        Console.WriteLine("wrote " + d + " bytes -> " + outPath);
    }
}
