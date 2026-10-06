// 快捷输入模组 QuickInputMod
// 功能：
//   ① 定时输入：输入一段字符，在用户给定的总时间、每按键时间（可选填）内按顺序输出虚拟按键。
//   ② 快速编排：
//        a. 逐键编排：输入要按下的按键、按下时长、是否吸附（相邻按键间无空闲时间）、键与键之间若需的等待时长。
//        b. 同时按键：同一行用 "+" 连接多个键（如 "ctrl+shift+a"），同按下、同松开（和弦）。
// 实现 IVkMod + IUiMod：界面作为 QuickInputPanel 控件直接注入主程序右侧“快捷输入”标签页，
// 无需点“操作/导入…”弹窗；同时保留 ICommandMod 弹窗备用通道（同一界面控件）。
// 编译方式（在 模组开发 目录执行）：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:library ^
//     /out:..\mods\QuickInputMod.dll /r:System.dll /r:System.Core.dll ^
//     /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"..\虚拟键盘模拟器.exe" ^
//     "快捷输入模组 QuickInputMod.cs"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using VkSim;

namespace VkSim.ModQuick
{
    // ================= 一个待生成的按键步骤（计划项） =================
    public class KeyStepSpec
    {
        public string Key;    // 键名或组合键，如 "a" / "ctrl+shift+a"
        public int Press;     // 按下时刻(ms，绝对时间)
        public int Release;   // 松开时刻(ms，绝对时间)
        public KeyStepSpec(string key, int press, int release) { Key = key; Press = press; Release = release; }
    }

    // ================= 纯计划逻辑（与界面分离，便于校验/测试） =================
    public static class QuickPlanner
    {
        // 字符 -> 键名（组合键用 "+" 连接）。返回 null 表示该字符无法用单键表达（中文等）。
        public static string KeyFromChar(char c)
        {
            if (c >= 'a' && c <= 'z') return c.ToString();
            if (c >= 'A' && c <= 'Z') return "shift+" + (char)(c + 32);
            if (c >= '0' && c <= '9') return c.ToString();
            switch (c)
            {
                case ' ': return "space";
                case '\n': return "enter";
                case '\t': return "tab";
                case '`': return "`";
                case '-': return "-";
                case '=': return "=";
                case '[': return "[";
                case ']': return "]";
                case '\\': return "\\";
                case ';': return ";";
                case '\'': return "'";
                case ',': return ",";
                case '.': return ".";
                case '/': return "/";
                case '!': return "shift+1";
                case '@': return "shift+2";
                case '#': return "shift+3";
                case '$': return "shift+4";
                case '%': return "shift+5";
                case '^': return "shift+6";
                case '&': return "shift+7";
                case '*': return "shift+8";
                case '(': return "shift+9";
                case ')': return "shift+0";
                case '_': return "shift+-";
                case '+': return "shift+=";
                case '{': return "shift+[";
                case '}': return "shift+]";
                case '|': return "shift+\\";
                case ':': return "shift+;";
                case '"': return "shift+'";
                case '<': return "shift+,";
                case '>': return "shift+.";
                case '?': return "shift+/";
                case '~': return "shift+`";
            }
            return null;
        }

