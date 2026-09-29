// Offline WPF integration checks against the built application, with synthetic accounts.
// The harness invokes the actual routed button events; it never starts login or live probes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodeUsageMonit;

public static class UiRegression {
    static MonitorPanel panel;
    static Window window;
    static Application app;
    static readonly List<string> passed = new List<string>(), failed = new List<string>();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(string name) { return (T)typeof(MonitorPanel).GetField(name, Private).GetValue(panel); }
    static object Call(string name, params object[] args) { return typeof(MonitorPanel).GetMethod(name, Private).Invoke(panel, args); }
    static void Require(bool ok, string note) { if (!ok) throw new Exception(note); }
    static void Check(string name, Action test) { try { test(); passed.Add(name); } catch (Exception e) { failed.Add(name + ": " + e.GetBaseException().Message); } }
    static void Pump() {
        var frame = new DispatcherFrame();
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); window.UpdateLayout();
    }
    static void Finish(Task task) { while (!task.IsCompleted) { Pump(); System.Threading.Thread.Sleep(10); } task.GetAwaiter().GetResult(); Pump(); }
    static IEnumerable<DependencyObject> Tree(DependencyObject root) {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static ButtonBase Button(string name, DependencyObject root = null) {
        Pump(); return Tree(root ?? window).OfType<ButtonBase>().First(b => AutomationProperties.GetName(b) == name || Convert.ToString(b.Content) == name);
    }
    static void Click(ButtonBase button) { Require(button.IsEnabled, "button disabled"); if (button is RadioButton) ((RadioButton)button).IsChecked = true; button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(); }
    static void Shot(string name) {
        Pump(); var target = (FrameworkElement)window.Content;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth), (int)Math.Ceiling(target.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(target); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        using (var file = File.Create(Path.Combine(Store.Root, "verification", name + ".png"))) png.Save(file);
    }
    [STAThread] public static int Main() {
        Store.EnableDemoMode(); Directory.CreateDirectory(Store.Data);
        Store.Write("settings.json", new AppConfig { UiVersion = 2, Enabled = ProviderCatalog.Ids, WindowLeft = 40, WindowTop = 40, SurfaceOpacity = 1 });
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            panel = new MonitorPanel(app, true); window = Field<Window>("window");
            window.ShowActivated = false; Field<WindowFrame>("frame").Restore(); window.Show(); Pump();
            var config = Field<AppConfig>("config");
            var states = Field<Dictionary<string, ProviderState>>("states");
            Check("Overview filters preserve every provider card and meter instance", () => {
                Call("SelectProvider", "overview"); Pump();
                var body = Field<StackPanel>("body"); var cards = body.Children.Cast<UIElement>().Skip(1).ToArray();
                var meters = cards.SelectMany(c => Tree(c)).OfType<UniformGrid>().ToArray();
                foreach (string name in new[] { "Codex", "Claude", "全部", "Codex", "全部" }) {
                    Click(Button("图表：" + name));
                    Require(cards.SequenceEqual(body.Children.Cast<UIElement>().Skip(1)), "provider cards rebuilt");
                    Require(meters.SequenceEqual(cards.SelectMany(c => Tree(c)).OfType<UniformGrid>()), "meters rebuilt");
                }
                Shot("overview");
            });
            Check("Per-provider refresh targets only one busy indicator and preserves other state", () => {
                var untouched = states.Where(p => p.Key != "codex").ToDictionary(p => p.Key, p => p.Value);
                Click(Button("刷新 Codex"));
                Require(Field<HashSet<string>>("refreshingIds").SetEquals(new[] { "codex" }), "unexpected refresh target");
                Require(!Button("刷新 Codex").IsEnabled && Button("刷新 Claude").IsEnabled, "buttons not independent");
                Finish(Task.Delay(700));
                Require(Field<HashSet<string>>("refreshingIds").Count == 0, "busy flag stuck");
                Require(untouched.All(p => Object.ReferenceEquals(p.Value, states[p.Key])), "another provider state replaced");
            });
            Check("Overlapping fetch generations reject older results independently per provider", () => {
                int older = (int)Call("BeginFetch", "codex"), other = (int)Call("BeginFetch", "claude"), newer = (int)Call("BeginFetch", "codex");
                Require(!(bool)Call("Latest", "codex", older) && (bool)Call("Latest", "codex", newer) && (bool)Call("Latest", "claude", other), "stale result accepted");
            });
            Check("Four sizes use one HWND and restore full geometry at 80/100/140 percent scale", () => {
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                foreach (double scale in new[] { .8, 1.0, 1.4 }) {
                    Field<WindowFrame>("frame").SetScale(scale, false);
                    foreach (string size in new[] { "small", "medium", "large", "full" }) {
                        Call("SetDisplaySize", size); Pump();
                        double width = size == "small" ? 172 * scale : size == "full" ? 420 : 360 * scale;
                        double height = size == "small" ? 172 * scale : size == "medium" ? 176 * scale : size == "large" ? 390 * scale : 790;
                        Require(Math.Abs(window.ActualWidth - width) <= 1 && Math.Abs(window.ActualHeight - height) <= 1, size + " actual=" + window.ActualWidth + "x" + window.ActualHeight);
                        Require(app.Windows.Count == 1 && new System.Windows.Interop.WindowInteropHelper(window).Handle == handle, "extra window");
                        Require(Field<FrameworkElement>("scaleRoot").Visibility == (size == "full" ? Visibility.Visible : Visibility.Collapsed), "wrong root");
                        if (scale == 1) Shot(size);
                    }
                }
                Field<WindowFrame>("frame").SetScale(1, false);
            });
            Check("Large overview exposes all enabled providers with a scrollable list", () => {
                Call("SetDisplaySize", "large"); Call("SelectCompact", "overview"); Pump();
                foreach (string id in config.Enabled) Require(Button("查看 " + ProviderCatalog.Name(id)) != null, "missing " + id);
                var scroll = Field<ScrollViewer>("compactScroll");
                Require(scroll.ScrollableHeight > 0, "overview cannot scroll"); scroll.ScrollToEnd(); Pump();
                Require(scroll.VerticalOffset > 0, "scroll failed"); scroll.ScrollToHome();
                Shot("large-overview"); Click(Button("查看 Codex")); Require(config.CompactProvider == "codex", "provider selection failed"); Shot("large-codex");
            });
            Check("Compact position survives opening and cancelling settings", () => {
                window.Left = 123; window.Top = 145;
                Call("OpenSettings"); Pump(); Require(Field<string>("activeSize") == "full", "settings not full");
                Call("CloseSettings"); Pump();
                Require(Field<string>("activeSize") == "large" && Math.Abs(window.Left - 123) < 1 && Math.Abs(window.Top - 145) < 1, "compact placement lost");
            });
            Check("Expired accounts with cached quotas have connect links in all compact sizes", () => {
                string old = states["codex"].Status; states["codex"].Status = "expired"; states["codex"].Stale = true;
                foreach (string size in new[] { "small", "medium", "large" }) {
                    Call("SetDisplaySize", size); Call("Render"); Pump();
                    Click(Button("连接 Codex"));
                    Require(config.DisplaySize == "full" && Field<string>("selected") == "codex", "connection target wrong");
                    Require(Button("我已登录，刷新") != null, "missing retry");
                }
                states["codex"].Status = old; states["codex"].Stale = false;
            });
            Check("Card connection inputs and unsaved region/key survive a refresh", () => {
                foreach (string id in new[] { "deepseek", "kimi", "opencode", "zcode" }) {
                    states[id].Status = "setup"; Call("SelectProvider", id); Pump();
                    var box = Tree(window).OfType<PasswordBox>().First(p => AutomationProperties.GetName(p) == ProviderCatalog.Name(id) + " API Key");
                    box.Password = "synthetic-key-do-not-send";
                    if (id == "kimi" || id == "zcode") Click(Button("国际"));
                    Finish((Task)Call("RefreshOne", "codex"));
                    var rebuilt = Tree(window).OfType<PasswordBox>().First(p => AutomationProperties.GetName(p) == ProviderCatalog.Name(id) + " API Key");
                    Require(rebuilt.Password == "synthetic-key-do-not-send", "draft lost");
                    Require(!Button(ProviderCatalog.Name(id) + " 保存并连接").IsEnabled, "demo could save secrets");
                    if (id == "kimi" || id == "zcode") Require(Field<Dictionary<string, string>>("regionDrafts").ContainsKey(id), "region lost");
                }
                Shot("card-connect");
                states["copilot"].Status = "setup"; Call("SelectProvider", "copilot"); Pump();
                Require(!Button("使用 GitHub 登录").IsEnabled, "demo could authenticate");
                foreach (string id in new[] { "codex", "claude", "grok", "cursor", "antigravity" }) {
                    states[id].Status = "setup"; Call("SelectProvider", id); Pump(); Require(Button("我已登录，刷新") != null, "no connection action for " + id);
                }
            });
            Check("Settings offer four exclusive sizes and only Acrylic/Mica; transparency endpoints and cancel/save work", () => {
                Call("SetDisplaySize", "full"); Call("OpenSettings"); Pump();
                var view = Field<FrameworkElement>("settingsView");
                foreach (string name in new[] { "小", "中", "大", "完整", "毛玻璃", "Mica" }) Require(Button(name, view) != null, "missing option " + name);
                Require(!Tree(view).OfType<Button>().Any(b => Convert.ToString(b.Content) == "不透明"), "obsolete material");
                var slider = Tree(view).OfType<Slider>().First(s => AutomationProperties.GetName(s) == "背景透明度");
                Require(slider.Minimum == 0 && slider.Maximum == 100, "wrong transparency range");
                var surface = (Border)window.FindName("Surface");
                slider.Value = 0; Pump(); Require(((SolidColorBrush)surface.Background).Color.A == 255, "zero not opaque");
                slider.Value = 100; Pump(); Require(((SolidColorBrush)surface.Background).Color.A == 0, "100 not transparent");
                Click(Button("Mica", view)); slider.Value = 40; Pump(); Shot("settings");
                Call("CloseSettings"); Pump(); Require(config.Material == "acrylic" && config.SurfaceOpacity == 1 && ((SolidColorBrush)surface.Background).Color.A == 255, "cancel did not restore");
                Call("OpenSettings"); Pump(); view = Field<FrameworkElement>("settingsView");
                Click(Button("小", view)); Click(Button("大", view)); Click(Button("中", view)); Click(Button("Mica", view));
                Tree(view).OfType<Slider>().First(s => AutomationProperties.GetName(s) == "背景透明度").Value = 100;
                Click(Button("保存设置", view));
                var saved = Store.Read<AppConfig>("settings.json");
                Require(saved.DisplaySize == "medium" && saved.Material == "mica" && saved.SurfaceOpacity == 0 && app.Windows.Count == 1, "save failed");
            });
            Check("Quoted CLI launcher works with spaces and a batch wrapper without launching a real login", () => {
                string dir = Path.Combine(Store.Root, "verification", "mock cli space"); Directory.CreateDirectory(dir);
                string command = Path.Combine(dir, "mock.cmd"); File.WriteAllText(command, "@echo off\r\necho arg=[%~1]\r\n");
                var info = new System.Diagnostics.ProcessStartInfo("cmd.exe", MonitorPanel.CliArguments(command, "login", false)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var process = System.Diagnostics.Process.Start(info)) { string result = process.StandardOutput.ReadToEnd(); process.WaitForExit(); Require(process.ExitCode == 0 && result.Trim() == "arg=[login]", "quoted launch failed"); }
            });
            Check("Cold compact startup restores saved size and zoom without creating another window", () => {
                panel.Dispose(); typeof(MonitorPanel).GetField("quitting", Private).SetValue(panel, true); window.Close();
                Store.Write("settings.json", new AppConfig { UiVersion = 2, DisplaySize = "small", CompactProvider = "codex", UiScale = 1.4, CompactLeft = 40, CompactTop = 40 });
                panel = new MonitorPanel(app, true); window = Field<Window>("window"); window.ShowActivated = false; window.Show(); Pump();
                var scale = (ScaleTransform)Field<Border>("compactRoot").LayoutTransform;
                Require(Math.Abs(window.ActualWidth - 172 * 1.4) < 1 && Math.Abs(scale.ScaleX - 1.4) < .001 && app.Windows.Count == 1, "cold size/zoom incorrect");
            });
        } catch (Exception e) { failed.Add("Harness: " + e.GetBaseException().Message); }
        finally {
            if (panel != null) { panel.Dispose(); typeof(MonitorPanel).GetField("quitting", Private).SetValue(panel, true); window.Close(); }
            File.WriteAllText(Path.Combine(Store.Root, "verification", "ui-regression.json"), J.Serializer().Serialize(new { passed = passed.Count, failed = failed.Count, checks = passed, errors = failed }));
        }
        return failed.Count == 0 ? 0 : 1;
    }
}
