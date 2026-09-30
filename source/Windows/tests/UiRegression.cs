// Offline integration tests against the built WPF application. No real credentials or login.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodeUsageMonit;

public static class UiRegression {
    static MonitorPanel panel; static Window window; static Application app;
    static readonly List<string> passed = new List<string>(), failed = new List<string>();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(string name) { return (T)typeof(MonitorPanel).GetField(name, Private).GetValue(panel); }
    static object Call(string name, params object[] args) { return typeof(MonitorPanel).GetMethod(name, Private).Invoke(panel, args); }
    static void Require(bool ok, string note) { if (!ok) throw new Exception(note); }
    static void Check(string name, Action test) { try { test(); passed.Add(name); } catch (Exception e) { failed.Add(name + ": " + e.GetBaseException().Message); Call("CloseRangePicker"); Call("CloseSettings"); } }
    static void Pump() { var f = new DispatcherFrame(); app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => f.Continue = false)); Dispatcher.PushFrame(f); window.UpdateLayout(); }
    static void Finish(Task task) { while (!task.IsCompleted) { Pump(); System.Threading.Thread.Sleep(10); } task.GetAwaiter().GetResult(); Pump(); }
    static IEnumerable<DependencyObject> Tree(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var c in Tree(VisualTreeHelper.GetChild(root, i))) yield return c; }
    static string VisibleText() { Pump(); return String.Join("\n", Tree(window).OfType<TextBlock>().Select(t => new TextRange(t.ContentStart, t.ContentEnd).Text)); }
    static ButtonBase Button(string name, DependencyObject root = null) { Pump(); return Tree(root ?? window).OfType<ButtonBase>().First(b => b.IsVisible && (AutomationProperties.GetName(b) == name || Convert.ToString(b.Content) == name)); }
    static void Click(ButtonBase b) { Require(b.IsEnabled, "disabled: " + AutomationProperties.GetName(b)); if (b is RadioButton) ((RadioButton)b).IsChecked = true; b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(); }
    static Rect Bounds() { return new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight); }
    static void SameBounds(Rect old) { Rect current = Bounds(); Require(Math.Abs(current.X - old.X) < 1 && Math.Abs(current.Y - old.Y) < 1 && Math.Abs(current.Width - old.Width) < 1 && Math.Abs(current.Height - old.Height) < 1, "monitor geometry changed: " + old + " -> " + current); }
    static void Shot(string name, Window targetWindow = null) {
        ShotElement(name, (FrameworkElement)(targetWindow ?? window).Content);
    }
    static void ShotElement(string name, FrameworkElement target) {
        Pump(); target.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth), (int)Math.Ceiling(target.ActualHeight), 96, 96, PixelFormats.Pbgra32); bmp.Render(target);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using (var file = File.Create(Path.Combine(Store.Root, "verification", name + ".png"))) png.Save(file);
    }
    static TextBox TimeBox(DependencyObject root, string name) { return Tree(root).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name); }
    static void PickDate(DependencyObject root, DateTime date) {
        string name = "选择日期 " + date.ToString("yyyy-MM-dd");
        if (!Tree(root).OfType<ButtonBase>().Any(b => AutomationProperties.GetName(b) == name)) Click(Button("上个月", root));
        Click(Button(name, root));
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    static int Hit(Point logical) { Point p = window.PointToScreen(logical); long packed = ((long)(int)p.Y << 16) | ((long)(int)p.X & 65535); return (int)SendMessage(new WindowInteropHelper(window).Handle, 0x84, IntPtr.Zero, new IntPtr(packed)); }
    [STAThread] public static int Main() {
        Store.EnableDemoMode(); Directory.CreateDirectory(Store.Data); Store.Write("settings.json", new AppConfig { UiVersion = 2, Enabled = ProviderCatalog.Ids, WindowLeft = 40, WindowTop = 40, SurfaceOpacity = 1 });
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            panel = new MonitorPanel(app, true); window = Field<Window>("window"); window.ShowActivated = false; Field<WindowFrame>("frame").Restore(); window.Show(); Pump();
            var config = Field<AppConfig>("config"); var states = Field<Dictionary<string, ProviderState>>("states");
            Check("Overview chart filtering retains every account card and quota meter", () => {
                Call("SelectProvider", "overview"); Pump(); var body = Field<StackPanel>("body"); var cards = body.Children.Cast<UIElement>().Skip(1).ToArray(); var meters = cards.SelectMany(c => Tree(c)).OfType<UniformGrid>().ToArray();
                foreach (string name in new[] { "Codex", "Claude", "全部" }) { Click(Button("图表：" + name)); Require(cards.SequenceEqual(body.Children.Cast<UIElement>().Skip(1)) && meters.SequenceEqual(cards.SelectMany(c => Tree(c)).OfType<UniformGrid>()), "cards rebuilt"); } Shot("full-overview");
            });
            Check("Full panel context menu exposes all four mutually exclusive layouts", () => {
                Require(window.ContextMenu != null, "no inherited panel context menu"); var menu = window.ContextMenu; menu.PlacementTarget = Field<StackPanel>("body"); menu.IsOpen = true; Pump();
                var options = menu.Items.OfType<MenuItem>().Take(4).ToList(); Require(options.Count == 4 && options.Count(m => m.IsChecked) == 1, "invalid choices");
                menu.IsOpen = false; options[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(); Require(config.DisplaySize == "small", "context action failed");
            });
            Check("Every layout has native resize edges and retains its own geometry", () => {
                var hwnd = new WindowInteropHelper(window).Handle;
                foreach (string size in MonitorPanel.DisplaySizes) {
                    Call("SetDisplaySize", size); Pump(); Require(window.ResizeMode == ResizeMode.CanResize, "not resizable " + size);
                    Require(Hit(new Point(2, window.ActualHeight / 2)) == 10 && Hit(new Point(window.ActualWidth - 2, window.ActualHeight / 2)) == 11, "missing native horizontal grips " + size);
                    Require(Hit(new Point(window.ActualWidth / 2, window.ActualHeight - 2)) == 15, "missing native bottom grip " + size);
                    window.Width += 31; window.Height += 47; window.Left = 62; window.Top = 71; Pump(); var bounds = Bounds();
                    Call("SetDisplaySize", size == "full" ? "small" : "full"); Call("SetDisplaySize", size); Pump(); SameBounds(bounds);
                    Require(app.Windows.Count == 1 && new WindowInteropHelper(window).Handle == hwnd, "extra main window");
                }
                Field<WindowFrame>("frame").SetScale(.8, false); Call("SetDisplaySize", "small"); window.Width = 150; window.Height = 150; Pump();
                Call("SetDisplaySize", "full"); Call("SetDisplaySize", "small"); Pump(); Require(Math.Abs(window.ActualWidth - 150) < 1 && Math.Abs(window.ActualHeight - 150) < 1, "zoomed geometry changed");
                Field<WindowFrame>("frame").SetScale(1, false);
            });
            Check("Small medium and large overview rows navigate in place and support 7/30 day filters", () => {
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Field<WindowFrame>("frame").ResetPosition(); Call("SelectCompact", "overview"); Pump(); Rect bounds = Bounds();
                    foreach (string id in config.Enabled) Require(Button("查看 " + ProviderCatalog.Name(id)) != null, "missing overview row");
                    Click(Button("概览 时间范围")); var popup = Field<Popup>("rangePopup"); Click(Button("快捷范围 7d", popup.Child)); Click(Button("确定时间范围", popup.Child)); Require(config.UsageRanges["overview"].Preset == "7d", "period failed"); Shot(size + "-overview");
                    Click(Button("查看 Codex")); SameBounds(bounds); Require(config.CompactProvider == "codex", "navigation failed");
                    Click(Button("查看 Codex 用量详情")); SameBounds(bounds); Require(Field<string>("compactPage") == "details", "details missing"); Shot(size + "-detail");
                    Click(Button("返回平台")); Click(Button("切换到 概览")); SameBounds(bounds); Require(config.CompactProvider == "overview", "back failed");
                }
            });
            Check("Coin icons switch providers without changing layout", () => {
                Call("SetDisplaySize", "small"); Call("SelectCompact", "codex"); Rect bounds = Bounds(); Click(Button("切换到 Claude"));
                Require(config.CompactProvider == "claude", "wrong selected provider"); SameBounds(bounds);
            });
            Check("Connecting from each compact size stays at that exact size and uses real buttons", () => {
                states["codex"].Status = "expired"; states["codex"].Stale = true;
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Call("SelectCompact", "codex"); Pump(); Rect bounds = Bounds();
                    var connect = Button("连接 Codex"); Require(connect.Style == window.Resources["PrimaryButton"], "connection not a styled button"); Click(connect);
                    Require(config.DisplaySize == size && Field<string>("compactPage") == "connect" && app.Windows.Count == 1, "connection escaped layout"); SameBounds(bounds); Require(Button("我已登录，刷新") != null, "missing login retry");
                    Shot(size + "-connect"); Click(Button("返回平台")); SameBounds(bounds);
                }
                states["codex"].Status = "ready"; states["codex"].Stale = false;
            });
            Check("API key and region drafts survive independent refresh in compact connection pages", () => {
                Call("SetDisplaySize", "small");
                foreach (string id in new[] { "deepseek", "kimi", "opencode", "zcode" }) {
                    Call("OpenProviderConnection", id); Pump(); Rect bounds = Bounds(); var box = Tree(window).OfType<PasswordBox>().Single(p => p.IsVisible); box.Password = "synthetic-key-do-not-send";
                    if (id == "kimi" || id == "zcode") Click(Button("国际")); Finish((Task)Call("RefreshOne", "codex"));
                    Require(Tree(window).OfType<PasswordBox>().Single(p => p.IsVisible).Password == "synthetic-key-do-not-send", "draft lost"); Require(!Button(ProviderCatalog.Name(id) + " 保存并连接").IsEnabled, "demo could write secrets"); SameBounds(bounds);
                    Shot("small-key-connect");
                }
                Call("OpenProviderConnection", "copilot"); Require(!Button("使用 GitHub 登录").IsEnabled, "demo could authorize");
            });
            Check("Every size opens a separate singleton settings window without resizing the monitor", () => {
                foreach (string size in MonitorPanel.DisplaySizes) {
                    Call("SetDisplaySize", size); Pump(); Rect bounds = Bounds(); Call("OpenSettings"); Pump(); var settings = Field<Window>("settingsWindow");
                    Require(settings != null && settings != window && app.Windows.Count == 2, "settings not separate"); SameBounds(bounds);
                    Call("OpenSettings"); Require(Field<Window>("settingsWindow") == settings && app.Windows.Count == 2, "duplicate settings");
                    object content = Field<FrameworkElement>("settingsView"); Call("Render"); Pump(); Require(Field<FrameworkElement>("settingsView") == content, "settings draft rebuilt"); SameBounds(bounds);
                    settings.Close(); Pump(); Require(Field<Window>("settingsWindow") == null && config.DisplaySize == size && app.Windows.Count == 1, "close changed layout"); SameBounds(bounds);
                }
            });
            Check("Acrylic-only settings preview endpoints revert on close and persist on save", () => {
                Call("SetDisplaySize", "medium"); Rect bounds = Bounds(); Call("OpenSettings"); Pump(); var settings = Field<Window>("settingsWindow"); var view = Field<FrameworkElement>("settingsView");
                Require(!Tree(view).OfType<ButtonBase>().Any(b => Convert.ToString(b.Content) == "Mica"), "obsolete Mica option");
                var slider = Tree(view).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "界面透明度"); Require(slider.Minimum == 0 && slider.Maximum == 100, "wrong transparency range");
                var surface = (Border)window.FindName("Surface"); slider.Value = 100; Pump(); Require(((SolidColorBrush)surface.Background).Color.A == 0, "100% endpoint failed");
                slider.Value = 0; Pump(); Require(((SolidColorBrush)surface.Background).Color.A == 255, "0% endpoint failed"); slider.Value = 40; Pump(); Shot("settings", settings);
                settings.Close(); Pump(); Require(config.SurfaceOpacity == 1 && ((SolidColorBrush)surface.Background).Color.A == 255, "close failed to revert"); SameBounds(bounds);
                Call("OpenSettings"); view = Field<FrameworkElement>("settingsView"); Pump(); Tree(view).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "界面透明度").Value = 40;
                Click(Button("保存设置", view)); Require(config.Material == "acrylic" && config.SurfaceOpacity == .6 && config.DisplaySize == "medium", "save failed"); SameBounds(bounds);
            });
            Check("Changing layouts while settings are open is not undone by unrelated settings save", () => {
                Call("SetDisplaySize", "small"); Call("OpenSettings"); Pump(); Call("SetDisplaySize", "large"); var bounds = Bounds(); Click(Button("保存设置", Field<FrameworkElement>("settingsView"))); Require(config.DisplaySize == "large", "stale settings layout won"); SameBounds(bounds);
            });
            Check("Per-provider refresh remains independent in every compact layout", () => {
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Call("SelectCompact", "overview"); Rect bounds = Bounds(); var other = states["claude"];
                    Click(Button("刷新 Codex")); Require(Field<HashSet<string>>("refreshingIds").SetEquals(new[] { "codex" }), "wrong refresh target");
                    Require(!Button("刷新 Codex").IsEnabled && Button("刷新 Claude").IsEnabled, "refresh controls coupled"); Finish(Task.Delay(700)); Require(Object.ReferenceEquals(other, states["claude"]), "unrelated state changed"); SameBounds(bounds);
                }
            });
            Check("Full account actions expose connection and per-provider copy without mixing data", () => {
                Call("SetDisplaySize", "full"); Call("SelectProvider", "codex"); var menuButton = (Button)Button("Codex 操作"); Click(menuButton);
                var connect = menuButton.ContextMenu.Items.OfType<MenuItem>().First(m => Convert.ToString(m.Header) == "连接 / 更换账号"); menuButton.ContextMenu.IsOpen = false; connect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(); Require(Button("我已登录，刷新") != null, "ready account cannot reconnect");
                string summary = (string)Call("ProviderSummary", "codex"); Require(summary.StartsWith("Codex") && !summary.Contains("@") && !summary.Contains("Claude"), "summary mixes accounts or exposes email");
            });
            Check("Overlap generations reject old results for each provider independently", () => {
                int old = (int)Call("BeginFetch", "codex"), other = (int)Call("BeginFetch", "claude"), fresh = (int)Call("BeginFetch", "codex"); Require(!(bool)Call("Latest", "codex", old) && (bool)Call("Latest", "codex", fresh) && (bool)Call("Latest", "claude", other), "stale result accepted");
            });
            Check("CLI quoting works for batch launchers in directories with spaces", () => {
                string dir = Path.Combine(Store.Root, "verification", "mock cli space"); Directory.CreateDirectory(dir); string path = Path.Combine(dir, "mock.cmd"); File.WriteAllText(path, "@echo off\r\necho arg=[%~1]\r\n");
                var info = new System.Diagnostics.ProcessStartInfo("cmd.exe", MonitorPanel.CliArguments(path, "login", false)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = System.Diagnostics.Process.Start(info)) { string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(); Require(p.ExitCode == 0 && output.Trim() == "arg=[login]", "quoted launch failed"); }
            });
            Check("Different presets retain square, landscape, and detailed provider designs", () => {
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Field<WindowFrame>("frame").ResetPosition(); Call("SelectCompact", "claude"); Pump();
                    var grids = Tree(Field<Border>("compactRoot")).OfType<UniformGrid>().ToList(); int cells = size == "small" ? 16 : size == "medium" ? 20 : 24;
                    Require(grids.Count(g => g.Columns == cells && g.Height <= 8) == (size == "small" ? 1 : 2), "density not distinct " + size);
                    Require(!Tree(window).OfType<ButtonBase>().Any(b => b.IsVisible && AutomationProperties.GetName(b) == "切换平台"), "wide picker remains"); Shot(size + "-summary");
                }
            });
            Check("Overview progress tracks align despite mixed refresh and connect buttons", () => {
                string original = states["claude"].Status; states["claude"].Status = "error";
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Call("SelectCompact", "overview"); Pump();
                    var codex = Tree(Button("查看 Codex")).OfType<UniformGrid>().Single(g => g.Columns == 20);
                    var claude = Tree(Button("查看 Claude")).OfType<UniformGrid>().Single(g => g.Columns == 20);
                    var cursor = Tree(Button("查看 Cursor")).OfType<UniformGrid>().Single(g => g.Columns == 20);
                    Require(Math.Abs(codex.ActualWidth - claude.ActualWidth) < .1 && Math.Abs(codex.ActualWidth - cursor.ActualWidth) < .1, "unequal quota tracks " + size);
                    Shot(size + "-aligned-overview");
                }
                states["claude"].Status = original;
            });
            Check("Calendar is available in all layouts with five presets and no monitor resize", () => {
                foreach (string size in MonitorPanel.DisplaySizes) {
                    Call("SetDisplaySize", size); if (size == "full") Call("SelectProvider", "codex"); else Call("SelectCompact", "codex"); Pump(); Rect bounds = Bounds();
                    Click(Button("Codex 时间范围")); var popup = Field<Popup>("rangePopup"); Require(popup.IsOpen && app.Windows.Count == 1, "picker changed window count");
                    foreach (string quick in UsagePeriod.Presets) Require(Button("快捷范围 " + quick, popup.Child) != null, "missing preset");
                    Click(Button("快捷范围 14d", popup.Child)); SameBounds(bounds); Call("Render"); Require(Field<Popup>("rangePopup") == popup && popup.IsOpen, "background render closed picker");
                    Click(Button("取消", popup.Child)); SameBounds(bounds);
                }
            });
            Check("Calendar dates and HH:mm apply per provider and persist independently", () => {
                Call("SetDisplaySize", "large"); Call("SelectCompact", "codex"); Rect bounds = Bounds(); Click(Button("Codex 时间范围"));
                var popup = Field<Popup>("rangePopup"); var surface = (FrameworkElement)popup.Child;
                Click(Button("快捷范围 today", surface)); Click(Button("开始日期", surface)); PickDate(surface, DateTime.Today.AddDays(-1)); TimeBox(surface, "开始时间").Text = "09:15";
                Click(Button("结束日期", surface)); PickDate(surface, DateTime.Today.AddDays(-1)); TimeBox(surface, "结束时间").Text = "17:45";
                Pump(); ShotElement("calendar-custom", surface); Click(Button("确定时间范围", surface));
                var range = UsagePeriod.Resolve(config.UsageRanges["codex"], DateTime.UtcNow, TimeZoneInfo.Local);
                Require(range.StartUtc.ToLocalTime() == DateTime.Today.AddDays(-1).AddHours(9).AddMinutes(15) && range.EndUtc.ToLocalTime() == DateTime.Today.AddDays(-1).AddHours(17).AddMinutes(45), "range mismatch");
                Require(!config.UsageRanges.ContainsKey("claude") && Store.Read<AppConfig>("settings.json").UsageRanges["codex"].Preset == "custom", "scope or persistence failed"); SameBounds(bounds);
            });
            Check("Invalid calendar input stays open without changing saved statistics", () => {
                string saved = J.Serializer().Serialize(config.UsageRanges["codex"]); Click(Button("Codex 时间范围")); var popup = Field<Popup>("rangePopup"); var child = popup.Child;
                TimeBox(child, "结束时间").Text = "88:88"; Click(Button("确定时间范围", child)); Require(popup.IsOpen && Tree(child).OfType<TextBlock>().Any(t => t.Text.Contains("HH:mm")), "bad time accepted");
                TimeBox(child, "结束时间").Text = "08:00"; Click(Button("确定时间范围", child)); Require(popup.IsOpen && Tree(child).OfType<TextBlock>().Any(t => t.Text.Contains("晚于")), "reversed time accepted");
                Click(Button("取消", child)); Require(saved == J.Serializer().Serialize(config.UsageRanges["codex"]), "cancel changed range");
            });
            Check("Follow-now disables the fixed end time while keeping the chosen start", () => {
                string start = config.UsageRanges["codex"].StartUtc; Click(Button("Codex 时间范围")); var popup = Field<Popup>("rangePopup");
                Tree(popup.Child).OfType<CheckBox>().Single().IsChecked = true; Pump(); Require(!TimeBox(popup.Child, "结束时间").IsEnabled, "fixed end still editable"); Click(Button("确定时间范围", popup.Child));
                Require(config.UsageRanges["codex"].FollowNow && config.UsageRanges["codex"].StartUtc == start, "following now moved start");
            });
            Check("Range reads execute once and only the latest queued interval is accepted", () => {
                // Exercise the real asynchronous history process path with a local fixture.
                // Pi takes no credential or hourly-log path; the data root remains demo-only.
                string counter = Path.Combine(Store.Root, "tools", "fixture-calls.txt"); File.WriteAllText(counter, "");
                var previous = config.UsageRanges; config.UsageRanges = new Dictionary<string, UsageRangeChoice>();
                Call("SetDisplaySize", "small"); Call("SelectCompact", "pi");
                typeof(MonitorPanel).GetField("demo", Private).SetValue(panel, false);
                try {
                    Call("ApplyUsageRange", "pi", new UsageRangeChoice { Preset = "today" });
                    DateTime deadline = DateTime.UtcNow.AddSeconds(8);
                    while (Field<HashSet<string>>("periodBusy").Contains("pi") && DateTime.UtcNow < deadline) { Pump(); System.Threading.Thread.Sleep(10); }
                    Pump(); Require(File.ReadAllLines(counter).Length == 1 && !Field<HashSet<string>>("periodBusy").Contains("pi"), "duplicate range read");
                    Call("ApplyUsageRange", "pi", new UsageRangeChoice { Preset = "1d" });
                    Call("ApplyUsageRange", "pi", new UsageRangeChoice { Preset = "7d" });
                    Call("ApplyUsageRange", "pi", new UsageRangeChoice { Preset = "14d" });
                    deadline = DateTime.UtcNow.AddSeconds(8);
                    while (Field<HashSet<string>>("periodBusy").Contains("pi") && DateTime.UtcNow < deadline) { Pump(); System.Threading.Thread.Sleep(10); }
                    Pump(); var usage = (PeriodUsage)Call("RangeUsage", "pi");
                    Require(File.ReadAllLines(counter).Length == 3 && !Field<HashSet<string>>("periodBusy").Contains("pi"), "obsolete queued read ran");
                    var snapshot = ((System.Collections.IDictionary)Field<object>("periodSnapshots"))["pi"];
                    Require(((string)snapshot.GetType().GetField("Key").GetValue(snapshot)).StartsWith("14d|") && ((UsageHistory)snapshot.GetType().GetField("History").GetValue(snapshot)).Days.Single().Day == DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd"), "latest history result was not accepted");
                    Require(usage.Period.StartUtc > DateTime.UtcNow.AddDays(-14).AddMinutes(-1) && usage.Period.StartUtc < DateTime.UtcNow.AddDays(-14).AddMinutes(1), "older range replaced selection");
                } finally { typeof(MonitorPanel).GetField("demo", Private).SetValue(panel, true); config.UsageRanges = previous; }
            });
            var originalCursor = states["cursor"]; var originalGrok = states["grok"];
            var botCursor = Parsers.Cursor(J.Parse("{\"individualUsage\":{\"plan\":{\"totalPercentUsed\":30,\"autoPercentUsed\":20,\"apiPercentUsed\":40}}}"));
            botCursor.Status = "ready"; botCursor.Quotas.Add(Parsers.CursorGrokBot(J.Parse("{\"usagePercent\":8.6,\"nextResetTimestampUtc\":\"2026-10-05T00:00:00Z\"}"))); states["cursor"] = botCursor;
            var grok = Parsers.Grok(J.Parse("{\"config\":{\"creditUsagePercent\":63,\"productUsage\":[{\"product\":\"build\",\"usagePercent\":55},{\"product\":\"chat\",\"usagePercent\":8}]}}")); grok.Status = "ready"; states["grok"] = grok;
            typeof(MonitorPanel).GetField("grokBotActivity", Private).SetValue(panel, new BotActivity { Updated = DateTime.UtcNow.ToString("o"), Sessions = 3, TodaySessions = 1, Days = { new BotActivityDay { Day = DateTime.Today.ToString("yyyy-MM-dd"), UserMessages = 5, AssistantMessages = 6 } } });
            Check("Full Cursor and overview retain the fourth Bot quota and its local activity", () => {
                Call("SetDisplaySize", "full"); Call("SelectProvider", "cursor"); string text = VisibleText();
                Require(text.IndexOf("Grok Bot · 每周", StringComparison.Ordinal) > text.IndexOf("API / 手动模型", StringComparison.Ordinal), "Bot order");
                Require(text.Contains("91.4%") && text.Contains("11 条消息") && text.Contains("本机记录未提供"), "Bot data or activity missing");
                Call("SelectProvider", "overview"); Require(VisibleText().Contains("Grok Bot · 每周"), "fourth quota hidden");
            });
            Check("All compact sizes expose Bot quota and local activity through their details", () => {
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Call("SelectCompact", "cursor");
                    if (size == "large") Require(VisibleText().Contains("Grok Bot · 每周"), "large card hides Bot");
                    Rect bounds = Bounds(); Click(Button("查看 Cursor 用量详情")); SameBounds(bounds); string text = VisibleText();
                    Require(text.Contains("Grok Bot · 每周") && text.Contains("91.4%") && text.Contains("11 条消息"), "Bot missing in " + size);
                }
            });
            Check("Full and compact Grok details label consumption without a fake remaining quota", () => {
                Call("SetDisplaySize", "full"); Call("SelectProvider", "grok"); string text = VisibleText();
                Require(text.Contains("37%") && text.Contains("已消耗总额度 55%") && !text.Contains("Build 占比") && !text.Contains("45%"), "full Grok quota semantics");
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Call("SelectCompact", "grok"); Click(Button("查看 Grok 用量详情")); text = VisibleText();
                    Require(text.Contains("37%") && text.Contains("已消耗总额度 55%") && !text.Contains("Build 占比"), "compact Grok quota semantics");
                }
            });
            Check("Failed refresh retains the last Grok breakdown with the last good quota", () => {
                Call("Accept", "grok", new ProviderState { Id = "grok", Status = "error", Message = "synthetic timeout" });
                Require(states["grok"].Stale && states["grok"].Quotas.Count == 1 && states["grok"].ProductUsage.Count == 2, "cached breakdown lost");
            });
            states["cursor"] = originalCursor; states["grok"] = originalGrok;
            Check("Saved custom compact geometry and zoom survive cold startup", () => {
                panel.Dispose(); typeof(MonitorPanel).GetField("quitting", Private).SetValue(panel, true); window.Close();
                var saved = new AppConfig { UiVersion = 2, DisplaySize = "small", CompactProvider = "overview", UiScale = 1.4 };
                saved.Layouts["small"] = new PanelPlacement { Width = 340, Height = 420, Left = 40, Top = 40 }; Store.Write("settings.json", saved);
                panel = new MonitorPanel(app, true); window = Field<Window>("window"); window.ShowActivated = false; window.Show(); Pump();
                Require(window.ActualWidth == 340 && window.ActualHeight == 420 && ((ScaleTransform)Field<Border>("compactRoot").LayoutTransform).ScaleX == 1.4 && app.Windows.Count == 1, "cold geometry changed");
            });
        } catch (Exception e) { failed.Add("Harness: " + e.GetBaseException().Message); }
        finally { if (panel != null) { panel.Dispose(); typeof(MonitorPanel).GetField("quitting", Private).SetValue(panel, true); window.Close(); } File.WriteAllText(Path.Combine(Store.Root, "verification", "ui-regression.json"), J.Serializer().Serialize(new { passed = passed.Count, failed = failed.Count, checks = passed, errors = failed })); }
        return failed.Count == 0 ? 0 : 1;
    }
}