        // 功能①：定时输入计划。
        // 保证按顺序（不重叠），且整段在 totalMs 内完成；超出容量的每键时长会被压缩。
        public static List<KeyStepSpec> PlanTimed(string text, int totalMs,
            bool perKeyUsed, int perKeyMs, bool snap,
            out int skipped, out string note)
        {
            skipped = 0;
            note = "";
            List<string> keys = new List<string>();
            foreach (char c in text)
            {
                if (c == '\r') continue;
                string k = KeyFromChar(c);
                if (k == null) { skipped++; continue; }
                keys.Add(k);
            }
            List<KeyStepSpec> res = new List<KeyStepSpec>();
            int N = keys.Count;
            if (N == 0) return res;
            if (totalMs < 1) totalMs = 1;

            int maxDur = (int)Math.Max(1, Math.Floor((double)totalMs / N));
            int dur;
            if (perKeyUsed && perKeyMs > 0)
            {
                if (perKeyMs > maxDur)
                {
                    dur = maxDur;
                    note = "指定每键时长超出总时间容量，已压缩为 " + maxDur + "ms 以保证顺序完成";
                }
                else dur = perKeyMs;
            }
            else dur = maxDur;

            for (int i = 0; i < N; i++)
            {
                int press, release;
                if (snap)
                {
                    press = i * dur;
                    release = press + dur;
                }
                else if (N == 1)
                {
                    press = 0;
                    release = dur;
                }
                else
                {
                    double spacing = (double)(totalMs - dur) / (N - 1);
                    if (spacing < dur) spacing = dur;
                    press = (int)Math.Round(i * spacing);
                    release = press + dur;
                }
                res.Add(new KeyStepSpec(keys[i], press, release));
            }
            return res;
        }

        // 功能②：快速编排计划。rows 每项为 (key, dur, snap, wait)。
        // 吸附(snap=true)：该键在前一键松开后立即按下（无空闲）；否则按下前等待 wait 毫秒。
        public static List<KeyStepSpec> PlanArrangement(List<ArrItem> rows)
        {
            List<KeyStepSpec> res = new List<KeyStepSpec>();
            int t = 0;
            foreach (ArrItem r in rows)
            {
                if (string.IsNullOrEmpty(r.Key)) continue;
                int dur = r.Dur < 1 ? 100 : r.Dur;
                int wait = r.Wait < 0 ? 0 : r.Wait;
                int press, release;
                if (r.Snap) { press = t; release = press + dur; }
                else { press = t + wait; release = press + dur; }
                t = release;
                res.Add(new KeyStepSpec(r.Key, press, release));
            }
            return res;
        }
    }

    // 编排表格一行
    public class ArrItem
    {
        public string Key; public int Dur; public bool Snap; public int Wait;
        public ArrItem(string key, int dur, bool snap, int wait) { Key = key; Dur = dur; Snap = snap; Wait = wait; }
    }

    // ================= 模组入口（UI 注入 + 弹窗备用） =================
    public class QuickInputMod : IVkMod, IUiMod, ICommandMod
    {
        private QuickInputPanel _panel;

        public string Name { get { return "快捷输入模组"; } }
        public string Description { get { return "①定时输入字符；②快速编排按键(时长/吸附/等待)，支持同时按键(和弦)；界面直接注入主程序右侧面板"; } }

        public string UiTitle { get { return "快捷输入"; } }
        public Control UiContent { get { return _panel; } }

        public void Initialize(IVkContext ctx)
        {
            _panel = new QuickInputPanel(ctx);
            if (ctx != null)
                ctx.SetStatus("快捷输入模组已载入：界面已注入主程序右侧“快捷输入”页（无需弹窗）");
        }

        // 备用弹窗通道：把同一面板控件放进一个小窗口
        public void OnCommand(IVkContext ctx)
        {
            if (ctx == null) return;
            QuickInputPanel pan = new QuickInputPanel(ctx);
            using (Form f = new Form())
            {
                f.Text = "快捷输入模组";
                f.ClientSize = new Size(600, 528);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Color.FromArgb(38, 42, 50);
                pan.Dock = DockStyle.Fill;
                f.Controls.Add(pan);
                f.ShowDialog();
            }
        }

        public void Dispose()
        {
            if (_panel != null) { _panel.Dispose(); _panel = null; }
        }
    }

    // ================= 注入主程序右侧面板的嵌入视图（两标签页） =================
    public class QuickInputPanel : UserControl
    {
        private IVkContext _ctx;
        private float _scale = 1f;
        private const int LogicalW = 404;   // 与主程序 ModPanelW(420) 配合，内容留出左右边距后实际宽 380
        // 标签页1
        private TextBox txtChars;
        private NumericUpDown nudTotal, nudPerKey;
        private CheckBox chkPerKey, chkSnap1, chkClear1;
        private Label lblPreview1;
        // 标签页2
        private DataGridView grid;
        private CheckBox chkClear2;

