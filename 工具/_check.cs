using System;
using System.Collections.Generic;
using System.IO;

class Check
{
    class MidiNote { public int Note; public int StartTick; public int EndTick; }
    static List<MidiNote> Parse(byte[] b, out int division, out int format, out int ntracks, out double lastTempoMs)
    {
        List<MidiNote> raws = new List<MidiNote>();
        int pos = 0;
        if (!Tag(b, pos, "MThd")) throw new Exception("not MThd");
        pos += 8;
        format = BE16(b, pos); pos += 2;
        ntracks = BE16(b, pos); pos += 2;
        division = BE16(b, pos); pos += 2;
        double uspq = 500000.0;
        lastTempoMs = 0;
        for (int t = 0; t < ntracks; t++)
        {
            List<MidiNote> tnotes = new List<MidiNote>();
            if (!Tag(b, pos, "MTrk")) throw new Exception("no MTrk");
            int trackLen = BE32(b, pos + 4); pos += 8;
            int end = Math.Min(pos + trackLen, b.Length);
            int absTick = 0; int status = 0;
            Dictionary<int, int> on = new Dictionary<int, int>();
            while (pos < end)
            {
                absTick += ReadVLQ(b, ref pos);
                int b0 = b[pos];
                if (b0 == 0xFF)
                {
                    pos++; int mt = b[pos]; pos++;
                    int ml = ReadVLQ(b, ref pos);
                    if (mt == 0x51 && ml == 3) uspq = (b[pos] << 16) | (b[pos + 1] << 8) | b[pos + 2];
                    pos += ml;
                    if (mt == 0x2F) break;
                }
                else if (b0 == 0xF0 || b0 == 0xF7) { pos++; int sl = ReadVLQ(b, ref pos); pos += sl; }
                else
                {
                    if (b0 >= 0x80) { status = b0; pos++; }
                    int st = status & 0xF0;
                    if (st == 0xC0 || st == 0xD0) { pos += 1; }
                    else if (st == 0x80 || st == 0x90 || st == 0xA0 || st == 0xB0 || st == 0xE0)
                    {
                        int d1 = b[pos], d2 = b[pos + 1]; pos += 2;
                        int key = (status & 0x0F) * 128 + d1;
                        if (st == 0x90 && d2 > 0) { if (!on.ContainsKey(key)) on[key] = absTick; }
                        else { int stk; if (on.TryGetValue(key, out stk)) { tnotes.Add(new MidiNote { Note = d1, StartTick = stk, EndTick = absTick }); on.Remove(key); } }
                    }
                    else throw new Exception("unknown 0x" + b0.ToString("X2"));
                }
            }
            int tBlack = 0, tEnd = 0, tMaxN = 0; int tMin = 1000; var ts = new HashSet<int>();
            foreach (var n in tnotes) { if (n.Note % 12 == 1 || n.Note % 12 == 3 || n.Note % 12 == 6 || n.Note % 12 == 8 || n.Note % 12 == 10) tBlack++; ts.Add(n.Note); if (n.EndTick > tEnd) tEnd = n.EndTick; if (n.Note < tMin) tMin = n.Note; if (n.Note > tMaxN) tMaxN = n.Note; }
            Console.WriteLine("TRACK" + t + ": notes=" + tnotes.Count + " black=" + tBlack + " distinct=" + ts.Count + " pitch=" + NoteName(tMin) + ".." + NoteName(tMaxN) + " maxTick=" + tEnd);
            raws.AddRange(tnotes);
        }
        lastTempoMs = uspq;
        raws.Sort((a, b2) => a.StartTick.CompareTo(b2.StartTick));
        return raws;
    }
    static bool Tag(byte[] b, int pos, string s) { for (int i = 0; i < s.Length; i++) if (b[pos + i] != (byte)s[i]) return false; return true; }
    static int BE16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
    static int BE32(byte[] b, int p) { return (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]; }
    static int ReadVLQ(byte[] b, ref int p) { int v = 0; for (int i = 0; i < 4; i++) { byte x = b[p++]; v = (v << 7) | (x & 0x7F); if ((x & 0x80) == 0) break; } return v; }
    static string NoteName(int n)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return names[n % 12] + (n / 12 - 1);
    }

    static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "a.mid";
        byte[] b = File.ReadAllBytes(path);
        int division, format, ntracks; double uspq;
        var notes = Parse(b, out division, out format, out ntracks, out uspq);
        Console.WriteLine("format=" + format + " tracks=" + ntracks + " division=" + division + " tempo=" + (60000000.0 / uspq).ToString("0.0") + "BPM");
        int maxEnd = 0; foreach (var n in notes) if (n.EndTick > maxEnd) maxEnd = n.EndTick;
        double perMs = uspq / 1000.0 / division;
        Console.WriteLine("TOTAL notes=" + notes.Count + " maxEndTick=" + maxEnd + " dur=" + (maxEnd * perMs / 1000.0).ToString("0.00") + "s (file tempo)");
        int black = 0; var seen = new HashSet<int>();
        foreach (var n in notes) { if (n.Note % 12 == 1 || n.Note % 12 == 3 || n.Note % 12 == 6 || n.Note % 12 == 8 || n.Note % 12 == 10) black++; seen.Add(n.Note); }
        var lastStart = new Dictionary<int, int>();
        foreach (var n in notes) { int c; lastStart.TryGetValue(n.StartTick, out c); lastStart[n.StartTick] = c + 1; }
        int chord = 0; foreach (var kv in lastStart) if (kv.Value > 1) chord += kv.Value - 1;
        Console.WriteLine("blackKeys=" + black + " (" + (100.0 * black / notes.Count).ToString("0.0") + "%) chordsExtra=" + chord + " distinctPitch=" + seen.Count);
        int minN = 1000, maxN = 0; foreach (var n in notes) { if (n.Note < minN) minN = n.Note; if (n.Note > maxN) maxN = n.Note; }
        Console.WriteLine("pitchRange=" + NoteName(minN) + ".." + NoteName(maxN));
        Console.Write("first30:"); for (int i = 0; i < Math.Min(30, notes.Count); i++) Console.Write(NoteName(notes[i].Note) + "@" + notes[i].StartTick + " ");
        Console.WriteLine();
    }
}
