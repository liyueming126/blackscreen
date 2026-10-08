using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NapScreenOff
{
    internal class Config
    {
        public string StartTime = "12:10";
        public string EndTime = "13:10";
        public bool Enabled = true;
        public string Policy = "temp";
        public string Days = "1234567";
        public bool ShowHint = false;
        private readonly string _path;

        public string FilePath { get { return _path; } }

        public Config(string path)
        {
            _path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                    int eq = t.IndexOf('=');
                    if (eq < 0) continue;
                    string key = t.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = t.Substring(eq + 1).Trim();
                    if (key == "start") StartTime = val;
                    else if (key == "end") EndTime = val;
                    else if (key == "enabled") Enabled = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
                    else if (key == "policy") Policy = val;
                    else if (key == "days") Days = SanitizeDays(val);
                    else if (key == "hint") ShowHint = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
                }
            }
            catch { }

            TimeSpan r;
            if (!ParseTime(StartTime, out r)) StartTime = "12:10";
            if (!ParseTime(EndTime, out r)) EndTime = "13:10";
            if (!IsValidPolicy(Policy)) Policy = "temp";
            if (Days.Length == 0) Days = "1234567";
        }

        public static bool ParseTime(string s, out TimeSpan r)
        {
            return TimeSpan.TryParseExact(s, "h\\:mm", CultureInfo.InvariantCulture, out r)
                || TimeSpan.TryParseExact(s, "hh\\:mm", CultureInfo.InvariantCulture, out r);
        }

        public static bool IsValidPolicy(string s)
        {
            return s == "temp" || s == "day";
        }

        private static string SanitizeDays(string s)
        {
            if (string.IsNullOrEmpty(s)) return "1234567";
            string r = "";
            foreach (char c in s)
            {
                if (c >= '1' && c <= '7' && r.IndexOf(c) < 0) r += c;
            }
            return r.Length > 0 ? r : "1234567";
        }

        // 1=周一 ... 7=周日
        public bool IsDayEnabled(DateTime d)
        {
            int n = d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
            return Days.IndexOf((char)('0' + n)) >= 0;
        }

        public TimeSpan Start
        {
            get { TimeSpan r; return ParseTime(StartTime, out r) ? r : new TimeSpan(12, 10, 0); }
        }

        public TimeSpan End
        {
            get { TimeSpan r; return ParseTime(EndTime, out r) ? r : new TimeSpan(13, 10, 0); }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_path,
                    "; 午休息屏配置文件（可手动编辑，修改后需重启程序生效）\n"
                    + "; start/end 为 HH:mm 格式\n"
                    + "; days: 每周生效日期，1=周一、7=周日，如 days=1234567 表示每天\n"
                    + "; policy: temp=可临时退出(1分钟) / day=退出后当天不再显示\n"
                    + "; hint: true=息屏时在黑屏上显示操作提示文字 / false=纯净黑屏\n"
                    + "start=" + StartTime + "\nend=" + EndTime + "\nenabled=" + (Enabled ? "true" : "false") + "\npolicy=" + Policy + "\ndays=" + Days + "\nhint=" + (ShowHint ? "true" : "false") + "\n",
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    // 息屏方式：弹出全屏纯黑覆盖窗口（看起来像显示器关了，但系统仍在运行）
    internal class ScreenOffForm : Form
    {
        private bool _allowClose;
        private bool _cursorHidden;
        private readonly Label _hint = new Label();
        private readonly System.Windows.Forms.Timer _hintTimer = new System.Windows.Forms.Timer();

        public event EventHandler ExitRequest;
        // 息屏期间用户按了除 Esc 外的键、或单击鼠标（想操作却摸黑时的提示）
        public event EventHandler PassiveInput;

        public ScreenOffForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            KeyPreview = true;
            // 防止输入法候选窗在息屏画面上冒出来
            ImeMode = ImeMode.Disable;

            _hint.AutoSize = false;
            _hint.TextAlign = ContentAlignment.MiddleCenter;
            _hint.ForeColor = Color.FromArgb(118, 118, 118);
            _hint.BackColor = Color.Black;
            _hint.Font = new Font("Microsoft YaHei UI", 15F);
            _hint.Visible = false;
            Controls.Add(_hint);
            _hintTimer.Tick += delegate { _hintTimer.Stop(); _hint.Visible = false; };

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Raise(ExitRequest);
                else Raise(PassiveInput);
            };
            MouseClick += delegate { Raise(PassiveInput); };
            MouseDoubleClick += delegate(object s, MouseEventArgs e) { Raise(ExitRequest); };
            _hint.MouseClick += delegate { Raise(PassiveInput); };
            _hint.MouseDoubleClick += delegate(object s, MouseEventArgs e) { Raise(ExitRequest); };
            Deactivate += delegate(object s, EventArgs e) { if (Visible) TopMost = true; };
        }

        private void Raise(EventHandler h)
        {
            if (h != null) h(this, EventArgs.Empty);
        }

        private void CenterHint()
        {
            int w = Math.Min(ClientSize.Width - 80, 780);
            int h = 170;
            _hint.Bounds = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CenterHint();
        }

        // 在纯黑画面上短暂显示提示文字（息屏时唯一的反馈渠道）
        public void ShowHint(string text, int ms)
        {
            if (string.IsNullOrEmpty(text) || !Visible) return;
            _hint.Text = text;
            CenterHint();
            _hint.Visible = true;
            _hintTimer.Stop();
            _hintTimer.Interval = ms;
            _hintTimer.Start();
        }

        public void ShowOverlay()
        {
            if (Visible) return;
            // 每次弹出前按当前显示器布局重新计算全屏范围
            Bounds = SystemInformation.VirtualScreen;
            Show();
            if (!_cursorHidden) { Cursor.Hide(); _cursorHidden = true; }
            Activate();
        }

        public void HideOverlay()
        {
            if (!Visible) return;
            _hintTimer.Stop();
            _hint.Visible = false;
            if (_cursorHidden) { Cursor.Show(); _cursorHidden = false; }
            Hide();
        }

        public void ForceClose()
        {
            if (_cursorHidden) { Cursor.Show(); _cursorHidden = false; }
            _allowClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 只拦截用户主动关闭（Alt+F4 等）；系统关机/注销/任务管理器结束任务必须放行
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    internal class SettingsForm : Form
    {
        private static readonly string[] PolicyNames = {
            "临时退出（双击/Esc 退出，1 分钟后重新息屏）",
            "退出后当天不再显示息屏"
        };
        private static readonly string[] DayLabels = { "一", "二", "三", "四", "五", "六", "日" };

        private readonly TextBox _start = new TextBox();
        private readonly TextBox _end = new TextBox();
        private readonly CheckBox _enabled = new CheckBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly ComboBox _policy = new ComboBox();
        private readonly CheckBox _showHint = new CheckBox();
        private readonly CheckBox[] _dayChecks = new CheckBox[7];

        public string StartSetting { get { return _start.Text.Trim(); } }
        public string EndSetting { get { return _end.Text.Trim(); } }
        public bool EnabledSetting { get { return _enabled.Checked; } }
        public bool AutoStartSetting { get { return _autoStart.Checked; } }
        public bool ShowHintSetting { get { return _showHint.Checked; } }
        public string PolicySetting { get { return PolicyOf(_policy.SelectedIndex); } }
        public string DaysSetting
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 7; i++)
                    if (_dayChecks[i].Checked) sb.Append((char)('1' + i));
                return sb.Length == 0 ? "1234567" : sb.ToString();
            }
        }

        private static string PolicyOf(int idx)
        {
            return idx == 1 ? "day" : "temp";
        }

        private static int IndexOfPolicy(string p)
        {
            return p == "day" ? 1 : 0;
        }

        public SettingsForm(string start, string end, bool enabled, string policy, string days, bool autoStart, bool showHint)
        {
            Text = "午休息屏 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 234);
            // 按屏幕 DPI 自动缩放控件，适应不同分辨率/缩放级别
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label l1 = new Label { Text = "开始时间（HH:mm）", Location = new Point(15, 12), AutoSize = true };
            _start.Text = start;
            _start.Location = new Point(150, 9);
            _start.Width = 75;

            Label l2 = new Label { Text = "结束时间（HH:mm）", Location = new Point(15, 44), AutoSize = true };
            _end.Text = end;
            _end.Location = new Point(150, 41);
            _end.Width = 75;

            _autoStart.Text = "开机自启动";
            _autoStart.Checked = autoStart;
            _autoStart.Location = new Point(262, 9);
            _autoStart.AutoSize = true;

            _enabled.Text = "启用午休息屏";
            _enabled.Checked = enabled;
            _enabled.Location = new Point(262, 41);
            _enabled.AutoSize = true;

            Label l3 = new Label { Text = "息屏退出方式", Location = new Point(15, 78), AutoSize = true };
            _policy.DropDownStyle = ComboBoxStyle.DropDownList;
            _policy.Location = new Point(150, 75);
            _policy.Width = 215;
            _policy.Items.AddRange(PolicyNames);
            _policy.SelectedIndex = IndexOfPolicy(policy);
            // 下拉列表宽度按最长选项自适应，保证文字完整可见
            int maxW = 0;
            foreach (string s in PolicyNames)
            {
                int w = TextRenderer.MeasureText(s, _policy.Font).Width;
                if (w > maxW) maxW = w;
            }
            _policy.DropDownWidth = maxW + SystemInformation.VerticalScrollBarWidth + 4;

            Label l4 = new Label { Text = "生效日期", Location = new Point(15, 114), AutoSize = true };
            for (int i = 0; i < 7; i++)
            {
                CheckBox c = new CheckBox();
                c.Text = DayLabels[i];
                c.AutoSize = false;
                c.Size = new Size(34, 20);
                c.Checked = days.IndexOf((char)('1' + i)) >= 0;
                c.Location = new Point(100 + i * 37, 112);
                _dayChecks[i] = c;
                Controls.Add(c);
            }

            _showHint.Text = "息屏时在黑屏上显示操作提示文字";
            _showHint.Checked = showHint;
            _showHint.Location = new Point(15, 142);
            _showHint.AutoSize = true;

            Button ok = new Button { Text = "保存", Location = new Point(150, 198) };
            ok.Click += delegate { if (ValidateInput()) DialogResult = DialogResult.OK; };
            Button cancel = new Button { Text = "取消", Location = new Point(245, 198) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

            Controls.AddRange(new Control[] { l1, _start, l2, _end, _autoStart, _enabled, l3, _policy, l4, _showHint, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private bool ValidateInput()
        {
            TimeSpan d1, d2;
            if (!Config.ParseTime(StartSetting, out d1) || !Config.ParseTime(EndSetting, out d2))
            {
                MessageBox.Show(this, "时间格式错误，请使用 HH:mm 格式，例如 12:10。", "午休息屏");
                return false;
            }
            if (d2 <= d1)
            {
                MessageBox.Show(this, "结束时间必须晚于开始时间。", "午休息屏");
                return false;
            }
            bool anyDay = false;
            foreach (CheckBox c in _dayChecks)
                if (c.Checked) anyDay = true;
            if (!anyDay)
            {
                MessageBox.Show(this, "请至少选择一个生效日期。", "午休息屏");
                return false;
            }
            return true;
        }
    }

    internal class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "NapScreenOff";

        private readonly Config _cfg;
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ScreenOffForm _overlay = new ScreenOffForm();
        private readonly Mutex _mutex;
        private readonly ToolStripMenuItem _mEnable = new ToolStripMenuItem("启用午休息屏");
        private readonly ToolStripMenuItem _mAuto = new ToolStripMenuItem("开机自启动");

        private System.Windows.Forms.Timer _clock;
        private System.Windows.Forms.Timer _testTimer;
        private bool _testing;
        private DateTime _dismissUntil;
        private string _trayText;

        public TrayApp(string baseDir, Mutex mutex, bool showSettingsAtStart)
        {
            _mutex = mutex;
            _cfg = new Config(Program.ResolveConfigPath(baseDir));
            if (!File.Exists(_cfg.FilePath)) _cfg.Save();

            _overlay.ExitRequest += delegate { DismissScreenOff(false); };
            _overlay.PassiveInput += delegate { ShowStatusHint(); };

            _tray.Icon = MakeIcon();
            _tray.BalloonTipTitle = "午休息屏";
            _tray.Visible = true;

            SyncAutoStartPath();

            _mEnable.Checked = _cfg.Enabled;
            _mEnable.Click += delegate
            {
                _cfg.Enabled = !_cfg.Enabled;
                _mEnable.Checked = _cfg.Enabled;
                _cfg.Save();
                Update();
            };

            _mAuto.Checked = IsAutoStart();
            _mAuto.Click += delegate
            {
                bool now = IsAutoStart();
                SetAutoStart(!now);
                _mAuto.Checked = !now;
            };

            ToolStripMenuItem mExitBlack = new ToolStripMenuItem("立即退出当前息屏");
            mExitBlack.Click += delegate { DismissScreenOff(true); };

            ToolStripMenuItem mTest = new ToolStripMenuItem("测试息屏（5 秒）");
            mTest.Click += delegate { StartTest(); };

            ToolStripMenuItem mSettings = new ToolStripMenuItem("设置...");
            mSettings.Click += delegate { ShowSettings(); };

            ToolStripMenuItem mExit = new ToolStripMenuItem("退出");
            mExit.Click += delegate { ExitApp(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(_mEnable);
            menu.Items.Add(_mAuto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mExitBlack);
            menu.Items.Add(mTest);
            menu.Items.Add(mSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mExit);
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowSettings(); };

            // 1 秒轮询：到点后最多 1 秒内息屏/恢复（原来 10 秒轮询最多要等 10 秒）
            _clock = new System.Windows.Forms.Timer();
            _clock.Interval = 1000;
            _clock.Tick += delegate
            {
                try { Update(); }
                catch { }   // 时钟里的异常不能杀死托盘程序
            };
            _clock.Start();

            Update();

            if (showSettingsAtStart) ShowSettings();
        }

        private void Update()
        {
            if (_testing) return;

            TimeSpan now = DateTime.Now.TimeOfDay;
            bool inWindow = _cfg.Enabled && _cfg.IsDayEnabled(DateTime.Now)
                && _cfg.End > _cfg.Start && now >= _cfg.Start && now < _cfg.End;

            if (inWindow)
            {
                if (DateTime.Now >= _dismissUntil)
                {
                    _dismissUntil = DateTime.MinValue;
                    if (!_overlay.Visible)
                    {
                        _overlay.ShowOverlay();
                        if (_cfg.ShowHint) _overlay.ShowHint(PolicyHintText(), 4000);
                    }
                    SetTrayText("午休息屏：息屏中，至 " + _cfg.EndTime);
                }
                else
                {
                    if (_overlay.Visible) _overlay.HideOverlay();
                    if (_cfg.Policy == "day")
                        SetTrayText("午休息屏：今天不再显示");
                    else
                        SetTrayText("午休息屏：已暂停，" + _dismissUntil.ToString("HH:mm") + " 自动恢复");
                }
            }
            else
            {
                if (_overlay.Visible) _overlay.HideOverlay();
                SetTrayText("午休息屏：" + _cfg.StartTime + "-" + _cfg.EndTime + (_cfg.Enabled ? " 已启用" : " 已停用"));
            }
        }

        private void SetTrayText(string text)
        {
            if (_trayText == text) return;
            _trayText = text;
            _tray.Text = text;
        }

        private string PolicyHintText()
        {
            if (_cfg.Policy == "day")
                return "午休息屏中（至 " + _cfg.EndTime + "）\n按 Esc 或双击鼠标退出后，今天不再显示";
            return "午休息屏中（至 " + _cfg.EndTime + "）\n按 Esc 或双击鼠标可临时退出";
        }

        private void ShowStatusHint()
        {
            if (!_cfg.ShowHint) return;
            if (_testing)
                _overlay.ShowHint("测试息屏（5 秒）\n按 Esc 或双击鼠标可提前结束", 2500);
            else
                _overlay.ShowHint(PolicyHintText(), 2500);
        }

        private void DismissScreenOff(bool fromMenu)
        {
            if (_testing) { EndTest(); return; }

            if (!_overlay.Visible)
            {
                if (fromMenu) _tray.ShowBalloonTip(3000, "午休息屏", "当前没有在息屏。", ToolTipIcon.None);
                return;
            }

            _overlay.HideOverlay();
            if (_cfg.Policy == "day")
            {
                _dismissUntil = DateTime.Today.AddDays(1);
                _tray.ShowBalloonTip(3000, "午休息屏", "已退出息屏，今天不再显示。", ToolTipIcon.None);
            }
            else
            {
                _dismissUntil = DateTime.Now.AddMinutes(1);
                _tray.ShowBalloonTip(3000, "午休息屏", "已临时退出息屏，" + _dismissUntil.ToString("HH:mm") + " 将自动重新息屏。", ToolTipIcon.None);
            }
            Update();
        }

        private void StartTest()
        {
            if (_testing) return;

            _testing = true;
            _overlay.ShowOverlay();
            if (_cfg.ShowHint) _overlay.ShowHint("测试息屏（5 秒）\n按 Esc 或双击鼠标可提前结束", 4500);
            SetTrayText("午休息屏：测试息屏中（5 秒）…");

            if (_testTimer == null)
            {
                _testTimer = new System.Windows.Forms.Timer();
                _testTimer.Tick += delegate { EndTest(); };
            }
            _testTimer.Interval = 5000;
            _testTimer.Start();
        }

        private void EndTest()
        {
            if (!_testing) return;
            _testing = false;
            if (_testTimer != null) _testTimer.Stop();
            Update();   // 按真实时段重新计算：在时段内保持息屏，否则立即恢复
        }

        private void ShowSettings()
        {
            using (SettingsForm f = new SettingsForm(_cfg.StartTime, _cfg.EndTime, _cfg.Enabled, _cfg.Policy, _cfg.Days, IsAutoStart(), _cfg.ShowHint))
            {
                // 息屏中打开设置时，让窗口显示在黑屏之上
                f.TopMost = _overlay.Visible;
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _cfg.StartTime = f.StartSetting;
                    _cfg.EndTime = f.EndSetting;
                    _cfg.Enabled = f.EnabledSetting;
                    _cfg.Policy = f.PolicySetting;
                    _cfg.Days = f.DaysSetting;
                    _cfg.ShowHint = f.ShowHintSetting;
                    _cfg.Save();

                    bool reg = IsAutoStart();
                    if (f.AutoStartSetting != reg) SetAutoStart(f.AutoStartSetting);
                    else if (f.AutoStartSetting) SyncAutoStartPath();

                    _mAuto.Checked = f.AutoStartSetting;
                    _mEnable.Checked = _cfg.Enabled;
                    Update();
                }
            }
        }

        // exe 被移动或改名后（整理文件夹、更新版本），自启动仍指向旧路径，这里自动纠正
        private static void SyncAutoStartPath()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return;
                    string old = k.GetValue(RunValueName) as string;
                    if (old == null) return;
                    string cur = "\"" + Application.ExecutablePath + "\"";
                    if (!string.Equals(old, cur, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(RunValueName, cur);
                }
            }
            catch { }
        }

        private static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return k != null && k.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机自启动失败：" + ex.Message, "午休息屏");
            }
        }

        private static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (SolidBrush br = new SolidBrush(Color.Black)) g.FillRectangle(br, 1, 1, 14, 14);
                    using (Pen p = new Pen(Color.DimGray)) g.DrawEllipse(p, 4, 4, 8, 8);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void ExitApp()
        {
            _tray.Visible = false;
            _tray.Dispose();
            if (_overlay.Visible) _overlay.ForceClose();
            _clock.Stop();
            if (_testTimer != null) _testTimer.Stop();
            try { _mutex.ReleaseMutex(); } catch { }
            ExitThread();
        }
    }

    internal static class Program
    {
        // 优先用 exe 同目录（方便整包分发）；以下两种情况改用各用户自己的 LocalAppData：
        // 1) 目录不可写（Program Files 等）2) 网络共享路径（可写但多人共用，配置必须各人独立）
        // 首次切换时若 exe 旁附带 config.ini（分发者预置的默认值），复制一份作为个人初始配置
        public static string ResolveConfigPath(string baseDir)
        {
            bool isNetwork = baseDir.StartsWith(@"\\", StringComparison.Ordinal);
            if (!isNetwork)
            {
                try { isNetwork = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(baseDir))).DriveType == DriveType.Network; }
                catch { }
            }
            if (isNetwork) return UserConfigPath(baseDir);

            try
            {
                string probe = Path.Combine(baseDir, ".wprobe" + Environment.TickCount);
                using (File.Create(probe)) { }
                File.Delete(probe);
                return Path.Combine(baseDir, "config.ini");
            }
            catch
            {
                return UserConfigPath(baseDir);
            }
        }

        private static string UserConfigPath(string baseDir)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NapScreenOff");
            string path = Path.Combine(dir, "config.ini");
            try
            {
                Directory.CreateDirectory(dir);
                string shipped = Path.Combine(baseDir, "config.ini");
                if (!File.Exists(path) && File.Exists(shipped))
                    File.Copy(shipped, path, false);
            }
            catch { }
            return path;
        }

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool firstInstance;
            Mutex m = new Mutex(true, "NapScreenOff_SingleInstance", out firstInstance);
            if (!firstInstance)
            {
                MessageBox.Show("午休息屏已在后台运行，请查看任务栏右侧的托盘图标。", "午休息屏");
                return;
            }

            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrEmpty(dir)) dir = ".";

            bool showSettings = false;
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a.Equals("/settings", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase))
                    showSettings = true;
            }

            try
            {
                Application.Run(new TrayApp(dir, m, showSettings));
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：" + ex.Message, "午休息屏");
            }
        }
    }
}