        private int S(float v) { return (int)(v * _scale + 0.5f); }

        public QuickInputPanel(IVkContext ctx)
        {
            _ctx = ctx;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                _scale = g.DpiX / 96f;
            if (_scale < 1f) _scale = 1f;

            BackColor = Color.FromArgb(38, 42, 50);
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.BackColor = Color.FromArgb(38, 42, 50);
            tabs.Font = new Font("Segoe UI", 9f);
            tabs.TabPages.Add(BuildTab1());
            tabs.TabPages.Add(BuildTab2());
            Controls.Add(tabs);
        }

        private void StyleBtn(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            if (primary) { b.BackColor = Color.FromArgb(0, 120, 212); b.ForeColor = Color.White; }
            else { b.BackColor = Color.FromArgb(60, 66, 78); b.ForeColor = Color.FromArgb(233, 236, 241); }
            b.Cursor = Cursors.Hand;
        }

        private Label MakeLabel(string text, Point loc, Size size, int alpha)
        {
            Label l = new Label();
            l.Text = text; l.Location = loc; l.Size = size; l.AutoSize = false;
            l.ForeColor = Color.FromArgb(alpha, 233, 236, 241);
            return l;
        }

        // ============ 标签页1：定时输入 ============
        private TabPage BuildTab1()
        {
            TabPage p = new TabPage("① 定时输入");
            p.BackColor = Color.FromArgb(38, 42, 50);
            p.AutoScroll = true;
            int x = S(12), w = S(LogicalW - 24);

            Label hint = MakeLabel("输入一段字符，按顺序输出虚拟按键，整段在“总时间”内完成。大写=shift+字母；中文等无法用单键表达的字符自动跳过并在生成时提示。",
                new Point(x, S(12)), new Size(w, S(40)), 200);
            p.Controls.Add(hint);

            Label l1 = MakeLabel("字符:", new Point(x, S(58)), new Size(S(44), S(26)), 233);
            p.Controls.Add(l1);
            txtChars = new TextBox();
            txtChars.Location = new Point(x + S(44), S(56)); txtChars.Size = new Size(w - S(44), S(26));
            txtChars.ForeColor = Color.White; txtChars.BackColor = Color.FromArgb(40, 45, 54);
            txtChars.BorderStyle = BorderStyle.FixedSingle;
            txtChars.Text = "Hello 123";
            p.Controls.Add(txtChars);

            Label l2 = MakeLabel("总时间(ms):", new Point(x, S(94)), new Size(S(88), S(26)), 233);
            p.Controls.Add(l2);
            nudTotal = new NumericUpDown();
            nudTotal.Minimum = 10; nudTotal.Maximum = 3600000; nudTotal.Value = 1000; nudTotal.Increment = 50;
            nudTotal.Location = new Point(x + S(92), S(92)); nudTotal.Size = new Size(S(92), S(26));
            nudTotal.BackColor = Color.FromArgb(40, 45, 54); nudTotal.ForeColor = Color.White;
            p.Controls.Add(nudTotal);

            chkPerKey = new CheckBox();
            chkPerKey.Text = "指定每键时长:"; chkPerKey.Checked = false;
            chkPerKey.Location = new Point(x + S(188), S(95)); chkPerKey.Size = new Size(S(116), S(22));
            chkPerKey.ForeColor = Color.FromArgb(233, 236, 241);
            chkPerKey.CheckedChanged += (s, e) => { nudPerKey.Enabled = chkPerKey.Checked; };
            p.Controls.Add(chkPerKey);

            nudPerKey = new NumericUpDown();
            nudPerKey.Minimum = 1; nudPerKey.Maximum = 60000; nudPerKey.Value = 60; nudPerKey.Enabled = false;
            nudPerKey.Location = new Point(x + S(306), S(92)); nudPerKey.Size = new Size(S(74), S(26));
            nudPerKey.BackColor = Color.FromArgb(40, 45, 54); nudPerKey.ForeColor = Color.White;
            p.Controls.Add(nudPerKey);

            chkSnap1 = new CheckBox();
            chkSnap1.Text = "吸附（相邻键背靠背无空闲）"; chkSnap1.Checked = true;
            chkSnap1.Location = new Point(x, S(126)); chkSnap1.Size = new Size(S(240), S(22));
            chkSnap1.ForeColor = Color.FromArgb(233, 236, 241);
            p.Controls.Add(chkSnap1);

            lblPreview1 = MakeLabel("", new Point(x, S(156)), new Size(w, S(150)), 170);
            lblPreview1.TextAlign = ContentAlignment.TopLeft;
            p.Controls.Add(lblPreview1);

            chkClear1 = new CheckBox();
            chkClear1.Text = "生成前清空当前序列"; chkClear1.Checked = true;
            chkClear1.Location = new Point(x, S(314)); chkClear1.Size = new Size(S(180), S(22));
            chkClear1.ForeColor = Color.FromArgb(233, 236, 241);
            p.Controls.Add(chkClear1);

            Button gen = new Button(); gen.Text = "生成步骤";
            gen.Location = new Point(w - S(110), S(308)); gen.Size = new Size(S(110), S(34));
            StyleBtn(gen, true);
            gen.Click += (s, e) => GenTimed();
            p.Controls.Add(gen);

            txtChars.TextChanged += (s, e) => UpdatePreview1();
            nudTotal.ValueChanged += (s, e) => UpdatePreview1();
            nudPerKey.ValueChanged += (s, e) => UpdatePreview1();
            chkPerKey.CheckedChanged += (s, e) => UpdatePreview1();
            chkSnap1.CheckedChanged += (s, e) => UpdatePreview1();

            p.AutoScrollMinSize = new Size(w, S(360));
            UpdatePreview1();
            return p;
        }

