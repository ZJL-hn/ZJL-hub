using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VkSim
{
    public class MainForm : Form
    {
        // 数据
        private List<KeyStep> steps = new List<KeyStep>();
        private int nextId = 1;
        private object stepsLock = new object();

        // 控件
        private KeyboardView kbView;
        private DataGridView grid, grid2;   // 步骤网格（左栏 + 右栏并排）
        private int _side = 0;              // 当前选中所在栏：0=左栏 1=右栏
        private int _curRow = -1;           // 当前选中行（所在栏内）
        private TimelineView tlView;
        private TextBox tbKey, tbPress, tbRelease, tbDur;
        private NumericUpDown nudLoop, nudSpeed, nudDelay;
        private Label lblStatus, lblSec;
        private Button btnRun, btnStop, btnRecord, btnSave, btnOpen, btnNew;

        // 运行 / 录制状态
        private bool playing = false;
        private bool recording = false;
        private volatile bool cancelPlay;
        private Thread playThread;
        private KeyRecorder recorder;
        private PreciseClock recClock;
        private Dictionary<int, KeyStep> recPressed = new Dictionary<int, KeyStep>();
        private HashSet<string> curDown = new HashSet<string>();
        private bool refreshing = false;   // 网格程序化刷新守卫，防止 CellValueChanged 无限递归

        // 防误触开关：运行中若前台仍是本应用，则跳过按键（默认开启）
        private Button btnSelf;
        private bool selfProtect = true;

        // 模组系统
        private List<IVkMod> mods = new List<IVkMod>();
        private ModContext modCtx;
        private Button btnMod;
        private TabControl modTabs;            // 模组注入界面的容器（右侧标签页，有模组注入时显示）
        private Button btnModToggle;           // 缩起/展开模组栏按钮
        private bool _modCollapsed;            // 模组栏是否被缩起
        private const int ModPanelW = 420;     // 模组面板逻辑宽度（像素，按 DPI 缩放）

        // 全局急停热键 Ctrl+F9（最小化/后台亦生效）
        private const int GLOBAL_STOP_ID = 0x4B50;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_F9 = 0x78;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // 配色（Fluent 暗色）
        private static readonly Color BG1 = Color.FromArgb(31, 34, 41);
        private static readonly Color BG2 = Color.FromArgb(46, 51, 60);
        private static readonly Color Accent = Color.FromArgb(0, 120, 212);
        private static readonly Color GlassText = Color.FromArgb(233, 236, 241);

        // DPI 缩放（布局按物理 DPI 放大；字号用 pt 单位会自动随 DPI 缩放，不得再乘 _scale）
        private float _scale = 1f;
        private const int BaseW = 1240, BaseH = 820;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr dc, int index);
        private struct FixedInfo { public Control C; public Point Loc; public Size Size; }
        private List<FixedInfo> _fixed = new List<FixedInfo>();
        private void Fixed(Control c, int x, int y, int w, int h)
        {
            c.Location = new Point(x, y); c.Size = new Size(w, h);
            FixedInfo fi; fi.C = c; fi.Loc = new Point(x, y); fi.Size = new Size(w, h);
            _fixed.Add(fi);
        }

        public MainForm()
        {
            Text = "虚拟键盘模拟器 · Fluent";
            AutoScaleMode = AutoScaleMode.None;   // 手动按 _scale 缩放，禁止自动缩放
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                int dpi = GetDeviceCaps(dc, 88);   // LOGPIXELSX
                ReleaseDC(IntPtr.Zero, dc);
                if (dpi > 0) _scale = dpi / 96f;
            }
            catch { }
            ClientSize = new Size((int)(BaseW * _scale), (int)(BaseH * _scale));
            MinimumSize = new Size((int)(1160 * _scale), (int)(760 * _scale));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(38, 42, 50);
            KeyPreview = true;
            KeyDown += OnFormKey;

            BuildToolbar();
            BuildEditorGrid();
            BuildTimeline();
            BuildStatus();

            // 模组注入界面容器（先建好，InitMods 里模组 Initialize 会往里加标签页）
            modTabs = new DarkTabs();
            modTabs.Visible = false;
            modTabs.BackColor = Color.FromArgb(38, 42, 50);
            modTabs.Font = new Font("Segoe UI", 9f);
            Controls.Add(modTabs);

            // 缩起/展开模组栏按钮：醒目高亮蓝，展开时位于面板右上角（点它收起），收起时位于右缘（点它展开）
            btnModToggle = new Button();
            btnModToggle.Text = "«";
            btnModToggle.FlatStyle = FlatStyle.Flat;
            btnModToggle.FlatAppearance.BorderSize = 0;
            btnModToggle.BackColor = Accent;
            btnModToggle.ForeColor = Color.White;
            btnModToggle.Cursor = Cursors.Hand;
            btnModToggle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnModToggle.Click += (s, e) => ToggleModPanel();
            Controls.Add(btnModToggle);

            InitMods();
            LayoutControls();
        }

        // ---------- 响应式布局：内容随窗口缩放 ----------
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        private void LayoutControls()
        {
            if (kbView == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            foreach (FixedInfo fi in _fixed)
            {
                fi.C.Location = new Point((int)(fi.Loc.X * _scale), (int)(fi.Loc.Y * _scale));
                fi.C.Size = new Size((int)(fi.Size.Width * _scale), (int)(fi.Size.Height * _scale));
            }

            // 模组注入面板（右栏标签页）：有模组注入且未缩起时占用右侧宽度，主内容相应左缩。
            // 窗口偏窄时自动收缩面板，保证主内容始终有约 520 逻辑像素，避免模组栏遮挡原有 UI。
            bool showMod = modTabs != null && modTabs.TabPages.Count > 0 && !_modCollapsed;
            int panelW = 0;
            if (showMod)
            {
                int ideal = (int)(ModPanelW * _scale);
                int maxFit = W - (int)(16 * _scale) - (int)(544 * _scale);
                panelW = Math.Min(ideal, Math.Max(maxFit, (int)(300 * _scale)));
            }
            int mainW = W - (int)(16 * _scale);
            if (panelW > 0) mainW -= (panelW + (int)(8 * _scale));
            if (mainW < (int)(520 * _scale)) mainW = (int)(520 * _scale);

            kbView.Location = new Point((int)(8 * _scale), (int)(52 * _scale));
            kbView.Size = new Size(mainW, (int)(300 * _scale));
            int gw = mainW / 2;
            grid.Location = new Point((int)(8 * _scale), (int)(360 * _scale));
            grid.Size = new Size(gw - (int)(4 * _scale), (int)(176 * _scale));
            grid2.Location = new Point((int)(8 * _scale) + gw + (int)(4 * _scale), (int)(360 * _scale));
            grid2.Size = new Size(gw - (int)(4 * _scale), (int)(176 * _scale));
            int tlY = (int)(588 * _scale);
            int tlH = H - tlY - (int)(30 * _scale);
            if (tlH < (int)(140 * _scale)) tlH = (int)(140 * _scale);
            tlView.Location = new Point((int)(8 * _scale), tlY);
            tlView.Size = new Size(mainW, tlH);
            // 状态栏始终贴在时间轴底部之下，避免窗口高度偏小时被时间轴覆盖
            int statusH = (int)(22 * _scale);
            int statusY = Math.Max(tlY + tlH + (int)(4 * _scale), H - statusH - (int)(2 * _scale));
            lblStatus.Location = new Point((int)(8 * _scale), statusY);
            lblStatus.Size = new Size(W - (int)(30 * _scale), statusH);
            lblSec.Location = new Point(W - (int)(220 * _scale), statusY);
            lblSec.Height = statusH;

            // 模组面板定位：从工具栏下方延伸到状态栏上方
            if (modTabs != null)
            {
                if (panelW > 0)
                {
                    modTabs.Location = new Point(W - panelW - (int)(8 * _scale), (int)(52 * _scale));
                    modTabs.Size = new Size(panelW, Math.Max(statusY - (int)(54 * _scale), (int)(120 * _scale)));
                    modTabs.Visible = true;
                }
                else modTabs.Visible = false;
            }

            // 缩起/展开按钮：只在有模组注入时显示；展开时在面板右上角（tab 行内）点它收起，收起时在右缘点它展开
            if (btnModToggle != null)
            {
                bool hasMod = modTabs != null && modTabs.TabPages.Count > 0;
                btnModToggle.Visible = hasMod;
                if (hasMod)
                {
                    btnModToggle.Text = _modCollapsed ? "»" : "«";
                    btnModToggle.Size = new Size((int)(30 * _scale), (int)(26 * _scale));
                    if (_modCollapsed)
                        btnModToggle.Location = new Point(W - (int)(8 * _scale) - (int)(30 * _scale), (int)(H / 2 - (int)(13 * _scale)));
                    else
                        btnModToggle.Location = new Point(W - (int)(8 * _scale) - (int)(30 * _scale), (int)(54 * _scale));
                }
            }

            kbView.SetScale(_scale);
            tlView.SetScale(_scale);
            tlView.RecalcMax();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (LinearGradientBrush br = new LinearGradientBrush(ClientRectangle, BG1, BG2, 90f))
                e.Graphics.FillRectangle(br, ClientRectangle);
        }

        // ---------- 工具栏 ----------
        private void BuildToolbar()
        {
            btnRun = MakeButton("▶  运行", 10, 8, 92, 26, Accent, Color.White);
            btnRun.Click += (s, e) => StartPlay();

            btnStop = MakeButton("■  停止", 108, 8, 70, 26, Color.FromArgb(60, 66, 78), GlassText);
            btnStop.Click += (s, e) => { cancelPlay = true; };

            btnRecord = MakeButton("●  录制", 184, 8, 70, 26, Color.FromArgb(60, 66, 78), GlassText);
            btnRecord.Click += (s, e) => ToggleRecord();

            Label l1 = new Label(); l1.Text = "循环:"; l1.ForeColor = GlassText; l1.BackColor = Color.Transparent;
            Fixed(l1, 270, 12, 40, 20);
            nudLoop = new NumericUpDown(); nudLoop.Minimum = 1; nudLoop.Maximum = 9999; nudLoop.Value = 1;
            Fixed(nudLoop, 308, 9, 60, 24);

            Label l2 = new Label(); l2.Text = "速度:"; l2.ForeColor = GlassText; l2.BackColor = Color.Transparent;
            Fixed(l2, 380, 12, 40, 20);
            nudSpeed = new NumericUpDown(); nudSpeed.Minimum = 5; nudSpeed.Maximum = 500; nudSpeed.Value = 100; nudSpeed.Increment = 5;
            Fixed(nudSpeed, 418, 9, 56, 24);
            Label l3 = new Label(); l3.Text = "%"; l3.ForeColor = GlassText; l3.BackColor = Color.Transparent;
            Fixed(l3, 478, 12, 24, 20);

            Label l4 = new Label(); l4.Text = "延迟:"; l4.ForeColor = GlassText; l4.BackColor = Color.Transparent;
            Fixed(l4, 508, 12, 40, 20);
            nudDelay = new NumericUpDown(); nudDelay.Minimum = 0; nudDelay.Maximum = 999;
            nudDelay.DecimalPlaces = 1; nudDelay.Increment = 0.5m; nudDelay.Value = 0;
            Fixed(nudDelay, 548, 9, 60, 24);

            btnNew = MakeButton("新建", 616, 8, 60, 26, Color.FromArgb(60, 66, 78), GlassText);
            btnNew.Click += (s, e) => ClearAll();
            btnOpen = MakeButton("打开", 680, 8, 60, 26, Color.FromArgb(60, 66, 78), GlassText);
            btnOpen.Click += (s, e) => OpenProject();
            btnSave = MakeButton("保存", 744, 8, 60, 26, Color.FromArgb(60, 66, 78), GlassText);
            btnSave.Click += (s, e) => SaveProject();

            btnSelf = MakeButton("防误触:开", 812, 8, 86, 26, Accent, Color.White);
            btnSelf.Click += (s, e) => ToggleSelfProtect();

            btnMod = MakeButton("模组", 906, 8, 56, 26, Color.FromArgb(60, 66, 78), GlassText);
            btnMod.Click += (s, e) => OpenModList();

            Label help = new Label(); help.Text = "Ctrl+F9急停 · Ctrl连点组合 · 滚轮缩放 · 空白拖动平移";
            help.ForeColor = Color.FromArgb(150, 233, 236, 241); help.BackColor = Color.Transparent;
            Fixed(help, 970, 12, 270, 20);

            // 仅添加非按钮控件（按钮已由 MakeButton 自行加入，避免重复导致层级混乱）
            Controls.Add(nudLoop); Controls.Add(nudSpeed); Controls.Add(nudDelay); Controls.Add(l1); Controls.Add(l2); Controls.Add(l3); Controls.Add(l4); Controls.Add(help);
        }

        private void BuildEditorGrid()
        {            kbView = new KeyboardView();
            kbView.Location = new Point(8, 52);
            kbView.Size = new Size(ClientSize.Width - 16, 300);
            kbView.KeyClicked += OnKeyClicked;
            Controls.Add(kbView);

            // 步骤网格：左栏 + 右栏并排（ReadOnly 展示与选中，编辑走底部编辑行）
            grid = MakeStepGrid();
            grid2 = MakeStepGrid();
            Controls.Add(grid);
            Controls.Add(grid2);
            grid.SelectionChanged += (s, e) => { _side = 0; _curRow = grid.CurrentRow == null ? -1 : grid.CurrentRow.Index; SyncEditorFromGrid(); };
            grid2.SelectionChanged += (s, e) => { _side = 1; _curRow = grid2.CurrentRow == null ? -1 : grid2.CurrentRow.Index; SyncEditorFromGrid(); };

            // 编辑器输入行
            int y0 = 544;
            AddLabel("按键", 8, y0 + 8);
            tbKey = AddBox(44, y0 + 4, 150);
            AddLabel("按下ms", 202, y0 + 8);
            tbPress = AddBox(252, y0 + 4, 90);
            AddLabel("松开ms", 350, y0 + 8);
            tbRelease = AddBox(400, y0 + 4, 90);
            AddLabel("时长ms", 498, y0 + 8);
            tbDur = AddBox(548, y0 + 4, 90);
            // 输入“按下/松开”后自动算出按压时长（时长 = 松开 - 按下）
            tbPress.TextChanged += (s, e) => AutoDur();
            tbRelease.TextChanged += (s, e) => AutoDur();

            Button bAdd = MakeButton("＋ 追加", 650, y0 + 4, 74, 26, Accent, Color.White);
            bAdd.Click += (s, e) => AddStep(false);
            Button bIns = MakeButton("插入", 728, y0 + 4, 60, 26, Color.FromArgb(60, 66, 78), GlassText);
            bIns.Click += (s, e) => AddStep(true);
            Button bUpd = MakeButton("更新", 792, y0 + 4, 60, 26, Color.FromArgb(60, 66, 78), GlassText);
            bUpd.Click += (s, e) => UpdateStep();
            Button bDel = MakeButton("删除", 856, y0 + 4, 60, 26, Color.FromArgb(60, 66, 78), GlassText);
            bDel.Click += (s, e) => DeleteStep();
            Button bUp = MakeButton("上移", 920, y0 + 4, 56, 26, Color.FromArgb(60, 66, 78), GlassText);
            bUp.Click += (s, e) => MoveStep(-1);
            Button bDn = MakeButton("下移", 980, y0 + 4, 56, 26, Color.FromArgb(60, 66, 78), GlassText);
            bDn.Click += (s, e) => MoveStep(1);
            Button bClr = MakeButton("清空", 1040, y0 + 4, 56, 26, Color.FromArgb(120, 60, 60), Color.White);
            bClr.Click += (s, e) => { if (MsgOk("清空全部步骤?")) ClearAll(); };
        }

        private void BuildTimeline()
        {
            tlView = new TimelineView();
            tlView.Location = new Point(8, 582);
            tlView.Size = new Size(ClientSize.Width - 16, 200);
            tlView.Steps = steps;
            tlView.StepEdited += OnStepEdited;
            tlView.StepSelected += OnStepSelected;
            Controls.Add(tlView);
        }

        private void BuildStatus()
        {
            lblStatus = new Label(); lblStatus.Text = "待机 — 运行前请把目标软件窗口置于前台";
            lblStatus.AutoSize = false; lblStatus.Width = 700; lblStatus.Height = 20;
            lblStatus.Location = new Point(8, 794);
            lblStatus.ForeColor = Color.FromArgb(170, 233, 236, 241);
            lblStatus.BackColor = Color.Transparent;
            lblSec = new Label(); lblSec.Text = ""; lblSec.AutoSize = true;
            lblSec.Location = new Point(ClientSize.Width - 220, 794);
            lblSec.ForeColor = Color.FromArgb(170, 233, 236, 241);
            lblSec.BackColor = Color.Transparent;
            Controls.Add(lblStatus);
            Controls.Add(lblSec);
        }

        // ---------- 辅助控件 ----------
        private Panel GlassPanel(int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y); p.Size = new Size(w, h);
            p.BackColor = Color.Transparent;
            p.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(0, 0, p.Width, p.Height);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                    R.Fill(e.Graphics, b, r, 8);
                using (Pen pe = new Pen(Color.FromArgb(60, 255, 255, 255)))
                    R.Stroke(e.Graphics, pe, r, 8);
            };
            return p;
        }

        private Button MakeButton(string text, int x, int y, int w, int h, Color bg, Color fg)
        {
            Button b = new Button();
            b.Text = text;
            Fixed(b, x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = bg; b.ForeColor = fg;
            b.Font = new Font("Segoe UI", 9f);
            b.Cursor = Cursors.Hand;
            b.MouseEnter += (s, e) => b.BackColor = Lighten(bg, 20);
            b.MouseLeave += (s, e) => b.BackColor = bg;
            Controls.Add(b);
            return b;
        }

        private static Color Lighten(Color c, int a)
        { return Color.FromArgb(c.R + a > 255 ? 255 : c.R + a, c.G + a > 255 ? 255 : c.G + a, c.B + a > 255 ? 255 : c.B + a); }

        private void AddLabel(string t, int x, int y)
        {
            Label l = new Label(); l.Text = t; l.AutoSize = true; l.ForeColor = GlassText; l.BackColor = Color.Transparent;
            Fixed(l, x, y, 60, 20); Controls.Add(l);
        }
        private TextBox AddBox(int x, int y, int w)
        {
            TextBox tb = new TextBox();
            Fixed(tb, x, y, w, 22);
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = Color.FromArgb(52, 58, 68); tb.ForeColor = Color.White;
            tb.Font = new Font("Segoe UI", 9f);
            Controls.Add(tb); return tb;
        }

        // ---------- 键盘点击 ----------
        private void OnKeyClicked(string name, bool combo)
        {
            if (playing || recording) return;
            // 应用模组键名映射（音乐模组把 q~u/a~j 映射为音名）
            if (modCtx != null) name = modCtx.Map(name);
            if (!combo) { tbKey.Text = name; }
            else
            {
                string cur = tbKey.Text.Trim();
                if (cur.Length == 0) tbKey.Text = name;
                else if (!cur.Contains(name)) tbKey.Text = cur + "+" + name;
            }
            tbKey.Focus();
        }

        // ---------- 网格数据 ----------
        private void OnGridCellChanged(object s, DataGridViewCellEventArgs e)
        {
            if (refreshing) return;   // 程序化刷新期间不响应，避免递归
            if (e.RowIndex < 0) return;
            KeyStep st = steps[e.RowIndex];
            string col = grid.Columns[e.ColumnIndex].Name;
            if (col == "cKey") st.KeyName = GetStr(e.RowIndex, "cKey");
            else if (col == "cP") st.PressTime = GetInt(e.RowIndex, "cP", st.PressTime);
            else if (col == "cR") st.ReleaseTime = GetInt(e.RowIndex, "cR", st.ReleaseTime);
            FixStep(st);
            SortSteps();
        }

        private void FixStep(KeyStep st)
        {
            if (st.ReleaseTime <= st.PressTime) st.ReleaseTime = st.PressTime + 50;
            if (st.PressTime < 0) st.PressTime = 0;
        }
        private string GetStr(int r, string col)
        {
            object v = grid.Rows[r].Cells[col].Value; return v == null ? "" : v.ToString();
        }
        private int GetInt(int r, string col, int dflt)
        {
            int x; if (int.TryParse(GetStr(r, col), out x)) return x; return dflt;
        }

        private int HalfStart() { return (steps.Count + 1) / 2; }
        private int StepIndex() { return _side == 0 ? _curRow : HalfStart() + _curRow; }

        private DataGridView MakeStepGrid()
        {
            DataGridView g = new DataGridView();
            g.BackgroundColor = Color.FromArgb(40, 45, 54);
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 68);
            g.ColumnHeadersDefaultCellStyle.ForeColor = GlassText;
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            g.DefaultCellStyle.BackColor = Color.FromArgb(40, 45, 54);
            g.DefaultCellStyle.ForeColor = Color.FromArgb(233, 236, 241);
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 212);
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.EditMode = DataGridViewEditMode.EditOnEnter;
            g.Columns.Add("cId", "#");
            g.Columns.Add("cKey", "按键");
            g.Columns.Add("cP", "按下(ms)");
            g.Columns.Add("cR", "松开(ms)");
            g.Columns.Add("cD", "时长(ms)");
            g.Columns["cId"].Width = (int)(40 * _scale); g.Columns["cId"].ReadOnly = true;
            g.Columns["cKey"].Width = (int)(160 * _scale);
            g.Columns["cP"].Width = (int)(110 * _scale); g.Columns["cR"].Width = (int)(110 * _scale); g.Columns["cD"].Width = (int)(110 * _scale);
            g.Columns["cD"].ReadOnly = true;
            g.ReadOnly = true;
            g.RowTemplate.Height = (int)(24 * _scale);
            g.ColumnHeadersHeight = (int)(34 * _scale);
            return g;
        }

        private void RefreshGrid()
        {
            refreshing = true;
            try
            {
                grid.Rows.Clear(); grid2.Rows.Clear();
                int n = steps.Count, half = (n + 1) / 2;
                for (int i = 0; i < half; i++) { int r = grid.Rows.Add(); FillRow(grid, r, steps[i], i + 1); }
                for (int i = half; i < n; i++) { int r = grid2.Rows.Add(); FillRow(grid2, r, steps[i], i + 1); }
            }
            finally { refreshing = false; }
        }

        private void FillRow(DataGridView g, int r, KeyStep st, int no)
        {
            g.Rows[r].Tag = st;
            g.Rows[r].Cells["cId"].Value = no;
            g.Rows[r].Cells["cKey"].Value = st.KeyName;
            g.Rows[r].Cells["cP"].Value = st.PressTime;
            g.Rows[r].Cells["cR"].Value = st.ReleaseTime;
            g.Rows[r].Cells["cD"].Value = st.Duration;
        }

        private void SortSteps()
        {
            steps.Sort((a, b) => a.PressTime.CompareTo(b.PressTime));
            RefreshGrid();
            tlView.RecalcMax();
        }

        // ---------- 步骤增删改 ----------
        // 按下/松开已输入时自动填按压时长（松开-按下；松开不小于按下才更新）
        private void AutoDur()
        {
            if (tbPress == null || tbRelease == null || tbDur == null) return;
            int p, r;
            if (int.TryParse(tbPress.Text, out p) && int.TryParse(tbRelease.Text, out r) && r >= p)
                tbDur.Text = (r - p).ToString();
        }

        private void AddStep(bool insertBefore)
        {
            if (playing) return;
            int press; if (!int.TryParse(tbPress.Text, out press)) press = 0;
            int dur; bool hasDur = int.TryParse(tbDur.Text, out dur);
            int release = press + 100;
            if (hasDur && dur > 0) release = press + dur;
            else { int rr; if (int.TryParse(tbRelease.Text, out rr)) release = rr; }
            string key = tbKey.Text.Trim();
            if (key.Length == 0) { lblStatus.Text = "请先填写按键"; return; }
            if (press < 0) press = 0;
            if (release <= press) release = press + 50;
            KeyStep st = new KeyStep(nextId++, key, press, release);
            int ins = StepIndex();
            if (insertBefore && ins >= 0 && ins < steps.Count)
                steps.Insert(ins, st);
            else steps.Add(st);
            SortSteps();
            lblStatus.Text = "已添加步骤: " + st.KeyName;
            if (modCtx != null) modCtx.RaiseStepAdded(st);
        }

        private void UpdateStep()
        {
            int idx = StepIndex();
            if (idx < 0 || idx >= steps.Count) return;
            KeyStep st = steps[idx];
            string key = tbKey.Text.Trim();
            if (key.Length > 0) st.KeyName = key;
            int p; if (int.TryParse(tbPress.Text, out p)) st.PressTime = p;
            int d; if (int.TryParse(tbDur.Text, out d) && d > 0) st.ReleaseTime = st.PressTime + d;
            else { int rr; if (int.TryParse(tbRelease.Text, out rr)) st.ReleaseTime = rr; }
            FixStep(st);
            SortSteps();
        }

        private void DeleteStep()
        {
            int idx = StepIndex();
            if (idx < 0 || idx >= steps.Count) return;
            KeyStep st = steps[idx];
            steps.RemoveAt(idx);
            SortSteps();
            if (modCtx != null) modCtx.RaiseStepRemoved(st);
        }

        private void MoveStep(int dir)
        {
            int i = StepIndex();
            int j = i + dir;
            if (i < 0 || j < 0 || j >= steps.Count) return;
            KeyStep tmp = steps[i]; steps[i] = steps[j]; steps[j] = tmp;
            SortSteps();
            OnStepSelected(steps[j]);
        }

        private void ClearAll()
        {
            steps.Clear(); nextId = 1;
            RefreshGrid(); tlView.RecalcMax();
            tbKey.Text = tbPress.Text = tbRelease.Text = tbDur.Text = "";
        }

        // ---------- 模组系统 ----------
        private void InitMods()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mods");
            modCtx = new ModContext(this, dir);
            mods = ModLoader.LoadAll(dir, modCtx, (m) => lblStatus.Text = m);
            if (mods.Count > 0) lblStatus.Text = "已载入 " + mods.Count + " 个模组";
        }

        private void OpenModList()
        {
            if (modCtx == null) return;
            using (ModListForm f = new ModListForm(mods, modCtx, (m) => lblStatus.Text = m))
                f.ShowDialog(this);
        }

        // ---- 供 ModContext 调用的内部方法（由模组通过 IVkContext 触发，均回到 UI 线程） ----
        internal List<KeyStep> ModSteps { get { return steps; } }

        internal void ModAddStep(string key, int press, int release)
        {
            if (playing) return;
            KeyStep st = new KeyStep(nextId++, key, press, release);
            if (st.ReleaseTime <= st.PressTime) st.ReleaseTime = st.PressTime + 50;
            if (st.PressTime < 0) st.PressTime = 0;
            steps.Add(st);
            ScheduleRefresh();   // 批量生成时合并成一次刷新，避免逐条重建表格导致卡死
            if (modCtx != null) modCtx.RaiseStepAdded(st);
        }

        // 去抖：模组批量 AddStep 时只入内存列表，待所有步骤加完后再统一排序+重建表格（一次完成）
        private bool _modGridDirty = false;
        private void ScheduleRefresh()
        {
            if (_modGridDirty) return;
            _modGridDirty = true;
            BeginInvoke((Action)(() => { _modGridDirty = false; SortSteps(); }));
        }

        internal void ModRemoveStep(KeyStep st)
        {
            if (steps.Remove(st))
            {
                SortSteps();
                if (modCtx != null) modCtx.RaiseStepRemoved(st);
            }
        }

        internal void ModClear()
        {
            steps.Clear(); nextId = 1;
            RefreshGrid(); tlView.RecalcMax();
            tbKey.Text = tbPress.Text = tbRelease.Text = tbDur.Text = "";
        }

        internal void ModInject(string combo)
        {
            string[] tokens = combo.Split('+');
            try
            {
                Native.PressCombo(tokens);
                Thread.Sleep(30);
                Native.ReleaseCombo(tokens);
            }
            catch { }
        }

        internal void ModSetStatus(string msg)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => ModSetStatus(msg))); return; }
            lblStatus.Text = msg;
        }

        // 模组界面注入：把模组控件作为右侧新标签页挂到 modTabs（无需弹窗，Initialize 即注入）
        internal Control ModAddUi(string title, Control content)
        {
            if (content == null || modTabs == null) return content;
            if (InvokeRequired) { BeginInvoke((Action)(() => ModAddUi(title, content))); return content; }
            TabPage tp = new TabPage(title ?? "模组界面");
            tp.BackColor = Color.FromArgb(38, 42, 50);
            tp.BorderStyle = BorderStyle.None;   // 去掉标签页白边框
            tp.Padding = new Padding(0);
            content.Dock = DockStyle.Fill;
            tp.Controls.Add(content);
            modTabs.TabPages.Add(tp);
            LayoutControls();
            return tp;
        }

        // 模组界面移除：卸载模组时删掉对应标签页，空则隐藏面板恢复主界面全宽
        internal void ModRemoveUi(Control content)
        {
            if (content == null || modTabs == null) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => ModRemoveUi(content))); return; }
            for (int i = modTabs.TabPages.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(modTabs.TabPages[i], content) || modTabs.TabPages[i].Controls.Contains(content))
                {
                    modTabs.TabPages[i].Controls.Clear();
                    modTabs.TabPages.RemoveAt(i);
                }
            }
            LayoutControls();
        }

        // 缩起/展开模组栏
        private void ToggleModPanel()
        {
            _modCollapsed = !_modCollapsed;
            LayoutControls();
        }

        // 自绘暗色 TabControl：去掉系统默认白色边框（含非客户区边框），标签页头用暗色绘制，贴合整体 Fluent 深色主题
        private class DarkTabs : TabControl
        {
            public DarkTabs()
            {
                DrawMode = TabDrawMode.OwnerDrawFixed;
                SizeMode = TabSizeMode.Fixed;
                ItemSize = new Size(100, 26);
            }

            // 关键：去掉 WS_BORDER 与 WS_EX_CLIENTEDGE，真正消除 TabControl 外圈白框（非客户区边框 OnPaint 盖不住）
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.Style &= ~0x00800000;      // WS_BORDER
                    cp.ExStyle &= ~0x00000200;    // WS_EX_CLIENTEDGE
                    return cp;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // 整体背景
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(38, 42, 50)))
                    e.Graphics.FillRectangle(bg, ClientRectangle);
                // 逐个绘制标签页头（无系统边框/白框）
                for (int i = 0; i < TabCount; i++)
                {
                    Rectangle r = GetTabRect(i);
                    bool sel = i == SelectedIndex;
                    Color c = sel ? Color.FromArgb(30, 34, 42) : Color.FromArgb(45, 50, 60);
                    using (SolidBrush b = new SolidBrush(c))
                        e.Graphics.FillRectangle(b, r);
                    TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, r,
                        sel ? Color.White : Color.FromArgb(180, 188, 198),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                // 页头下方一条 1px 分隔线，保持层次但不带白框
                using (Pen p = new Pen(Color.FromArgb(30, 34, 42)))
                    e.Graphics.DrawLine(p, 0, ItemSize.Height, ClientSize.Width, ItemSize.Height);
            }
        }

        // 模组读写播放速度%（音乐模组 BPM 联动）
        internal double ModGetSpeed() { return (double)nudSpeed.Value; }
        internal void ModSetSpeed(double v)
        {
            if (nudSpeed == null) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => ModSetSpeed(v))); return; }
            double c = v;
            if (c < (double)nudSpeed.Minimum) c = (double)nudSpeed.Minimum;
            if (c > (double)nudSpeed.Maximum) c = (double)nudSpeed.Maximum;
            nudSpeed.Value = (decimal)c;
        }

        private void SyncEditorFromGrid()
        {
            int idx = StepIndex();
            if (idx < 0 || idx >= steps.Count) return;
            KeyStep st = steps[idx];
            tbKey.Text = st.KeyName;
            tbPress.Text = st.PressTime.ToString();
            tbRelease.Text = st.ReleaseTime.ToString();
            tbDur.Text = st.Duration.ToString();
        }

        private void OnStepEdited(KeyStep st)
        {
            for (int gi = 0; gi < 2; gi++)
            {
                DataGridView g = gi == 0 ? grid : grid2;
                for (int i = 0; i < g.Rows.Count; i++)
                {
                    if (ReferenceEquals(g.Rows[i].Tag, st))
                    {
                        g.Rows[i].Cells["cP"].Value = st.PressTime;
                        g.Rows[i].Cells["cR"].Value = st.ReleaseTime;
                        g.Rows[i].Cells["cD"].Value = st.Duration;
                        break;
                    }
                }
            }
            SortSteps();
        }

        private void OnStepSelected(KeyStep st)
        {
            if (grid == null || grid2 == null) return;
            for (int gi = 0; gi < 2; gi++)
            {
                DataGridView g = gi == 0 ? grid : grid2;
                for (int i = 0; i < g.Rows.Count; i++)
                {
                    if (ReferenceEquals(g.Rows[i].Tag, st))
                    {
                        g.ClearSelection(); g.Rows[i].Selected = true; g.CurrentCell = g.Rows[i].Cells["cKey"];
                        _side = gi; _curRow = i; return;
                    }
                }
            }
        }

        // ---------- 保存 / 打开 ----------
        private void SaveProject()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "虚拟键盘方案 (*.vks)|*.vks";
                dlg.FileName = "方案.vks";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                List<string> lines = new List<string>();
                lines.Add("VKS1");
                lock (stepsLock) foreach (KeyStep st in steps) lines.Add(st.KeyName + ";" + st.PressTime + ";" + st.ReleaseTime);
                File.WriteAllLines(dlg.FileName, lines.ToArray());
                lblStatus.Text = "已保存: " + Path.GetFileName(dlg.FileName);
            }
        }

        private void OpenProject()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "虚拟键盘方案 (*.vks)|*.vks|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string[] lines = File.ReadAllLines(dlg.FileName);
                if (lines.Length == 0 || lines[0].Trim() != "VKS1") { lblStatus.Text = "文件格式不正确"; return; }
                steps.Clear();
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split(';');
                    if (p.Length < 3) continue;
                    int pr, re;
                    if (int.TryParse(p[1], out pr) && int.TryParse(p[2], out re))
                        steps.Add(new KeyStep(nextId++, p[0], pr, re));
                }
                SortSteps();
                lblStatus.Text = "已打开: " + Path.GetFileName(dlg.FileName) + "（" + steps.Count + " 步）";
            }
        }

        // ---------- 录制 ----------
        private void ToggleRecord()
        {
            if (playing) return;
            if (!recording)
            {
                recorder = new KeyRecorder();
                recorder.KeyEvent += OnRecKey;
                if (!recorder.Install()) { lblStatus.Text = "录制钩子安装失败"; return; }
                recClock = new PreciseClock();
                recPressed.Clear();
                recording = true;
                btnRecord.Text = "■ 停止录制";
                btnRecord.BackColor = Color.FromArgb(200, 60, 60);
                lblStatus.Text = "● 录制中 — 在目标窗口敲键即可生成步骤（按 F9 或再点停止结束）";
            }
            else
            {
                recorder.Uninstall();
                recording = false;
                btnRecord.Text = "●  录制";
                btnRecord.BackColor = Color.FromArgb(60, 66, 78);
                // 收尾未松开的键
                foreach (KeyValuePair<int, KeyStep> kv in recPressed)
                { kv.Value.ReleaseTime = (int)recClock.ElapsedMs; }
                recPressed.Clear();
                SortSteps();
                lblStatus.Text = "录制结束，共 " + steps.Count + " 步";
            }
        }

        private void OnRecKey(int vk, bool down)
        {
            string name = KeyMap.NameOf(vk);
            if (name == null) return;
            string orig = name;
            // 应用模组键名映射（音乐模组把 q~u/a~j 映射为音名）
            if (modCtx != null) name = modCtx.Map(name);
            double now = recClock.ElapsedMs;
            if (down)
            {
                if (recPressed.ContainsKey(vk)) return; // 长按重复
                KeyStep st = new KeyStep(nextId++, name, (int)now, (int)now + 50);
                recPressed[vk] = st;
                steps.Add(st);
                SortSteps();
                // 录制所见即所得：显示按下键与最终注入键，便于确认模组映射
                lblStatus.Text = (orig != name) ? ("录制：按 " + orig + " → 注入 " + name + "（模组映射）") : ("录制：捕获 " + name);
            }
            else
            {
                KeyStep st;
                if (recPressed.TryGetValue(vk, out st)) { st.ReleaseTime = Math.Max(st.PressTime + 5, (int)now); recPressed.Remove(vk); }
            }
        }

        // ---------- 执行 ----------
        private void StartPlay()
        {
            if (playing || recording) return;
            if (steps.Count == 0) { lblStatus.Text = "没有可执行的步骤"; return; }
            playing = true;
            cancelPlay = false;
            btnRun.BackColor = Color.FromArgb(0, 110, 190);
            if (modCtx != null) modCtx.RaisePlayStarted();
            // 运行时最小化自身，让键盘焦点落到下面的目标窗口，按键才会作用到目标
            this.WindowState = FormWindowState.Minimized;
            int loop = (int)nudLoop.Value;
            double speed = (double)nudSpeed.Value / 100.0;
            double delayMs = (double)nudDelay.Value * 1000.0;   // 延迟不受速度缩放影响

            List<PlayEvt> evts = new List<PlayEvt>();
            double total = 0;
            foreach (KeyStep st in steps) total = Math.Max(total, st.ReleaseTime);
            for (int l = 0; l < loop; l++)
            {
                double off = delayMs + l * total / speed;
                foreach (KeyStep st in steps)
                {
                    evts.Add(new PlayEvt(off + st.PressTime / speed, true, st));
                    evts.Add(new PlayEvt(off + st.ReleaseTime / speed, false, st));
                }
            }
            evts.Sort((a, b) => a.Time.CompareTo(b.Time));

            playThread = new Thread(() => RunPlayback(evts, delayMs));
            playThread.IsBackground = true;
            playThread.Start();
        }

        private class PlayEvt { public double Time; public bool IsPress; public KeyStep Step; public PlayEvt(double t, bool p, KeyStep s) { Time = t; IsPress = p; Step = s; } }

        private void RunPlayback(List<PlayEvt> evts, double delayMs)
        {
            Native.timeBeginPeriod(1);
            try
            {
                PreciseClock clock = new PreciseClock();
                HashSet<string> held = new HashSet<string>();
                bool skipNotified = false;

                // 延迟等待（最小化期间给用户时间切换到目标窗口），标题显示倒计时
                if (delayMs > 0)
                {
                    while (clock.ElapsedMs < delayMs)
                    {
                        if (cancelPlay) break;
                        UiCountdown(delayMs - clock.ElapsedMs);
                        Thread.Sleep(80);
                    }
                    UiCountdown(-1);
                    if (cancelPlay) { return; }
                }

                foreach (PlayEvt ev in evts)
                {
                    clock.SleepUntil(ev.Time);
                    if (cancelPlay) break;
                    // 防误触：开启时，若当前前台仍是本应用，跳过本次按键
                    if (selfProtect && GetForegroundWindow() == this.Handle)
                    {
                        if (!skipNotified) { skipNotified = true; UiSkipHint(); }
                        continue;
                    }
                    skipNotified = false;
                    try
                    {
                        string[] tokens = ev.Step.KeyTokens;
                        if (ev.IsPress) { Native.PressCombo(tokens); foreach (string t in tokens) held.Add(t.Trim()); UiKeyChange(tokens, true); }
                        else { Native.ReleaseCombo(tokens); foreach (string t in tokens) held.Remove(t.Trim()); UiKeyChange(tokens, false); }
                    }
                    catch { }
                    UiCursor(ev.Time);
                }
                // 安全收尾：松开所有仍按住的键
                foreach (string t in held) try { Native.KeyUp(KeyMap.VkOf(t)); } catch { }
            }
            finally
            {
                Native.timeEndPeriod(1);
                UiDone();
            }
        }

        private void ToggleSelfProtect()
        {
            selfProtect = !selfProtect;
            btnSelf.Text = selfProtect ? "防误触:开" : "防误触:关";
            btnSelf.BackColor = selfProtect ? Accent : Color.FromArgb(60, 66, 78);
            lblStatus.Text = selfProtect ? "已开启防误触：运行中若焦点仍在本应用，按键将跳过" : "已关闭防误触：按键会照常作用于本应用";
        }

        private void UiSkipHint()
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => UiSkipHint())); return; }
            Text = "焦点在本应用，按键已跳过 — 请点击目标窗口";
        }

        private void UiCountdown(double remainMs)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => UiCountdown(remainMs))); return; }
            if (remainMs < 0) { Text = "虚拟键盘模拟器 · Fluent"; return; }
            Text = string.Format("延迟 {0:0.0} 秒后开始…", remainMs / 1000.0);
        }

        private void UiKeyChange(string[] tokens, bool down)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => UiKeyChange(tokens, down))); return; }
            foreach (string t in tokens) { if (down) curDown.Add(t); else curDown.Remove(t); }
            kbView.DownKeys = curDown;
            kbView.Invalidate();
        }

        private void UiCursor(double ms)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => UiCursor(ms))); return; }
            tlView.CursorMs = ms; tlView.Invalidate();
        }

        private void UiDone()
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => UiDone())); return; }
            playing = false;
            btnRun.BackColor = Accent;
            Text = "虚拟键盘模拟器 · Fluent";
            // 播放结束自动恢复窗口
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            if (modCtx != null) modCtx.RaisePlayEnded();
            tlView.CursorMs = -1; tlView.Invalidate();
            curDown.Clear(); kbView.DownKeys = curDown; kbView.Invalidate();
            lblStatus.Text = "执行完成";
        }

        private void OnFormKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F9 && (playing || recording))
            {
                cancelPlay = true;
                if (recording) ToggleRecord();
                lblStatus.Text = "已急停 (F9)";
                e.Handled = true;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 注册全局急停热键 Ctrl+F9（仅注册，不改变窗口尺寸）
            try { RegisterHotKey(this.Handle, GLOBAL_STOP_ID, MOD_CONTROL, VK_F9); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            // 跨 DPI 显示器移动时实时重排（Per-Monitor DPI 变化）
            if (m.Msg == 0x02E0)
            {
                int dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                if (dpi > 0) _scale = dpi / 96f;
                RECT r = (RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RECT));
                SetBounds(r.L, r.T, r.R - r.L, r.B - r.T, BoundsSpecified.None);
                LayoutControls();
            }
            else if (m.Msg == WM_HOTKEY && (int)m.WParam == GLOBAL_STOP_ID)
            {
                if (playing || recording)
                {
                    cancelPlay = true;
                    if (recording) ToggleRecord();
                    if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                    lblStatus.Text = "已全局急停 (Ctrl+F9)";
                }
                return;
            }
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancelPlay = true;
            try { UnregisterHotKey(this.Handle, GLOBAL_STOP_ID); } catch { }
            foreach (IVkMod m in mods) try { m.Dispose(); } catch { }
            if (recorder != null) { recorder.Uninstall(); recorder = null; }
            base.OnFormClosing(e);
        }

        private bool MsgOk(string msg)
        { return MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }
    }

    public static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int v);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr v);

        // DPI 感知：PerMonitorV2（Win10 1703+）→ PerMonitor（Win8.1+）→ System（Win7）
        static void SetDpiAware()
        {
            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); return; } catch { }
            try { SetProcessDpiAwareness(2); return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        [STAThread]
        public static void Main()
        {
            SetDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
