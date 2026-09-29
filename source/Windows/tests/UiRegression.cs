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
    static void Check(string name, Action test) { try { test(); passed.Add(name); } catch (Exception e) { failed.Add(name + ": " + e.GetBaseException().Message); Call("CloseSettings"); } }
    static void Pump() { var f = new DispatcherFrame(); app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => f.Continue = false)); Dispatcher.PushFrame(f); window.UpdateLayout(); }
    static void Finish(Task task) { while (!task.IsCompleted) { Pump(); System.Threading.Thread.Sleep(10); } task.GetAwaiter().GetResult(); Pump(); }
    static IEnumerable<DependencyObject> Tree(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var c in Tree(VisualTreeHelper.GetChild(root, i))) yield return c; }
    static ButtonBase Button(string name, DependencyObject root = null) { Pump(); return Tree(root ?? window).OfType<ButtonBase>().First(b => b.IsVisible && (AutomationProperties.GetName(b) == name || Convert.ToString(b.Content) == name)); }
    static void Click(ButtonBase b) { Require(b.IsEnabled, "disabled: " + AutomationProperties.GetName(b)); if (b is RadioButton) ((RadioButton)b).IsChecked = true; b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(); }
    static Rect Bounds() { return new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight); }
    static void SameBounds(Rect old) { Rect current = Bounds(); Require(Math.Abs(current.X - old.X) < 1 && Math.Abs(current.Y - old.Y) < 1 && Math.Abs(current.Width - old.Width) < 1 && Math.Abs(current.Height - old.Height) < 1, "monitor geometry changed: " + old + " -> " + current); }
    static void Shot(string name, Window targetWindow = null) {
        Pump(); var target = (FrameworkElement)(targetWindow ?? window).Content; target.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth), (int)Math.Ceiling(target.ActualHeight), 96, 96, PixelFormats.Pbgra32); bmp.Render(target);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using (var file = File.Create(Path.Combine(Store.Root, "verification", name + ".png"))) png.Save(file);
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
            });
            Check("Small medium and large overview rows navigate in place and support 7/30 day filters", () => {
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Field<WindowFrame>("frame").ResetPosition(); Call("SelectCompact", "overview"); Pump(); Rect bounds = Bounds();
                    foreach (string id in config.Enabled) Require(Button("查看 " + ProviderCatalog.Name(id)) != null, "missing overview row");
                    Click(Button("7 天")); Require(Field<int>("compactDays") == 7, "period failed"); Click(Button("30 天")); Shot(size + "-overview");
                    Click(Button("查看 Codex")); SameBounds(bounds); Require(config.CompactProvider == "codex", "navigation failed");
                    Click(Button("查看 Codex 用量详情")); SameBounds(bounds); Require(Field<string>("compactPage") == "details", "details missing"); Shot(size + "-detail");
                    Click(Button("返回平台")); Click(Button("返回概览")); SameBounds(bounds); Require(config.CompactProvider == "overview", "back failed");
                }
            });
            Check("Compact platform menu switches providers without changing layout", () => {
                Call("SetDisplaySize", "small"); Call("SelectCompact", "codex"); Rect bounds = Bounds(); var pick = (Button)Button("切换平台"); Click(pick);
                var claude = pick.ContextMenu.Items.OfType<MenuItem>().First(m => Convert.ToString(m.Header) == "Claude"); pick.ContextMenu.IsOpen = false; claude.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
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
                var slider = Tree(view).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "背景透明度"); Require(slider.Minimum == 0 && slider.Maximum == 100, "wrong transparency range");
                var surface = (Border)window.FindName("Surface"); slider.Value = 100; Pump(); Require(((SolidColorBrush)surface.Background).Color.A == 0, "100% endpoint failed");
                slider.Value = 0; Pump(); Require(((SolidColorBrush)surface.Background).Color.A == 255, "0% endpoint failed"); slider.Value = 40; Pump(); Shot("settings", settings);
                settings.Close(); Pump(); Require(config.SurfaceOpacity == 1 && ((SolidColorBrush)surface.Background).Color.A == 255, "close failed to revert"); SameBounds(bounds);
                Call("OpenSettings"); view = Field<FrameworkElement>("settingsView"); Pump(); Tree(view).OfType<Slider>().Single(s => AutomationProperties.GetName(s) == "背景透明度").Value = 40;
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