        private void UpdatePreview1()
        {
            int skip; string note;
            List<KeyStepSpec> specs = QuickPlanner.PlanTimed(txtChars.Text, (int)nudTotal.Value,
                chkPerKey.Checked, (int)nudPerKey.Value, chkSnap1.Checked, out skip, out note);
            if (specs.Count == 0)
            {
                lblPreview1.Text = "（没有可输出的字符" + (skip > 0 ? "；已跳过 " + skip + " 个不支持字符" : "") + "）";
                return;
            }
            int span = specs[specs.Count - 1].Release;
            int dur = specs[0].Release - specs[0].Press;
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format("预览：{0} 键 | 每键 {1}ms | 总跨度约 {2}ms", specs.Count, dur, span));
            if (skip > 0) sb.Append(" | 已跳过 " + skip + " 个不支持字符");
            sb.Append(Environment.NewLine);
            int shown = Math.Min(specs.Count, 8);
            for (int i = 0; i < shown; i++) sb.Append(specs[i].Key + "[" + specs[i].Press + "-" + specs[i].Release + "]  ");
            if (specs.Count > shown) sb.Append("…");
            if (note != "") sb.Append(Environment.NewLine + note);
            lblPreview1.Text = sb.ToString();
        }

        private void GenTimed()
        {
            if (_ctx == null) return;
            int skip; string note;
            List<KeyStepSpec> specs = QuickPlanner.PlanTimed(txtChars.Text, (int)nudTotal.Value,
                chkPerKey.Checked, (int)nudPerKey.Value, chkSnap1.Checked, out skip, out note);
            if (specs.Count == 0)
            {
                _ctx.SetStatus("快捷输入：没有可输出的字符" + (skip > 0 ? "（已跳过 " + skip + " 个不支持字符）" : "") + "，未生成");
                return;
            }
            if (chkClear1.Checked) _ctx.ClearSteps();
            foreach (KeyStepSpec sp in specs) _ctx.AddStep(sp.Key, sp.Press, sp.Release);
            int span = specs[specs.Count - 1].Release;
            int dur = specs[0].Release - specs[0].Press;
            _ctx.SetStatus(string.Format("快捷输入已生成 {0} 步，总跨度约 {1}ms（每键 {2}ms）{3}——请把目标软件置于前台后点“运行”",
                specs.Count, span, dur, (skip > 0 ? "，跳过 " + skip + " 个不支持字符" : "") + (note != "" ? "，" + note : "")));
        }

