using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodeUsageMonit {
    // Settings have their own window; editing never changes the monitor's geometry.
    public sealed partial class MonitorPanel {
        private FrameworkElement settingsView;
        private Window settingsWindow;
        private Border settingsSurface;
        private Grid settingsContentHost;
        private TextBlock settingsTitle;
        private Button settingsBack;
        private static readonly int[] RefreshSteps = { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };

        private void OpenSettings() {
            if (settingsWindow != null) { if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate(); return; }
            settingsView = BuildSettings();
            settingsContentHost = new Grid(); settingsContentHost.Children.Add(settingsView);
            var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 5) };
            settingsBack = IconAction("", "返回设置", BackFromSettings); settingsBack.Visibility = Visibility.Collapsed; head.Children.Add(settingsBack);
            settingsTitle = Label("偏好设置", 18, Ink); settingsTitle.FontWeight = FontWeights.SemiBold; settingsTitle.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(settingsTitle); layout.Children.Add(head);
            Grid.SetRow(settingsContentHost, 1); layout.Children.Add(settingsContentHost);
            settingsSurface = new Border { Child = layout, Background = Brush("#FF15171B") };
            settingsWindow = new Window { Title = "设置 · codeusagemonit", Width = 550, Height = Math.Min(760, SystemParameters.WorkArea.Height - 30), MinWidth = 460, MinHeight = 500, Content = settingsSurface, Background = Brushes.Transparent, Foreground = Ink, Resources = window.Resources, FontFamily = window.FontFamily, FontSize = 12, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = true, ShowActivated = !demo, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            settingsWindow.SourceInitialized += delegate { PreviewTransparency(config.SurfaceOpacity); };
            settingsWindow.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape) { BackFromSettings(); e.Handled = true; } };
            settingsWindow.Closed += delegate {
                settingsWindow = null; settingsSurface = null; settingsContentHost = null; settingsView = null; customEditor = null; customList = null;
                frame.ApplyMaterial(); settingsButton.ClearValue(Control.ForegroundProperty); Render();
            };
            settingsButton.Foreground = AccentBrush; settingsWindow.Show();
            if (!demo) settingsWindow.Activate();
        }
        private void CloseSettings() {
            if (settingsWindow != null) settingsWindow.Close();
        }
        private void PreviewTransparency(double opacity) {
            frame.ApplyMaterial("acrylic", opacity);
            if (settingsWindow != null) WindowFrame.ApplyBackdrop(settingsWindow, settingsSurface, "acrylic", opacity);
        }
        private FrameworkElement BuildSettings() {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var content = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(14, 6, 8, 8), Margin = new Thickness(0, 0, 4, 0) });

            // Providers
            content.Children.Add(SectionTitle("显示的平台"));
            var providers = new StackPanel();
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, -6) };
            var choices = new Dictionary<string, CheckBox>();
            foreach (string id in ProviderCatalog.All) {
                var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
                FrameworkElement icon = Icon(id, 13); icon.Margin = new Thickness(0, 0, 6, 0); chipContent.Children.Add(icon);
                chipContent.Children.Add(new TextBlock { Text = ProviderCatalog.Name(id), VerticalAlignment = VerticalAlignment.Center });
                var chip = new CheckBox { Style = Styled("Chip"), Content = chipContent, IsChecked = config.Enabled.Contains(id) };
                System.Windows.Automation.AutomationProperties.SetName(chip, ProviderCatalog.Name(id));
                choices[id] = chip; chips.Children.Add(chip);
            }
            providers.Children.Add(chips);
            providers.Children.Add(Hint("关闭的平台不再联网查询，也不出现在标签栏。", 10));
            providers.Children.Add(Separator());
            CheckBox thirdPartySwitch = SwitchRow("统计第三方 API 用量", "按本机日志统计经中转站的 Token；服务商的限额无法得知", config.ShowThirdParty);
            providers.Children.Add(thirdPartySwitch);
            content.Children.Add(Card(providers));

            // Window
            content.Children.Add(SectionTitle("窗口"));
            var windowCard = new StackPanel();
            string displaySize = config.DisplaySize, originalDisplaySize = config.DisplaySize;
            var sizeRow = Row();
            AddRow(sizeRow, FieldLabel("显示尺寸", null), Segmented(DisplaySizes.Select(s => new[] { s, SizeName(s) }).ToArray(), displaySize, code => displaySize = code));
            windowCard.Children.Add(sizeRow);
            windowCard.Children.Add(Hint("四种布局都能拖边缘调大小，并分别记忆位置和宽高。", 7));
            windowCard.Children.Add(Separator());
            CheckBox onTop = SwitchRow("置于其他窗口上方", "默认关闭；单击窗口可正常操作", config.AlwaysOnTop);
            CheckBox autoHide = SwitchRow("点击其他窗口时自动收起", "仅完整面板；标题栏的图钉按钮也可以切换", config.HideOnDeactivate);
            windowCard.Children.Add(onTop); windowCard.Children.Add(Separator()); windowCard.Children.Add(autoHide); windowCard.Children.Add(Separator());
            // Transparency is shown as 0–100 (0 = opaque); the config keeps the surface alpha.
            double transparency = Math.Round((1 - config.SurfaceOpacity) * 100);
            var transparencyValue = Label(transparency.ToString("0") + "%", 12, InkDim); Tabular(transparencyValue);
            var transparencySlider = new Slider { Minimum = 0, Maximum = 100, Value = transparency, SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(transparencySlider, "界面透明度");
            var transparencyRow = Row(); transparencyRow.Margin = new Thickness(0, 12, 0, 0);
            AddRow(transparencyRow, FieldLabel("界面透明度", "0 为不透明，越高越通透"), transparencyValue);
            windowCard.Children.Add(transparencyRow); windowCard.Children.Add(transparencySlider);
            transparencySlider.ValueChanged += delegate {
                transparency = Math.Round(transparencySlider.Value); transparencyValue.Text = transparency.ToString("0") + "%";
                PreviewTransparency(1 - transparency / 100);
            };
            windowCard.Children.Add(Separator());
            var zoomRow = Row(); var zoomValue = Label((config.UiScale * 100).ToString("0") + "%", 12, InkDim); Tabular(zoomValue);
            AddRow(zoomRow, FieldLabel("界面缩放", "也可以在面板上按 Ctrl + 滚轮"), zoomValue); windowCard.Children.Add(zoomRow);
            var zoom = new Slider { Minimum = .8, Maximum = 1.4, Value = config.UiScale, TickFrequency = .05, IsSnapToTickEnabled = true, SmallChange = .05, LargeChange = .1, Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(zoom, "界面缩放");
            zoom.ValueChanged += delegate { zoomValue.Text = (zoom.Value * 100).ToString("0") + "%"; };
            windowCard.Children.Add(zoom); windowCard.Children.Add(Separator());
            var placeRow = Row(); var reset = new Button { Style = Styled("SecondaryButton"), Content = "重置" };
            reset.Click += delegate { frame.ResetPosition(); };
            AddRow(placeRow, FieldLabel("窗口位置与尺寸", "回到屏幕右下角、默认大小"), reset); windowCard.Children.Add(placeRow);
            content.Children.Add(Card(windowCard));

            // Data & network
            content.Children.Add(SectionTitle("数据与网络"));
            var dataCard = new StackPanel();
            int minutes = config.RefreshMinutes;
            var intervalRow = Row(); var stepper = new StackPanel { Orientation = Orientation.Horizontal };
            var minus = new Button { Style = Styled("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = "更频繁" };
            var value = Label(minutes + " 分钟", 12, Ink); value.MinWidth = 58; value.TextAlignment = TextAlignment.Center; Tabular(value);
            var plus = new Button { Style = Styled("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = "更少" };
            List<int> steps = RefreshSteps.Concat(new[] { minutes }).Distinct().OrderBy(n => n).ToList();
            minus.Click += delegate { int i = steps.IndexOf(minutes); if (i > 0) minutes = steps[i - 1]; value.Text = minutes + " 分钟"; };
            plus.Click += delegate { int i = steps.IndexOf(minutes); if (i < steps.Count - 1) minutes = steps[i + 1]; value.Text = minutes + " 分钟"; };
            stepper.Children.Add(minus); stepper.Children.Add(value); stepper.Children.Add(plus);
            AddRow(intervalRow, FieldLabel("自动刷新", "每次刷新会查询所有已启用平台的额度"), stepper); dataCard.Children.Add(intervalRow); dataCard.Children.Add(Separator());

            string proxyMode = config.Proxy == "direct" ? "direct" : config.Proxy == "auto" || String.IsNullOrWhiteSpace(config.Proxy) ? "auto" : "custom";
            var proxyBox = new TextBox { Text = proxyMode == "custom" ? config.Proxy : "", Margin = new Thickness(0, 8, 0, 0), Visibility = proxyMode == "custom" ? Visibility.Visible : Visibility.Collapsed };
            System.Windows.Automation.AutomationProperties.SetName(proxyBox, "代理地址");
            var proxyHint = Hint(ProxyHint(proxyMode), 6);
            var proxyRow = Row();
            AddRow(proxyRow, FieldLabel("网络代理", null), Segmented(new[] { new[] { "auto", "自动" }, new[] { "direct", "直连" }, new[] { "custom", "自定义" } }, proxyMode, code => {
                proxyMode = code; proxyBox.Visibility = code == "custom" ? Visibility.Visible : Visibility.Collapsed; proxyHint.Text = ProxyHint(code);
                if (code == "custom" && proxyBox.Text.Length == 0) { proxyBox.Text = "http://127.0.0.1:7890"; proxyBox.Focus(); proxyBox.SelectAll(); }
            }));
            dataCard.Children.Add(proxyRow); dataCard.Children.Add(proxyBox); dataCard.Children.Add(proxyHint); dataCard.Children.Add(Separator());
            CheckBox hidden = SwitchRow("隐藏账户邮箱的部分字符", "截图或分享时更安全", config.HideAccounts);
            CheckBox autoStart = SwitchRow("登录 Windows 后自动运行", demo ? "演示模式下不可更改" : "启动后只驻留托盘", !demo && StartupEnabled()); autoStart.IsEnabled = !demo;
            dataCard.Children.Add(hidden); dataCard.Children.Add(Separator()); dataCard.Children.Add(autoStart);
            content.Children.Add(Card(dataCard));

            // Accounts and keys
            content.Children.Add(SectionTitle("账号与密钥"));
            var keyCard = new StackPanel();
            keyCard.Children.Add(CopilotLoginRow());
            string kimiRegion = config.KimiRegion, zaiRegion = config.ZaiRegion;
            var keyFields = new List<KeyField>();
            foreach (var spec in new[] {
                new { Id = "deepseek", Title = "DeepSeek", Hint = "查询 API 账户余额" },
                new { Id = "kimi", Title = "Kimi Code", Hint = "在 kimi.com/code/console 创建 API Key" },
                new { Id = "opencode", Title = "OpenCode Go", Hint = "OpenCode Go 订阅的 API Key" },
                new { Id = "zcode", Title = "ZCode · GLM 编码套餐", Hint = "智谱 / Z.ai 的 API Key" } }) {
                keyCard.Children.Add(Separator());
                FrameworkElement region = null;
                if (spec.Id == "kimi") region = Segmented(new[] { new[] { "china", "国内" }, new[] { "international", "国际" } }, kimiRegion, code => kimiRegion = code);
                if (spec.Id == "zcode") region = Segmented(new[] { new[] { "china", "国内" }, new[] { "global", "国际" } }, zaiRegion, code => zaiRegion = code);
                keyFields.Add(KeyRow(keyCard, spec.Id, spec.Title, spec.Hint, region));
            }
            keyCard.Children.Add(Hint("密钥以 Windows DPAPI 加密保存在本机 data 目录，只有当前 Windows 用户能解密；同名环境变量优先。", 12));
            content.Children.Add(Card(keyCard));

            // Custom providers
            content.Children.Add(SectionTitle("自定义平台"));
            customList = new StackPanel();
            RenderCustomList();
            content.Children.Add(Card(customList));

            // About
            content.Children.Add(SectionTitle("关于"));
            var about = new StackPanel();
            var name = Label("codeusagemonit V" + AppInfo.ShortVersion, 12.5, Ink); name.FontWeight = FontWeights.SemiBold; about.Children.Add(name);
            about.Children.Add(Hint("code用量监控", 3));
            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 8, 0, 0) };
            var folder = new Button { Style = Styled("LinkButton"), Content = "打开数据目录" };
            folder.Click += delegate { try { Process.Start("explorer.exe", "\"" + Store.Data + "\""); } catch { } };
            var guide = new Button { Style = Styled("LinkButton"), Content = "使用说明" };
            guide.Click += delegate { string path = Path.Combine(Store.Root, "使用说明.md"); try { if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } };
            links.Children.Add(folder); links.Children.Add(guide); about.Children.Add(links);
            content.Children.Add(Card(about));

            // Action bar
            var bar = new Grid { Margin = new Thickness(14, 0, 14, 10) };
            bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { Height = 1, Background = Hairline, Margin = new Thickness(-14, 0, -14, 10) }; Grid.SetColumnSpan(line, 3); bar.Children.Add(line);
            var error = new TextBlock { FontSize = 11, Foreground = WarnBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(error, 1); bar.Children.Add(error);
            var cancel = new Button { Style = Styled("SecondaryButton"), Content = "取消", Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += delegate { CloseSettings(); };
            Grid.SetRow(cancel, 1); Grid.SetColumn(cancel, 1); bar.Children.Add(cancel);
            var save = new Button { Style = Styled("PrimaryButton"), Content = "保存", IsDefault = false };
            System.Windows.Automation.AutomationProperties.SetName(save, "保存设置");
            Grid.SetRow(save, 1); Grid.SetColumn(save, 2); bar.Children.Add(save);
            Grid.SetRow(bar, 1); root.Children.Add(bar);

            save.Click += async delegate {
                if (!choices.Any(p => p.Value.IsChecked == true)) { error.Text = "请至少保留一个平台。"; return; }
                string proxyValue = proxyMode == "custom" ? proxyBox.Text.Trim() : proxyMode;
                if (proxyMode == "custom" && proxyValue.Length == 0) { error.Text = "请填写代理地址，例如 http://127.0.0.1:7890"; return; }
                try {
                    ProviderService.ResolveProxy(proxyValue);
                    config.Proxy = proxyValue; config.RefreshMinutes = minutes; config.HideAccounts = hidden.IsChecked == true;
                    config.Enabled = ProviderCatalog.All.Where(id => choices.ContainsKey(id) ? choices[id].IsChecked == true : config.Enabled.Contains(id)).ToArray();
                    config.KimiRegion = kimiRegion; config.ZaiRegion = zaiRegion;
                    config.AlwaysOnTop = onTop.IsChecked == true; config.HideOnDeactivate = autoHide.IsChecked == true; config.Material = "acrylic"; config.SurfaceOpacity = WindowFrame.ClampOpacity(1 - transparency / 100);
                    if (displaySize != originalDisplaySize) SetDisplaySize(displaySize);
                    bool thirdPartyOn = thirdPartySwitch.IsChecked == true && !config.ShowThirdParty; config.ShowThirdParty = thirdPartySwitch.IsChecked == true;
                    foreach (KeyField field in keyFields) {
                        if (demo) break;
                        if (field.Remove != null && field.Remove.IsChecked == true) { Store.SetProviderKey(field.Id, ""); states[field.Id] = new ProviderState { Id = field.Id }; }
                        else if (!String.IsNullOrWhiteSpace(field.Box.Password)) { Store.SetProviderKey(field.Id, field.Box.Password); states[field.Id] = new ProviderState { Id = field.Id }; }
                    }
                    if (!demo) SetStartup(autoStart.IsChecked == true);
                    frame.SetScale(zoom.Value, false); UpdatePin(); UpdateScaleLabel();
                    Store.Write("settings.json", config); refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
                    CloseSettings();
                    await Refresh();
                    if (thirdPartyOn) { var scan = ScanHistory(); }
                } catch (Exception ex) { error.Text = ex is ArgumentException ? ex.Message : "设置未能保存，请检查数据目录是否可写。"; }
            };
            return root;
        }
        private static string ProxyHint(string mode) {
            return mode == "auto" ? "使用 Windows 系统代理，其次读取 HTTPS_PROXY / HTTP_PROXY。只作用于本程序。" : mode == "direct" ? "不使用任何代理直接连接。" : "填写 http://主机:端口，例如 http://127.0.0.1:7890。";
        }
        private static TextBlock SectionTitle(string text) {
            var title = Label(text, 11, InkDim); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(2, 10, 0, 7); return title;
        }
        private static TextBlock Hint(string text, double top) {
            return new TextBlock { Text = text, FontSize = 10.5, Foreground = InkFaint, TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, top, 0, 0) };
        }
        private static FrameworkElement FieldLabel(string title, string hint) {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            stack.Children.Add(Label(title, 12.5, Ink));
            if (hint != null) { var sub = Label(hint, 10.5, InkFaint); sub.Margin = new Thickness(0, 2, 0, 0); stack.Children.Add(sub); }
            return stack;
        }
        private CheckBox SwitchRow(string title, string hint, bool value) {
            var box = new CheckBox { Style = Styled("Switch"), Content = FieldLabel(title, hint), IsChecked = value };
            System.Windows.Automation.AutomationProperties.SetName(box, title);
            return box;
        }
        private static Border Separator() { return new Border { Height = 1, Background = Hairline, Margin = new Thickness(0, 11, 0, 11) }; }
        private FrameworkElement Segmented(string[][] options, string current, Action<string> changed) {
            var grid = new UniformGrid { Rows = 1, Columns = options.Length };
            string group = "seg" + Guid.NewGuid().ToString("N");
            foreach (string[] option in options) {
                string code = option[0];
                var button = new RadioButton { Style = Styled("Segment"), Content = option[1], GroupName = group, IsChecked = code == current };
                System.Windows.Automation.AutomationProperties.SetName(button, option[1]);
                button.Checked += delegate { changed(code); };
                grid.Children.Add(button);
            }
            return new Border { Background = Brush("#0DFFFFFF"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = grid, VerticalAlignment = VerticalAlignment.Center };
        }

        // ── Accounts and keys ─────────────────────────────────────────────
        private sealed class KeyField { public string Id; public PasswordBox Box; public CheckBox Remove; }
        private KeyField KeyRow(StackPanel card, string id, string title, string hint, FrameworkElement region) {
            var head = Row();
            AddRow(head, FieldLabel(title, hint), region ?? (UIElement)new Border());
            card.Children.Add(head);
            var field = new KeyField { Id = id, Box = new PasswordBox { IsEnabled = !demo, Margin = new Thickness(0, 8, 0, 0) } };
            System.Windows.Automation.AutomationProperties.SetName(field.Box, title + " API Key");
            card.Children.Add(field.Box);
            string env = demo ? "" : Store.KeyEnvironmentName(id, config); bool saved = !demo && Store.HasSavedKey(id);
            card.Children.Add(Hint(demo ? "演示模式不读取或保存密钥。" : env.Length > 0 ? "正在使用环境变量 " + env + "（优先于这里保存的密钥）。" : saved ? "已保存。留空则保留，填写新密钥会替换它。" : "未设置。", 5));
            if (saved) { field.Remove = SwitchRow("移除已保存的密钥", null, false); field.Remove.Margin = new Thickness(0, 8, 0, 0); card.Children.Add(field.Remove); }
            return field;
        }
        // GitHub device flow: show a one-time code, open github.com/login/device, poll.
        private FrameworkElement CopilotLoginRow() {
            var stack = new StackPanel();
            var head = Row(); var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            string existing = demo ? "" : ProviderService.CopilotToken();
            var status = Hint(demo ? "演示模式不登录。" : existing.Length > 0 ? (Store.HasSavedKey("copilot") ? "已登录（本程序保存的 GitHub 授权）。" : "已发现 Copilot 客户端保存的 GitHub 授权，可直接使用。") : "未登录。只申请 read:user 权限，用于读取 Copilot 额度。", 4);
            var login = new Button { Style = Styled("SecondaryButton"), Content = existing.Length > 0 ? "重新登录" : "使用 GitHub 登录", IsEnabled = !demo };
            var logout = new Button { Style = Styled("LinkButton"), Content = "退出", Visibility = Store.HasSavedKey("copilot") && !demo ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 0, 4, 0) };
            buttons.Children.Add(logout); buttons.Children.Add(login);
            AddRow(head, FieldLabel("GitHub Copilot", "OAuth 设备码登录"), buttons);
            stack.Children.Add(head); stack.Children.Add(status);
            var device = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
            var code = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Ink, FontFamily = new FontFamily("Consolas, Segoe UI") };
            var open = new Button { Style = Styled("PrimaryButton"), Content = "复制代码并打开 GitHub", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            var cancel = new Button { Style = Styled("LinkButton"), Content = "取消", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 4, 0, 0) };
            device.Children.Add(Hint("在浏览器里输入下面的代码并授权：", 0)); device.Children.Add(code); device.Children.Add(open); device.Children.Add(cancel);
            stack.Children.Add(device);
            bool cancelled = false; ProviderService.DeviceLogin pending = null;
            stack.Unloaded += delegate { cancelled = true; };
            open.Click += delegate {
                if (pending == null) return;
                try { Clipboard.SetText(pending.UserCode); } catch { }
                try { Process.Start(new ProcessStartInfo(pending.VerificationUri) { UseShellExecute = true }); } catch { }
            };
            cancel.Click += delegate { cancelled = true; device.Visibility = Visibility.Collapsed; login.IsEnabled = true; status.Text = "已取消登录。"; };
            logout.Click += delegate { Store.SetProviderKey("copilot", ""); states["copilot"] = new ProviderState { Id = "copilot" }; logout.Visibility = Visibility.Collapsed; login.Content = "使用 GitHub 登录"; status.Text = "已退出。" + (ProviderService.CopilotToken().Length > 0 ? "仍会使用 Copilot 客户端保存的授权。" : ""); };
            login.Click += async delegate {
                cancelled = false; login.IsEnabled = false; status.Text = "正在向 GitHub 申请设备码…";
                try {
                    using (var service = new ProviderService(config)) {
                        pending = await service.StartCopilotLogin();
                        if (cancelled) return;
                        code.Text = pending.UserCode; device.Visibility = Visibility.Visible; status.Text = "等待你在 GitHub 上授权…";
                        DateTime until = DateTime.UtcNow.AddSeconds(pending.ExpiresIn);
                        while (!cancelled && DateTime.UtcNow < until) {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, pending.Interval)));
                            if (cancelled) break;
                            string token = await service.PollCopilotLogin(pending);
                            if (cancelled || quitting) return;
                            if (token == null) continue;
                            Store.SetProviderKey("copilot", token); states["copilot"] = new ProviderState { Id = "copilot" };
                            device.Visibility = Visibility.Collapsed; status.Text = "登录成功。保存设置并启用 Copilot 后即可显示额度。"; logout.Visibility = Visibility.Visible; login.Content = "重新登录";
                            break;
                        }
                        if (!cancelled && device.Visibility == Visibility.Visible) { device.Visibility = Visibility.Collapsed; status.Text = "设备码已过期，请重新登录。"; }
                    }
                } catch (Exception e) { device.Visibility = Visibility.Collapsed; status.Text = e is ProviderException ? e.Message : "无法连接 GitHub，请检查网络或代理设置。"; }
                finally { login.IsEnabled = true; pending = null; }
            };
            return stack;
        }

        // ── Custom providers ──────────────────────────────────────────────
        private StackPanel customList;
        private FrameworkElement customEditor;
        private void RenderCustomList() {
            if (customList == null) return;
            customList.Children.Clear();
            customList.Children.Add(Hint("用一个返回 JSON 的 GET 接口接入任意服务（中转站后台、自建网关、公司内部额度接口等），用字段路径映射额度和余额。", 0));
            foreach (CustomProvider provider in ProviderCatalog.Custom.Values.OrderBy(p => p.Name)) {
                CustomProvider captured = provider;
                var row = Row(); row.Margin = new Thickness(0, 10, 0, 0);
                var left = new StackPanel { Orientation = Orientation.Horizontal };
                FrameworkElement icon = Icon(provider.Id, 14); icon.Margin = new Thickness(0, 0, 8, 0); left.Children.Add(icon);
                var text = new TextBlock { FontSize = 12.5, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 };
                text.Inlines.Add(provider.Name); text.Inlines.Add(new System.Windows.Documents.Run("  " + provider.Host) { Foreground = InkFaint, FontSize = 10.5 });
                left.Children.Add(text);
                var edit = new Button { Style = Styled("LinkButton"), Content = "编辑", IsEnabled = !demo };
                edit.Click += delegate { OpenCustomEditor(captured); };
                AddRow(row, left, edit); customList.Children.Add(row);
            }
            var add = new Button { Style = Styled("SecondaryButton"), Content = "添加自定义平台", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), IsEnabled = !demo };
            add.Click += delegate { OpenCustomEditor(null); };
            customList.Children.Add(add);
            if (demo) customList.Children.Add(Hint("演示模式不保存自定义平台。", 6));
        }
        // Back/Esc: leave the custom editor first, then the settings page.
        private void BackFromSettings() { if (customEditor != null) CloseCustomEditor(); else CloseSettings(); }
        private void CloseCustomEditor() {
            if (customEditor == null) return;
            customEditor = null; settingsContentHost.Children.Clear(); settingsContentHost.Children.Add(settingsView); settingsTitle.Text = "偏好设置"; settingsBack.Visibility = Visibility.Collapsed; RenderCustomList();
        }
        private void OpenCustomEditor(CustomProvider existing) {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var content = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(14, 6, 8, 8), Margin = new Thickness(0, 0, 4, 0) });
            content.Children.Add(SectionTitle(existing == null ? "添加自定义平台" : "编辑 · " + existing.Name));
            var card = new StackPanel();
            card.Children.Add(Hint("用 JSON 描述接口和字段路径。url 必须是 https（本机或局域网可以用 http）；只发 GET、不跟随重定向、响应上限 1 MB。", 0));
            var json = new TextBox { Text = existing != null ? existing.Source : CustomProviders.Template, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), FontSize = 11.5, Height = 290, VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            json.Padding = new Thickness(10, 8, 10, 8);
            System.Windows.Automation.AutomationProperties.SetName(json, "平台定义 JSON");
            card.Children.Add(json);
            card.Children.Add(Hint("字段：name、url、auth（none / bearer / x-api-key / token / header + header 名称）、windows[]（label + usedPercent 或 remainingPercent 或 used/remaining + limit；可选 ratio、resetsAt、resetInSeconds、windowHours）、balance（amount + currency）、plan、account。路径写法：data.items[0].used。", 8));
            card.Children.Add(Separator());
            var secretHead = Row();
            bool hasSecret = existing != null && Store.HasSavedKey(existing.Id);
            AddRow(secretHead, FieldLabel("密钥", "按 auth 方式放进请求头，DPAPI 加密保存"), Label(hasSecret ? "已保存" : "未保存", 11, InkFaint));
            card.Children.Add(secretHead);
            var secret = new PasswordBox { Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(secret, "自定义平台密钥");
            card.Children.Add(secret);
            card.Children.Add(Hint(hasSecret ? "留空则保留已保存的密钥。" : "auth 为 none 时可以留空。", 5));
            content.Children.Add(Card(card));
            var result = new TextBlock { FontSize = 11, Foreground = InkDim, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(2, 2, 2, 8) };
            content.Children.Add(result);

            var bar = new Grid { Margin = new Thickness(14, 0, 14, 10) };
            bar.ColumnDefinitions.Add(new ColumnDefinition()); for (int i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { Height = 1, Background = Hairline, Margin = new Thickness(-14, 0, -14, 10) }; Grid.SetColumnSpan(line, 4); bar.Children.Add(line);
            var delete = new Button { Style = Styled("LinkButton"), Content = "删除此平台", Foreground = WarnBrush, HorizontalAlignment = HorizontalAlignment.Left, Visibility = existing == null ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(-6, 0, 0, 0) };
            Grid.SetRow(delete, 1); bar.Children.Add(delete);
            var test = new Button { Style = Styled("SecondaryButton"), Content = "测试", Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Style = Styled("SecondaryButton"), Content = "取消", Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Style = Styled("PrimaryButton"), Content = "保存" };
            System.Windows.Automation.AutomationProperties.SetName(save, "保存自定义平台");
            foreach (var pair in new[] { new { B = test, C = 1 }, new { B = cancel, C = 2 }, new { B = save, C = 3 } }) { Grid.SetRow(pair.B, 1); Grid.SetColumn(pair.B, pair.C); bar.Children.Add(pair.B); }
            Grid.SetRow(bar, 1); root.Children.Add(bar);

            cancel.Click += delegate { CloseCustomEditor(); };
            test.Click += async delegate {
                CustomProvider parsed;
                try { parsed = CustomProviders.Parse(json.Text); } catch (ArgumentException e) { result.Foreground = WarnBrush; result.Text = e.Message; return; }
                string key = secret.Password.Length > 0 ? secret.Password : existing != null ? Store.SavedKey(existing.Id) : "";
                test.IsEnabled = false; result.Foreground = InkDim; result.Text = "正在请求 " + parsed.Host + " …";
                try {
                    ProviderState state;
                    using (var service = new ProviderService(config)) state = await service.FetchCustom(parsed, key);
                    var lines = state.Quotas.Select(q => "✓ " + q.Label + "：剩余 " + q.Remaining.ToString("0.#") + "%" + (q.ResetUtc.Length > 0 ? "，" + Countdown(q.ResetUtc) : "")).ToList();
                    lines.AddRange(state.Balances.Select(b => "✓ 余额：" + b.Currency + " " + b.Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)));
                    if (state.Plan.Length > 0 && state.Plan != "自定义") lines.Add("✓ 套餐：" + state.Plan);
                    if (state.Account.Length > 0) lines.Add("✓ 账户：" + state.Account);
                    int missing = parsed.Windows.Count - state.Quotas.Count;
                    if (missing > 0) lines.Add("有 " + missing + " 个窗口在响应中找不到对应字段，将不会显示。");
                    result.Foreground = GoodBrush; result.Text = String.Join("\n", lines);
                } catch (Exception e) { result.Foreground = WarnBrush; result.Text = e is ProviderException ? e.Message : e is TaskCanceledException ? "请求超时（15 秒）" : "请求失败：" + e.GetType().Name; }
                finally { test.IsEnabled = true; }
            };
            save.Click += delegate {
                CustomProvider parsed;
                try { parsed = CustomProviders.Parse(json.Text); } catch (ArgumentException e) { result.Foreground = WarnBrush; result.Text = e.Message; return; }
                if (ProviderCatalog.Ids.Contains(parsed.Id) || (ProviderCatalog.Custom.ContainsKey(parsed.Id) && (existing == null || existing.Id != parsed.Id))) { result.Foreground = WarnBrush; result.Text = "已有同样 id 的平台（" + parsed.Id + "），请在 JSON 里指定不同的 id。"; return; }
                try {
                    var list = ProviderCatalog.Custom.Values.Where(p => existing == null || p.Id != existing.Id).ToList(); list.Add(parsed);
                    CustomProviders.Save(list);
                    if (existing != null && existing.Id != parsed.Id) {
                        // Renamed: carry the saved secret and the enabled flag over to the new id.
                        string old = Store.SavedKey(existing.Id); if (old.Length > 0 && secret.Password.Length == 0) Store.SetProviderKey(parsed.Id, old);
                        Store.SetProviderKey(existing.Id, ""); states.Remove(existing.Id); config.Enabled = config.Enabled.Where(id => id != existing.Id).ToArray();
                    }
                    if (secret.Password.Length > 0) Store.SetProviderKey(parsed.Id, secret.Password);
                    RegisterCustomProviders(list);
                    states[parsed.Id] = new ProviderState { Id = parsed.Id };
                    if (!config.Enabled.Contains(parsed.Id)) config.Enabled = config.Enabled.Concat(new[] { parsed.Id }).ToArray();
                    Store.Write("settings.json", config);
                    CloseCustomEditor();
                    var ignored = Refresh();
                } catch (Exception e) { result.Foreground = WarnBrush; result.Text = "保存失败：" + e.Message; }
            };
            bool confirm = false;
            delete.Click += delegate {
                if (!confirm) { confirm = true; delete.Content = "再次点击确认删除"; return; }
                var list = ProviderCatalog.Custom.Values.Where(p => p.Id != existing.Id).ToList();
                try { CustomProviders.Save(list); Store.SetProviderKey(existing.Id, ""); } catch { }
                RegisterCustomProviders(list); states.Remove(existing.Id);
                config.Enabled = config.Enabled.Where(id => id != existing.Id).ToArray(); SaveConfig();
                CloseCustomEditor();
            };
            customEditor = root;
            settingsContentHost.Children.Clear(); settingsContentHost.Children.Add(root); settingsTitle.Text = "自定义平台"; settingsBack.Visibility = Visibility.Visible;
        }
    }
}
