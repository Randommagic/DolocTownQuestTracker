using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Scans a decompressed Unity SerializedFile for TextAsset payloads and writes each
// out as <name>.json. Layout per asset: [nameLen i32le][name][pad to 4][dataLen i32le][data]
static class ExtractTables
{
    static int I32(byte[] b, int p)
    {
        return b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
    }

    static bool NameOk(byte[] b, int p, int len)
    {
        for (int i = 0; i < len; i++)
        {
            byte c = b[p + i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    static void Main(string[] args)
    {
        byte[] f = File.ReadAllBytes(args[0]);
        string outDir = args[1];
        Directory.CreateDirectory(outDir);

        int found = 0;
        var names = new List<string>();
        for (int p = 0; p + 8 < f.Length; p++)
        {
            int nameLen = I32(f, p);
            if (nameLen < 4 || nameLen > 80) continue;
            if (p + 4 + nameLen + 8 >= f.Length) continue;
            if (!NameOk(f, p + 4, nameLen)) continue;

            string name = Encoding.ASCII.GetString(f, p + 4, nameLen);
            if (name.IndexOf("_tb", StringComparison.Ordinal) < 0) continue;

            int q = p + 4 + nameLen;
            int pad = (4 - (nameLen % 4)) % 4;
            q += pad;
            if (q + 4 >= f.Length) continue;
            int dataLen = I32(f, q);
            if (dataLen < 2 || dataLen > 40000000) continue;
            if (q + 4 + dataLen > f.Length) continue;
            byte c0 = f[q + 4];
            if (c0 != (byte)'[' && c0 != (byte)'{') continue;

            string json = Encoding.UTF8.GetString(f, q + 4, dataLen);
            File.WriteAllText(Path.Combine(outDir, name + ".json"), json, new UTF8Encoding(false));
            names.Add(name + "  (" + dataLen + " bytes)");
            found++;
            p = q + 4 + dataLen - 1;
        }
        Console.WriteLine("extracted " + found + " tables -> " + outDir);
        names.Sort();
        foreach (var n in names) Console.WriteLine("  " + n);
    }
}
