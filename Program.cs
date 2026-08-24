using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BlacksreenNap
{
    internal class Config
    {
        public string StartTime = "12:10";
        public string EndTime = "13:10";
        public bool Enabled = true;
        public string Policy = "temp";
        public string Days = "1234567";
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
            return s == "strict" || s == "temp" || s == "day";
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
                    "; 午休黑屏配置文件（可手动编辑，修改后需重启程序生效）\n"
                    + "; start/end 为 HH:mm 格式\n"
                    + "; days: 每周生效日期，1=周一、7=周日，如 days=1234567 表示每天\n"
                    + "; policy: strict=强制锁定 / temp=可临时退出(1分钟) / day=退出后当天不再显示\n"
                    + "start=" + StartTime + "\nend=" + EndTime + "\nenabled=" + (Enabled ? "true" : "false") + "\npolicy=" + Policy + "\ndays=" + Days + "\n",
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    internal class BlackForm : Form
    {
        private bool _allowClose;

        public event EventHandler ExitRequest;

        public BlackForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) RaiseExitRequest();
            };
            MouseDoubleClick += delegate(object s, MouseEventArgs e) { RaiseExitRequest(); };
            Deactivate += delegate(object s, EventArgs e) { if (Visible) TopMost = true; };
        }

        private void RaiseExitRequest()
        {
            if (ExitRequest != null) ExitRequest(this, EventArgs.Empty);
        }

        public void ShowBlack()
        {
            if (Visible) { Activate(); return; }
            Cursor.Hide();
            Show();
            Activate();
        }

        public void HideBlack()
        {
            if (!Visible) return;
            Cursor.Show();
            Hide();
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    internal class SettingsForm : Form
    {
        private static readonly string[] PolicyNames = {
            "强制锁定（黑屏期间无法手动退出）",
            "临时退出（双击/Esc 退出，1 分钟后重新黑屏）",
            "退出后当天不再显示黑屏"
        };
        private static readonly string[] DayLabels = { "一", "二", "三", "四", "五", "六", "日" };

        private readonly TextBox _start = new TextBox();
        private readonly TextBox _end = new TextBox();
        private readonly CheckBox _enabled = new CheckBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly ComboBox _policy = new ComboBox();
        private readonly CheckBox[] _dayChecks = new CheckBox[7];

        public string StartSetting { get { return _start.Text.Trim(); } }
        public string EndSetting { get { return _end.Text.Trim(); } }
        public bool EnabledSetting { get { return _enabled.Checked; } }
        public bool AutoStartSetting { get { return _autoStart.Checked; } }
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
            if (idx == 0) return "strict";
            if (idx == 2) return "day";
            return "temp";
        }

        private static int IndexOfPolicy(string p)
        {
            if (p == "strict") return 0;
            if (p == "day") return 2;
            return 1;
        }

        public SettingsForm(string start, string end, bool enabled, string policy, string days, bool autoStart)
        {
            Text = "午休黑屏 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 200);

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

            _enabled.Text = "启用午休黑屏";
            _enabled.Checked = enabled;
            _enabled.Location = new Point(262, 41);
            _enabled.AutoSize = true;

            Label l3 = new Label { Text = "黑屏退出方式", Location = new Point(15, 78), AutoSize = true };
            _policy.DropDownStyle = ComboBoxStyle.DropDownList;
            _policy.Location = new Point(150, 75);
            _policy.Width = 175;
            _policy.Items.AddRange(PolicyNames);
            _policy.SelectedIndex = IndexOfPolicy(policy);

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

            Button ok = new Button { Text = "保存", Location = new Point(150, 160) };
            ok.Click += delegate { if (ValidateInput()) DialogResult = DialogResult.OK; };
            Button cancel = new Button { Text = "取消", Location = new Point(245, 160) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

            Controls.AddRange(new Control[] { l1, _start, l2, _end, _autoStart, _enabled, l3, _policy, l4, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private bool ValidateInput()
        {
            TimeSpan d1, d2;
            if (!Config.ParseTime(StartSetting, out d1) || !Config.ParseTime(EndSetting, out d2))
            {
                MessageBox.Show(this, "时间格式错误，请使用 HH:mm 格式，例如 12:10。", "午休黑屏");
                return false;
            }
            if (d2 <= d1)
            {
                MessageBox.Show(this, "结束时间必须晚于开始时间。", "午休黑屏");
                return false;
            }
            bool anyDay = false;
            foreach (CheckBox c in _dayChecks)
                if (c.Checked) anyDay = true;
            if (!anyDay)
            {
                MessageBox.Show(this, "请至少选择一个生效日期。", "午休黑屏");
                return false;
            }
            return true;
        }
    }

    internal class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "BlackScreenNap";

        private readonly Config _cfg;
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly BlackForm _black = new BlackForm();
        private readonly Mutex _mutex;
        private readonly ToolStripMenuItem _mEnable = new ToolStripMenuItem("启用午休黑屏");
        private readonly ToolStripMenuItem _mAuto = new ToolStripMenuItem("开机自启动");

        private System.Windows.Forms.Timer _clock;
        private System.Windows.Forms.Timer _testTimer;
        private bool _testing;
        private DateTime _dismissUntil;

        public TrayApp(string baseDir, Mutex mutex, bool showSettingsAtStart)
        {
            _mutex = mutex;
            _cfg = new Config(Path.Combine(baseDir, "config.ini"));
            if (!File.Exists(_cfg.FilePath)) _cfg.Save();

            _black.ExitRequest += delegate { DismissBlack(false); };

            _tray.Icon = MakeIcon();
            _tray.Visible = true;

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

            ToolStripMenuItem mExitBlack = new ToolStripMenuItem("立即退出当前黑屏");
            mExitBlack.Click += delegate { DismissBlack(true); };

            ToolStripMenuItem mTest = new ToolStripMenuItem("测试黑屏（5 秒）");
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

            _clock = new System.Windows.Forms.Timer();
            _clock.Interval = 10000;
            _clock.Tick += delegate { Update(); };
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
                    _black.ShowBlack();
                }
                _tray.Text = "午休黑屏：黑屏中，至 " + _cfg.EndTime;
            }
            else
            {
                if (_black.Visible) _black.HideBlack();
                _tray.Text = "午休黑屏：" + _cfg.StartTime + "-" + _cfg.EndTime + (_cfg.Enabled ? " 已启用" : " 已停用");
            }
        }

        private void DismissBlack(bool informative)
        {
            if (!_black.Visible) return;

            if (_cfg.Policy == "strict")
            {
                if (informative)
                    MessageBox.Show("当前为强制锁定模式，黑屏无法手动退出，将在结束时间自动恢复。", "午休黑屏");
                return;
            }

            _black.HideBlack();
            if (_cfg.Policy == "day")
                _dismissUntil = DateTime.Today.AddDays(1);
            else
                _dismissUntil = DateTime.Now.AddMinutes(1);
        }

        private void StartTest()
        {
            _testing = true;
            _black.ShowBlack();
            _testTimer = new System.Windows.Forms.Timer();
            _testTimer.Interval = 5000;
            _testTimer.Tick += delegate
            {
                _testing = false;
                if (_black.Visible) _black.HideBlack();
                _testTimer.Stop();
                _testTimer.Dispose();
            };
            _testTimer.Start();
        }

        private void ShowSettings()
        {
            using (SettingsForm f = new SettingsForm(_cfg.StartTime, _cfg.EndTime, _cfg.Enabled, _cfg.Policy, _cfg.Days, IsAutoStart()))
            {
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _cfg.StartTime = f.StartSetting;
                    _cfg.EndTime = f.EndSetting;
                    _cfg.Enabled = f.EnabledSetting;
                    _cfg.Policy = f.PolicySetting;
                    _cfg.Days = f.DaysSetting;
                    _cfg.Save();

                    bool reg = IsAutoStart();
                    if (f.AutoStartSetting != reg) SetAutoStart(f.AutoStartSetting);

                    _mAuto.Checked = f.AutoStartSetting;
                    _mEnable.Checked = _cfg.Enabled;
                    Update();
                }
            }
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
                MessageBox.Show("设置开机自启动失败：" + ex.Message, "午休黑屏");
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
            if (_black.Visible) _black.ForceClose();
            _clock.Stop();
            if (_testTimer != null) _testTimer.Stop();
            try { _mutex.ReleaseMutex(); } catch { }
            ExitThread();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool firstInstance;
            Mutex m = new Mutex(true, "BlackScreenNap_SingleInstance", out firstInstance);
            if (!firstInstance)
            {
                MessageBox.Show("午休黑屏已在后台运行，请查看任务栏右侧的托盘图标。", "午休黑屏");
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
                MessageBox.Show("程序启动失败：" + ex.Message, "午休黑屏");
            }
        }
    }
}