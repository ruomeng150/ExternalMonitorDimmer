using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ExternalMonitorDimmer
{
    internal sealed class MainForm : Form
    {
        private const int ImmediateSleepHotKeyId = 0x454D;
        private const int SupportedHotKeyModifiers =
            NativeMethods.HotKeyModifierAlt |
            NativeMethods.HotKeyModifierControl |
            NativeMethods.HotKeyModifierShift;
        private const int TriggerModeIdle = 0;
        private const int TriggerModeScreenSaver = 1;

        private static readonly Color WindowBackground = Color.FromArgb(247, 248, 246);
        private static readonly Color Surface = Color.White;
        private static readonly Color TextPrimary = Color.FromArgb(31, 37, 34);
        private static readonly Color TextSecondary = Color.FromArgb(91, 99, 94);
        private static readonly Color Border = Color.FromArgb(216, 221, 218);
        private static readonly Color Accent = Color.FromArgb(24, 121, 78);
        private static readonly Color AccentHover = Color.FromArgb(18, 101, 64);
        private static readonly Color Warning = Color.FromArgb(178, 106, 0);
        private static readonly Color Error = Color.FromArgb(184, 49, 47);
        private static readonly Color Inactive = Color.FromArgb(132, 140, 135);

        private readonly bool startHidden;
        private readonly AppSettings settings;
        private readonly System.Windows.Forms.Timer monitorTimer;

        private NumericUpDown idleValue;
        private ComboBox idleUnit;
        private ComboBox triggerMode;
        private TrackBar brightnessSlider;
        private NumericUpDown brightnessValue;
        private CheckBox autoStartCheck;
        private CheckBox screenSaverCheck;
        private CheckBox syncLockCheck;
        private TextBox hotKeyText;
        private Button clearHotKeyButton;
        private Label statusLabel;
        private Panel statusDot;
        private Label idleStatusLabel;
        private Label monitorCountLabel;
        private ListView monitorList;
        private Button applyButton;
        private Button stopButton;
        private NotifyIcon trayIcon;
        private ToolStripMenuItem trayToggleItem;
        private ToolStripMenuItem traySleepItem;
        private Icon applicationIcon;
        private ToolTip toolTip;

        private bool monitoring;
        private bool dimmed;
        private bool busy;
        private bool allowExit;
        private bool trayHintShown;
        private bool syncingBrightnessControls;
        private bool syncingIdleUnit;
        private bool hotKeyRegistered;
        private bool hotKeyChordDown;
        private bool immediateSleepPending;
        private bool immediateSleepActive;
        private bool immediateSleepTriggeredByMouse;
        private int lastIdleUnitIndex;
        private int pendingHotKeyModifiers;
        private int pendingHotKeyKey;
        private int registeredHotKeyModifiers;
        private int registeredHotKeyKey;
        private int immediateSleepTriggerModifiers;
        private int immediateSleepTriggerKey;
        private uint immediateSleepInputTick;
        private DateTime immediateSleepDueUtc = DateTime.MinValue;
        private DateTime immediateSleepStartedUtc = DateTime.MinValue;
        private Process immediateScreenSaverProcess;
        private DateTime nextDimAttemptUtc = DateTime.MinValue;
        private DateTime nextRestoreAttemptUtc = DateTime.MinValue;
        private DateTime nextStatusUpdateUtc = DateTime.MinValue;
        private bool screenSaverStateKnown;
        private bool lastScreenSaverRunning;
        private bool sessionNotificationsRegistered;
        private bool immediateSleepSyncLock;
        private bool immediateSleepSessionLocked;

        public MainForm(bool startHidden)
        {
            this.startHidden = startHidden;
            settings = SettingsStore.LoadSettings();
            settings.AutoStart = StartupManager.IsEnabled();

            Text = "外接显示器休眠调光";
            BackColor = WindowBackground;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 646);
            MinimumSize = new Size(660, 600);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            applicationIcon = CreateApplicationIcon();
            Icon = applicationIcon;
            toolTip = new ToolTip();

            BuildInterface();
            BuildTrayIcon();
            LoadSettingsIntoControls();

            monitorTimer = new System.Windows.Forms.Timer();
            monitorTimer.Interval = 200;
            monitorTimer.Tick += MonitorTimerTick;

            Shown += FormShown;
            FormClosing += FormClosingHandler;
            FormClosed += FormClosedHandler;
            Resize += FormResizeHandler;
        }

        public void ShowFromTray()
        {
            bool rebindHotKey = hotKeyRegistered;
            if (rebindHotKey)
            {
                UnregisterHotKeyForCurrentHandle();
            }

            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();

            if (rebindHotKey)
            {
                RegisterSavedHotKey();
            }
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = WindowBackground;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 272F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(CreateSeparator(), 0, 1);
            root.Controls.Add(BuildSettingsPanel(), 0, 2);
            root.Controls.Add(CreateSeparator(), 0, 3);
            root.Controls.Add(BuildMonitorPanel(), 0, 4);
            root.Controls.Add(BuildFooter(), 0, 5);
        }

        private Control BuildHeader()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Surface;

            Label title = new Label();
            title.AutoSize = true;
            title.Location = new Point(24, 14);
            title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = TextPrimary;
            title.Text = "外接显示器休眠调光";
            panel.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Location = new Point(26, 52);
            subtitle.ForeColor = TextSecondary;
            subtitle.Text = "DDC/CI 显示器";
            panel.Controls.Add(subtitle);

            statusLabel = new Label();
            statusLabel.AutoEllipsis = true;
            statusLabel.Size = new Size(220, 24);
            statusLabel.Location = new Point(panel.Width - 246, 31);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = TextSecondary;
            statusLabel.Text = "未启动";
            panel.Controls.Add(statusLabel);

            statusDot = new Panel();
            statusDot.Size = new Size(14, 14);
            statusDot.Location = new Point(panel.Width - 268, 36);
            statusDot.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            statusDot.Tag = Inactive;
            statusDot.Paint += StatusDotPaint;
            panel.Controls.Add(statusDot);

            return panel;
        }

        private Control BuildSettingsPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = WindowBackground;

            Label heading = new Label();
            heading.AutoSize = true;
            heading.Location = new Point(24, 14);
            heading.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
            heading.Text = "自动调光";
            panel.Controls.Add(heading);

            Label triggerModeLabel = new Label();
            triggerModeLabel.AutoSize = true;
            triggerModeLabel.Location = new Point(270, 18);
            triggerModeLabel.ForeColor = TextSecondary;
            triggerModeLabel.Text = "触发模式";
            panel.Controls.Add(triggerModeLabel);

            triggerMode = new ComboBox();
            triggerMode.DropDownStyle = ComboBoxStyle.DropDownList;
            triggerMode.FlatStyle = FlatStyle.Flat;
            triggerMode.Items.Add("程序检测空闲时间");
            triggerMode.Items.Add("跟随 Windows 屏幕保护程序");
            triggerMode.Location = new Point(340, 11);
            triggerMode.Size = new Size(286, 30);
            triggerMode.AccessibleName = "触发模式";
            triggerMode.SelectedIndexChanged += TriggerModeChanged;
            toolTip.SetToolTip(triggerMode,
                "程序检测模式使用下方的未操作时长；跟随模式由 Windows 屏幕保护程序负责启动。");
            panel.Controls.Add(triggerMode);

            Label idleLabel = new Label();
            idleLabel.AutoSize = true;
            idleLabel.Location = new Point(24, 48);
            idleLabel.ForeColor = TextSecondary;
            idleLabel.Text = "未操作时长";
            panel.Controls.Add(idleLabel);

            idleValue = new NumericUpDown();
            idleValue.Location = new Point(24, 72);
            idleValue.Size = new Size(116, 28);
            idleValue.Minimum = 1;
            idleValue.Maximum = 86400;
            idleValue.TextAlign = HorizontalAlignment.Right;
            idleValue.Font = new Font(Font.FontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
            idleValue.AccessibleName = "未操作时长";
            panel.Controls.Add(idleValue);

            idleUnit = new ComboBox();
            idleUnit.DropDownStyle = ComboBoxStyle.DropDownList;
            idleUnit.FlatStyle = FlatStyle.Flat;
            idleUnit.Items.Add("秒");
            idleUnit.Items.Add("分钟");
            idleUnit.Location = new Point(148, 71);
            idleUnit.Size = new Size(82, 30);
            idleUnit.AccessibleName = "时间单位";
            idleUnit.SelectedIndexChanged += IdleUnitChanged;
            panel.Controls.Add(idleUnit);

            Label brightnessLabel = new Label();
            brightnessLabel.AutoSize = true;
            brightnessLabel.Location = new Point(270, 48);
            brightnessLabel.ForeColor = TextSecondary;
            brightnessLabel.Text = "最低亮度";
            panel.Controls.Add(brightnessLabel);

            brightnessSlider = new TrackBar();
            brightnessSlider.Location = new Point(264, 69);
            brightnessSlider.Size = new Size(276, 40);
            brightnessSlider.Minimum = 0;
            brightnessSlider.Maximum = 100;
            brightnessSlider.TickFrequency = 10;
            brightnessSlider.SmallChange = 1;
            brightnessSlider.LargeChange = 10;
            brightnessSlider.ValueChanged += BrightnessSliderChanged;
            panel.Controls.Add(brightnessSlider);

            brightnessValue = new NumericUpDown();
            brightnessValue.Location = new Point(550, 72);
            brightnessValue.Size = new Size(76, 28);
            brightnessValue.Minimum = 0;
            brightnessValue.Maximum = 100;
            brightnessValue.TextAlign = HorizontalAlignment.Right;
            brightnessValue.Font = new Font(Font.FontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
            brightnessValue.AccessibleName = "最低亮度数值";
            brightnessValue.ValueChanged += BrightnessValueChanged;
            panel.Controls.Add(brightnessValue);

            Label percentLabel = new Label();
            percentLabel.AutoSize = true;
            percentLabel.Location = new Point(633, 77);
            percentLabel.Text = "%";
            panel.Controls.Add(percentLabel);

            Label hotKeyLabel = new Label();
            hotKeyLabel.AutoSize = true;
            hotKeyLabel.Location = new Point(24, 118);
            hotKeyLabel.ForeColor = TextSecondary;
            hotKeyLabel.Text = "立即屏保快捷键";
            panel.Controls.Add(hotKeyLabel);

            hotKeyText = new TextBox();
            hotKeyText.Location = new Point(24, 142);
            hotKeyText.Size = new Size(250, 28);
            hotKeyText.ReadOnly = true;
            hotKeyText.ShortcutsEnabled = false;
            hotKeyText.BackColor = Surface;
            hotKeyText.ForeColor = TextPrimary;
            hotKeyText.Cursor = Cursors.Hand;
            hotKeyText.Font = new Font(Font.FontFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
            hotKeyText.AccessibleName = "立即屏保快捷键";
            hotKeyText.KeyDown += HotKeyTextKeyDown;
            hotKeyText.Leave += delegate { UpdateHotKeyText(); };
            toolTip.SetToolTip(hotKeyText, "单击后按下组合键；字母和数字需配合 Ctrl、Alt 或 Shift。");
            panel.Controls.Add(hotKeyText);

            clearHotKeyButton = CreateSecondaryButton("清除", 72);
            clearHotKeyButton.Location = new Point(284, 136);
            clearHotKeyButton.AccessibleName = "清除立即屏保快捷键";
            clearHotKeyButton.Click += delegate
            {
                pendingHotKeyModifiers = 0;
                pendingHotKeyKey = 0;
                UpdateHotKeyText();
                hotKeyText.Focus();
            };
            panel.Controls.Add(clearHotKeyButton);

            autoStartCheck = new CheckBox();
            autoStartCheck.AutoSize = true;
            autoStartCheck.FlatStyle = FlatStyle.Flat;
            autoStartCheck.Location = new Point(24, 196);
            autoStartCheck.Text = "登录 Windows 后自动运行";
            panel.Controls.Add(autoStartCheck);

            screenSaverCheck = new CheckBox();
            screenSaverCheck.AutoSize = true;
            screenSaverCheck.FlatStyle = FlatStyle.Flat;
            screenSaverCheck.Location = new Point(340, 196);
            screenSaverCheck.Text = "同步使用黑屏屏保";
            panel.Controls.Add(screenSaverCheck);

            syncLockCheck = new CheckBox();
            syncLockCheck.AutoSize = true;
            syncLockCheck.FlatStyle = FlatStyle.Flat;
            syncLockCheck.Location = new Point(340, 224);
            syncLockCheck.Text = "快捷键进入屏保时同步锁屏";
            toolTip.SetToolTip(syncLockCheck,
                "勾选后，使用自定义快捷键或托盘菜单进入屏保时会同时锁定 Windows；解锁后恢复亮度。此选项不影响自动调光。");
            panel.Controls.Add(syncLockCheck);

            return panel;
        }

        private Control BuildMonitorPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = WindowBackground;

            Label heading = new Label();
            heading.AutoSize = true;
            heading.Location = new Point(24, 16);
            heading.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
            heading.Text = "显示器";
            panel.Controls.Add(heading);

            monitorCountLabel = new Label();
            monitorCountLabel.AutoSize = true;
            monitorCountLabel.Location = new Point(88, 19);
            monitorCountLabel.ForeColor = TextSecondary;
            monitorCountLabel.Text = "正在检测";
            panel.Controls.Add(monitorCountLabel);

            Button refreshButton = CreateSecondaryButton("重新检测", 92);
            refreshButton.Location = new Point(panel.Width - 116, 10);
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refreshButton.Click += delegate { RefreshMonitorList(); };
            panel.Controls.Add(refreshButton);

            monitorList = new ListView();
            monitorList.Location = new Point(24, 48);
            monitorList.Size = new Size(panel.Width - 48, panel.Height - 64);
            monitorList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            monitorList.View = View.Details;
            monitorList.FullRowSelect = true;
            monitorList.GridLines = true;
            monitorList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            monitorList.HideSelection = false;
            monitorList.BackColor = Surface;
            monitorList.BorderStyle = BorderStyle.FixedSingle;
            monitorList.Columns.Add("显示输出", 128);
            monitorList.Columns.Add("显示器", 280);
            monitorList.Columns.Add("当前亮度", 110);
            monitorList.Columns.Add("范围", 100);
            monitorList.Resize += delegate { ResizeMonitorColumns(); };
            panel.Controls.Add(monitorList);

            return panel;
        }

        private Control BuildFooter()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Surface;

            idleStatusLabel = new Label();
            idleStatusLabel.AutoEllipsis = true;
            idleStatusLabel.Location = new Point(24, 29);
            idleStatusLabel.Size = new Size(210, 22);
            idleStatusLabel.ForeColor = TextSecondary;
            idleStatusLabel.Text = "空闲 0.0 秒";
            panel.Controls.Add(idleStatusLabel);

            applyButton = CreatePrimaryButton("应用并开始", 126);
            applyButton.Location = new Point(panel.Width - 476, 18);
            applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            applyButton.Click += delegate { ApplySettingsAndStart(); };
            panel.Controls.Add(applyButton);

            stopButton = CreateSecondaryButton("停止监控", 104);
            stopButton.Location = new Point(panel.Width - 340, 18);
            stopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            stopButton.Click += delegate { StopMonitoring(true); };
            panel.Controls.Add(stopButton);

            Button hideButton = CreateSecondaryButton("隐藏到托盘", 112);
            hideButton.Location = new Point(panel.Width - 226, 18);
            hideButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hideButton.Click += delegate { HideToTray(); };
            panel.Controls.Add(hideButton);

            Button exitButton = CreateSecondaryButton("退出程序", 96);
            exitButton.Location = new Point(panel.Width - 104, 18);
            exitButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            exitButton.Click += delegate { ExitApplication(); };
            panel.Controls.Add(exitButton);

            return panel;
        }

        private Control CreateSeparator()
        {
            Panel separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = Border;
            return separator;
        }

        private Button CreatePrimaryButton(string text, int width)
        {
            Button button = new Button();
            button.Size = new Size(width, 40);
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
            button.MouseEnter += delegate { button.BackColor = AccentHover; };
            button.MouseLeave += delegate { button.BackColor = Accent; };
            return button;
        }

        private Button CreateSecondaryButton(string text, int width)
        {
            Button button = new Button();
            button.Size = new Size(width, 40);
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Surface;
            button.ForeColor = TextPrimary;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void BuildTrayIcon()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Font;

            ToolStripMenuItem showItem = new ToolStripMenuItem("显示窗口");
            showItem.Click += delegate { ShowFromTray(); };
            menu.Items.Add(showItem);

            traySleepItem = new ToolStripMenuItem("立即进入屏幕保护程序");
            traySleepItem.Click += delegate { RequestImmediateScreenSaver(0, 0, true); };
            menu.Items.Add(traySleepItem);
            menu.Items.Add(new ToolStripSeparator());

            trayToggleItem = new ToolStripMenuItem("开始监控");
            trayToggleItem.Click += delegate
            {
                if (monitoring)
                {
                    StopMonitoring(true);
                }
                else
                {
                    ApplySettingsAndStart();
                }
            };
            menu.Items.Add(trayToggleItem);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出程序");
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = applicationIcon;
            trayIcon.Text = "外接显示器休眠调光";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
        }

        private void LoadSettingsIntoControls()
        {
            triggerMode.SelectedIndex = settings.TriggerMode == TriggerModeScreenSaver
                ? TriggerModeScreenSaver
                : TriggerModeIdle;

            syncingIdleUnit = true;
            bool useMinutes = settings.DisplayMinutes && settings.IdleSeconds >= 60;
            idleUnit.SelectedIndex = useMinutes ? 1 : 0;
            lastIdleUnitIndex = idleUnit.SelectedIndex;
            idleValue.Maximum = useMinutes ? 1440 : 86400;
            decimal displayedValue = useMinutes
                ? Math.Max(1, (decimal)Math.Round(settings.IdleSeconds / 60.0))
                : Math.Max(1, settings.IdleSeconds);
            idleValue.Value = Math.Min(idleValue.Maximum, displayedValue);
            syncingIdleUnit = false;

            syncingBrightnessControls = true;
            int brightness = Math.Max(0, Math.Min(100, settings.DimPercent));
            brightnessSlider.Value = brightness;
            brightnessValue.Value = brightness;
            syncingBrightnessControls = false;

            autoStartCheck.Checked = settings.AutoStart;
            screenSaverCheck.Checked = settings.SyncBlankScreenSaver;
            syncLockCheck.Checked = settings.SyncLockWorkstation;
            UpdateTriggerModeControls();

            if (IsValidHotKey(settings.HotKeyModifiers, settings.HotKeyKey))
            {
                pendingHotKeyModifiers = settings.HotKeyModifiers;
                pendingHotKeyKey = settings.HotKeyKey;
            }
            else
            {
                pendingHotKeyModifiers = 0;
                pendingHotKeyKey = 0;
            }
            UpdateHotKeyText();
        }

        private void TriggerModeChanged(object sender, EventArgs e)
        {
            UpdateTriggerModeControls();
        }

        private void UpdateTriggerModeControls()
        {
            bool useScreenSaver = triggerMode != null &&
                triggerMode.SelectedIndex == TriggerModeScreenSaver;
            idleValue.Enabled = !useScreenSaver;
            idleUnit.Enabled = !useScreenSaver;
            screenSaverCheck.Enabled = !useScreenSaver;

            if (useScreenSaver)
            {
                screenSaverCheck.Checked = false;
                toolTip.SetToolTip(screenSaverCheck,
                    "跟随模式使用 Windows 自己的屏保设置，不会修改屏保超时或程序。");
                idleStatusLabel.Text = "等待 Windows 屏保";
            }
            else
            {
                toolTip.SetToolTip(screenSaverCheck,
                    "勾选后，程序会把 Windows 屏保同步为黑屏屏保并使用相同的未操作时长。");
            }
        }

        private void FormShown(object sender, EventArgs e)
        {
            SettingsStore.Log(String.Format(
                "Main window shown. StartHidden={0}, Visible={1}, WindowState={2}, ShowInTaskbar={3}.",
                startHidden,
                Visible,
                WindowState,
                ShowInTaskbar));
            RecoverSavedBrightness();
            RefreshMonitorList();
            monitorTimer.Start();
            RegisterSessionNotifications();
            RegisterSavedHotKey();

            if (settings.MonitoringEnabled)
            {
                try
                {
                    ApplyScreenSaverMode();
                    monitoring = true;
                    screenSaverStateKnown = false;
                    SetStatus("监控中", Accent);
                    UpdateMonitoringControls();
                }
                catch (Exception ex)
                {
                    monitoring = false;
                    settings.MonitoringEnabled = false;
                    SettingsStore.SaveSettings(settings);
                    SetStatus("未启动", Error);
                    UpdateMonitoringControls();
                    SettingsStore.Log("Could not restore monitoring state: " + ex.Message);

                    if (!startHidden)
                    {
                        MessageBox.Show(
                            this,
                            ex.Message,
                            "无法恢复监控",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
            else
            {
                SetStatus("未启动", Inactive);
                UpdateMonitoringControls();
            }

            if (startHidden)
            {
                BeginInvoke(new Action(HideToTray));
            }

            SettingsStore.Log(String.Format(
                "Initial window state completed. Visible={0}, WindowState={1}, ShowInTaskbar={2}.",
                Visible,
                WindowState,
                ShowInTaskbar));
        }

        private void ApplySettingsAndStart()
        {
            try
            {
                CancelImmediateScreenSaver();
                ApplyHotKeyRegistration(pendingHotKeyModifiers, pendingHotKeyKey);
                ReadSettingsFromControls();
                StartupManager.SetEnabled(settings.AutoStart);
                ApplyScreenSaverMode();

                if (dimmed || File.Exists(AppPaths.BrightnessStateFile))
                {
                    RestoreSavedBrightness();
                }

                settings.MonitoringEnabled = true;
                SettingsStore.SaveSettings(settings);
                monitoring = true;
                nextDimAttemptUtc = DateTime.MinValue;
                nextRestoreAttemptUtc = DateTime.MinValue;
                screenSaverStateKnown = false;
                SetStatus("监控中", Accent);
                UpdateMonitoringControls();
                SettingsStore.Log(String.Format(
                    "Monitoring started. Mode={0}, Idle={1}s, Dim={2}%.",
                    settings.TriggerMode == TriggerModeScreenSaver ? "ScreenSaver" : "Idle",
                    settings.IdleSeconds,
                    settings.DimPercent));
            }
            catch (Exception ex)
            {
                SetStatus("设置未应用", Error);
                MessageBox.Show(
                    this,
                    ex.Message,
                    "无法应用设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopMonitoring(bool persist)
        {
            CancelImmediateScreenSaver();
            monitoring = false;
            RestoreSavedBrightness();
            TryRestoreScreenSaver(true);

            if (persist)
            {
                settings.MonitoringEnabled = false;
                SettingsStore.SaveSettings(settings);
            }

            SetStatus("已停止", Inactive);
            UpdateMonitoringControls();
            SettingsStore.Log("Monitoring stopped.");
        }

        private void CancelImmediateScreenSaver()
        {
            bool wasActive = immediateSleepPending || immediateSleepActive;
            immediateSleepPending = false;
            immediateSleepActive = false;
            immediateSleepSyncLock = false;
            immediateSleepSessionLocked = false;
            immediateSleepTriggeredByMouse = false;
            immediateSleepTriggerModifiers = 0;
            immediateSleepTriggerKey = 0;
            immediateSleepInputTick = 0;
            immediateSleepDueUtc = DateTime.MinValue;
            immediateSleepStartedUtc = DateTime.MinValue;
            DisposeImmediateScreenSaverProcess();

            if (wasActive)
            {
                SettingsStore.Log("Immediate screen saver cancelled.");
            }
        }

        private void RegisterSessionNotifications()
        {
            if (sessionNotificationsRegistered || !IsHandleCreated)
            {
                return;
            }

            try
            {
                NativeMethods.RegisterSessionNotifications(Handle);
                sessionNotificationsRegistered = true;
                SettingsStore.Log("Session lock notifications registered.");
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Could not register session lock notifications: " + ex.Message);
            }
        }

        private void UnregisterSessionNotifications()
        {
            if (!sessionNotificationsRegistered || !IsHandleCreated)
            {
                return;
            }

            NativeMethods.UnregisterSessionNotifications(Handle);
            sessionNotificationsRegistered = false;
            SettingsStore.Log("Session lock notifications unregistered.");
        }

        private void ApplyScreenSaverMode()
        {
            if (settings.TriggerMode == TriggerModeIdle && settings.SyncBlankScreenSaver)
            {
                ScreenSaverManager.EnableBlank(settings.IdleSeconds);
            }
            else
            {
                ScreenSaverManager.RestoreOriginal();
            }
        }

        private bool TryRestoreScreenSaver(bool showError)
        {
            try
            {
                ScreenSaverManager.RestoreOriginal();
                return true;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Could not restore screen saver settings: " + ex.Message);
                if (showError)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "无法恢复屏保设置",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }
        }

        private void RegisterSavedHotKey()
        {
            try
            {
                ApplyHotKeyRegistration(pendingHotKeyModifiers, pendingHotKeyKey);
            }
            catch (Exception ex)
            {
                string message = "无法注册快捷键 " +
                    FormatHotKey(pendingHotKeyModifiers, pendingHotKeyKey) +
                    "，它可能已被其他程序占用。";
                SettingsStore.Log("Hot key registration failed at startup: " + ex.Message);

                if (startHidden)
                {
                    trayIcon.ShowBalloonTip(
                        4000,
                        "立即屏保快捷键不可用",
                        message,
                        ToolTipIcon.Warning);
                }
                else
                {
                    MessageBox.Show(
                        this,
                        message,
                        "快捷键不可用",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void ApplyHotKeyRegistration(int modifiers, int key)
        {
            if (!IsValidHotKey(modifiers, key))
            {
                throw new InvalidOperationException("请选择有效的立即屏保快捷键。");
            }

            if (hotKeyRegistered &&
                registeredHotKeyModifiers == modifiers &&
                registeredHotKeyKey == key)
            {
                return;
            }

            bool hadPrevious = hotKeyRegistered;
            int previousModifiers = registeredHotKeyModifiers;
            int previousKey = registeredHotKeyKey;

            if (hadPrevious)
            {
                NativeMethods.UnregisterGlobalHotKey(Handle, ImmediateSleepHotKeyId);
                hotKeyRegistered = false;
            }

            try
            {
                if (key != 0)
                {
                    NativeMethods.RegisterGlobalHotKey(
                        Handle,
                        ImmediateSleepHotKeyId,
                        (uint)modifiers,
                        (uint)key);
                    hotKeyRegistered = true;
                }

                registeredHotKeyModifiers = modifiers;
                registeredHotKeyKey = key;
                traySleepItem.ShortcutKeyDisplayString = key == 0
                    ? String.Empty
                    : FormatHotKey(modifiers, key);
                SettingsStore.Log(key == 0
                    ? "Immediate screen saver hot key cleared."
                    : "Immediate screen saver hot key registered: " + FormatHotKey(modifiers, key));
            }
            catch (Exception ex)
            {
                registeredHotKeyModifiers = 0;
                registeredHotKeyKey = 0;

                if (hadPrevious)
                {
                    try
                    {
                        NativeMethods.RegisterGlobalHotKey(
                            Handle,
                            ImmediateSleepHotKeyId,
                            (uint)previousModifiers,
                            (uint)previousKey);
                        hotKeyRegistered = true;
                        registeredHotKeyModifiers = previousModifiers;
                        registeredHotKeyKey = previousKey;
                        traySleepItem.ShortcutKeyDisplayString =
                            FormatHotKey(previousModifiers, previousKey);
                    }
                    catch (Exception restoreException)
                    {
                        SettingsStore.Log(
                            "Could not restore previous hot key registration: " +
                            restoreException.Message);
                    }
                }

                throw new InvalidOperationException(
                    "无法注册快捷键 " + FormatHotKey(modifiers, key) +
                    "，它可能已被其他程序占用。",
                    ex);
            }
        }

        private void RequestImmediateScreenSaver(int modifiers, int key, bool triggeredByMouse)
        {
            if (immediateSleepPending || immediateSleepActive)
            {
                return;
            }

            immediateSleepPending = true;
            immediateSleepTriggeredByMouse = triggeredByMouse;
            immediateSleepTriggerModifiers = modifiers;
            immediateSleepTriggerKey = key;
            immediateSleepDueUtc = DateTime.MinValue;
            SetStatus("准备进入屏保", Warning);
            SettingsStore.Log("Immediate screen saver requested.");
        }

        private bool AreImmediateSleepTriggerKeysReleased()
        {
            if (immediateSleepTriggeredByMouse &&
                (NativeMethods.IsKeyDown((int)Keys.LButton) ||
                    NativeMethods.IsKeyDown((int)Keys.RButton)))
            {
                return false;
            }

            if (immediateSleepTriggerKey != 0 &&
                NativeMethods.IsKeyDown(immediateSleepTriggerKey))
            {
                return false;
            }

            if ((immediateSleepTriggerModifiers & NativeMethods.HotKeyModifierControl) != 0 &&
                NativeMethods.IsKeyDown((int)Keys.ControlKey))
            {
                return false;
            }

            if ((immediateSleepTriggerModifiers & NativeMethods.HotKeyModifierAlt) != 0 &&
                NativeMethods.IsKeyDown((int)Keys.Menu))
            {
                return false;
            }

            return (immediateSleepTriggerModifiers & NativeMethods.HotKeyModifierShift) == 0 ||
                !NativeMethods.IsKeyDown((int)Keys.ShiftKey);
        }

        private void PerformImmediateScreenSaver()
        {
            immediateSleepPending = false;
            immediateSleepDueUtc = DateTime.MinValue;
            busy = true;
            bool syncLock = settings.SyncLockWorkstation;

            try
            {
                if (syncLock)
                {
                    RegisterSessionNotifications();
                    if (!sessionNotificationsRegistered)
                    {
                        throw new InvalidOperationException("无法监控 Windows 锁屏状态，未进入屏幕保护程序。");
                    }
                }

                if (!dimmed && !File.Exists(AppPaths.BrightnessStateFile) &&
                    !DimMonitors())
                {
                    throw new InvalidOperationException("外接显示器亮度调节失败，未进入屏幕保护程序。");
                }

                DisposeImmediateScreenSaverProcess();
                immediateScreenSaverProcess = ScreenSaverManager.StartBlank();
                immediateSleepInputTick = NativeMethods.GetLastInputTickCount();
                immediateSleepStartedUtc = DateTime.UtcNow;
                immediateSleepActive = true;
                immediateSleepSyncLock = syncLock;
                immediateSleepSessionLocked = false;
                SetStatus("屏幕保护程序中", Warning);
                SettingsStore.Log("Immediate blank screen saver started.");

                if (syncLock)
                {
                    SettingsStore.Log("Requesting Windows workstation lock.");
                    NativeMethods.LockWorkStation();
                }
            }
            catch (Exception ex)
            {
                immediateSleepActive = false;
                immediateSleepSyncLock = false;
                immediateSleepSessionLocked = false;
                DisposeImmediateScreenSaverProcess();
                RestoreSavedBrightness();
                SetStatus(monitoring ? "监控中" : "未启动", monitoring ? Accent : Inactive);
                SettingsStore.Log("Could not start immediate screen saver: " + ex.Message);

                if (Visible)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "无法进入屏幕保护程序",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                else
                {
                    trayIcon.ShowBalloonTip(
                        4000,
                        "无法进入屏幕保护程序",
                        ex.Message,
                        ToolTipIcon.Error);
                }
            }
            finally
            {
                busy = false;
            }
        }

        private bool ImmediateScreenSaverHasEnded(uint lastInputTick, DateTime now)
        {
            if (immediateSleepSyncLock)
            {
                // LockWorkStation can close scrnsave.scr and input timestamps can change;
                // the unlock session notification is the authoritative wake signal here.
                return false;
            }

            if (now >= immediateSleepStartedUtc.AddMilliseconds(350) &&
                lastInputTick != immediateSleepInputTick)
            {
                return true;
            }

            if (immediateScreenSaverProcess == null)
            {
                return false;
            }

            try
            {
                immediateScreenSaverProcess.Refresh();
                return immediateScreenSaverProcess.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void FinishImmediateScreenSaver()
        {
            immediateSleepActive = false;
            immediateSleepSyncLock = false;
            immediateSleepSessionLocked = false;
            DisposeImmediateScreenSaverProcess();

            busy = true;
            try
            {
                bool restored = RestoreSavedBrightness();
                if (restored)
                {
                    SetStatus(monitoring ? "监控中" : "未启动", monitoring ? Accent : Inactive);
                }
                nextDimAttemptUtc = DateTime.UtcNow.AddSeconds(1);
                SettingsStore.Log("Immediate screen saver ended; brightness restore attempted.");
            }
            finally
            {
                busy = false;
            }
        }

        private void DisposeImmediateScreenSaverProcess()
        {
            if (immediateScreenSaverProcess == null)
            {
                return;
            }

            try
            {
                immediateScreenSaverProcess.Refresh();
                if (!immediateScreenSaverProcess.HasExited)
                {
                    immediateScreenSaverProcess.Kill();
                    immediateScreenSaverProcess.WaitForExit(500);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                SettingsStore.Log("Could not stop immediate screen saver: " + ex.Message);
            }
            finally
            {
                immediateScreenSaverProcess.Dispose();
                immediateScreenSaverProcess = null;
            }
        }

        private void MonitorTimerTick(object sender, EventArgs e)
        {
            if (busy)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            uint idleMilliseconds = 0;
            if (settings.TriggerMode == TriggerModeIdle)
            {
                try
                {
                    idleMilliseconds = NativeMethods.GetIdleMilliseconds();
                }
                catch (Exception ex)
                {
                    SetStatus("输入检测失败", Error);
                    SettingsStore.Log("Input detection failed: " + ex.Message);
                    return;
                }

                if (now >= nextStatusUpdateUtc)
                {
                    idleStatusLabel.Text = String.Format("空闲 {0:0.0} 秒", idleMilliseconds / 1000.0);
                    nextStatusUpdateUtc = now.AddMilliseconds(500);
                }
            }

            if (immediateSleepPending)
            {
                if (!AreImmediateSleepTriggerKeysReleased())
                {
                    immediateSleepDueUtc = DateTime.MinValue;
                    return;
                }

                if (immediateSleepDueUtc == DateTime.MinValue)
                {
                    immediateSleepDueUtc = now.AddMilliseconds(450);
                    return;
                }

                if (now >= immediateSleepDueUtc)
                {
                    PerformImmediateScreenSaver();
                }
                return;
            }

            PollConfiguredHotKey();

            if (immediateSleepActive)
            {
                uint lastInputTick;
                try
                {
                    lastInputTick = NativeMethods.GetLastInputTickCount();
                }
                catch (Exception ex)
                {
                    SettingsStore.Log("Could not detect immediate screen saver wake: " + ex.Message);
                    return;
                }

                if (ImmediateScreenSaverHasEnded(lastInputTick, now))
                {
                    FinishImmediateScreenSaver();
                }
                return;
            }

            if (!monitoring)
            {
                return;
            }

            if (settings.TriggerMode == TriggerModeScreenSaver)
            {
                MonitorScreenSaver(now);
                return;
            }

            ulong threshold = (ulong)settings.IdleSeconds * 1000UL;
            if ((ulong)idleMilliseconds >= threshold)
            {
                if (!dimmed && now >= nextDimAttemptUtc)
                {
                    busy = true;
                    try
                    {
                        if (!DimMonitors())
                        {
                            nextDimAttemptUtc = now.AddSeconds(2);
                        }
                    }
                    finally
                    {
                        busy = false;
                    }
                }
            }
            else if (dimmed || File.Exists(AppPaths.BrightnessStateFile))
            {
                if (now >= nextRestoreAttemptUtc)
                {
                    busy = true;
                    try
                    {
                        RestoreSavedBrightness();
                        nextRestoreAttemptUtc = now.AddSeconds(2);
                    }
                    finally
                    {
                        busy = false;
                    }
                }
            }
            else
            {
                SetStatus("监控中", Accent);
            }
        }

        private void MonitorScreenSaver(DateTime now)
        {
            bool running;
            try
            {
                running = NativeMethods.IsScreenSaverRunning();
            }
            catch (Exception ex)
            {
                SetStatus("屏保状态检测失败", Error);
                SettingsStore.Log("Screen saver detection failed: " + ex.Message);
                return;
            }

            if (now >= nextStatusUpdateUtc)
            {
                idleStatusLabel.Text = running
                    ? "Windows 屏幕保护程序运行中"
                    : "等待 Windows 屏幕保护程序";
                nextStatusUpdateUtc = now.AddMilliseconds(500);
            }

            bool stateChanged = !screenSaverStateKnown || running != lastScreenSaverRunning;
            screenSaverStateKnown = true;
            lastScreenSaverRunning = running;

            if (running)
            {
                if (!dimmed && !File.Exists(AppPaths.BrightnessStateFile) &&
                    now >= nextDimAttemptUtc)
                {
                    busy = true;
                    try
                    {
                        if (DimMonitors())
                        {
                            SetStatus("屏保中，已调暗", Warning);
                        }
                        else
                        {
                            nextDimAttemptUtc = now.AddSeconds(2);
                        }
                    }
                    finally
                    {
                        busy = false;
                    }
                }
                else if (dimmed || File.Exists(AppPaths.BrightnessStateFile))
                {
                    SetStatus("屏保中，已调暗", Warning);
                }
                else if (stateChanged)
                {
                    SetStatus("屏保中，准备调暗", Warning);
                }
            }
            else if (dimmed || File.Exists(AppPaths.BrightnessStateFile))
            {
                if (now >= nextRestoreAttemptUtc)
                {
                    busy = true;
                    try
                    {
                        RestoreSavedBrightness();
                        nextRestoreAttemptUtc = now.AddSeconds(2);
                    }
                    finally
                    {
                        busy = false;
                    }
                }
            }
            else
            {
                SetStatus("监控中", Accent);
            }
        }

        private bool DimMonitors()
        {
            List<MonitorInfo> monitors = NativeMethods.GetBrightnessMonitors();
            if (monitors.Count == 0)
            {
                SetStatus("未检测到 DDC/CI 显示器", Warning);
                return false;
            }

            BrightnessState state = new BrightnessState();
            state.SavedAt = DateTime.Now;
            foreach (MonitorInfo monitor in monitors)
            {
                state.Monitors.Add(BrightnessSnapshot.FromMonitor(monitor));
            }
            SettingsStore.SaveBrightnessState(state);

            int changedCount = 0;
            foreach (MonitorInfo monitor in monitors)
            {
                double range = monitor.Maximum - monitor.Minimum;
                uint target = (uint)Math.Round(
                    monitor.Minimum + (range * settings.DimPercent / 100.0),
                    MidpointRounding.AwayFromZero);

                if (NativeMethods.SetBrightness(monitor.DeviceName, monitor.PhysicalIndex, target))
                {
                    changedCount++;
                    SettingsStore.Log(String.Format(
                        "Dimmed {0} from {1} to {2}.",
                        monitor.Description,
                        monitor.Current,
                        target));
                }
            }

            if (changedCount == 0)
            {
                SettingsStore.DeleteBrightnessState();
                SetStatus("亮度写入失败", Error);
                return false;
            }

            dimmed = true;
            SetStatus("屏幕已调暗", Warning);
            return true;
        }

        private bool RestoreSavedBrightness()
        {
            BrightnessState state = SettingsStore.LoadBrightnessState();
            if (state == null || state.Monitors.Count == 0)
            {
                dimmed = false;
                return true;
            }

            List<MonitorInfo> currentMonitors;
            try
            {
                currentMonitors = NativeMethods.GetBrightnessMonitors();
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Could not enumerate monitors for restore: " + ex.Message);
                dimmed = true;
                return false;
            }

            List<BrightnessSnapshot> remaining = new List<BrightnessSnapshot>();
            foreach (BrightnessSnapshot snapshot in state.Monitors)
            {
                MonitorInfo current = FindCurrentMonitor(snapshot, currentMonitors);
                bool restored = false;

                if (current != null)
                {
                    for (int attempt = 0; attempt < 3 && !restored; attempt++)
                    {
                        restored = NativeMethods.SetBrightness(
                            current.DeviceName,
                            current.PhysicalIndex,
                            snapshot.Brightness);
                        if (!restored)
                        {
                            Thread.Sleep(150);
                        }
                    }
                }

                if (restored)
                {
                    SettingsStore.Log(String.Format(
                        "Restored {0} to {1}.",
                        snapshot.Description,
                        snapshot.Brightness));
                }
                else
                {
                    remaining.Add(snapshot);
                }
            }

            if (remaining.Count == 0)
            {
                SettingsStore.DeleteBrightnessState();
                dimmed = false;
                if (monitoring)
                {
                    SetStatus("监控中", Accent);
                }
                return true;
            }

            state.Monitors = remaining;
            SettingsStore.SaveBrightnessState(state);
            dimmed = true;
            SetStatus("等待显示器重新连接", Warning);
            return false;
        }

        private void RecoverSavedBrightness()
        {
            if (!File.Exists(AppPaths.BrightnessStateFile))
            {
                return;
            }

            SetStatus("正在恢复亮度", Warning);
            RestoreSavedBrightness();
        }

        private MonitorInfo FindCurrentMonitor(
            BrightnessSnapshot saved,
            List<MonitorInfo> currentMonitors)
        {
            MonitorInfo match = FindUnique(currentMonitors, delegate(MonitorInfo monitor)
            {
                return !String.IsNullOrEmpty(saved.DeviceKey) &&
                    monitor.DeviceKey == saved.DeviceKey &&
                    monitor.PhysicalIndex == saved.PhysicalIndex;
            });
            if (match != null)
            {
                return match;
            }

            match = FindUnique(currentMonitors, delegate(MonitorInfo monitor)
            {
                return !String.IsNullOrEmpty(saved.DeviceId) &&
                    monitor.DeviceId == saved.DeviceId &&
                    monitor.PhysicalIndex == saved.PhysicalIndex;
            });
            if (match != null)
            {
                return match;
            }

            match = FindUnique(currentMonitors, delegate(MonitorInfo monitor)
            {
                return monitor.DeviceName == saved.DeviceName &&
                    monitor.PhysicalIndex == saved.PhysicalIndex;
            });
            if (match != null)
            {
                return match;
            }

            return FindUnique(currentMonitors, delegate(MonitorInfo monitor)
            {
                return monitor.Description == saved.Description &&
                    monitor.PhysicalIndex == saved.PhysicalIndex;
            });
        }

        private MonitorInfo FindUnique(
            List<MonitorInfo> monitors,
            Predicate<MonitorInfo> predicate)
        {
            MonitorInfo found = null;
            foreach (MonitorInfo monitor in monitors)
            {
                if (!predicate(monitor))
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }
                found = monitor;
            }
            return found;
        }

        private void RefreshMonitorList()
        {
            try
            {
                List<MonitorInfo> monitors = NativeMethods.GetBrightnessMonitors();
                monitorList.BeginUpdate();
                monitorList.Items.Clear();

                foreach (MonitorInfo monitor in monitors)
                {
                    double range = monitor.Maximum - monitor.Minimum;
                    double percent = range <= 0
                        ? 0
                        : ((monitor.Current - monitor.Minimum) / range) * 100.0;

                    ListViewItem row = new ListViewItem(monitor.DeviceName);
                    row.SubItems.Add(String.IsNullOrWhiteSpace(monitor.Description)
                        ? "外接显示器"
                        : monitor.Description);
                    row.SubItems.Add(String.Format("{0:0}%", percent));
                    row.SubItems.Add(String.Format("{0}-{1}", monitor.Minimum, monitor.Maximum));
                    monitorList.Items.Add(row);
                }

                monitorList.EndUpdate();
                monitorCountLabel.Text = monitors.Count == 0
                    ? "未检测到支持 DDC/CI 的显示器"
                    : String.Format("{0} 台可控制", monitors.Count);
                ResizeMonitorColumns();
            }
            catch (Exception ex)
            {
                monitorCountLabel.Text = "检测失败";
                SettingsStore.Log("Monitor refresh failed: " + ex.Message);
            }
        }

        private void ReadSettingsFromControls()
        {
            int multiplier = idleUnit.SelectedIndex == 1 ? 60 : 1;
            long seconds = Decimal.ToInt64(idleValue.Value) * multiplier;
            settings.IdleSeconds = (int)Math.Max(1, Math.Min(86400, seconds));
            settings.DisplayMinutes = idleUnit.SelectedIndex == 1;
            settings.TriggerMode = triggerMode.SelectedIndex == TriggerModeScreenSaver
                ? TriggerModeScreenSaver
                : TriggerModeIdle;
            settings.DimPercent = Decimal.ToInt32(brightnessValue.Value);
            settings.AutoStart = autoStartCheck.Checked;
            settings.SyncBlankScreenSaver = settings.TriggerMode == TriggerModeIdle &&
                screenSaverCheck.Checked;
            settings.SyncLockWorkstation = syncLockCheck.Checked;
            settings.HotKeyModifiers = pendingHotKeyModifiers;
            settings.HotKeyKey = pendingHotKeyKey;
            settings.SettingsVersion = 4;
        }

        private void IdleUnitChanged(object sender, EventArgs e)
        {
            if (syncingIdleUnit || idleUnit.SelectedIndex < 0)
            {
                return;
            }

            syncingIdleUnit = true;
            int oldMultiplier = lastIdleUnitIndex == 1 ? 60 : 1;
            int newMultiplier = idleUnit.SelectedIndex == 1 ? 60 : 1;
            decimal seconds = idleValue.Value * oldMultiplier;
            idleValue.Maximum = idleUnit.SelectedIndex == 1 ? 1440 : 86400;
            decimal converted = Math.Max(1, Math.Round(seconds / newMultiplier));
            idleValue.Value = Math.Min(idleValue.Maximum, converted);
            lastIdleUnitIndex = idleUnit.SelectedIndex;
            syncingIdleUnit = false;
        }

        private void BrightnessSliderChanged(object sender, EventArgs e)
        {
            if (syncingBrightnessControls)
            {
                return;
            }

            syncingBrightnessControls = true;
            brightnessValue.Value = brightnessSlider.Value;
            syncingBrightnessControls = false;
        }

        private void BrightnessValueChanged(object sender, EventArgs e)
        {
            if (syncingBrightnessControls)
            {
                return;
            }

            syncingBrightnessControls = true;
            brightnessSlider.Value = Decimal.ToInt32(brightnessValue.Value);
            syncingBrightnessControls = false;
        }

        private void HotKeyTextKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Escape)
            {
                pendingHotKeyModifiers = 0;
                pendingHotKeyKey = 0;
                UpdateHotKeyText();
                return;
            }

            if (IsModifierKey(e.KeyCode))
            {
                hotKeyText.Text = "请再按一个按键";
                return;
            }

            int modifiers = 0;
            if (e.Control)
            {
                modifiers |= (int)NativeMethods.HotKeyModifierControl;
            }
            if (e.Alt)
            {
                modifiers |= (int)NativeMethods.HotKeyModifierAlt;
            }
            if (e.Shift)
            {
                modifiers |= (int)NativeMethods.HotKeyModifierShift;
            }

            if (!IsValidHotKey(modifiers, (int)e.KeyCode))
            {
                System.Media.SystemSounds.Beep.Play();
                hotKeyText.Text = "字母/数字需配合 Ctrl 等";
                hotKeyText.SelectAll();
                return;
            }

            pendingHotKeyModifiers = modifiers;
            pendingHotKeyKey = (int)e.KeyCode;
            UpdateHotKeyText();
        }

        private void PollConfiguredHotKey()
        {
            bool chordDown = IsConfiguredHotKeyDown();
            if (chordDown && !hotKeyChordDown)
            {
                RequestImmediateScreenSaver(
                    registeredHotKeyModifiers,
                    registeredHotKeyKey,
                    false);
            }
            hotKeyChordDown = chordDown;
        }

        private bool IsConfiguredHotKeyDown()
        {
            if (registeredHotKeyKey == 0)
            {
                return false;
            }

            if (!NativeMethods.IsKeyDown(registeredHotKeyKey))
            {
                return false;
            }

            if ((registeredHotKeyModifiers & NativeMethods.HotKeyModifierControl) != 0 &&
                !NativeMethods.IsKeyDown((int)Keys.ControlKey))
            {
                return false;
            }

            if ((registeredHotKeyModifiers & NativeMethods.HotKeyModifierAlt) != 0 &&
                !NativeMethods.IsKeyDown((int)Keys.Menu))
            {
                return false;
            }

            return (registeredHotKeyModifiers & NativeMethods.HotKeyModifierShift) == 0 ||
                NativeMethods.IsKeyDown((int)Keys.ShiftKey);
        }

        private void UpdateHotKeyText()
        {
            hotKeyText.Text = pendingHotKeyKey == 0
                ? "未设置（点击录入）"
                : FormatHotKey(pendingHotKeyModifiers, pendingHotKeyKey);
            hotKeyText.Select(0, 0);
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LWin || key == Keys.RWin;
        }

        private static bool IsValidHotKey(int modifiers, int key)
        {
            if ((modifiers & ~SupportedHotKeyModifiers) != 0)
            {
                return false;
            }

            if (key == 0)
            {
                return modifiers == 0;
            }

            Keys virtualKey = (Keys)key;
            bool functionKey = virtualKey >= Keys.F1 && virtualKey <= Keys.F24;
            return !IsModifierKey(virtualKey) && (modifiers != 0 || functionKey);
        }

        private static string FormatHotKey(int modifiers, int key)
        {
            if (key == 0)
            {
                return "未设置";
            }

            List<string> parts = new List<string>();
            if ((modifiers & NativeMethods.HotKeyModifierControl) != 0)
            {
                parts.Add("Ctrl");
            }
            if ((modifiers & NativeMethods.HotKeyModifierAlt) != 0)
            {
                parts.Add("Alt");
            }
            if ((modifiers & NativeMethods.HotKeyModifierShift) != 0)
            {
                parts.Add("Shift");
            }

            Keys virtualKey = (Keys)key;
            string keyName;
            if (virtualKey >= Keys.D0 && virtualKey <= Keys.D9)
            {
                keyName = ((char)('0' + ((int)virtualKey - (int)Keys.D0))).ToString();
            }
            else if (virtualKey >= Keys.NumPad0 && virtualKey <= Keys.NumPad9)
            {
                keyName = "Num " +
                    ((char)('0' + ((int)virtualKey - (int)Keys.NumPad0))).ToString();
            }
            else
            {
                keyName = virtualKey.ToString();
            }

            parts.Add(keyName);
            return String.Join(" + ", parts.ToArray());
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
            statusDot.Tag = color;
            statusDot.Invalidate();

            string trayText = "外接显示器调光 - " + text;
            trayIcon.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText;
        }

        private void UpdateMonitoringControls()
        {
            stopButton.Enabled = monitoring;
            stopButton.ForeColor = monitoring ? TextPrimary : Inactive;
            trayToggleItem.Text = monitoring ? "停止监控" : "开始监控";
            applyButton.Text = monitoring ? "应用设置" : "应用并开始";
        }

        private void HideToTray()
        {
            SettingsStore.Log(String.Format(
                "HideToTray called. Visible={0}, WindowState={1}.",
                Visible,
                WindowState));
            bool rebindHotKey = hotKeyRegistered;
            if (rebindHotKey)
            {
                UnregisterHotKeyForCurrentHandle();
            }

            Hide();
            ShowInTaskbar = false;

            if (rebindHotKey)
            {
                RegisterSavedHotKey();
            }

            if (!trayHintShown)
            {
                trayIcon.ShowBalloonTip(
                    2000,
                    "外接显示器休眠调光",
                    "程序正在通知区域运行。",
                    ToolTipIcon.Info);
                trayHintShown = true;
            }
        }

        private void ExitApplication()
        {
            allowExit = true;
            Close();
        }

        private void UnregisterHotKeyForCurrentHandle()
        {
            if (!hotKeyRegistered)
            {
                return;
            }

            NativeMethods.UnregisterGlobalHotKey(Handle, ImmediateSleepHotKeyId);
            hotKeyRegistered = false;
            hotKeyChordDown = false;
            SettingsStore.Log("Immediate screen saver hot key unregistered for handle transition.");
        }

        private void FormClosingHandler(object sender, FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            monitorTimer.Stop();
            monitoring = false;
            CancelImmediateScreenSaver();
            if (hotKeyRegistered)
            {
                NativeMethods.UnregisterGlobalHotKey(Handle, ImmediateSleepHotKeyId);
                hotKeyRegistered = false;
            }
            hotKeyChordDown = false;
            RestoreSavedBrightness();
            TryRestoreScreenSaver(false);
        }

        private void FormClosedHandler(object sender, FormClosedEventArgs e)
        {
            UnregisterSessionNotifications();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            toolTip.Dispose();
            applicationIcon.Dispose();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmWtsSessionChange)
            {
                int sessionState = message.WParam.ToInt32();
                if (sessionState == NativeMethods.WtsSessionLock &&
                    immediateSleepActive && immediateSleepSyncLock)
                {
                    immediateSleepSessionLocked = true;
                    SetStatus("已锁屏，亮度保持最低", Warning);
                    SettingsStore.Log("Windows workstation locked for immediate screen saver.");
                    return;
                }

                if (sessionState == NativeMethods.WtsSessionUnlock &&
                    immediateSleepActive && immediateSleepSyncLock)
                {
                    SettingsStore.Log(immediateSleepSessionLocked
                        ? "Windows workstation unlocked; restoring brightness."
                        : "Windows workstation unlock received before lock notification; restoring brightness.");
                    FinishImmediateScreenSaver();
                    return;
                }
            }

            if (message.Msg == NativeMethods.WmHotKey &&
                message.WParam.ToInt32() == ImmediateSleepHotKeyId)
            {
                SettingsStore.Log("WM_HOTKEY received for immediate screen saver.");
                RequestImmediateScreenSaver(
                    registeredHotKeyModifiers,
                    registeredHotKeyKey,
                    false);
                return;
            }

            base.WndProc(ref message);
        }

        private void FormResizeHandler(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                SettingsStore.Log("Window minimized; hiding to tray.");
                HideToTray();
            }
        }

        private void StatusDotPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = statusDot.Tag is Color ? (Color)statusDot.Tag : Inactive;
            using (SolidBrush brush = new SolidBrush(color))
            {
                e.Graphics.FillEllipse(brush, 2, 2, 10, 10);
            }
        }

        private void ResizeMonitorColumns()
        {
            if (monitorList.Columns.Count != 4 || monitorList.ClientSize.Width <= 0)
            {
                return;
            }

            int available = monitorList.ClientSize.Width - 8;
            monitorList.Columns[0].Width = 128;
            monitorList.Columns[2].Width = 104;
            monitorList.Columns[3].Width = 92;
            monitorList.Columns[1].Width = Math.Max(160, available - 324);
        }

        private Icon CreateApplicationIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (SolidBrush body = new SolidBrush(TextPrimary))
                using (SolidBrush screen = new SolidBrush(Accent))
                using (Pen stand = new Pen(TextPrimary, 2F))
                {
                    graphics.FillRectangle(body, 3, 5, 26, 18);
                    graphics.FillRectangle(screen, 6, 8, 20, 12);
                    graphics.DrawLine(stand, 16, 23, 16, 27);
                    graphics.DrawLine(stand, 10, 28, 22, 28);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    NativeMethods.ReleaseIconHandle(handle);
                }
            }
        }
    }
}
