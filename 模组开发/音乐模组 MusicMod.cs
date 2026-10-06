// 音乐模组 v5：三种操控方式可选 + MIDI 导入（乐器适配 + BPM 调节 + 速度联动）
//  模式① 高音+低音：q w e r t y u / a s d f g h j → 注入 c d e f g a b
//  模式② 全模式   ：q~u 高音、a~j 中音、z~m 低音（21 键）→ 注入 c d e f g a b
//  模式③ 吉他模式 ：q~u = 和弦行(C Dm Em F G Am G7)、a~j = 高音 do~ti、z~m = 低音 do~ti
//          点击/录制：21 键原样注入（游戏键位）；MIDI 导入：和弦识别→q~u，单音按八度→a~j 或 z~m
//  三处（点击虚拟键盘、录制、MIDI 导入生成步骤）键名均随所选模式生效
//  · MIDI 导入后缓存原始音符(tick)，之后“操作/导入…”直接弹调整框：改乐器/BPM/操控方式 → 点“应用设置”立即重生成
//  · 乐器：钢琴(最短100ms/间隙20ms)、圆号(最短350ms/间隙90ms/音长×1.15)、竖琴(最短150ms/间隙60ms/音长×0.7)
//  · BPM↔主程序速度%联动：100% = 120BPM
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VkSim;

namespace VkSim.ModMusic
{
    public class MusicMod : IVkMod, ICommandMod
    {
        public string Name { get { return "音乐模组"; } }
        public string Description { get { return "三种操控方式（高音+低音/全模式/吉他模式）；导入 MIDI（乐器+BPM）生成步骤并演奏"; } }