        // ============ 标签页2：快速编排 ============
        private TabPage BuildTab2()
        {
            TabPage p = new TabPage("② 快速编排");
            p.BackColor = Color.FromArgb(38, 42, 50);
            p.AutoScroll = true;
            int x = S(12), w = S(LogicalW - 24);

            Button bAdd = new Button(); bAdd.Text = "添加按键";
            bAdd.Location = new Point(x, S(14)); bAdd.Size = new Size(S(76), S(30)); StyleBtn(bAdd, true);
            bAdd.Click += (s, e) => grid.Rows.Add("a", 100, true, 0);
            p.Controls.Add(bAdd);

            Button bChord = new Button(); bChord.Text = "和弦…";
            bChord.Location = new Point(x + S(80), S(14)); bChord.Size = new Size(S(64), S(30)); StyleBtn(bChord, false);
            bChord.Click += (s, e) => AddChord();
            p.Controls.Add(bChord);

            Button bDel = new Button(); bDel.Text = "删除选中";
            bDel.Location = new Point(x + S(148), S(14)); bDel.Size = new Size(S(76), S(30)); StyleBtn(bDel, false);
            bDel.Click += (s, e) =>
            {
                List<DataGridViewRow> sel = new List<DataGridViewRow>();
                foreach (DataGridViewRow r in grid.SelectedRows) sel.Add(r);
                foreach (DataGridViewRow r in sel) if (!r.IsNewRow) grid.Rows.Remove(r);
            };
            p.Controls.Add(bDel);

            Button bUp = new Button(); bUp.Text = "上移";
            bUp.Location = new Point(x + S(228), S(14)); bUp.Size = new Size(S(44), S(30)); StyleBtn(bUp, false);
            bUp.Click += (s, e) => MoveRow(-1);
            p.Controls.Add(bUp);

            Button bDown = new Button(); bDown.Text = "下移";
            bDown.Location = new Point(x + S(276), S(14)); bDown.Size = new Size(S(44), S(30)); StyleBtn(bDown, false);
            bDown.Click += (s, e) => MoveRow(1);
            p.Controls.Add(bDown);

            Button bClear = new Button(); bClear.Text = "清空";
            bClear.Location = new Point(x + S(324), S(14)); bClear.Size = new Size(S(40), S(30)); StyleBtn(bClear, false);
            bClear.Click += (s, e) => grid.Rows.Clear();
            p.Controls.Add(bClear);

            grid = new DataGridView();
            grid.Location = new Point(x, S(50)); grid.Size = new Size(w, S(290));
            grid.BackgroundColor = Color.FromArgb(30, 34, 42);
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 50, 60);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(233, 236, 241);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 50, 60);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(40, 45, 54);
            grid.DefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 212);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9f);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f);

            grid.Columns.Add("key", "按键/组合键");
            grid.Columns.Add("dur", "按下时长ms");
            // “吸附”必须是真正的复选框列：显式创建，不能只 Add 字符串标题（那会是文本框列，强转会抛 InvalidCastException）
            DataGridViewCheckBoxColumn sc = new DataGridViewCheckBoxColumn();
            sc.Name = "snap";
            sc.HeaderText = "吸附";
            sc.Width = S(50);
            sc.TrueValue = true;
            sc.FalseValue = false;
            grid.Columns.Add(sc);
            grid.Columns.Add("wait", "等待ms");
            grid.Columns[0].Width = S(150);
            grid.Columns[1].Width = S(76);
            grid.Columns[3].Width = S(56);

            p.Controls.Add(grid);

            // 示例行，便于上手
            grid.Rows.Add("a", 100, true, 0);
            grid.Rows.Add("s", 100, true, 0);
            grid.Rows.Add("ctrl+shift+a", 120, true, 0);
            grid.Rows.Add("enter", 100, false, 200);

            Label info = MakeLabel("吸附：相邻键背靠背无空闲；未勾选则按下前等待“等待ms”。和弦：一行多个键用“+”连接，同时按下/松开（如 ctrl+shift+a）。",
                new Point(x, S(348)), new Size(w, S(40)), 170);
            p.Controls.Add(info);

            chkClear2 = new CheckBox();
            chkClear2.Text = "生成前清空当前序列"; chkClear2.Checked = true;
            chkClear2.Location = new Point(x, S(390)); chkClear2.Size = new Size(S(180), S(22));
            chkClear2.ForeColor = Color.FromArgb(233, 236, 241);
            p.Controls.Add(chkClear2);

            Button gen = new Button(); gen.Text = "生成步骤";
            gen.Location = new Point(w - S(110), S(384)); gen.Size = new Size(S(110), S(34));
            StyleBtn(gen, true);
            gen.Click += (s, e) => GenArrangement();
            p.Controls.Add(gen);

            p.AutoScrollMinSize = new Size(w, S(430));
            return p;
        }

        private void MoveRow(int dir)
        {
            if (grid.SelectedRows.Count != 1) return;
            DataGridViewRow r = grid.SelectedRows[0];
            if (r.IsNewRow) return;
            int idx = r.Index;
            int ni = idx + dir;
            if (ni < 0 || ni >= grid.Rows.Count) return;
            DataGridViewRow other = grid.Rows[ni];
            if (other.IsNewRow) return;
            object[] a = RowData(r);
            object[] b = RowData(other);
            SetRow(r, b);
            SetRow(other, a);
            grid.ClearSelection();
            grid.Rows[ni].Selected = true;
        }

        private static object[] RowData(DataGridViewRow r)
        {
            return new object[] { r.Cells[0].Value, r.Cells[1].Value, r.Cells[2].Value, r.Cells[3].Value };
        }

        private static void SetRow(DataGridViewRow r, object[] a)
        {
            r.Cells[0].Value = a[0]; r.Cells[1].Value = a[1]; r.Cells[2].Value = a[2]; r.Cells[3].Value = a[3];
        }

        private void AddChord()
        {
            using (ChordComposer c = new ChordComposer(_scale))
            {
                if (c.ShowDialog(this) != DialogResult.OK) return;
                string k = c.Result;
                if (k == "") return;
                grid.Rows.Add(k, 120, true, 0);
            }
        }

        private void GenArrangement()
        {
            if (_ctx == null) return;
            List<ArrItem> rows = new List<ArrItem>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow) continue;
                object vk = r.Cells[0].Value;
                string key = vk == null ? "" : vk.ToString().Trim();
                if (key == "") continue;
                int dur = ParseInt(r.Cells[1].Value, 100, 1, 60000);
                bool snap = ToBool(r.Cells[2].Value);
                int wait = ParseInt(r.Cells[3].Value, 0, 0, 60000);
                rows.Add(new ArrItem(key, dur, snap, wait));
            }
            if (rows.Count == 0)
            {
                _ctx.SetStatus("快捷输入：编排列表为空，未生成");
                return;
            }
            List<KeyStepSpec> specs = QuickPlanner.PlanArrangement(rows);
            if (chkClear2.Checked) _ctx.ClearSteps();
            foreach (KeyStepSpec sp in specs) _ctx.AddStep(sp.Key, sp.Press, sp.Release);
            _ctx.SetStatus(string.Format("快速编排已生成 {0} 步，总时长约 {1}ms——请把目标软件置于前台后点“运行”",
                specs.Count, specs[specs.Count - 1].Release));
        }

        private static int ParseInt(object o, int def, int min, int max)
        {
            string s = o == null ? "" : o.ToString();
            int v;
            if (!int.TryParse(s, out v)) v = def;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        private static bool ToBool(object o)
        {
            if (o is bool) return (bool)o;
            return false;
        }
    }

    // ================= 和弦（同时按键）组合器 =================
    public class ChordComposer : Form
    {
        private float _scale = 1f;
        private TextBox txt;
        private CheckedListBox mods;

        private int S(float v) { return (int)(v * _scale + 0.5f); }

        public string Result
        {
            get
            {
                StringBuilder mb = new StringBuilder();
                foreach (object it in mods.CheckedItems) { if (mb.Length > 0) mb.Append("+"); mb.Append(it.ToString()); }
                string tail = txt.Text.Trim().TrimStart('+');
                if (mb.Length == 0) return tail;
                if (tail == "") return mb.ToString();
                return mb.ToString() + "+" + tail;
            }
        }

        public ChordComposer(float scale)
        {
            _scale = scale;
            Text = "和弦（同时按键）";
            ClientSize = new Size(S(360), S(250));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(38, 42, 50);
            Font = new Font("Segoe UI", 9f);

            Label l0 = new Label();
            l0.Text = "勾选修饰键，并填写主键（可多个用“+”），例如：a / f5 / 1+2+3";
            l0.Location = new Point(S(14), S(12)); l0.Size = new Size(S(330), S(30));
            l0.ForeColor = Color.FromArgb(200, 233, 236, 241); l0.AutoSize = false;
            Controls.Add(l0);

            mods = new CheckedListBox();
            mods.Location = new Point(S(14), S(46)); mods.Size = new Size(S(120), S(120));
            mods.CheckOnClick = true;
            mods.BackColor = Color.FromArgb(40, 45, 54); mods.ForeColor = Color.FromArgb(233, 236, 241);
            mods.BorderStyle = BorderStyle.FixedSingle;
            mods.Items.Add("ctrl"); mods.Items.Add("alt"); mods.Items.Add("shift"); mods.Items.Add("win");
            Controls.Add(mods);

            Label l1 = new Label();
            l1.Text = "主键(可组合，如 a / f5 / 1+2+3):";
            l1.Location = new Point(S(146), S(46)); l1.Size = new Size(S(200), S(20));
            l1.ForeColor = Color.FromArgb(180, 233, 236, 241); l1.AutoSize = false;
            Controls.Add(l1);

            txt = new TextBox();
            txt.Location = new Point(S(146), S(68)); txt.Size = new Size(S(200), S(26));
            txt.ForeColor = Color.White; txt.BackColor = Color.FromArgb(40, 45, 54);
            txt.BorderStyle = BorderStyle.FixedSingle; txt.Text = "a";
            Controls.Add(txt);

            Label hint = new Label();
            hint.Text = "结果预览："; hint.Location = new Point(S(14), S(180)); hint.Size = new Size(S(70), S(24));
            hint.ForeColor = Color.FromArgb(180, 233, 236, 241); hint.AutoSize = false;
            Controls.Add(hint);

            Label prev = new Label();
            prev.Name = "prev"; prev.Location = new Point(S(88), S(180)); prev.Size = new Size(S(258), S(24));
            prev.ForeColor = Color.FromArgb(120, 255, 200, 120); prev.AutoSize = false;
            Controls.Add(prev);
            Action refresh = null;
            refresh = () => prev.Text = Result == "" ? "（空）" : Result;
            mods.ItemCheck += (s, e) => refresh();
            txt.TextChanged += (s, e) => refresh();
            refresh();

            Button ok = new Button(); ok.Text = "确定";
            ok.Location = new Point(S(160), S(210)); ok.Size = new Size(S(80), S(30));
            ok.FlatStyle = FlatStyle.Flat; ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = Color.FromArgb(0, 120, 212); ok.ForeColor = Color.White;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cc = new Button(); cc.Text = "取消";
            cc.Location = new Point(S(252), S(210)); cc.Size = new Size(S(80), S(30));
            cc.FlatStyle = FlatStyle.Flat; cc.FlatAppearance.BorderSize = 0;
            cc.BackColor = Color.FromArgb(60, 66, 78); cc.ForeColor = Color.FromArgb(233, 236, 241);
            cc.DialogResult = DialogResult.Cancel;
            Controls.Add(cc);
        }
    }
}
