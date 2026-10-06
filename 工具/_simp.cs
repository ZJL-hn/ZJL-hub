using System;
using System.Collections.Generic;
using System.IO;

class Simp
{
    class N { public int Note; public int Start; public int End; }
    static List<N>[] Parse(byte[] b, out int division)
    {
        int pos = 0;
        List<N>[] perTrack = new List<N>[16];
        pos += 8; int fmt = BE16(b, pos); pos += 2; int nt = BE16(b, pos); pos += 2; division = BE16(b, pos); pos += 2;
        for (int t = 0; t < nt; t++)
        {
            List<N> tl = new List<N>();
            if (t < perTrack.Length) perTrack[t] = tl;
            if (!Tag(b, pos, "MTrk")) throw new Exception("no MTrk");
            int trackLen = BE32(b, pos + 4); pos += 8;
            int end = Math.Min(pos + trackLen, b.Length);
            int abs = 0, st = 0; var on = new Dictionary<int, int>();
            while (pos < end)
            {
                abs += VLQ(b, ref pos); int b0 = b[pos];
                if (b0 == 0xFF) { pos++; int mt = b[pos]; pos++; int ml = VLQ(b, ref pos); pos += ml; if (mt == 0x2F) break; }
                else if (b0 == 0xF0 || b0 == 0xF7) { pos++; int sl = VLQ(b, ref pos); pos += sl; }
                else
                {
                    if (b0 >= 0x80) { st = b0; pos++; }
                    int s = st & 0xF0;
                    if (s == 0xC0 || s == 0xD0) pos += 1;
                    else if (s == 0x80 || s == 0x90 || s == 0xA0 || s == 0xB0 || s == 0xE0)
                    {
                        int d1 = b[pos], d2 = b[pos + 1]; pos += 2;
                        int ch = st & 0x0F;
                        if (s == 0x90 && d2 > 0) { int k = ch * 128 + d1; if (!on.ContainsKey(k)) on[k] = abs; }
                        else { int sk; if (on.TryGetValue(ch * 128 + d1, out sk)) { tl.Add(new N { Note = d1, Start = sk, End = abs }); on.Remove(ch * 128 + d1); } }
                    }
                    else throw new Exception("ev");
                }
            }
        }
        return perTrack;
    }
    static bool Tag(byte[] b, int pos, string s) { for (int i = 0; i < s.Length; i++) if (b[pos + i] != (byte)s[i]) return false; return true; }
    static int BE16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
    static int BE32(byte[] b, int p) { return (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]; }
    static int VLQ(byte[] b, ref int p) { int v = 0; for (int i = 0; i < 4; i++) { byte x = b[p++]; v = (v << 7) | (x & 0x7F); if ((x & 0x80) == 0) break; } return v; }
    static void W32(BinaryWriter w, int v) { w.Write((byte)((v >> 24) & 255)); w.Write((byte)((v >> 16) & 255)); w.Write((byte)((v >> 8) & 255)); w.Write((byte)(v & 255)); }
    static void W16(BinaryWriter w, int v) { w.Write((byte)((v >> 8) & 255)); w.Write((byte)(v & 255)); }
    static void VLQW(BinaryWriter w, int v) { List<byte> t = new List<byte>(); t.Add((byte)(v & 0x7F)); v >>= 7; while (v > 0) { t.Insert(0, (byte)((v & 0x7F) | 0x80)); v >>= 7; } foreach (byte x in t) w.Write(x); }
    static string NoteName(int n) { string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }; return names[n % 12] + (n / 12 - 1); }

    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.WriteLine("EXCEPTION: " + ex.ToString()); }
    }
    static void Run(string[] args)
    {
        string src = args.Length > 0 ? args[0] : "in.mid";
        string outp = args.Length > 1 ? args[1] : "out.mid";
        int division;
        byte[] b = File.ReadAllBytes(src);
        List<N>[] per = Parse(b, out division);
        // 主旋律 = 最高音最高的轨
        List<N> mel = null; int melMax = -1;
        foreach (var tl in per)
        {
            if (tl == null || tl.Count == 0) continue;
            int mx = -1; foreach (var n in tl) if (n.Note > mx) mx = n.Note;
            if (mx > melMax) { melMax = mx; mel = tl; }
        }
        if (mel == null) { Console.WriteLine("no melody track"); return; }
        // 去和弦：同 StartTick 保留最高音
        var byTick = new SortedDictionary<int, List<N>>();
        foreach (var n in mel) { if (!byTick.ContainsKey(n.Start)) byTick[n.Start] = new List<N>(); byTick[n.Start].Add(n); }
        var seq = new List<N>();
        foreach (var kv in byTick)
        {
            N best = kv.Value[0];
            foreach (var n in kv.Value) if (n.Note > best.Note) best = n;
            seq.Add(best);
        }
        int maxEnd = 0; int black = 0; foreach (var n in seq) { if (n.End > maxEnd) maxEnd = n.End; if (n.Note % 12 == 1 || n.Note % 12 == 3 || n.Note % 12 == 6 || n.Note % 12 == 8 || n.Note % 12 == 10) black++; }
        Console.WriteLine("melTrack(orig)=" + mel.Count + " mono=" + seq.Count + " durTicks=" + maxEnd + " black=" + black + " (" + (100.0 * black / seq.Count).ToString("0.0") + "%)");
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("MThd")); W32(w, 6); W16(w, 0); W16(w, 1); W16(w, division);
        w.Write(System.Text.Encoding.ASCII.GetBytes("MTrk")); int bodyPos = (int)ms.Position; W32(w, 0);
        VLQW(w, 0); w.Write((byte)0xFF); w.Write((byte)0x51); w.Write((byte)3); w.Write((byte)0x04); w.Write((byte)0x93); w.Write((byte)0xE0); // 200bpm
        var events = new List<KeyValuePair<long, byte[]>>();
        foreach (var n in seq)
        {
            events.Add(new KeyValuePair<long, byte[]>(n.Start, new byte[] { 0x90, (byte)n.Note, 0x64 }));
            events.Add(new KeyValuePair<long, byte[]>(n.End, new byte[] { 0x80, (byte)n.Note, 0x40 }));
        }
        events.Sort((a, c) => a.Key.CompareTo(c.Key));
        long prev = 0;
        foreach (var e in events) { VLQW(w, (int)(e.Key - prev)); w.Write(e.Value); prev = e.Key; }
        VLQW(w, 0); w.Write((byte)0xFF); w.Write((byte)0x2F); w.Write((byte)0x00);
        int bodyLen = (int)ms.Position - bodyPos - 4;
        ms.Position = bodyPos; W32(w, bodyLen); ms.Position = ms.Length;
        File.WriteAllBytes(outp, ms.ToArray());
        Console.WriteLine("wrote " + outp + " " + ms.Length + "B");
    }
}
