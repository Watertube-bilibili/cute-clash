// THESIS: A calm navy connection console for a dependable Windows 7 proxy client.
// OWN-WORLD: Navy instruments, milk workspace, approved kitten badge; native controls and soft geometry.
// STORY: Import a Clash profile, choose routing, connect, and inspect the actual running engine.
// FIRST VIEWPORT: Kitten navigation; dominant connection action; profile, independent capture controls and real traffic.
// FORM: Native Windows utility, Operate mode; task-constrained code-led layout, no illustrative data.
// FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, and DESIGN.md
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CuteClash
{
    public sealed class MainForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(23, 43, 77);
        private static readonly Color Muted = Color.FromArgb(91, 107, 126);
        private static readonly Color Accent = Color.FromArgb(23, 43, 77);
        private static readonly Color Line = Color.FromArgb(222, 229, 238);
        private static readonly Color Workspace = Color.FromArgb(249, 249, 246);
        private static readonly Color NavMuted = Color.FromArgb(196, 210, 230);
        private static readonly Color SidebarColor = Color.FromArgb(23, 43, 77);
        private readonly AppController controller;
        private readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        private readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, int> delays = new Dictionary<string, int>();
        private readonly List<Button> actionButtons = new List<Button>();
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem trayConnect;
        private readonly Panel contentHost;
        private readonly Label pageTitle;
        private readonly Label pageDescription;
        private readonly Label sidebarState;
        private readonly Button connectButton;
        private readonly ToolStripStatusLabel statusText;
        private readonly ToolStripStatusLabel busyText;
        private Button overviewConnect;
        private Label profileEmpty;
        private Label connectionTitle;
        private Label connectionHint;
        private Label overviewProfile;
        private Label overviewMode;
        private Label overviewUpload;
        private Label overviewDownload;
        private Label overviewConnections;
        private Label overviewEndpoint;
        private Label engineVersion;
        private Label profileHint;
        private Label proxyHint;
        private Label groupHeading;
        private Label settingsHint;
        private CheckBox systemProxy;
        private CheckBox tunMode;
        private ComboBox modeChoice;
        private ComboBox languageChoice;
        private Label languageHint;
        private ListView profileList;
        private ListBox groupList;
        private ListView nodeList;
        private TextBox logBox;
        private NumericUpDown mixedPort;
        private NumericUpDown controllerPort;
        private Button selectProxyButton;
        private Button useProfileButton;
        private Button updateProfileButton;
        private Button validateProfileButton;
        private Button deleteProfileButton;
        private Button testProxyButton;
        private Button refreshGroupsButton;
        private bool busy;
        private bool syncing;
        private bool snapshotBusy;
        private bool exitRequested;
        private bool closeInProgress;
        private bool trayHintShown;
        private string activePage = "Overview";
        private IList<ProxyGroup> groups = new List<ProxyGroup>();
        private int refreshTick;

        public MainForm(AppController appController)
        {
            SuspendLayout();
            controller = appController;
            Localization.Language = controller.Settings.Language;
            Text = "cute clash";
            Font = new Font(AppFont, 9F, FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = Ink;
            BackColor = Workspace;
            ClientSize = new Size(1024, 680);
            MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = MakeIcon();

            TableLayoutPanel shell = new TableLayoutPanel();
            shell.Dock = DockStyle.Fill;
            shell.Margin = Padding.Empty;
            shell.Padding = Padding.Empty;
            shell.ColumnCount = 2;
            shell.RowCount = 2;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196F));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            Controls.Add(shell);

            Panel sidebar = new Panel { Dock = DockStyle.Fill, BackColor = SidebarColor, Margin = Padding.Empty, Padding = new Padding(16, 24, 16, 20) };
            sidebar.Paint += delegate(object sender, PaintEventArgs e) { using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height); };
            shell.Controls.Add(sidebar, 0, 0);
            TableLayoutPanel sidebarLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 276));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            sidebar.Controls.Add(sidebarLayout);
            Panel brand = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            SoftPanel badge = new SoftPanel { BackColor = Workspace, CornerRadius = 12, Size = new Size(47, 47), Location = new Point(4, 0) };
            PictureBox kitten = new PictureBox { Image = BrandBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(33, 33), Location = new Point(7, 6), BackColor = Workspace, TabStop = false };
            kitten.Disposed += delegate { kitten.Image.Dispose(); };
            badge.Controls.Add(kitten); brand.Controls.Add(badge);
            Label brandName = LabelOf("cute clash", 14.5F, FontStyle.Bold, Color.White);
            brandName.Location = new Point(60, 10); brandName.AutoSize = true; brand.Controls.Add(brandName);
            Label brandSubtitle = LabelOf(Localization.T("为 Windows 7 而造", "Built for Windows 7"), 8.5F, FontStyle.Regular, NavMuted);
            brandSubtitle.Location = new Point(7, 62); brandSubtitle.AutoSize = true; brand.Controls.Add(brandSubtitle);
            sidebarLayout.Controls.Add(brand, 0, 0);
            FlowLayoutPanel navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            sidebarLayout.Controls.Add(navigation, 0, 1);
            AddNavigation(navigation, "Overview", Localization.T("总览", "Overview"), Localization.T("连接状态与路由方式", "Connection status and routing"));
            AddNavigation(navigation, "Profiles", Localization.T("配置", "Profiles"), Localization.T("导入和管理 Clash 配置", "Import and manage Clash profiles"));
            AddNavigation(navigation, "Proxies", Localization.T("代理", "Proxies"), Localization.T("切换节点与测试延迟", "Switch nodes and test latency"));
            AddNavigation(navigation, "Settings", Localization.T("设置", "Settings"), Localization.T("语言、本地端口与内核信息", "Language, local ports and core details"));
            AddNavigation(navigation, "Logs", Localization.T("日志", "Logs"), Localization.T("查看内核运行记录", "View core activity and diagnostics"));
            Panel sidebarBottom = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            sidebarState = LabelOf(Localization.T("未连接", "Disconnected"), 10F, FontStyle.Bold, Color.White);
            sidebarState.AutoSize = true;
            sidebarState.Location = new Point(8, 8);
            sidebarBottom.Controls.Add(sidebarState);
            Label platform = LabelOf("Windows 7 SP1 +\r\n.NET Framework 4.8", 8F, FontStyle.Regular, NavMuted);
            platform.AutoSize = true;
            platform.Location = new Point(8, 35);
            sidebarBottom.Controls.Add(platform);
            sidebarLayout.Controls.Add(sidebarBottom, 0, 3);

            TableLayoutPanel main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(28, 24, 28, 20) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.Controls.Add(main, 1, 0);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            Panel heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            pageTitle = LabelOf(Localization.T("总览", "Overview"), 21F, FontStyle.Bold, Ink);
            pageTitle.Location = new Point(0, 0);
            pageTitle.AutoSize = true;
            pageDescription = LabelOf(Localization.T("连接状态与路由方式", "Connection status and routing"), 9F, FontStyle.Regular, Muted);
            pageDescription.Location = new Point(1, 42);
            pageDescription.AutoSize = true;
            heading.Controls.Add(pageTitle);
            heading.Controls.Add(pageDescription);
            header.Controls.Add(heading, 0, 0);
            connectButton = MakeButton(Localization.T("开始连接", "Connect"), true, 136);
            connectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            connectButton.Margin = new Padding(0, 7, 0, 0);
            connectButton.Height = 40;
            connectButton.Click += async delegate { await ToggleConnectionAsync(); };
            header.Controls.Add(connectButton, 1, 0);
            main.Controls.Add(header, 0, 0);
            contentHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            main.Controls.Add(contentHost, 0, 1);

            StatusStrip status = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = true, BackColor = Color.White, Font = Font, Padding = new Padding(16, 0, 16, 0) };
            statusText = new ToolStripStatusLabel(Localization.T("就绪 · 导入配置后连接", "Ready · Import a profile to connect")) { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            busyText = new ToolStripStatusLabel("");
            status.Items.Add(statusText);
            status.Items.Add(busyText);
            shell.Controls.Add(status, 0, 1);
            shell.SetColumnSpan(status, 2);

            BuildOverview();
            BuildProfiles();
            BuildProxies();
            BuildSettings();
            BuildLogs();

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            ToolStripMenuItem showItem = new ToolStripMenuItem(Localization.T("显示 cute clash", "Show cute clash"));
            showItem.Click += delegate { RestoreWindow(); };
            trayMenu.Items.Add(showItem);
            trayConnect = new ToolStripMenuItem(Localization.T("开始连接", "Connect"));
            trayConnect.Click += async delegate { await ToggleConnectionAsync(); };
            trayMenu.Items.Add(trayConnect);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem(Localization.T("退出并断开连接", "Disconnect and quit"));
            exitItem.Click += async delegate { await ExitAsync(); };
            trayMenu.Items.Add(exitItem);
            tray = new NotifyIcon { Icon = Icon, Text = Localization.T("cute clash · 未连接", "cute clash · Disconnected"), ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { RestoreWindow(); };

            controller.Log += OnLog;
            controller.StateChanged += OnControllerStateChanged;
            refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            refreshTimer.Tick += async delegate { await RefreshSnapshotAsync(); };
            refreshTimer.Start();
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };
            FormClosing += OnFormClosing;
            FormClosed += delegate { refreshTimer.Stop(); refreshTimer.Dispose(); tray.Visible = false; tray.Dispose(); controller.Log -= OnLog; controller.StateChanged -= OnControllerStateChanged; controller.Dispose(); };
            Shown += delegate { SyncState(); };
            ShowPageForCapture("Overview");
            SyncState();
            // Set the design baseline after all controls exist. Setting it before
            // layout construction lets WinForms scale an empty form at high DPI.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }

        private static string AppFont { get { return Localization.Language == "en" ? "Segoe UI" : "Microsoft YaHei"; } }

        private static Label LabelOf(string text, float size, FontStyle style, Color color)
        {
            return new Label { Text = text, Font = new Font(AppFont, size, style), ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false };
        }

        private Button MakeButton(string text, bool primary, int width)
        {
            SoftButton button = new SoftButton { Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Padding = new Padding(4, 0, 4, 0), EdgeColor = primary ? Color.Transparent : Line, HoverColor = primary ? Color.FromArgb(39, 65, 104) : Color.FromArgb(235, 240, 246), PressColor = primary ? Color.FromArgb(14, 30, 55) : Color.FromArgb(224, 233, 244) };
            actionButtons.Add(button);
            return button;
        }

        private void AddNavigation(FlowLayoutPanel host, string key, string text, string description)
        {
            SoftButton button = new SoftButton { Text = text, Glyph = key, AccessibleName = text, Tag = description, BackColor = SidebarColor, ForeColor = NavMuted, TextAlign = ContentAlignment.MiddleLeft, Width = 164, Height = 45, Margin = new Padding(0, 0, 0, 7), Cursor = Cursors.Hand, CornerRadius = 9, HoverColor = Color.FromArgb(41, 64, 99), PressColor = Color.FromArgb(49, 75, 112) };
            button.Click += async delegate { ShowPageForCapture(key); if (key == "Proxies" && controller.IsRunning && !busy) await RunActionAsync(Localization.T("读取代理组", "Load proxy groups"), RefreshGroupsAsync); };
            navButtons.Add(key, button);
            host.Controls.Add(button);
        }

        private Panel NewPage(string key)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = Workspace, Visible = false, Margin = Padding.Empty };
            contentHost.Controls.Add(panel);
            pages.Add(key, panel);
            return panel;
        }

        public void ShowPageForCapture(string name)
        {
            if (!pages.ContainsKey(name)) return;
            activePage = name;
            foreach (KeyValuePair<string, Panel> item in pages) item.Value.Visible = item.Key == name;
            pages[name].BringToFront();
            foreach (KeyValuePair<string, Button> item in navButtons)
            {
                item.Value.BackColor = item.Key == name ? Color.FromArgb(233, 239, 248) : SidebarColor;
                ((SoftButton)item.Value).HoverColor = item.Key == name ? Color.FromArgb(243, 246, 251) : Color.FromArgb(41, 64, 99);
                item.Value.ForeColor = item.Key == name ? Accent : NavMuted;
                item.Value.Font = new Font(Font, item.Key == name ? FontStyle.Bold : FontStyle.Regular);
            }
            connectButton.Visible = name != "Overview";
            pageTitle.Text = navButtons[name].Text;
            pageDescription.Text = (string)navButtons[name].Tag;
        }

        private void BuildOverview()
        {
            Panel page = NewPage("Overview");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            page.Controls.Add(layout);

            SoftPanel state = new SoftPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = SidebarColor, Padding = new Padding(24, 22, 24, 22), CornerRadius = 16 };
            TableLayoutPanel stateGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, BackColor = SidebarColor };
            stateGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); stateGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            stateGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); stateGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            connectionTitle = LabelOf(Localization.T("尚未连接", "Disconnected"), 20F, FontStyle.Bold, Color.White); connectionTitle.AutoSize = true; connectionTitle.Margin = Padding.Empty;
            connectionHint = LabelOf(Localization.T("添加 Clash 配置，选择路由方式后开始连接。", "Add a Clash profile, choose routing options, then connect."), 9F, FontStyle.Regular, NavMuted); connectionHint.Dock = DockStyle.Fill; connectionHint.Margin = new Padding(1, 9, 20, 0);
            stateGrid.Controls.Add(connectionTitle, 0, 0); stateGrid.Controls.Add(connectionHint, 0, 1);
            overviewConnect = MakeButton(Localization.T("开始连接", "Connect"), false, 138); overviewConnect.Height = 45; overviewConnect.Margin = new Padding(0, 8, 0, 0); overviewConnect.Anchor = AnchorStyles.Top | AnchorStyles.Right; ((SoftButton)overviewConnect).EdgeColor = Color.Transparent;
            overviewConnect.Click += async delegate { await ToggleConnectionAsync(); };
            stateGrid.Controls.Add(overviewConnect, 1, 0); stateGrid.SetRowSpan(overviewConnect, 2);
            state.Controls.Add(stateGrid); layout.Controls.Add(state, 0, 0);

            TableLayoutPanel profileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 25, 0, 0) };
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
            Panel profileText = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label caption = LabelOf(Localization.T("当前配置", "Active profile"), 9F, FontStyle.Regular, Muted); caption.AutoSize = true; caption.Location = new Point(0, 0);
            overviewProfile = LabelOf(Localization.T("尚未添加配置", "No profile added"), 12F, FontStyle.Bold, Ink); overviewProfile.Location = new Point(0, 27); overviewProfile.Size = new Size(440, 31); overviewProfile.AutoEllipsis = true;
            profileText.Resize += delegate { overviewProfile.Width = Math.Max(1, profileText.ClientSize.Width - 12); };
            profileText.Controls.Add(caption); profileText.Controls.Add(overviewProfile); profileRow.Controls.Add(profileText, 0, 0);
            Button manage = MakeButton(Localization.T("管理配置", "Manage profiles"), false, 132); manage.Anchor = AnchorStyles.Top | AnchorStyles.Right; manage.Margin = new Padding(0, 13, 0, 0); manage.Click += delegate { ShowPageForCapture("Profiles"); };
            profileRow.Controls.Add(manage, 1, 0); layout.Controls.Add(profileRow, 0, 1);

            SoftPanel routing = new SoftPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 13, 18, 12), Margin = new Padding(0, 0, 0, 16), CornerRadius = 12, EdgeColor = Line };
            TableLayoutPanel routeGrid = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            routeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); routeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            routeGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); routeGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            routeGrid.Controls.Add(AutoLabel(Localization.T("接管方式", "Traffic capture"), Muted), 0, 0); routeGrid.Controls.Add(AutoLabel(Localization.T("路由模式", "Routing mode"), Muted), 1, 0);
            FlowLayoutPanel checkRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, WrapContents = false };
            systemProxy = new CheckBox { Text = Localization.T("系统代理", "System proxy"), AutoSize = true, Margin = new Padding(0, 4, 15, 0), AccessibleDescription = Localization.T("让使用 Windows 代理设置的应用通过本地代理连接", "Route apps that use Windows proxy settings through the local proxy") };
            tunMode = new CheckBox { Text = Localization.T("TUN 模式", "TUN mode"), AutoSize = true, Margin = new Padding(0, 4, 0, 0), AccessibleDescription = Localization.T("以虚拟网卡接管网络连接，需要管理员权限", "Route network connections through a virtual adapter; requires administrator rights") };
            systemProxy.CheckedChanged += async delegate { if (!syncing) { bool requested = systemProxy.Checked; await RunActionAsync(Localization.T("更改系统代理", "Change system proxy"), async delegate { await controller.SetSystemProxyAsync(requested); }); } };
            tunMode.CheckedChanged += async delegate { if (!syncing) await ChangeTunAsync(); };
            checkRow.Controls.Add(systemProxy); checkRow.Controls.Add(tunMode); routeGrid.Controls.Add(checkRow, 0, 1);
            modeChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, MinimumSize = new Size(210, 0), MaximumSize = new Size(270, 0), Width = 240, Margin = new Padding(0, 0, 0, 0), AccessibleName = Localization.T("路由模式", "Routing mode") };
            modeChoice.Items.AddRange(new object[] { Localization.T("规则 · 按配置分流", "Rule · Follow profile rules"), Localization.T("全局 · 使用全局代理", "Global · Use global proxy"), Localization.T("直连 · 不经过代理", "Direct · Bypass proxy") });
            modeChoice.SelectedIndexChanged += async delegate { if (!syncing && modeChoice.SelectedIndex >= 0) { string mode = new[] { "rule", "global", "direct" }[modeChoice.SelectedIndex]; await RunActionAsync(Localization.T("切换路由模式", "Change routing mode"), async delegate { await controller.SetModeAsync(mode); }); } };
            routeGrid.Controls.Add(modeChoice, 1, 1); routing.Controls.Add(routeGrid); layout.Controls.Add(routing, 0, 2);

            TableLayoutPanel runtime = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            runtime.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); runtime.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); runtime.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            runtime.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); runtime.RowStyles.Add(new RowStyle(SizeType.Absolute, 41)); runtime.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            runtime.Controls.Add(AutoLabel(Localization.T("累计上传", "Total uploaded"), Muted), 0, 0); runtime.Controls.Add(AutoLabel(Localization.T("累计下载", "Total downloaded"), Muted), 1, 0); runtime.Controls.Add(AutoLabel(Localization.T("活动连接", "Active connections"), Muted), 2, 0);
            overviewUpload = LabelOf("—", 22F, FontStyle.Regular, Ink); overviewUpload.AutoSize = true;
            overviewDownload = LabelOf("—", 22F, FontStyle.Regular, Ink); overviewDownload.AutoSize = true;
            overviewConnections = LabelOf("—", 22F, FontStyle.Regular, Ink); overviewConnections.AutoSize = true;
            runtime.Controls.Add(overviewUpload, 0, 1); runtime.Controls.Add(overviewDownload, 1, 1); runtime.Controls.Add(overviewConnections, 2, 1);
            overviewEndpoint = AutoLabel(Localization.T("本地代理  127.0.0.1:7890", "Local proxy  127.0.0.1:7890"), Muted); overviewEndpoint.Margin = new Padding(0, 12, 0, 0); runtime.Controls.Add(overviewEndpoint, 0, 2); runtime.SetColumnSpan(overviewEndpoint, 3); layout.Controls.Add(runtime, 0, 3);
            overviewMode = AutoLabel(Localization.T("TUN 需要管理员权限；系统代理与 TUN 可分别启用。", "TUN requires administrator rights. System proxy and TUN are independent."), Muted); overviewMode.Dock = DockStyle.Fill; overviewMode.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(overviewMode, 0, 5);
        }

        private static Label AutoLabel(string text, Color color)
        {
            Label label = LabelOf(text, 9F, FontStyle.Regular, color);
            label.AutoSize = true;
            label.Margin = Padding.Empty;
            return label;
        }

        private static ListView NewListView()
        {
            return new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Color.White, ForeColor = Ink };
        }

        private void BuildProfiles()
        {
            Panel page = NewPage("Profiles");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            page.Controls.Add(layout);
            FlowLayoutPanel importRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            Button file = MakeButton(Localization.T("导入本地文件", "Import file"), true, 126);
            file.Click += async delegate { using (OpenFileDialog dialog = new OpenFileDialog { Title = Localization.T("导入 Clash 配置", "Import Clash profile"), Filter = Localization.T("YAML 配置 (*.yaml;*.yml)|*.yaml;*.yml|所有文件 (*.*)|*.*", "YAML profiles (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*"), Multiselect = false }) { if (dialog.ShowDialog(this) == DialogResult.OK) await RunActionAsync(Localization.T("导入配置", "Import profile"), async delegate { await controller.ImportFileAsync(dialog.FileName); }); } };
            Button url = MakeButton(Localization.T("添加订阅", "Add subscription"), false, 142);
            url.Click += async delegate { using (SubscriptionDialog dialog = new SubscriptionDialog(Font)) { if (dialog.ShowDialog(this) == DialogResult.OK) await RunActionAsync(Localization.T("下载订阅配置", "Download subscription"), async delegate { await controller.ImportUrlAsync(dialog.ProfileName, dialog.SubscriptionUrl); }); } };
            importRow.Controls.Add(file); importRow.Controls.Add(url);
            layout.Controls.Add(importRow, 0, 0);
            profileList = NewListView(); profileList.Margin = Padding.Empty;
            profileList.SelectedIndexChanged += delegate { UpdateSelectionActions(); };
            profileList.Columns.Add(Localization.T("配置名称", "Profile name"), 245); profileList.Columns.Add(Localization.T("来源", "Source"), 115); profileList.Columns.Add(Localization.T("更新于", "Updated"), 150); profileList.Columns.Add(Localization.T("状态", "Status"), 95);
            profileList.Resize += delegate { SizeProfileColumns(); };
            profileList.DoubleClick += async delegate { await UseSelectedProfileAsync(); };
            profileList.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; await UseSelectedProfileAsync(); } };
            SoftPanel profileSurface = new SoftPanel { Dock = DockStyle.Fill, BackColor = Color.White, CornerRadius = 14, EdgeColor = Line, Padding = new Padding(14, 13, 14, 13), Margin = Padding.Empty };
            profileList.BorderStyle = BorderStyle.None; profileSurface.Controls.Add(profileList);
            profileEmpty = LabelOf(Localization.T("还没有配置\r\n\r\n导入本地 YAML，或添加订阅开始使用。", "No profiles yet\r\n\r\nImport a YAML file or add a subscription to get started."), 11F, FontStyle.Regular, Muted);
            profileEmpty.Dock = DockStyle.Fill; profileEmpty.TextAlign = ContentAlignment.MiddleCenter; profileSurface.Controls.Add(profileEmpty);
            layout.Controls.Add(profileSurface, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0), WrapContents = false };
            Button use = MakeButton(Localization.T("使用选中配置", "Use selected"), false, 124); use.Click += async delegate { await UseSelectedProfileAsync(); };
            Button update = MakeButton(Localization.T("更新订阅", "Update"), false, 103); update.Click += async delegate { string id = SelectedProfileId(); if (id != null) await RunActionAsync(Localization.T("更新订阅", "Update"), async delegate { await controller.UpdateProfileAsync(id); }); };
            Button validate = MakeButton(Localization.T("检查配置", "Validate"), false, 103); validate.Click += async delegate { string id = SelectedProfileId(); if (id != null) await RunActionAsync(Localization.T("检查配置", "Validate"), async delegate { await controller.ValidateProfileAsync(id); statusText.Text = Localization.T("配置检查通过", "Profile validation passed"); }); };
            Button delete = MakeButton(Localization.T("删除", "Delete"), false, 74); delete.Click += async delegate { string id = SelectedProfileId(); if (id == null) return; ProfileInfo info = controller.Settings.Profiles.Find(delegate(ProfileInfo item) { return item.Id == id; }); if (MessageBox.Show(this, Localization.T("删除配置“", "Delete profile “") + (info == null ? Localization.T("选中配置", "selected profile") : info.Name) + Localization.T("”？\r\n这会移除本地副本，订阅服务不会受影响。", "”?\r\nThis removes the local copy. Your subscription service is unaffected."), Localization.T("删除配置", "Delete profile"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) await RunActionAsync(Localization.T("删除配置", "Delete profile"), async delegate { await controller.DeleteProfileAsync(id); }); };
            useProfileButton = use; updateProfileButton = update; validateProfileButton = validate; deleteProfileButton = delete;
            actions.Controls.Add(use); actions.Controls.Add(update); actions.Controls.Add(validate); actions.Controls.Add(delete);
            layout.Controls.Add(actions, 0, 2);
            profileHint = AutoLabel(Localization.T("导入 Clash / Mihomo YAML，或添加返回 YAML 的订阅地址。", "Import Clash / Mihomo YAML or add a subscription URL that returns YAML."), Muted); profileHint.Dock = DockStyle.Fill; profileHint.Margin = new Padding(0, 11, 0, 0);
            layout.Controls.Add(profileHint, 0, 3);
        }

        private void BuildProxies()
        {
            Panel page = NewPage("Proxies");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            page.Controls.Add(layout);
            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            Button refresh = MakeButton(Localization.T("刷新代理组", "Refresh groups"), false, 132); refresh.Click += async delegate { await RunActionAsync(Localization.T("读取代理组", "Load proxy groups"), RefreshGroupsAsync); };
            refreshGroupsButton = refresh;
            top.Controls.Add(refresh);
            groupHeading = AutoLabel(Localization.T("连接内核后读取配置中的代理组", "Connect to load proxy groups from your profile"), Muted); groupHeading.Margin = new Padding(5, 8, 0, 0); top.Controls.Add(groupHeading);
            layout.Controls.Add(top, 0, 0);
            TableLayoutPanel lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29)); lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 71));
            lists.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lists.Controls.Add(AutoLabel(Localization.T("代理组", "Proxy groups"), Muted), 0, 0); lists.Controls.Add(AutoLabel(Localization.T("节点", "Nodes"), Muted), 1, 0);
            groupList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 12, 0), HorizontalScrollbar = true, AccessibleName = Localization.T("代理组", "Proxy groups") };
            groupList.SelectedIndexChanged += delegate { FillNodes(); };
            nodeList = NewListView(); nodeList.Margin = Padding.Empty; nodeList.Columns.Add(Localization.T("节点名称", "Node name"), 267); nodeList.Columns.Add(Localization.T("状态 / 延迟", "Status / latency"), 116);
            nodeList.Resize += delegate { SizeNodeColumns(); };
            nodeList.SelectedIndexChanged += delegate { UpdateSelectionActions(); };
            nodeList.DoubleClick += async delegate { await SelectNodeAsync(); };
            nodeList.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; await SelectNodeAsync(); } };
            lists.Controls.Add(groupList, 0, 1); lists.Controls.Add(nodeList, 1, 1);
            SoftPanel proxySurface = new SoftPanel { Dock = DockStyle.Fill, BackColor = Color.White, CornerRadius = 14, EdgeColor = Line, Padding = new Padding(16), Margin = Padding.Empty };
            lists.BackColor = Color.White; nodeList.BorderStyle = BorderStyle.None; groupList.BackColor = Color.FromArgb(242, 246, 251); groupList.BorderStyle = BorderStyle.None;
            proxySurface.Controls.Add(lists); layout.Controls.Add(proxySurface, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0), WrapContents = false };
            selectProxyButton = MakeButton(Localization.T("使用选中节点", "Use selected"), false, 125); selectProxyButton.Click += async delegate { await SelectNodeAsync(); };
            Button delay = MakeButton(Localization.T("测试选中节点", "Test selected"), false, 125); delay.Click += async delegate { await TestNodeAsync(); };
            testProxyButton = delay;
            actions.Controls.Add(selectProxyButton); actions.Controls.Add(delay);
            layout.Controls.Add(actions, 0, 2);
            proxyHint = AutoLabel(Localization.T("先连接内核，再选择代理组。自动策略组由内核管理。", "Connect, then choose a proxy group. The core manages automatic groups."), Muted); proxyHint.Dock = DockStyle.Fill; proxyHint.Margin = new Padding(0, 11, 0, 0);
            layout.Controls.Add(proxyHint, 0, 3);
        }

        private void BuildSettings()
        {
            Panel page = NewPage("Settings"); page.AutoScroll = true;
            TableLayoutPanel stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 0, 1, 0) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(stack);

            TableLayoutPanel languageLayout;
            SoftPanel languageCard = SettingsSection(Localization.T("界面语言", "Interface language"), out languageLayout);
            Label languageLabel = AutoLabel("语言 / Language", Muted); languageLabel.Margin = new Padding(0, 7, 0, 0); languageLayout.Controls.Add(languageLabel, 0, 1);
            languageChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 176, AccessibleName = "语言 / Language", Margin = new Padding(0, 4, 12, 0) };
            languageChoice.Items.AddRange(new object[] { "简体中文", "English" }); languageChoice.SelectedIndex = controller.Settings.Language == "en" ? 1 : 0;
            FlowLayoutPanel languageActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = true };
            Button applyLanguage = MakeButton(Localization.T("应用语言", "Apply language"), false, 135); applyLanguage.Click += async delegate { await ApplyLanguageAsync(); };
            languageActions.Controls.Add(languageChoice); languageActions.Controls.Add(applyLanguage); languageLayout.Controls.Add(languageActions, 1, 1);
            languageHint = AutoLabel(Localization.T("保存后重启应用生效。重启会断开连接。", "Restart after saving to apply the language. Restarting disconnects the core."), Muted); languageHint.MaximumSize = new Size(650, 0); languageHint.Margin = new Padding(0, 12, 0, 0); languageLayout.Controls.Add(languageHint, 0, 2); languageLayout.SetColumnSpan(languageHint, 2);
            stack.Controls.Add(languageCard, 0, 0);

            TableLayoutPanel networkLayout;
            SoftPanel networkCard = SettingsSection(Localization.T("本地连接", "Local connection"), out networkLayout);
            Label mixedLabel = AutoLabel(Localization.T("HTTP / SOCKS 混合端口", "HTTP / SOCKS port"), Muted); mixedLabel.Margin = new Padding(0, 4, 0, 0); networkLayout.Controls.Add(mixedLabel, 0, 1);
            mixedPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 7890, MinimumSize = new Size(134, 0), Width = 134, Margin = new Padding(0, 0, 0, 14), AccessibleName = Localization.T("HTTP / SOCKS 混合端口", "HTTP / SOCKS port") }; networkLayout.Controls.Add(mixedPort, 1, 1);
            Label apiLabel = AutoLabel(Localization.T("本地控制端口", "Local controller port"), Muted); apiLabel.Margin = new Padding(0, 4, 0, 0); networkLayout.Controls.Add(apiLabel, 0, 2);
            controllerPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 19090, MinimumSize = new Size(134, 0), Width = 134, Margin = new Padding(0, 0, 0, 14), AccessibleName = Localization.T("本地控制端口", "Local controller port") }; networkLayout.Controls.Add(controllerPort, 1, 2);
            Button save = MakeButton(Localization.T("应用端口设置", "Apply ports"), true, 134); save.MinimumSize = new Size(134, 36); save.Click += async delegate { await ApplyPortsAsync(); }; save.Margin = Padding.Empty; networkLayout.Controls.Add(save, 1, 3);
            settingsHint = AutoLabel(Localization.T("端口仅监听本机。连接中应用会重启内核。", "Ports listen on this device only. Applying changes restarts a running core."), Muted); settingsHint.MaximumSize = new Size(650, 0); settingsHint.Margin = new Padding(0, 12, 0, 0); networkLayout.Controls.Add(settingsHint, 0, 4); networkLayout.SetColumnSpan(settingsHint, 2);
            stack.Controls.Add(networkCard, 0, 1);

            TableLayoutPanel aboutLayout;
            SoftPanel aboutCard = SettingsSection(Localization.T("内核与应用", "Core and app"), out aboutLayout);
            aboutLayout.Controls.Add(AutoLabel(Localization.T("Mihomo 内核", "Mihomo core"), Muted), 0, 1);
            engineVersion = AutoLabel(Localization.T("连接后显示运行版本", "Connect to see the running version"), Ink); engineVersion.Margin = new Padding(0, 0, 0, 13); aboutLayout.Controls.Add(engineVersion, 1, 1);
            Label dataLabel = AutoLabel(Localization.T("数据位置", "App data"), Muted); dataLabel.Margin = new Padding(0, 8, 0, 0); aboutLayout.Controls.Add(dataLabel, 0, 2);
            Button openFolder = MakeButton(Localization.T("打开数据文件夹", "Open data folder"), false, 150); openFolder.MinimumSize = new Size(150, 36); openFolder.Margin = new Padding(0, 0, 0, 16); openFolder.Click += delegate { try { controller.OpenDataFolder(); } catch (Exception ex) { ReportError(Localization.T("打开数据文件夹", "Open data folder"), ex); } }; aboutLayout.Controls.Add(openFolder, 1, 2);
            Label compatibility = AutoLabel(Localization.T("界面：.NET Framework 4.8 / Windows Forms\r\n协议支持由 Mihomo 内核决定。Windows 7 的依赖与兼容层安装见随包说明。", "Interface: .NET Framework 4.8 / Windows Forms\r\nProtocol support depends on Mihomo. See the bundled guide for Windows 7 requirements."), Muted); compatibility.MaximumSize = new Size(650, 0); compatibility.Margin = new Padding(0, 0, 0, 14); aboutLayout.Controls.Add(compatibility, 0, 3); aboutLayout.SetColumnSpan(compatibility, 2);
            FlowLayoutPanel links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
            links.Controls.Add(MakeLink(Localization.T("Mihomo 项目", "Mihomo project"), "https://github.com/MetaCubeX/mihomo")); links.Controls.Add(MakeLink(Localization.T("配置文档", "Configuration docs"), "https://wiki.metacubex.one/"));
            Button licenses = MakeButton(Localization.T("开源许可", "Licenses"), false, 95); licenses.Click += delegate { OpenLocalDocumentation("licenses"); }; links.Controls.Add(licenses);
            Button docs = MakeButton(Localization.T("使用说明", "User guide"), false, 95); docs.Click += delegate { OpenLocalDocumentation(Localization.Language == "en" ? "README.en.md" : "README.md"); }; links.Controls.Add(docs); aboutLayout.Controls.Add(links, 0, 4); aboutLayout.SetColumnSpan(links, 2);
            Label trayNote = AutoLabel(Localization.T("关闭窗口会保留在通知区域。彻底退出请右键托盘图标，选择“退出并断开连接”。", "Closing the window keeps the app in the notification area. To quit, right-click its icon and choose “Disconnect and quit”."), Muted); trayNote.MaximumSize = new Size(650, 0); trayNote.Margin = new Padding(0, 14, 0, 0); aboutLayout.Controls.Add(trayNote, 0, 5); aboutLayout.SetColumnSpan(trayNote, 2);
            stack.Controls.Add(aboutCard, 0, 2);
        }

        private SoftPanel SettingsSection(string title, out TableLayoutPanel layout)
        {
            SoftPanel surface = new SoftPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.White, CornerRadius = 14, EdgeColor = Line, Padding = new Padding(20, 17, 20, 19), Margin = new Padding(0, 0, 0, 14) };
            layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Label heading = LabelOf(title, 12F, FontStyle.Bold, Ink); heading.AutoSize = true; heading.Margin = new Padding(0, 0, 0, 16); layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 2);
            surface.Controls.Add(layout); return surface;
        }

        private LinkLabel MakeLink(string text, string url)
        {
            LinkLabel link = new LinkLabel { Text = text, AutoSize = true, LinkColor = Accent, ActiveLinkColor = Accent, VisitedLinkColor = Accent, Margin = new Padding(0, 8, 18, 0), LinkBehavior = LinkBehavior.HoverUnderline };
            link.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { ReportError(Localization.T("打开链接", "Open link"), ex); } };
            return link;
        }

        private void BuildLogs()
        {
            Panel page = NewPage("Logs");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            page.Controls.Add(layout);
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            Button clear = MakeButton(Localization.T("清空显示", "Clear display"), false, 104); clear.Click += delegate { logBox.Clear(); };
            Button copy = MakeButton(Localization.T("复制日志", "Copy logs"), false, 104); copy.Click += delegate { try { if (logBox.TextLength > 0) Clipboard.SetText(logBox.Text); statusText.Text = Localization.T("日志已复制；分享前请检查地址和凭据", "Logs copied. Check addresses and credentials before sharing."); } catch (Exception ex) { ReportError(Localization.T("复制日志", "Copy logs"), ex); } };
            toolbar.Controls.Add(clear); toolbar.Controls.Add(copy); layout.Controls.Add(toolbar, 0, 0);
            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None, BackColor = SidebarColor, ForeColor = Color.FromArgb(226, 235, 248), Font = new Font("Consolas", 9F), Margin = Padding.Empty, AccessibleName = Localization.T("运行日志", "Runtime logs") };
            SoftPanel console = new SoftPanel { Dock = DockStyle.Fill, BackColor = SidebarColor, CornerRadius = 14, Padding = new Padding(18), Margin = Padding.Empty };
            console.Controls.Add(logBox); layout.Controls.Add(console, 0, 1);
            Label hint = AutoLabel(Localization.T("日志可能包含访问域名。分享前请检查内容。", "Logs may contain visited domains. Check their contents before sharing."), Muted); hint.Dock = DockStyle.Fill; hint.Margin = new Padding(0, 12, 0, 0); layout.Controls.Add(hint, 0, 2);
        }

        private async Task ToggleConnectionAsync()
        {
            if (busy) return;
            if (!controller.IsRunning && String.IsNullOrEmpty(controller.Settings.SelectedProfileId))
            {
                RestoreWindow(); ShowPageForCapture("Profiles"); statusText.Text = Localization.T("请先导入并选择一份配置", "Import and select a profile first"); return;
            }
            if (!controller.IsRunning && controller.Settings.TunEnabled && !controller.IsAdmin)
            {
                await OfferElevationAsync(); return;
            }
            await RunActionAsync(controller.IsRunning ? Localization.T("断开连接", "Disconnect") : Localization.T("启动内核", "Start core"), async delegate { if (controller.IsRunning) await controller.StopAsync(); else { await controller.StartAsync(); await RefreshGroupsAsync(); } });
        }

        private async Task RunActionAsync(string description, Func<Task> action)
        {
            if (busy || exitRequested) return;
            busy = true; busyText.Text = description + "…"; UseWaitCursor = true; SyncState();
            try { await action(); }
            catch (Exception ex) { ReportError(description, ex); }
            finally { busy = false; UseWaitCursor = false; busyText.Text = ""; SyncState(); }
        }

        private void ReportError(string action, Exception error)
        {
            string message = AppController.Redact(error.Message);
            statusText.Text = action + Localization.T("失败：", " failed: ") + message;
            AppendLog(action + Localization.T("失败：", " failed: ") + message);
            RestoreWindow();
            MessageBox.Show(this, action + Localization.T("失败。\r\n\r\n", " failed.\r\n\r\n") + message + Localization.T("\r\n\r\n可在“日志”页查看详细记录。", "\r\n\r\nSee the Logs page for details."), "cute clash", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnControllerStateChanged()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(SyncState)); } catch (InvalidOperationException) { } }
            else SyncState();
        }

        private void OnLog(string line)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(AppendLog), line); } catch (InvalidOperationException) { } }
            else AppendLog(line);
        }

        private void AppendLog(string line)
        {
            if (logBox == null || logBox.IsDisposed) return;
            if (logBox.TextLength > 250000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 150000);
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
        }

        private void SyncState()
        {
            if (IsDisposed || Disposing) return;
            syncing = true;
            try
            {
                AppSettings settings = controller.Settings;
                bool running = controller.IsRunning;
                ProfileInfo selected = settings.Profiles.Find(delegate(ProfileInfo profile) { return profile.Id == settings.SelectedProfileId; });
                sidebarState.Text = running ? Localization.T("已连接", "Connected") : Localization.T("未连接", "Disconnected");
                sidebarState.ForeColor = Color.White;
                connectionTitle.Text = running ? Localization.T("代理内核运行中", "Proxy core is running") : Localization.T("尚未连接", "Disconnected");
                connectionTitle.ForeColor = Color.White;
                connectionHint.Text = running ? (settings.TunEnabled ? Localization.T("TUN 已启用，流量按当前路由模式处理。", "TUN is enabled. Traffic follows the current routing mode.") : settings.SystemProxyEnabled ? Localization.T("系统代理已启用，使用系统代理的应用可通过内核连接。", "System proxy is enabled. Apps that use it can connect through the core.") : Localization.T("内核已启动。启用系统代理，或让应用使用下方本地代理地址。", "The core is running. Enable system proxy or use the local proxy address below.")) : selected == null ? Localization.T("添加 Clash 配置，选择路由方式后开始连接。", "Add a Clash profile, choose routing options, then connect.") : Localization.T("当前配置已就绪。点击“开始连接”启动代理内核。", "Your profile is ready. Click Connect to start the proxy core.");
                overviewProfile.Text = selected == null ? Localization.T("尚未添加配置", "No profile added") : selected.Name;
                overviewEndpoint.Text = Localization.T("本地代理  127.0.0.1:", "Local proxy  127.0.0.1:") + settings.MixedPort + "     ·     " + (running ? Localization.T("本次运行累计流量", "Traffic during this session") : Localization.T("连接后显示流量", "Traffic appears after connecting"));
                systemProxy.Checked = settings.SystemProxyEnabled;
                tunMode.Checked = settings.TunEnabled;
                modeChoice.SelectedIndex = settings.Mode == "global" ? 1 : settings.Mode == "direct" ? 2 : 0;
                foreach (Button button in actionButtons) button.Enabled = !busy && !exitRequested;
                connectButton.Text = busy ? (running ? Localization.T("正在处理…", "Working…") : Localization.T("请稍候…", "Please wait…")) : running ? Localization.T("断开连接", "Disconnect") : Localization.T("开始连接", "Connect");
                overviewConnect.Text = connectButton.Text; overviewConnect.Enabled = connectButton.Enabled;
                systemProxy.Enabled = !busy; tunMode.Enabled = !busy; modeChoice.Enabled = !busy;
                profileList.Enabled = !busy; groupList.Enabled = !busy; nodeList.Enabled = !busy;
                mixedPort.Enabled = !busy; controllerPort.Enabled = !busy; languageChoice.Enabled = !busy;
                if (!mixedPort.Focused) mixedPort.Value = Math.Max(1024, Math.Min(65535, settings.MixedPort));
                if (!controllerPort.Focused) controllerPort.Value = Math.Max(1024, Math.Min(65535, settings.ControllerPort));
                engineVersion.Text = String.IsNullOrEmpty(controller.CoreVersion) ? Localization.T("连接后显示运行版本", "Connect to see the running version") : controller.CoreVersion;
                if (tray != null) tray.Text = "cute clash · " + (running ? Localization.T("已连接", "Connected") : Localization.T("未连接", "Disconnected"));
                if (trayConnect != null) { trayConnect.Text = running ? Localization.T("断开连接", "Disconnect") : Localization.T("开始连接", "Connect"); trayConnect.Enabled = !busy; }
                if (!busy) statusText.Text = running ? Localization.T("运行中 · ", "Running · ") + (selected == null ? "" : selected.Name) : Localization.T("就绪 · ", "Ready · ") + (selected == null ? Localization.T("导入配置后连接", "Import a profile to connect") : Localization.T("已断开连接", "Disconnected"));
                if (!running) { overviewUpload.Text = "—"; overviewDownload.Text = "—"; overviewConnections.Text = "—"; groups = new List<ProxyGroup>(); groupList.Items.Clear(); nodeList.Items.Clear(); groupHeading.Text = Localization.T("连接内核后读取配置中的代理组", "Connect to load proxy groups from your profile"); }
                UpdateProfileList();
                UpdateProxyHint();
                UpdateSelectionActions();
            }
            finally { syncing = false; }
        }

        private void UpdateProfileList()
        {
            string selectedId = profileList.SelectedItems.Count == 0 ? controller.Settings.SelectedProfileId : profileList.SelectedItems[0].Tag as string;
            profileList.BeginUpdate();
            try
            {
                profileList.Items.Clear();
                foreach (ProfileInfo profile in controller.Settings.Profiles)
                {
                    ListViewItem item = new ListViewItem(profile.Name) { Tag = profile.Id };
                    item.SubItems.Add(String.IsNullOrEmpty(profile.SourceUrl) ? Localization.T("本地文件", "Local file") : Localization.T("订阅", "Subscription"));
                    item.SubItems.Add(profile.UpdatedAt == DateTime.MinValue ? "—" : profile.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    bool active = profile.Id == controller.Settings.SelectedProfileId;
                    item.SubItems.Add(active ? Localization.T("使用中", "In use") : "");
                    if (active) { item.ForeColor = Accent; item.Font = new Font(Font, FontStyle.Bold); }
                    profileList.Items.Add(item);
                    item.Selected = profile.Id == selectedId;
                }
            }
            finally { profileList.EndUpdate(); }
            int count = controller.Settings.Profiles.Count;
            profileEmpty.Visible = count == 0; profileList.Visible = count > 0;
            profileHint.Text = count == 0 ? Localization.T("尚无配置。导入本地 YAML，或添加返回 Clash / Mihomo YAML 的订阅地址。", "No profiles yet. Import YAML or add a Clash / Mihomo YAML subscription URL.") : Localization.Format("{0} 份配置 · 双击使用。修改运行中的配置会重新启动内核。", count == 1 ? "{0} profile · Double-click to use. Changing the active profile restarts the core." : "{0} profiles · Double-click to use. Changing the active profile restarts the core.", count);
            SizeProfileColumns();
        }

        private void SizeProfileColumns()
        {
            if (profileList == null || profileList.Columns.Count != 4) return;
            // Native list-view columns do not follow WinForms DPI autoscaling.
            // Measure the live font so dates and translated headers stay readable.
            using (Font bold = new Font(profileList.Font, FontStyle.Bold))
            {
                int source = TextRenderer.MeasureText(Localization.T("本地文件", "Subscription"), bold).Width + 24;
                int updated = TextRenderer.MeasureText("2026-09-22 20:00", bold).Width + 24;
                int status = Math.Max(TextRenderer.MeasureText(profileList.Columns[3].Text, bold).Width, TextRenderer.MeasureText(Localization.T("使用中", "In use"), bold).Width) + 24;
                profileList.Columns[0].Width = Math.Max(140, profileList.ClientSize.Width - source - updated - status - 5);
                profileList.Columns[1].Width = source;
                profileList.Columns[2].Width = updated;
                profileList.Columns[3].Width = status;
            }
        }

        private void SizeNodeColumns()
        {
            if (nodeList == null || nodeList.Columns.Count != 2) return;
            using (Font bold = new Font(nodeList.Font, FontStyle.Bold))
            {
                int status = Math.Max(TextRenderer.MeasureText(nodeList.Columns[1].Text, bold).Width, TextRenderer.MeasureText(Localization.T("当前", "Current") + " · 9999 ms", bold).Width) + 24;
                nodeList.Columns[0].Width = Math.Max(140, nodeList.ClientSize.Width - status - 5);
                nodeList.Columns[1].Width = status;
            }
        }

        private string SelectedProfileId()
        {
            if (profileList.SelectedItems.Count > 0) return profileList.SelectedItems[0].Tag as string;
            statusText.Text = Localization.T("请先在列表中选择一份配置", "Select a profile from the list first"); return null;
        }

        private async Task UseSelectedProfileAsync()
        {
            string id = SelectedProfileId();
            if (id != null) await RunActionAsync(Localization.T("切换配置", "Switch profile"), async delegate { await controller.SelectProfileAsync(id); if (controller.IsRunning) await RefreshGroupsAsync(); });
        }

        private async Task RefreshGroupsAsync()
        {
            if (!controller.IsRunning) { statusText.Text = Localization.T("请先连接内核，再读取代理组", "Connect the core first to load proxy groups"); return; }
            ProxyGroup previous = groupList.SelectedItem as ProxyGroup;
            string previousName = previous == null ? null : previous.Name;
            IList<ProxyGroup> next = await controller.GetGroupsAsync();
            groups = next;
            groupList.BeginUpdate();
            try
            {
                groupList.Items.Clear();
                foreach (ProxyGroup group in groups) groupList.Items.Add(group);
                if (groupList.Items.Count > 0)
                {
                    int selectedIndex = 0;
                    for (int i = 0; i < groups.Count; i++) if (groups[i].Name == previousName) selectedIndex = i;
                    groupList.SelectedIndex = selectedIndex;
                }
                else FillNodes();
            }
            finally { groupList.EndUpdate(); }
            groupHeading.Text = groups.Count == 0 ? Localization.T("当前配置没有代理组", "This profile has no proxy groups") : Localization.Format("{0} 个代理组", groups.Count == 1 ? "{0} proxy group" : "{0} proxy groups", groups.Count);
            UpdateProxyHint();
        }

        private void FillNodes()
        {
            string selectedName = nodeList.SelectedItems.Count == 0 ? null : nodeList.SelectedItems[0].Tag as string;
            ProxyGroup group = groupList.SelectedItem as ProxyGroup;
            nodeList.BeginUpdate();
            try
            {
                nodeList.Items.Clear();
                if (group != null && group.Nodes != null)
                {
                    foreach (string node in group.Nodes)
                    {
                        ListViewItem item = new ListViewItem(node) { Tag = node };
                        int delay;
                        string latency = delays.TryGetValue(node, out delay) ? (delay <= 0 ? Localization.T("不可达", "Unreachable") : delay + " ms") : "";
                        item.SubItems.Add((node == group.Current ? Localization.T("当前", "Current") : "") + (node == group.Current && latency.Length > 0 ? " · " : "") + latency);
                        if (node == group.Current) { item.ForeColor = Accent; item.Font = new Font(Font, FontStyle.Bold); }
                        nodeList.Items.Add(item);
                        item.Selected = node == (selectedName ?? group.Current);
                    }
                }
            }
            finally { nodeList.EndUpdate(); }
            UpdateProxyHint();
        }

        private void UpdateProxyHint()
        {
            if (proxyHint == null || selectProxyButton == null) return;
            ProxyGroup group = groupList.SelectedItem as ProxyGroup;
            bool canSelect = group != null && String.Equals(group.Type, "Selector", StringComparison.OrdinalIgnoreCase);
            selectProxyButton.Enabled = controller.IsRunning && !busy && canSelect && nodeList.SelectedItems.Count > 0;
            proxyHint.Text = !controller.IsRunning ? Localization.T("先连接内核，再选择代理组。自动策略组由内核管理。", "Connect, then choose a proxy group. The core manages automatic groups.") : group == null ? Localization.T("配置中没有可显示的代理组。可在“配置”页检查文件。", "No proxy groups to display. Check the file on the Profiles page.") : canSelect ? Localization.T("双击节点或点击“使用选中节点”切换。延迟测试会连接测试地址。", "Double-click a node or click Use selected. A latency test connects to the test URL.") : Localization.T("此组为 ", "This group uses ") + GroupTypeLabel(group.Type) + Localization.T(" 策略，由内核自动选择；仍可测试节点延迟。", " selection, managed by the core. You can still test node latency.");
        }

        private void UpdateSelectionActions()
        {
            if (profileList == null || useProfileButton == null) return;
            string selectedId = profileList.SelectedItems.Count == 0 ? null : profileList.SelectedItems[0].Tag as string;
            ProfileInfo profile = controller.Settings.Profiles.Find(delegate(ProfileInfo item) { return item.Id == selectedId; });
            bool available = !busy && !exitRequested && profile != null;
            useProfileButton.Enabled = available;
            validateProfileButton.Enabled = available;
            deleteProfileButton.Enabled = available;
            updateProfileButton.Enabled = available && !String.IsNullOrEmpty(profile.SourceUrl);
            if (refreshGroupsButton != null) refreshGroupsButton.Enabled = !busy && !exitRequested && controller.IsRunning;
            if (testProxyButton != null) testProxyButton.Enabled = !busy && !exitRequested && controller.IsRunning && nodeList.SelectedItems.Count > 0;
            UpdateProxyHint();
        }

        private async Task SelectNodeAsync()
        {
            ProxyGroup group = groupList.SelectedItem as ProxyGroup;
            if (group == null || nodeList.SelectedItems.Count == 0) { statusText.Text = Localization.T("请先选择一个代理组和节点", "Select a proxy group and node first"); return; }
            if (!String.Equals(group.Type, "Selector", StringComparison.OrdinalIgnoreCase)) { statusText.Text = Localization.T("自动策略组由内核选择节点", "The core selects nodes in automatic groups"); return; }
            string node = nodeList.SelectedItems[0].Tag as string;
            await RunActionAsync(Localization.T("切换代理节点", "Switch proxy node"), async delegate { await controller.SelectProxyAsync(group.Name, node); await RefreshGroupsAsync(); });
        }

        private async Task TestNodeAsync()
        {
            if (nodeList.SelectedItems.Count == 0) { statusText.Text = Localization.T("请先选择要测速的节点", "Select a node to test first"); return; }
            string node = nodeList.SelectedItems[0].Tag as string;
            await RunActionAsync(Localization.T("测试节点延迟", "Test node latency"), async delegate { int value = await controller.TestDelayAsync(node); delays[node] = value; FillNodes(); });
        }

        private async Task ChangeTunAsync()
        {
            bool requested = tunMode.Checked;
            if (requested && !controller.IsAdmin)
            {
                syncing = true; tunMode.Checked = controller.Settings.TunEnabled; syncing = false;
                await OfferElevationAsync(); return;
            }
            await RunActionAsync(Localization.T("切换 TUN 模式", "Change TUN mode"), async delegate { await controller.SetTunAsync(requested); if (controller.IsRunning) await RefreshGroupsAsync(); });
        }

        private async Task OfferElevationAsync()
        {
            if (busy) return;
            RestoreWindow();
            if (MessageBox.Show(this, Localization.T("TUN 模式需要管理员权限来创建虚拟网卡。\r\n\r\n是否断开当前连接，并以管理员身份重新打开 cute clash？重新打开后可启用 TUN 并连接。", "TUN mode needs administrator rights to create a virtual adapter.\r\n\r\nDisconnect and reopen cute clash as administrator? You can then enable TUN and connect."), Localization.T("以管理员身份打开", "Run as administrator"), MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { SyncState(); return; }
            await RunActionAsync(Localization.T("重新打开应用", "Restart app"), async delegate
            {
                await controller.StopAsync();
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory, Arguments = "--wait-parent " + Process.GetCurrentProcess().Id });
                exitRequested = true;
                tray.Visible = false;
                Close();
            });
        }

        private static string GroupTypeLabel(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "selector": return Localization.T("手动选择", "manual");
                case "urltest": case "url-test": return Localization.T("延迟优选", "latency-based");
                case "fallback": return Localization.T("故障转移", "fallback");
                case "loadbalance": case "load-balance": return Localization.T("负载均衡", "load-balanced");
                case "relay": return Localization.T("中继", "relay");
                default: return type;
            }
        }

        private async Task ApplyLanguageAsync()
        {
            if (busy || syncing || exitRequested || languageChoice.SelectedIndex < 0) return;
            string requested = languageChoice.SelectedIndex == 1 ? "en" : "zh-CN";
            string previous = controller.Settings.Language;
            try
            {
                controller.Settings.Language = requested;
                controller.SaveSettings();
            }
            catch (Exception ex)
            {
                controller.Settings.Language = previous;
                ReportError(Localization.T("保存语言设置", "Save language"), ex);
                return;
            }
            if (requested == Localization.Language)
            {
                languageHint.Text = Localization.T("语言设置已保存。", "Language preference saved.");
                return;
            }
            languageHint.Text = Localization.T("语言设置已保存，下次启动应用时生效。", "Language preference saved. It applies the next time the app starts.");
            if (MessageBox.Show(this,
                Localization.T("语言设置已保存。是否现在重启 cute clash？\r\n\r\n当前连接将断开。重启后请手动连接。", "Language preference saved. Restart cute clash now?\r\n\r\nThe current connection will stop. Connect manually after restarting."),
                Localization.T("应用语言", "Apply language"), MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await RunActionAsync(Localization.T("重启应用", "Restart app"), async delegate
            {
                await controller.StopAsync();
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory, Arguments = "--wait-parent " + Process.GetCurrentProcess().Id });
                exitRequested = true;
                tray.Visible = false;
                Close();
            });
        }

        private async Task ApplyPortsAsync()
        {
            int mixed = (int)mixedPort.Value;
            int api = (int)controllerPort.Value;
            if (mixed == api) { MessageBox.Show(this, Localization.T("混合端口与控制端口不能相同。请修改其中一个端口。", "The mixed port and controller port must differ. Change one of them."), Localization.T("端口设置", "Port settings"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            await RunActionAsync(Localization.T("应用端口设置", "Apply ports"), async delegate
            {
                bool wasRunning = controller.IsRunning;
                if (wasRunning) await controller.StopAsync();
                controller.Settings.MixedPort = mixed;
                controller.Settings.ControllerPort = api;
                controller.SaveSettings();
                if (wasRunning) { await controller.StartAsync(); await RefreshGroupsAsync(); }
                settingsHint.Text = Localization.T("端口设置已保存。", "Port settings saved. ") + (wasRunning ? Localization.T("内核已使用新端口重新启动。", "The core restarted with the new ports.") : Localization.T("下次连接时生效。", "They will apply the next time you connect."));
            });
        }

        private async Task RefreshSnapshotAsync()
        {
            if (snapshotBusy || busy || exitRequested || !controller.IsRunning) return;
            snapshotBusy = true;
            try
            {
                RuntimeSnapshot snapshot = await controller.GetSnapshotAsync();
                if (!controller.IsRunning || IsDisposed || Disposing || exitRequested) return;
                overviewUpload.Text = FormatBytes(snapshot.UploadTotal);
                overviewDownload.Text = FormatBytes(snapshot.DownloadTotal);
                overviewConnections.Text = snapshot.Connections.ToString();
                if (!String.IsNullOrEmpty(snapshot.Version)) engineVersion.Text = snapshot.Version;
                refreshTick++;
                if (activePage == "Proxies" && refreshTick % 5 == 0 && !busy) await RefreshGroupsAsync();
            }
            catch (Exception ex) { if (!exitRequested) statusText.Text = Localization.T("状态暂不可用：", "Status temporarily unavailable: ") + AppController.Redact(ex.Message); }
            finally { snapshotBusy = false; }
        }

        private static string FormatBytes(long bytes)
        {
            double value = Math.Max(0, bytes); string[] units = { "B", "KB", "MB", "GB", "TB" }; int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }

        private void HideToTray()
        {
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, Localization.T("cute clash 已收至通知区域", "cute clash is in the notification area"), Localization.T("双击图标恢复窗口，右键可断开连接并退出。", "Double-click the icon to restore the window. Right-click to disconnect and quit."), ToolTipIcon.Info);
            }
        }

        private void RestoreWindow()
        {
            if (IsDisposed || Disposing) return;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (exitRequested) return;
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing || e.CloseReason == CloseReason.ApplicationExitCall)
            {
                // The controller's final Dispose path restores any owned proxy settings.
                exitRequested = true; tray.Visible = false; return;
            }
            e.Cancel = true;
            HideToTray();
        }

        public async Task ExitAsync()
        {
            if (closeInProgress || exitRequested) return;
            if (busy) { RestoreWindow(); statusText.Text = Localization.T("操作正在完成，请稍后再退出", "An operation is finishing. Please wait before quitting."); return; }
            closeInProgress = true; busy = true; busyText.Text = Localization.T("正在断开并退出…", "Disconnecting and quitting…"); SyncState();
            try { await controller.StopAsync(); }
            catch (Exception ex) { AppendLog(Localization.T("退出时清理：", "Cleanup on exit: ") + AppController.Redact(ex.Message)); }
            finally { exitRequested = true; tray.Visible = false; Close(); }
        }

        private void OpenLocalDocumentation(string relativePath)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                if (!Directory.Exists(path) && !File.Exists(path)) throw new FileNotFoundException(Localization.T("未找到随包文件：", "Bundled file not found: ") + relativePath);
                if (String.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { ReportError(Localization.T("打开说明", "Open guide"), ex); }
        }

        private static Icon MakeIcon()
        {
            return BrandIcon.Load();
        }

        private static Bitmap BrandBitmap()
        {
            using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("CuteClash.AppIcon"))
            {
                if (stream == null) using (Icon icon = BrandIcon.Load()) return icon.ToBitmap();
                using (Icon icon = new Icon(stream, 128, 128)) return icon.ToBitmap();
            }
        }

        private sealed class SubscriptionDialog : Form
        {
            private readonly TextBox nameBox;
            private readonly TextBox urlBox;
            public string ProfileName { get { return nameBox.Text.Trim(); } }
            public string SubscriptionUrl { get { return urlBox.Text.Trim(); } }

            public SubscriptionDialog(Font parentFont)
            {
                SuspendLayout();
                Text = Localization.T("添加订阅", "Add subscription"); Font = parentFont; ForeColor = Ink; BackColor = Color.White;
                FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(500, 278);
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24), Margin = Padding.Empty };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                Controls.Add(layout);
                layout.Controls.Add(AutoLabel(Localization.T("配置名称", "Profile name"), Ink), 0, 0);
                nameBox = new TextBox { Dock = DockStyle.Top, MaxLength = 120, AccessibleName = Localization.T("配置名称", "Profile name"), Margin = Padding.Empty }; layout.Controls.Add(nameBox, 0, 1);
                layout.Controls.Add(AutoLabel(Localization.T("订阅地址", "Subscription URL"), Ink), 0, 2);
                urlBox = new TextBox { Dock = DockStyle.Top, MaxLength = 8192, AccessibleName = Localization.T("订阅地址", "Subscription URL"), Margin = Padding.Empty, UseSystemPasswordChar = true }; layout.Controls.Add(urlBox, 0, 3);
                Label note = AutoLabel(Localization.T("填写返回 Clash / Mihomo YAML 的 HTTP(S) 地址。\r\n地址会保存到本机，请勿与他人分享。", "Enter an HTTP(S) URL that returns Clash / Mihomo YAML.\r\nThe URL is saved on this device. Keep it private."), Muted); note.Dock = DockStyle.Fill; layout.Controls.Add(note, 0, 4);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty, WrapContents = false };
                Button add = new Button { Text = Localization.T("添加", "Add"), Width = 92, Height = 33, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0) }; add.FlatAppearance.BorderSize = 0;
                Button cancel = new Button { Text = Localization.T("取消", "Cancel"), Width = 92, Height = 33, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.System, Margin = Padding.Empty };
                add.Click += delegate
                {
                    Uri uri;
                    if (ProfileName.Length == 0) { MessageBox.Show(this, Localization.T("请为配置填写一个名称。", "Enter a name for this profile."), Localization.T("添加订阅", "Add subscription"), MessageBoxButtons.OK, MessageBoxIcon.Information); nameBox.Focus(); return; }
                    if (!Uri.TryCreate(SubscriptionUrl, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) { MessageBox.Show(this, Localization.T("请填写以 https:// 或 http:// 开头的有效订阅地址。", "Enter a valid subscription URL starting with https:// or http://."), Localization.T("添加订阅", "Add subscription"), MessageBoxButtons.OK, MessageBoxIcon.Information); urlBox.Focus(); return; }
                    DialogResult = DialogResult.OK; Close();
                };
                actions.Controls.Add(add); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 5);
                AcceptButton = add; CancelButton = cancel;
                AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
                ResumeLayout(true);
            }
        }
    }
}
