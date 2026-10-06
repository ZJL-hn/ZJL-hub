// 虚拟键盘模拟器 安装程序（零依赖，Windows 自带 .NET4 编译）
// 嵌入虚拟键盘模拟器.exe + mods\MusicMod.dll，安装到用户可选目录，创建快捷方式，注册卸载。
// 用法：
//   双击运行 = 安装向导
//   setup.exe /uninstall = 卸载
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VkSetup
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0)
            {
                string a = args[0].ToLower();
                if (a == "/uninstall" || a == "-uninstall" || a == "uninstall") { RunUninstall(); return 0; }
                if ((a == "/install" || a == "-install" || a == "install") && args.Length > 1)
                {
                    string e; bool ok = DoInstall(args[1], out e);
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "vksetup_result.txt"), (ok ? "OK:" : "FAIL:") + e); } catch { }
                    return ok ? 0 : 1;
                }
            }
            Application.Run(new SetupForm());
            return 0;
        }

        // 核心安装：释放文件 + 创建快捷方式 + 注册卸载
        internal static bool DoInstall(string dir, out string exePath)
        {
            exePath = null;
            try
            {
                dir = Path.GetFullPath(dir);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                exePath = Path.Combine(dir, ExeFile);
                WriteRes("vkexe", exePath);
                WriteRes("musicmod", Path.Combine(dir, "mods", "MusicMod.dll"));
                MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exePath, dir);
                MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"), exePath, dir);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", "1.0");
                    k.SetValue("Publisher", "Doubao 办公");
                    k.SetValue("InstallLocation", dir);
                    string setupPath = Process.GetCurrentProcess().MainModule.FileName;
                    k.SetValue("UninstallString", "\"" + setupPath + "\" /uninstall");
                    k.SetValue("DisplayIcon", "\"" + exePath + "\"");
                    k.SetValue("NoModify", 1);
                    k.SetValue("NoRepair", 1);
                }
                return true;
            }
            catch { return false; }
        }

        // 从嵌入资源读取并写出文件
        internal static void WriteRes(string resName, string targetPath)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
            {
                if (s == null) throw new Exception("缺少嵌入资源: " + resName);
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                    s.CopyTo(fs);
            }
        }

        internal const string AppName = "虚拟键盘模拟器";
        internal const string ExeFile = "虚拟键盘模拟器.exe";
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VKSim";

        // 创建 .lnk 快捷方式（COM 晚绑定，兼容无 dynamic）
        internal static void MakeShortcut(string lnkPath, string target, string workDir)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { /* 快捷方式创建失败不影响安装完成 */ }
        }

        internal static string DefaultDir()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "Programs", AppName);
        }

        static void RunUninstall()
        {
            try
            {
                string installDir = "";
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    if (k != null) { object v = k.GetValue("InstallLocation"); if (v != null) installDir = v.ToString(); }
                if (Directory.Exists(installDir))
                {
                    // 删除快捷方式
                    try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk")); } catch { }
                    try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk")); } catch { }
                    // 删除安装目录（含 mods）
                    try { Directory.Delete(installDir, true); } catch { }
                }
                // 删除注册项
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
                MessageBox.Show(AppName + " 已卸载。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class SetupForm : Form
    {
        private TextBox tbDir;
        private Label lblStatus;
        private Button btnInstall, btnExit;
        private float _scale = 1f;
        private int S(float v) { return (int)(v * _scale + 0.5f); }

        public SetupForm()
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                _scale = g.DpiX / 96f;
            if (_scale < 1f) _scale = 1f;

            Text = "虚拟键盘模拟器 安装程序";
            ClientSize = new Size(S(500), S(340));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(38, 42, 50);
            Font = new Font("Segoe UI", 9f);

            Label t1 = new Label();
            t1.Text = "安装 虚拟键盘模拟器";
            t1.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            t1.ForeColor = Color.White;
            t1.Location = new Point(S(18), S(16)); t1.Size = new Size(S(460), S(34)); t1.AutoSize = false;
            Controls.Add(t1);

            Label t2 = new Label();
            t2.Text = "虚拟键盘模拟器：自定义按键序列 + 拖拽时间轴 + 录制 + 音乐模组（MIDI 演奏）。\n将安装程序文件与本机版一致，含音乐模组扩展。";
            t2.ForeColor = Color.FromArgb(200, 233, 236, 241);
            t2.Location = new Point(S(18), S(56)); t2.Size = new Size(S(464), S(56)); t2.AutoSize = false;
            Controls.Add(t2);

            Label l3 = new Label(); l3.Text = "安装位置:"; l3.ForeColor = Color.FromArgb(233, 236, 241);
            l3.Location = new Point(S(18), S(126)); l3.Size = new Size(S(80), S(24)); l3.AutoSize = false;
            Controls.Add(l3);

            tbDir = new TextBox();
            tbDir.Text = Program.DefaultDir();
            tbDir.Location = new Point(S(100), S(123)); tbDir.Size = new Size(S(300), S(24));
            Controls.Add(tbDir);

            Button btnBrowse = new Button(); btnBrowse.Text = "浏览…";
            btnBrowse.Location = new Point(S(404), S(122)); btnBrowse.Size = new Size(S(78), S(26));
            Style(btnBrowse);
            btnBrowse.Click += (s, e) => Browse();
            Controls.Add(btnBrowse);

            Label t4 = new Label();
            t4.Text = "将安装: 虚拟键盘模拟器.exe、mods\\MusicMod.dll（音乐模组）\n并创建桌面/开始菜单快捷方式，可在“控制面板-程序”中卸载。";
            t4.ForeColor = Color.FromArgb(170, 233, 236, 241);
            t4.Location = new Point(S(18), S(160)); t4.Size = new Size(S(464), S(48)); t4.AutoSize = false;
            Controls.Add(t4);

            lblStatus = new Label();
            lblStatus.ForeColor = Color.FromArgb(255, 200, 200, 120);
            lblStatus.Location = new Point(S(18), S(222)); lblStatus.Size = new Size(S(464), S(24)); lblStatus.AutoSize = false;
            Controls.Add(lblStatus);

            btnInstall = new Button(); btnInstall.Text = "安装";
            btnInstall.Location = new Point(S(300), S(262)); btnInstall.Size = new Size(S(90), S(36));
            Style(btnInstall); btnInstall.BackColor = Color.FromArgb(0, 120, 212); btnInstall.ForeColor = Color.White;
            btnInstall.Click += (s, e) => Install();
            Controls.Add(btnInstall);

            btnExit = new Button(); btnExit.Text = "退出";
            btnExit.Location = new Point(S(396), S(262)); btnExit.Size = new Size(S(86), S(36));
            Style(btnExit);
            btnExit.Click += (s, e) => Close();
            Controls.Add(btnExit);
        }

        private void Style(Button b)
        { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = Color.FromArgb(60, 66, 78); b.ForeColor = Color.FromArgb(233, 236, 241); b.Cursor = Cursors.Hand; }

        private void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择安装目录";
                d.SelectedPath = tbDir.Text;
                if (d.ShowDialog(this) == DialogResult.OK) tbDir.Text = d.SelectedPath;
            }
        }

        private void Install()
        {
            string dir = tbDir.Text.Trim();
            if (string.IsNullOrEmpty(dir)) { SetStatus("请选择安装目录"); return; }
            lblStatus.Text = "正在安装…";
            Application.DoEvents();
            string exePath;
            if (Program.DoInstall(dir, out exePath))
            {
                lblStatus.Text = "✔ 安装完成";
                MessageBox.Show(Program.AppName + " 安装完成。\n安装目录: " + Path.GetFullPath(dir) + "\n\n已创建桌面和开始菜单快捷方式，可通过“控制面板-程序-卸载”移除。", "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                lblStatus.Text = "安装失败";
                MessageBox.Show("安装失败，请检查目录是否可写。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetStatus(string s) { lblStatus.Text = s; }
    }
}
