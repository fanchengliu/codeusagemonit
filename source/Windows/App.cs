using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: AssemblyTitle("codeusagemonit")]
[assembly: AssemblyDescription("A single-tray Windows usage monitor, adapted from CodexBar")]
[assembly: AssemblyVersion("0.7.0.0")]
namespace CodeUsageMonit {
    public static class Program {
        private static Mutex mutex;
        private static EventWaitHandle showEvent;
        private static EventWaitHandle quitEvent;
        [STAThread] public static int Main(string[] args) {
            if (args.Contains("--demo") && args.Contains("--probe")) return 2;
            string instance = @"Local\codeusagemonit" + (args.Contains("--demo") ? ".Preview" : "");
            if (args.Contains("--quit")) { try { using (var signal = EventWaitHandle.OpenExisting(instance + ".Quit")) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { } return 0; }
            if (args.Contains("--self-test")) return SelfTests.Run();
            if (args.Contains("--probe")) return Probe().GetAwaiter().GetResult();
            if (args.Contains("--demo")) Store.EnableDemoMode();
            bool created; mutex = new Mutex(true, instance + ".Instance", out created);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instance + ".Show");
            if (!created) { showEvent.Set(); showEvent.Dispose(); mutex.Dispose(); return 0; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                try { File.AppendAllText(System.IO.Path.Combine(Store.Data, "errors.log"), DateTime.UtcNow.ToString("o") + " " + e.Exception.GetType().Name + " " + e.Exception.Message + "\n"); } catch { }
                MessageBox.Show("窗口遇到一个错误。请退出后重新打开；已保存的数据不受影响。", "codeusagemonit"); e.Handled = true;
            };
            var ui = new MonitorPanel(app, args.Contains("--demo"));
            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instance + ".Quit");
            RegisteredWaitHandle listener = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { app.Dispatcher.BeginInvoke(new Action(ui.Reveal)); }, null, Timeout.Infinite, false);
            RegisteredWaitHandle quitListener = ThreadPool.RegisterWaitForSingleObject(quitEvent, delegate { app.Dispatcher.BeginInvoke(new Action(ui.Quit)); }, null, Timeout.Infinite, false);
            app.Dispatcher.BeginInvoke(new Action(() => ui.Start(args.Contains("--background"))), DispatcherPriority.ApplicationIdle);
            app.Exit += delegate { listener.Unregister(null); quitListener.Unregister(null); quitEvent.Dispose(); ui.Dispose(); showEvent.Dispose(); mutex.ReleaseMutex(); mutex.Dispose(); };
            app.Run(); return 0;
        }
        private static async Task<int> Probe() {
            var states = new List<ProviderState>(); using (var service = new ProviderService(Store.Read<AppConfig>("settings.json"))) { foreach (string id in ProviderCatalog.Ids) states.Add(await service.Fetch(id)); }
            // Probe reports contain no tokens, raw API responses, or account identifiers.
            foreach (var s in states) s.Account = "";
            Store.Write("probe-results.json", states); return states.Any(s => s.Status == "ready") ? 0 : 1;
        }
    }
    public sealed partial class MonitorPanel : IDisposable {
        private readonly Application app;
        private readonly bool demo;
        private readonly Window window;
        private readonly WindowFrame frame;
        private readonly StackPanel body;
        private readonly UniformGrid tabs;
        private readonly Grid tabHost, settingsHost;
        private readonly FrameworkElement scaleRoot;
        private readonly HashSet<string> refreshingIds = new HashSet<string>();
        private readonly ScrollViewer bodyScroll;
        private readonly TextBlock status, titleText, refreshGlyph;
        private readonly Ellipse statusDot;
        private readonly FrameworkElement logo;
        private readonly Button refreshButton, pinButton, settingsButton, backButton;
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer refreshTimer, clockTimer;
        private AppConfig config;
        private Dictionary<string, ProviderState> states = new Dictionary<string, ProviderState>();
        private UsageHistory history;
        private LogIndex codexLogs, claudeLogs;
        private EndpointLog endpointLog;
        private ThirdPartySummary thirdParty = new ThirdPartySummary();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private DispatcherTimer endpointTimer;
        private string selected = "overview", statusNote = "";
        private bool refreshing, scanning, quitting;
        private DateTime lastRefresh = DateTime.MinValue, lastDeactivated = DateTime.MinValue, lastAutoHide = DateTime.MinValue;
        private readonly Dictionary<string, string> icons = new Dictionary<string, string>();
        public MonitorPanel(Application application, bool demoMode) {
            app = application; demo = demoMode; Directory.CreateDirectory(Store.Data); config = Store.Read<AppConfig>("settings.json");
            WindowFrame.Normalize(config);
            config.RefreshMinutes = Math.Max(1, Math.Min(60, config.RefreshMinutes)); if (config.Enabled == null || config.Enabled.Length == 0) config.Enabled = ProviderCatalog.DefaultEnabled;
            if (!demo) RegisterCustomProviders(CustomProviders.Load());
            using (var reader = XmlReader.Create(System.IO.Path.Combine(Store.Root, "Panel.xaml"))) window = (Window)XamlReader.Load(reader);
            // Tooltips live in popups; register the dark tooltip style application-wide too.
            foreach (object key in new object[] { typeof(ToolTip), typeof(ContextMenu), typeof(MenuItem), MenuItem.SeparatorStyleKey }) { object style = window.Resources[key]; if (style != null) app.Resources[key] = style; }
            frame = new WindowFrame(window, config, SaveConfig);
            body = (StackPanel)window.FindName("Body"); tabs = (UniformGrid)window.FindName("Tabs"); tabHost = (Grid)window.FindName("TabHost"); settingsHost = (Grid)window.FindName("SettingsHost");
            bodyScroll = (ScrollViewer)window.FindName("BodyScroll"); status = (TextBlock)window.FindName("StatusText"); statusDot = (Ellipse)window.FindName("StatusDot");
            titleText = (TextBlock)window.FindName("TitleText"); refreshGlyph = (TextBlock)window.FindName("RefreshGlyph"); logo = (FrameworkElement)window.FindName("Logo");
            scaleRoot = (FrameworkElement)window.FindName("ScaleRoot"); compactRoot = (Border)window.FindName("CompactRoot");
            refreshButton = (Button)window.FindName("RefreshButton"); pinButton = (Button)window.FindName("PinButton"); settingsButton = (Button)window.FindName("SettingsButton"); backButton = (Button)window.FindName("BackButton");
            foreach (string id in ProviderCatalog.All) states[id] = new ProviderState { Id = id };
            try {
                foreach (var s in Store.Read<List<ProviderState>>("quota-cache.json")) {
                    if (!states.ContainsKey(s.Id)) continue;
                    // Only entries that actually hold data are shown as the last good reading.
                    if (s.Quotas.Count == 0 && s.Balances.Count == 0) continue;
                    s.Stale = true; s.Status = "cached"; s.Message = "上次成功读取的数据"; states[s.Id] = s;
                }
            } catch { }
            history = Store.Read<UsageHistory>("history.json");
            codexLogs = demo ? null : Store.Read<LogIndex>("codex-logs.json");
            claudeLogs = demo ? null : Store.Read<LogIndex>("claude-logs.json");
            endpointLog = demo ? new EndpointLog() : Store.Read<EndpointLog>("endpoints.json");
            if (demo) SeedDemo(); else BuildThirdParty();
            LoadIcons();
            ((Button)window.FindName("HideButton")).Click += delegate { window.Hide(); };
            settingsButton.Click += delegate { OpenSettings(); };
            backButton.Click += delegate { BackFromSettings(); };
            refreshButton.Click += async delegate { await Refresh(); };
            pinButton.Click += delegate { config.HideOnDeactivate = !config.HideOnDeactivate; SaveConfig(); UpdatePin(); };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!quitting) { e.Cancel = true; window.Hide(); } };
            window.Deactivated += delegate {
                lastDeactivated = DateTime.UtcNow;
                // Never auto-hide while settings are open: the user may be copying an API key.
                if (config.HideOnDeactivate && settingsView == null && !IsCompact) { lastAutoHide = DateTime.UtcNow; window.Hide(); }
            };
            window.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { frame.SetScale(config.UiScale + (e.Delta > 0 ? .05 : -.05), true); UpdateScaleLabel(); e.Handled = true; }
            };
            window.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape) { if (IsCompact && (compactPage != "summary" || config.CompactProvider != "overview")) CompactBack(); else window.Hide(); e.Handled = true; }
                if (e.Key == Key.F5) { if (IsCompact && config.CompactProvider != "overview" && (Keyboard.Modifiers & ModifierKeys.Control) == 0) { var ignored = RefreshOne(config.CompactProvider); } else { var ignored = Refresh(); } e.Handled = true; }
            };
            tray = new Forms.NotifyIcon { Text = "codeusagemonit · 正在读取", Icon = new Drawing.Icon(System.IO.Path.Combine(Store.Root, "app.ico"), Forms.SystemInformation.SmallIconSize), Visible = true };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) app.Dispatcher.BeginInvoke(new Action(ToggleFromTray)); };
            tray.ContextMenuStrip = TrayMenu();
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(config.RefreshMinutes) }; refreshTimer.Tick += async delegate { await Refresh(); };
            clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) }; clockTimer.Tick += delegate { if (window.IsVisible && !refreshing) Render(); };
            if (demo) ((FrameworkElement)window.FindName("DemoBadge")).Visibility = Visibility.Visible;
            // Unhandled background clicks drag; buttons, inputs, and scrollbars keep their own input.
            compactRoot.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) { try { window.DragMove(); } catch (InvalidOperationException) { } } };
            window.ContextMenu = SizeMenu();
            UpdatePin();
            if (config.DisplaySize != "full") ApplyDisplaySize(config.DisplaySize); else Render();
        }
        private Forms.ContextMenuStrip TrayMenu() {
            var menu = new Forms.ContextMenuStrip { ShowImageMargin = false, Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false }, ForeColor = Drawing.Color.FromArgb(236, 238, 241), Font = new Drawing.Font("Microsoft YaHei UI", 9f), Padding = new Forms.Padding(2, 4, 2, 4) };
            menu.Items.Add("打开面板", null, delegate { app.Dispatcher.BeginInvoke(new Action(Reveal)); });
            menu.Items.Add("刷新全部额度", null, delegate { app.Dispatcher.BeginInvoke(new Action(() => { var ignored = Refresh(); })); });
            menu.Items.Add("设置…", null, delegate { app.Dispatcher.BeginInvoke(new Action(() => { Reveal(); OpenSettings(); })); });
            var sizes = new Forms.ToolStripMenuItem("显示尺寸");
            foreach (string size in DisplaySizes) { string captured = size; var item = new Forms.ToolStripMenuItem(SizeName(size) + (size == "full" ? "面板" : "尺寸")); item.Click += delegate { app.Dispatcher.BeginInvoke(new Action(() => SetDisplaySize(captured))); }; sizes.DropDownItems.Add(item); }
            sizes.DropDownOpening += delegate { foreach (Forms.ToolStripMenuItem item in sizes.DropDownItems) item.Checked = item.Text.StartsWith(SizeName(config.DisplaySize)); };
            var drop = (Forms.ToolStripDropDownMenu)sizes.DropDown; drop.ShowCheckMargin = true; drop.ShowImageMargin = false; drop.Renderer = menu.Renderer; drop.Font = menu.Font;
            foreach (Forms.ToolStripItem item in sizes.DropDownItems) { item.ForeColor = menu.ForeColor; item.Padding = new Forms.Padding(2, 4, 12, 4); }
            menu.Items.Add(sizes);
            menu.Items.Add("重置窗口位置", null, delegate { app.Dispatcher.BeginInvoke(new Action(() => { frame.ResetPosition(); Reveal(); })); });
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出 codeusagemonit", null, delegate { app.Dispatcher.BeginInvoke(new Action(Quit)); });
            foreach (Forms.ToolStripItem item in menu.Items) { item.Padding = new Forms.Padding(6, 5, 16, 5); item.ForeColor = menu.ForeColor; }
            return menu;
        }
        private sealed class DarkMenuColors : Forms.ProfessionalColorTable {
            private static readonly Drawing.Color Back = Drawing.Color.FromArgb(31, 33, 38), Hot = Drawing.Color.FromArgb(50, 54, 62), Edge = Drawing.Color.FromArgb(58, 62, 70);
            public override Drawing.Color ToolStripDropDownBackground { get { return Back; } }
            public override Drawing.Color MenuBorder { get { return Edge; } }
            public override Drawing.Color MenuItemBorder { get { return Hot; } }
            public override Drawing.Color MenuItemSelected { get { return Hot; } }
            public override Drawing.Color MenuItemSelectedGradientBegin { get { return Hot; } }
            public override Drawing.Color MenuItemSelectedGradientEnd { get { return Hot; } }
            public override Drawing.Color MenuItemPressedGradientBegin { get { return Hot; } }
            public override Drawing.Color MenuItemPressedGradientEnd { get { return Hot; } }
            public override Drawing.Color ImageMarginGradientBegin { get { return Back; } }
            public override Drawing.Color ImageMarginGradientMiddle { get { return Back; } }
            public override Drawing.Color ImageMarginGradientEnd { get { return Back; } }
            public override Drawing.Color SeparatorDark { get { return Edge; } }
            public override Drawing.Color SeparatorLight { get { return Back; } }
        }
        // The tray click arrives after the panel already lost focus to the taskbar, so
        // "was it active?" must look at the deactivation that the click itself caused.
        private void ToggleFromTray() {
            DateTime now = DateTime.UtcNow;
            if (window.IsVisible && (window.IsActive || (now - lastDeactivated).TotalMilliseconds < 600)) { window.Hide(); return; }
            if (!window.IsVisible && (now - lastAutoHide).TotalMilliseconds < 600) return;
            Reveal();
        }
        private void SeedDemo() {
            config.Enabled = ProviderCatalog.Ids;
            history = new UsageHistory { Zone = TimeZoneInfo.Local.Id, Updated = DateTime.UtcNow.ToString("o") };
            double[] shape = { .5, .8, 1.1, .3, 0, .9, 1.4, .6, .2, 0, 1.2, 1.8, .7, .4, 1, 1.5, .5, 0, .8, 1.3, 1.9, .6, .3, 1.1, 1.6, .9, .2, 1.2, 1.7, 1.4 };
            for (int i = 0; i < 30; i++) {
                string day = HistoryService.DayKey(DateTime.Today.AddDays(i - 29));
                if (shape[i] > 0) history.Days.Add(DemoDay(day, "codex", shape[i] * 9000000, 2.05e-6, "demo-model-a"));
                if (i % 3 != 1) history.Days.Add(DemoDay(day, "claude", (1.2 - shape[i] / 2) * 1500000, 1.1e-6, "demo-model-b"));
            }
            foreach (string id in ProviderCatalog.Ids) {
                var state = new ProviderState { Id = id, Status = "ready", Message = "演示数据", Plan = id == "deepseek" ? "API 余额" : "演示账户", Account = "demo." + id + "@example.com", LastSuccess = DateTime.UtcNow.ToString("o") };
                if (id == "deepseek") state.Balances.Add(new Balance { Currency = "USD", Amount = 8.62 });
                else {
                    state.Quotas.Add(new Quota { Label = "每周", Used = id == "codex" ? 38 : id == "claude" ? 3 : 20, ResetUtc = DateTime.UtcNow.AddDays(3).AddHours(5).ToString("o"), WindowSeconds = 604800 });
                    if (id == "claude") state.Quotas.Insert(0, new Quota { Label = "5 小时", Used = 64, ResetUtc = DateTime.UtcNow.AddHours(1.5).ToString("o"), WindowSeconds = 18000 });
                }
                states[id] = state;
            }
            states["cursor"].Quotas.Add(new Quota { Label = "API / 手动模型", Used = 96, ResetUtc = DateTime.UtcNow.AddDays(3).AddHours(5).ToString("o"), WindowSeconds = 2592000 });
            states["grok"] = new ProviderState { Id = "grok", Status = "setup", Message = ProviderCatalog.Help("grok") };
            states["copilot"].Plan = "Copilot Pro"; states["copilot"].Quotas.Clear();
            states["copilot"].Quotas.Add(new Quota { Label = "高级请求", Used = 42, ResetUtc = DateTime.UtcNow.AddDays(11).ToString("o"), WindowSeconds = 2592000 });
            states["kimi"].Plan = "Kimi Moderato"; states["kimi"].Quotas.Insert(0, new Quota { Label = "5 小时", Used = 55, ResetUtc = DateTime.UtcNow.AddHours(2.2).ToString("o"), WindowSeconds = 18000 });
            // One key provider stays unconnected so the demo shows connecting from a card.
            states["opencode"] = new ProviderState { Id = "opencode", Status = "setup", Message = ProviderCatalog.Help("opencode") };
            states["zcode"].Plan = "GLM Pro"; states["zcode"].Quotas.Insert(0, new Quota { Label = "5 小时", Used = 31, ResetUtc = DateTime.UtcNow.AddHours(1.1).ToString("o"), WindowSeconds = 18000 });
            states["pi"] = new ProviderState { Id = "pi", Status = "ready", Message = "仅本机用量", Plan = "本机日志", LastSuccess = DateTime.UtcNow.ToString("o") };
            for (int i = 0; i < 30; i++) {
                string day = HistoryService.DayKey(DateTime.Today.AddDays(i - 29));
                if (i % 2 == 0) history.Days.Add(DemoDay(day, "zcode", (0.4 + shape[i]) * 1800000, 0.6e-6, "demo-model-c"));
                if (i > 18 && i % 3 == 0) history.Days.Add(DemoDay(day, "pi", shape[i] * 900000, 1.4e-6, "demo-model-a"));
            }
            // A demo-only custom provider (kept in memory, never written to data/).
            CustomProvider sample = CustomProviders.Parse("{\"name\":\"示例网关\",\"id\":\"demo-gateway\",\"color\":\"#8FD1E8\",\"url\":\"https://gateway.example.com/v1/usage\",\"auth\":\"bearer\",\"windows\":[{\"label\":\"每日\",\"used\":\"daily.used\",\"limit\":\"daily.limit\"}],\"balance\":{\"amount\":\"balance\",\"currency\":\"CNY\"}}");
            RegisterCustomProviders(new[] { sample });
            states[sample.Id] = CustomProviders.Map(sample, J.Parse("{\"daily\":{\"used\":320,\"limit\":1000},\"balance\":46.8}"), DateTime.UtcNow);
            states[sample.Id].Status = "ready"; states[sample.Id].LastSuccess = DateTime.UtcNow.ToString("o"); states[sample.Id].Account = "demo@example.com";
            config.Enabled = ProviderCatalog.All.ToArray();
            states["codex"].Plan = "Pro 20x";
            states["codex"].ResetCreditsAvailable = 2;
            states["codex"].ResetCreditExpiries = new List<string> { DateTime.UtcNow.AddDays(8).AddHours(12).ToString("o"), DateTime.UtcNow.AddDays(26).AddHours(4).ToString("o") };
            var relayA = new EndpointUsage { App = Endpoints.ClaudeApp, Host = "relay-a.example.com", Key = "a1b2c3", Name = "示例中转 A", Current = true, MainModel = "demo-model-b", LastUsed = DateTime.UtcNow.AddMinutes(-12).ToString("o") };
            var relayB = new EndpointUsage { App = Endpoints.CodexApp, Host = "api.relay-b.example.org", Key = "d4e5f6", Name = "示例中转 B", MainModel = "demo-model-a", LastUsed = DateTime.UtcNow.AddDays(-3).ToString("o") };
            for (int i = 0; i < 30; i++) { relayA.Daily[i] = i >= 12 ? shape[i] * 2400000 : 0; relayB.Daily[i] = i < 27 && i % 4 != 0 ? shape[i] * 5200000 : 0; }
            foreach (var relay in new[] { new { Usage = relayA, Rate = 1.2e-6 }, new { Usage = relayB, Rate = 2.0e-6 } }) {
                EndpointUsage u = relay.Usage; u.Today = u.Daily[29]; u.Week = u.Daily.Skip(23).Sum(); u.Month = u.Daily.Sum(); u.Requests = Math.Round(u.Month / 180000);
                u.CostToday = u.Today * relay.Rate; u.CostWeek = u.Week * relay.Rate; u.CostMonth = u.Month * relay.Rate;
            }
            thirdParty = new ThirdPartySummary {
                Endpoints = new List<EndpointUsage> { relayA, relayB },
                ClaudeNow = new EndpointMark { App = Endpoints.ClaudeApp, Host = relayA.Host, Key = relayA.Key, Name = relayA.Name },
                CodexNow = new EndpointMark { App = Endpoints.CodexApp, Official = true },
                ClaudeTrackedSince = DateTime.UtcNow.AddDays(-18).ToString("o"), ClaudeUnattributed = 4800000
            };
        }
        private static DayUsage DemoDay(string day, string agent, double tokens, double rate, string model) {
            double cached = Math.Round(tokens * .972), input = Math.Round(tokens * .021), output = tokens - cached - input;
            return new DayUsage { Day = day, Agent = agent, Tokens = tokens, CachedTokens = cached, InputTokens = input, OutputTokens = output, Cost = tokens * rate, Models = new List<ModelUsage> { new ModelUsage { Model = model, Tokens = tokens } } };
        }
        private void RegisterCustomProviders(IEnumerable<CustomProvider> providers) {
            ProviderCatalog.Custom.Clear();
            foreach (CustomProvider provider in providers) ProviderCatalog.Custom[provider.Id] = provider;
            foreach (string id in ProviderCatalog.All) if (!states.ContainsKey(id)) states[id] = new ProviderState { Id = id };
        }
        public void Start(bool background) { if (!background) Reveal(); clockTimer.Start(); if (demo) return; ObserveEndpoints(); WatchEndpoints(); refreshTimer.Start(); var a = Refresh(); var b = ScanHistory(); }
        // Endpoint switches (e.g. CC Switch rewriting settings.json / config.toml) are
        // recorded as they happen, so later usage can be attributed to the right relay.
        private void WatchEndpoints() {
            endpointTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            endpointTimer.Tick += delegate { endpointTimer.Stop(); ObserveEndpoints(); };
            var folders = ClaudeLogs.ConfigDirs().Take(1).Concat(new[] { ProviderService.CodexHome() });
            foreach (string folder in folders) {
                try {
                    if (!Directory.Exists(folder)) continue;
                    var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                    FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e) {
                        string name = System.IO.Path.GetFileName(e.FullPath).ToLowerInvariant();
                        if (name == "settings.json" || name == "config.toml" || name == "auth.json") app.Dispatcher.BeginInvoke(new Action(() => { endpointTimer.Stop(); endpointTimer.Start(); }));
                    };
                    watcher.Changed += changed; watcher.Created += changed; watcher.Renamed += delegate(object sender, RenamedEventArgs e) { changed(sender, e); };
                    watcher.EnableRaisingEvents = true; watchers.Add(watcher);
                } catch { }
            }
        }
        private void ObserveEndpoints() {
            if (demo || !config.ShowThirdParty) return;
            try {
                if (Endpoints.Observe(endpointLog, DateTime.UtcNow, Endpoints.CcSwitchNames())) { Store.Write("endpoints.json", endpointLog); BuildThirdParty(); Render(); }
            } catch { }
        }
        private void BuildThirdParty() {
            if (demo) return;
            try { thirdParty = ThirdPartyReport.Build(codexLogs, claudeLogs, endpointLog, history, DateTime.UtcNow, TimeZoneInfo.Local); } catch { thirdParty = new ThirdPartySummary(); }
        }
        public void Reveal() { if (!window.IsVisible) { frame.Restore(); window.Show(); } if (!demo) window.Activate(); Render(); }
        private void UpdatePin() {
            window.Topmost = config.AlwaysOnTop;
            bool keepOpen = !config.HideOnDeactivate;
            pinButton.Foreground = keepOpen ? AccentBrush : InkDim;
            pinButton.ToolTip = keepOpen ? "保持展开中 · 点击后改为失焦自动收起（不影响置顶）" : "失焦自动收起中 · 点击保持展开（不影响置顶）";
        }
        private void SaveConfig() { try { Store.Write("settings.json", config); } catch { statusNote = "设置保存失败，请检查 data 目录权限"; UpdateStatus(); } }
        private void SetSpinning(bool on) {
            var rotate = refreshGlyph.RenderTransform as RotateTransform; if (rotate == null) return;
            if (on) rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            else rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        private async Task Refresh() {
            if (demo) { Render(); return; }
            if (refreshing) return; refreshing = true; statusNote = ""; SetSpinning(true); UpdateStatus();
            ObserveEndpoints();
            try {
                using (var service = new ProviderService(config)) {
                    var jobs = config.Enabled.Where(ProviderCatalog.IsKnown).Select(async id => {
                        int serial = BeginFetch(id); ProviderState result = await Task.Run(() => service.Fetch(id));
                        if (Latest(id, serial)) Accept(id, result); Render();
                    }).ToArray();
                    await Task.WhenAll(jobs);
                }
                lastRefresh = DateTime.Now; Store.Write("quota-cache.json", states.Values.ToList()); UpdateTray();
            } catch (Exception e) { statusNote = e is ArgumentException ? e.Message : "刷新未完成，稍后重试"; }
            finally { refreshing = false; SetSpinning(false); Render(); }
            DateTime stamp; if (!scanning && (!DateTime.TryParse(history.Updated, out stamp) || DateTime.UtcNow - stamp.ToUniversalTime() > TimeSpan.FromMinutes(15))) { var ignored = ScanHistory(); }
        }
        // A failed read keeps the last good values (marked stale) instead of blanking them.
        private void Accept(string id, ProviderState incoming) {
            ProviderState old; if (!states.TryGetValue(id, out old)) old = new ProviderState { Id = id };
            if (incoming.Status == "error" || incoming.Status == "expired") { incoming.Quotas = old.Quotas; incoming.Balances = old.Balances; incoming.LastSuccess = old.LastSuccess; incoming.Plan = old.Plan; incoming.Account = old.Account; incoming.ResetCreditsAvailable = old.ResetCreditsAvailable; incoming.ResetCreditExpiries = old.ResetCreditExpiries; incoming.ResetCreditsUpdated = old.ResetCreditsUpdated; incoming.Stale = old.Quotas.Count > 0 || old.Balances.Count > 0; }
            states[id] = incoming;
        }
        // A full refresh and a single-provider refresh can overlap; only the newest fetch
        // of a provider is applied, so an older result never overwrites a newer one.
        private readonly Dictionary<string, int> fetchSerial = new Dictionary<string, int>();
        private readonly Dictionary<string, int> singleFetchSerial = new Dictionary<string, int>();
        private int BeginFetch(string id) { int n; fetchSerial.TryGetValue(id, out n); fetchSerial[id] = ++n; return n; }
        private bool Latest(string id, int serial) { int n; return fetchSerial.TryGetValue(id, out n) && n == serial; }
        // Refreshes one provider (card / compact refresh buttons, after connecting).
        private async Task RefreshOne(string id) {
            if (demo) { refreshingIds.Add(id); Render(); await Task.Delay(600); refreshingIds.Remove(id); Render(); return; }
            // May overlap a full refresh: a key saved from a card must not wait for the next cycle.
            if (!ProviderCatalog.IsKnown(id)) return;
            int serial = BeginFetch(id); singleFetchSerial[id] = serial;
            refreshingIds.Add(id); Render();
            try {
                if (ProviderCatalog.LocalOnly(id)) {
                    UsageHistory next = await HistoryService.Read();
                    if (Latest(id, serial)) {
                        if (next.Error.Length > 0) statusNote = next.Error;
                        else {
                            history = HistoryService.MergeProvider(history, next, id, HistoryService.DayKey(DateTime.Today.AddDays(-29)), HistoryService.DayKey(DateTime.Today));
                            Store.Write("history.json", history); states[id] = new ProviderState { Id = id, Status = "ready", LastSuccess = DateTime.UtcNow.ToString("o"), Message = "本机历史已更新" };
                        }
                    }
                } else using (var service = new ProviderService(config)) { ProviderState result = await Task.Run(() => service.Fetch(id)); if (Latest(id, serial)) Accept(id, result); }
                Store.Write("quota-cache.json", states.Values.ToList()); UpdateTray();
            } catch (Exception e) { statusNote = e is ArgumentException ? e.Message : ProviderCatalog.Name(id) + " 刷新未完成"; }
            finally {
                int pending;
                if (singleFetchSerial.TryGetValue(id, out pending) && pending == serial) { singleFetchSerial.Remove(id); refreshingIds.Remove(id); }
                Render();
            }
        }
        private async Task ScanHistory() {
            if (demo || scanning) return;
            scanning = true; Render();
            try {
                // ccusage (daily $, per-model) and the hourly log indexes run side by side.
                Task<UsageHistory> read = Task.Run(() => HistoryService.Read());
                Task<LogIndex> logs = config.Enabled.Contains("codex") || config.ShowThirdParty ? Task.Run(() => CodexLogs.Scan(Store.Read<LogIndex>("codex-logs.json"), DateTime.UtcNow)) : Task.FromResult<LogIndex>(null);
                Task<LogIndex> claude = config.ShowThirdParty ? Task.Run(() => ClaudeLogs.Scan(Store.Read<LogIndex>("claude-logs.json"), DateTime.UtcNow)) : Task.FromResult<LogIndex>(null);
                UsageHistory next = await read;
                if (next.Error.Length == 0) { history = HistoryService.Merge(history, next, HistoryService.DayKey(DateTime.Today.AddDays(-29)), HistoryService.DayKey(DateTime.Today)); Store.Write("history.json", history); }
                else history.Error = next.Error;
                try { LogIndex index = await logs; if (index != null) { codexLogs = index; Store.Write("codex-logs.json", index); } } catch { }
                try { LogIndex index = await claude; if (index != null) { claudeLogs = index; Store.Write("claude-logs.json", index); } } catch { }
                BuildThirdParty();
            }
            catch { history.Error = "本地统计暂时不可用"; }
            finally { scanning = false; Render(); }
        }
        private void UpdateTray() {
            string text = "codeusagemonit";
            foreach (string id in new[] { "codex", "claude", "cursor" }) { ProviderState s = states[id]; Quota q = s.Quotas.FirstOrDefault(x => x.Label == "每周") ?? s.Quotas.FirstOrDefault(); if (s.Status == "ready" && q != null && config.Enabled.Contains(id)) text += "\n" + ProviderCatalog.Name(id) + " " + q.Remaining.ToString("0") + "%"; }
            tray.Text = text.Length <= 63 ? text : text.Substring(0, 63);
        }
        private void LoadIcons() {
            foreach (string id in ProviderCatalog.Ids) {
                try {
                    var xml = new XmlDocument(); xml.Load(System.IO.Path.Combine(Store.Root, "icons", id + ".svg")); var paths = xml.GetElementsByTagName("path");
                    if (paths.Count == 0) continue;
                    // WPF path markup: "F0" selects the even-odd fill rule used by some logos.
                    bool evenOdd = xml.OuterXml.Contains("fill-rule=\"evenodd\"") || xml.OuterXml.Contains("fill-rule:evenodd");
                    string all = evenOdd ? "F0 " : ""; foreach (XmlNode p in paths) { if (p.Attributes["d"] != null) all += p.Attributes["d"].Value + " "; }
                    icons[id] = all;
                } catch { }
            }
        }
        private FrameworkElement Icon(string id, double size) {
            string data; if (icons.TryGetValue(id, out data)) { try { return new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Fill = Brush(ProviderCatalog.Color(id)), Stretch = Stretch.Uniform, Width = size, Height = size }; } catch { } }
            if (id == "overview" || id == ProviderCatalog.ThirdParty) return new TextBlock { Text = id == "overview" ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size - 1, Width = size, Height = size, TextAlignment = TextAlignment.Center, Foreground = Brush(ProviderCatalog.Color(id)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return new TextBlock { Text = id == "antigravity" ? "Λ" : id == "grok" ? "𝕏" : ProviderCatalog.Name(id).Substring(0, 1), FontSize = size - 2, Width = size, Height = size, TextAlignment = TextAlignment.Center, Foreground = Brush(ProviderCatalog.Color(id)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        private bool StartupEnabled() { using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue("codeusagemonit") != null; }
        private void SetStartup(bool enabled) { using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (enabled) key.SetValue("codeusagemonit", "\"" + System.IO.Path.Combine(Store.Root, "codeusagemonit.exe") + "\" --background"); else key.DeleteValue("codeusagemonit", false); } }
        public void Quit() { frame.Capture(); SaveConfig(); quitting = true; CloseSettings(); tray.Visible = false; window.Close(); app.Shutdown(); }
        public void Dispose() { quitting = true; CloseSettings(); if (copilotFlow != null) copilotFlow.Cancelled = true; frame.Dispose(); refreshTimer.Stop(); clockTimer.Stop(); foreach (var watcher in watchers) watcher.Dispose(); tray.Visible = false; tray.Dispose(); }
    }
}