        // 模式① 键名映射（点击/录制）：14 键
        private static readonly Dictionary<string, string> Map14 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "q", "c" }, { "w", "d" }, { "e", "e" }, { "r", "f" },
            { "t", "g" }, { "y", "a" }, { "u", "b" },
            { "a", "c" }, { "s", "d" }, { "d", "e" }, { "f", "f" },
            { "g", "g" }, { "h", "a" }, { "j", "b" }
        };
        // 模式② 全模式：21 键（新增低音行 z~m → c~b）
        private static readonly Dictionary<string, string> Map21 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "q", "c" }, { "w", "d" }, { "e", "e" }, { "r", "f" },
            { "t", "g" }, { "y", "a" }, { "u", "b" },
            { "a", "c" }, { "s", "d" }, { "d", "e" }, { "f", "f" },
            { "g", "g" }, { "h", "a" }, { "j", "b" },
            { "z", "c" }, { "x", "d" }, { "c", "e" }, { "v", "f" },
            { "b", "g" }, { "n", "a" }, { "m", "b" }
        };
        // 模式③ 吉他模式：21 键原样（游戏键位 q~u=和弦 / a~j=高音 / z~m=低音）
        private static readonly Dictionary<string, string> MapGuitar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "q", "q" }, { "w", "w" }, { "e", "e" }, { "r", "r" },
            { "t", "t" }, { "y", "y" }, { "u", "u" },
            { "a", "a" }, { "s", "s" }, { "d", "d" }, { "f", "f" },
            { "g", "g" }, { "h", "h" }, { "j", "j" },
            { "z", "z" }, { "x", "x" }, { "c", "c" }, { "v", "v" },
            { "b", "b" }, { "n", "n" }, { "m", "m" }
        };

        public static readonly string[] ModeNames = new string[] {
            "高音+低音（q~u / a~j）",
            "全模式（q~u高 / a~j中 / z~m低）",
            "吉他模式（q~u和弦 / a~j高音 / z~m低音）"
        };

        // 吉他模式单音键位：高音行 a~j（do re mi fa so la ti = c d e f g a b），低音行 z~m
        private static readonly string[] HiKeys = { "a", "s", "d", "f", "g", "h", "j" };
        private static readonly string[] LoKeys = { "z", "x", "c", "v", "b", "n", "m" };
        // 吉他模式和弦 → 键位 q~u（G7 必须在 G 之前判定，4 音优先）
        private static readonly int[][] ChordPcs = new int[][] {
            new int[] { 0, 4, 7 },      // C  → q
            new int[] { 2, 5, 9 },      // Dm → w
            new int[] { 4, 7, 11 },     // Em → e
            new int[] { 5, 9, 0 },      // F  → r
            new int[] { 7, 11, 2, 5 },  // G7 → u
            new int[] { 7, 11, 2 },     // G  → t
            new int[] { 9, 0, 4 }       // Am → y
        };
        private static readonly string[] ChordKeys = { "q", "w", "e", "r", "u", "t", "y" };

        private class Inst
        {
            public string Name; public double MinDurMs; public double DurScale; public double GapMs;
            public Inst(string n, double min, double scale, double gap) { Name = n; MinDurMs = min; DurScale = scale; GapMs = gap; }
        }
        private static readonly Inst[] Insts = new Inst[] {
            new Inst("钢琴", 100, 1.0, 20),
            new Inst("圆号", 350, 1.15, 90),
            new Inst("竖琴", 150, 0.7, 60)
        };

        // 缓存：导入的 MIDI 原始音符（按 tick 存）+ 当前设置
        private List<MidiNote> _notes;
        private int _division = 480;
        private int _instIdx = 0;
        private int _bpm = 120;
        private int _mode = 0;   // 0=高音+低音 1=全模式 2=吉他模式

        public void Initialize(IVkContext ctx)
        {
            ctx.SetKeyNameMapper(Transform);
            ctx.SetStatus("音乐模组已载入（操控方式：高音+低音，q~u/a~j→c~b）；可在“模组管理”点“操作/导入…”换操控方式或加载 MIDI 谱");
        }

        public string Transform(string key)
        {
            if (key == null) return key;
            string t = key.Trim();
            if (_mode == 0) { string v; if (Map14.TryGetValue(t, out v)) return v; }
            else if (_mode == 1) { string v; if (Map21.TryGetValue(t, out v)) return v; }
            // 模式③吉他：21 键原样（游戏键位），其余键也原样
            return key;
        }

        // ============ 操作入口：导入 / 调整设置（含操控方式） ============
        public void OnCommand(IVkContext ctx)
        {
            while (true)
            {
                if (_notes == null)
                {
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Filter = "MIDI 文件 (*.mid;*.midi)|*.mid;*.midi|所有文件 (*.*)|*.*";
                        d.Title = "导入五线谱（MIDI）";
                        if (d.ShowDialog() != DialogResult.OK) return;
                        try
                        {
                            MidiScore sc = MidiScore.Parse(File.ReadAllBytes(d.FileName));
                            _notes = sc.Notes; _division = sc.Division;
                        }
                        catch (Exception ex) { ctx.SetStatus("MIDI 解析失败: " + ex.Message); return; }
                    }
                }

                _bpm = ClampBpm((int)Math.Round(120.0 * ctx.SpeedPercent / 100.0));

                using (ImportDlg cfg = new ImportDlg(_notes != null, _instIdx, _bpm, _mode))
                {
                    if (cfg.ShowDialog() != DialogResult.OK) return;
                    _instIdx = cfg.InstrumentIndex; _bpm = cfg.Bpm; _mode = cfg.Mode;
                    if (cfg.WantNewFile) { _notes = null; continue; }
                    Build(ctx);
                    return;
                }
            }
        }

        // 用缓存 + 当前设置生成步骤。步骤绝对时间按 120BPM 基准生成，实际速度交给主程序“速度%”（BPM 联动）。
        private void Build(IVkContext ctx)
        {
            ctx.ClearSteps();
            double per = 500.0 / _division;   // 基准 120BPM：60000/120/division 毫秒每 tick
            Inst inst = Insts[_instIdx];
            double cur = double.MinValue, lastBase = 0;

            if (_mode == 2)
            {
                // 吉他模式：按 StartTick 分组（同 tick 多音=和弦 → q~u；单音 → a~j/z~m 按八度）
                int i = 0;
                while (i < _notes.Count)
                {
                    int st = _notes[i].StartTick;
                    List<MidiNote> grp = new List<MidiNote>();
                    while (i < _notes.Count && _notes[i].StartTick == st) { grp.Add(_notes[i]); i++; }
                    string key = GuitarKeyForGroup(grp);
                    double startMs = st * per;
                    double endMs = double.MinValue;
                    for (int k = 0; k < grp.Count; k++) { double e = grp[k].EndTick * per; if (e > endMs) endMs = e; }
                    double dur = (endMs - startMs) * inst.DurScale;
                    if (dur < inst.MinDurMs) dur = inst.MinDurMs;
                    double press = (cur < 0) ? startMs : Math.Max(startMs, cur + inst.GapMs);
                    double rel = press + dur; cur = rel; lastBase = rel;
                    ctx.AddStep(key, (int)(press + 0.5), (int)(rel + 0.5));
                }
            }
            else
            {
                foreach (MidiNote n in _notes)
                {
                    double startMs = n.StartTick * per, endMs = n.EndTick * per;
                    double dur = (endMs - startMs) * inst.DurScale;
                    if (dur < inst.MinDurMs) dur = inst.MinDurMs;
                    double press = (cur < 0) ? startMs : Math.Max(startMs, cur + inst.GapMs);
                    double rel = press + dur; cur = rel; lastBase = rel;
                    ctx.AddStep(NoteToKey(n.Note), (int)(press + 0.5), (int)(rel + 0.5));
                }
            }

            int speedPct = (int)Math.Round(100.0 * _bpm / 120.0);
            ctx.SpeedPercent = speedPct;
            double totalMs = lastBase * 120.0 / _bpm;
            ctx.SetStatus(string.Format("已生成：{0} 个音符（操控方式={1}，乐器={2}，{3} BPM → 主程序速度 {4}%，总时长约 {5:0.0}s）——请把目标软件置于前台后点“运行”演奏",
                _notes.Count, ModeNames[_mode], inst.Name, _bpm, speedPct, totalMs / 1000.0));
        }

        private static int ClampBpm(int b) { if (b < 40) return 40; if (b > 240) return 240; return b; }

        // ============ 键名转换（MIDI → 注入键） ============
        // 模式①②：黑键就近向下近似白键，全部注入 c~b
        private static string NoteToKey(int note)
        {
            int n = note % 12;
            if (n == 1) n = 0; else if (n == 3) n = 2; else if (n == 6) n = 5;
            else if (n == 8) n = 7; else if (n == 10) n = 9;
            switch (n)
            {
                case 0: return "c"; case 2: return "d"; case 4: return "e"; case 5: return "f";
                case 7: return "g"; case 9: return "a"; case 11: return "b";
            }
            return null;
        }

        // 吉他模式：组内 ≥3 音先试和弦（G7/G/…），否则取最高音按单音处理
        private static string GuitarKeyForGroup(List<MidiNote> grp)
        {
            int maxN = -1;
            foreach (MidiNote n in grp) if (n.Note > maxN) maxN = n.Note;
            if (grp.Count >= 3)
            {
                List<int> pcs = new List<int>();
                foreach (MidiNote n in grp)
                {
                    int pc = ToWhite(n.Note) % 12;
                    if (!pcs.Contains(pc)) pcs.Add(pc);
                }
                pcs.Sort();
                int ci = ChordMatch(pcs.ToArray());
                if (ci >= 0) return ChordKeys[ci];
            }
            return GuitarSoloKey(ToWhite(maxN));
        }

        // 和弦识别：组内白键音名集合包含该和弦全部音名即命中（G7 判定在 G 之前）
        public static int ChordMatch(int[] pcs)
        {
            for (int c = 0; c < ChordPcs.Length; c++)
            {
                bool ok = true;
                for (int k = 0; k < ChordPcs[c].Length; k++)
                {
                    if (Array.IndexOf(pcs, ChordPcs[c][k]) < 0) { ok = false; break; }
                }
                if (ok) return c;
            }
            return -1;
        }

        // 吉他模式单音键位：音高 ≥ C4(60) 用高音行 a~j，否则低音行 z~m
        public static string GuitarSoloKey(int whiteNote)
        {
            int pc = whiteNote % 12;
            int idx = pc == 0 ? 0 : pc == 2 ? 1 : pc == 4 ? 2 : pc == 5 ? 3 : pc == 7 ? 4 : pc == 9 ? 5 : pc == 11 ? 6 : 0;
            if (whiteNote >= 60) return HiKeys[idx];
            return LoKeys[idx];
        }

        private static int ToWhite(int note)
        {
            int n = note % 12;
            if (n == 1 || n == 3 || n == 6 || n == 8 || n == 10) return note - 1;
            return note;
        }

        public void Dispose() { }
    }

    // ============ 调整对话框（乐器 + BPM + 操控方式） ============
    public class ImportDlg : Form
    {
        public int Bpm { get { return (int)nud.Value; } }
        public int InstrumentIndex { get { return combo.SelectedIndex; } }
        public int Mode { get { return comboMode.SelectedIndex; } }
        public bool WantNewFile = false;
        private ComboBox combo, comboMode;
        private NumericUpDown nud;
        private Label speedTag;
        private float _scale = 1f;
        private static readonly string[] Tips = new string[] {
            "钢琴：音头清晰、延音自然。最短音 100ms，间隙 20ms（接近连奏）",
            "圆号：起音柔和缓慢、需气息。最短音 350ms，间隙 90ms（换气），音长×1.15",
            "竖琴：拨弦后快速衰减。最短音 150ms，间隙 60ms，音长×0.7"
        };

        private int S(float v) { return (int)(v * _scale + 0.5f); }

        public ImportDlg(bool hasData, int instIdx, int bpm, int mode)
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                _scale = g.DpiX / 96f;
            if (_scale < 1f) _scale = 1f;

            Text = hasData ? "音乐模组 · 调整设置" : "音乐模组 · 导入设置";
            ClientSize = new Size(S(380), S(262));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(38, 42, 50);
            Font = new Font("Segoe UI", 9f);

            Label l1 = new Label(); l1.Text = "乐器:"; l1.Location = new Point(S(16), S(16)); l1.Size = new Size(S(60), S(24));
            l1.ForeColor = Color.FromArgb(233, 236, 241); l1.AutoSize = false; Controls.Add(l1);

            combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.AddRange(new object[] { "钢琴", "圆号", "竖琴" });
            combo.SelectedIndex = instIdx;
            combo.Location = new Point(S(82), S(14)); combo.Size = new Size(S(128), S(26));
            combo.SelectedIndexChanged += (s, e) => UpdateTip();
            Controls.Add(combo);

            Label l2 = new Label(); l2.Text = "速度(BPM):"; l2.Location = new Point(S(16), S(56)); l2.Size = new Size(S(84), S(24));
            l2.ForeColor = Color.FromArgb(233, 236, 241); l2.AutoSize = false; Controls.Add(l2);

            nud = new NumericUpDown();
            nud.Minimum = 40; nud.Maximum = 240; nud.Value = Math.Max(40, Math.Min(240, bpm)); nud.Increment = 5;
            nud.Location = new Point(S(106), S(54)); nud.Size = new Size(S(76), S(26));
            Controls.Add(nud);

            speedTag = new Label();
            speedTag.Location = new Point(S(190), S(56)); speedTag.Size = new Size(S(176), S(24));
            speedTag.ForeColor = Color.FromArgb(150, 233, 236, 241); speedTag.AutoSize = false; Controls.Add(speedTag);
            nud.ValueChanged += (s, e) => UpdateSpeedTag();
            UpdateSpeedTag();

            Label l3 = new Label(); l3.Text = "操控方式:"; l3.Location = new Point(S(16), S(94)); l3.Size = new Size(S(70), S(24));
            l3.ForeColor = Color.FromArgb(233, 236, 241); l3.AutoSize = false; Controls.Add(l3);

            comboMode = new ComboBox();
            comboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboMode.Items.AddRange(MusicMod.ModeNames);
            comboMode.SelectedIndex = mode;
            comboMode.Location = new Point(S(92), S(92)); comboMode.Size = new Size(S(272), S(26));
            Controls.Add(comboMode);

            Label tip = new Label(); tip.Name = "tip"; tip.Location = new Point(S(16), S(128)); tip.Size = new Size(S(348), S(62));
            tip.ForeColor = Color.FromArgb(150, 233, 236, 241); tip.AutoSize = false; Controls.Add(tip);

            Button ok = new Button(); ok.Text = hasData ? "应用设置" : "导入并生成";
            ok.Location = new Point(S(16), S(204)); ok.Size = new Size(S(104), S(34));
            ok.FlatStyle = FlatStyle.Flat; ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = Color.FromArgb(0, 120, 212); ok.ForeColor = Color.White;
            ok.DialogResult = DialogResult.OK; Controls.Add(ok);

            Button cc = new Button(); cc.Text = "取消"; cc.Location = new Point(S(286), S(204)); cc.Size = new Size(S(78), S(34));
            cc.FlatStyle = FlatStyle.Flat; cc.FlatAppearance.BorderSize = 0;
            cc.BackColor = Color.FromArgb(60, 66, 78); cc.ForeColor = Color.FromArgb(233, 236, 241);
            cc.DialogResult = DialogResult.Cancel; Controls.Add(cc);

            if (hasData)
            {
                Button nf = new Button(); nf.Text = "换一首…";
                nf.Location = new Point(S(132), S(204)); nf.Size = new Size(S(104), S(34));
                nf.FlatStyle = FlatStyle.Flat; nf.FlatAppearance.BorderSize = 0;
                nf.BackColor = Color.FromArgb(80, 86, 98); nf.ForeColor = Color.FromArgb(233, 236, 241);
                nf.Click += (s, e) => { WantNewFile = true; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(nf);
            }
            UpdateTip();
        }

        private void UpdateTip()
        {
            Label t = (Label)Controls["tip"];
            if (t != null) t.Text = Tips[combo.SelectedIndex];
        }

        private void UpdateSpeedTag()
        {
            if (speedTag != null) speedTag.Text = "→ 主程序速度 " + ((int)Math.Round(100.0 * (double)nud.Value / 120.0)).ToString() + "%";
        }
    }

    // ============ MIDI (SMF) 解析器（保留 tick，便于换 BPM 后重算） ============
    public class MidiNote
    {
        public int Note; public int StartTick; public int EndTick;
    }

    public class MidiScore
    {
        public List<MidiNote> Notes = new List<MidiNote>();
        public int Division = 480;

        public static MidiScore Parse(byte[] b)
        {
            if (b == null || b.Length < 14) throw new Exception("文件过小，不是有效的 MIDI");
            int pos = 0;
            if (!Tag(b, pos, "MThd")) throw new Exception("缺少 MThd 头，不是 MIDI 文件");
            pos += 8;
            int format = BE16(b, pos); pos += 2;
            int ntracks = BE16(b, pos); pos += 2;
            int division = BE16(b, pos); pos += 2;
            if ((division & 0x8000) != 0) throw new Exception("不支持 SMPTE 时基（请转成 PPQ 时基的 MIDI）");
            if (division <= 0) throw new Exception("MIDI 时基无效");

            List<RawNote> raws = new List<RawNote>();
            for (int t = 0; t < ntracks; t++)
            {
                if (pos + 8 > b.Length) break;
                if (!Tag(b, pos, "MTrk")) throw new Exception("轨道头缺失 (MTrk)");
                int trackLen = BE32(b, pos + 4);
                pos += 8;
                int end = Math.Min(pos + trackLen, b.Length);
                int absTick = 0; int status = 0;
                Dictionary<int, int> onNotes = new Dictionary<int, int>();
                while (pos < end)
                {
                    absTick += ReadVLQ(b, ref pos);
                    int b0 = b[pos];
                    if (b0 == 0xFF)
                    {
                        pos++; int mtype = b[pos]; pos++;
                        int mlen = ReadVLQ(b, ref pos);
                        pos += mlen;
                        if (mtype == 0x2F) break;
                    }
                    else if (b0 == 0xF0 || b0 == 0xF7) { pos++; int slen = ReadVLQ(b, ref pos); pos += slen; }
                    else
                    {
                        if (b0 >= 0x80) { status = b0; pos++; }
                        int st = status & 0xF0;
                        if (st == 0xC0 || st == 0xD0) { pos += 1; }
                        else if (st == 0x80 || st == 0x90 || st == 0xA0 || st == 0xB0 || st == 0xE0)
                        {
                            if (pos + 2 > end) break;
                            int d1 = b[pos], d2 = b[pos + 1]; pos += 2;
                            int key = (status & 0x0F) * 128 + d1;
                            if (st == 0x90 && d2 > 0)
                            { if (!onNotes.ContainsKey(key)) onNotes[key] = absTick; }
                            else
                            {
                                int stk; if (onNotes.TryGetValue(key, out stk)) { raws.Add(new RawNote(d1, stk, absTick)); onNotes.Remove(key); }
                            }
                        }
                        else throw new Exception("未知 MIDI 事件 0x" + b0.ToString("X2"));
                    }
                }
            }

            MidiScore s = new MidiScore();
            s.Division = division;
            foreach (RawNote r in raws)
            {
                MidiNote n = new MidiNote();
                n.Note = r.Note; n.StartTick = r.StartTick; n.EndTick = r.EndTick;
                s.Notes.Add(n);
            }
            s.Notes.Sort(delegate(MidiNote a, MidiNote b2) { return a.StartTick.CompareTo(b2.StartTick); });
            return s;
        }

        private class RawNote { public int Note; public int StartTick; public int EndTick; public RawNote(int n, int s, int e) { Note = n; StartTick = s; EndTick = e; } }
        private static bool Tag(byte[] b, int pos, string s) { for (int i = 0; i < s.Length; i++) if (b[pos + i] != (byte)s[i]) return false; return true; }
        private static int BE16(byte[] b, int pos) { return (b[pos] << 8) | b[pos + 1]; }
        private static int BE32(byte[] b, int pos) { return (b[pos] << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3]; }
        private static int ReadVLQ(byte[] b, ref int pos)
        {
            int val = 0;
            for (int i = 0; i < 4; i++) { if (pos >= b.Length) break; byte x = b[pos++]; val = (val << 7) | (x & 0x7F); if ((x & 0x80) == 0) break; }
            return val;
        }
    }
}
