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
    // Settings open in their own window (same surface, cards and controls as the panel), so
    // the panel keeps whatever display size it has. Changes apply on Save; Esc / close cancels.
    public sealed partial class MonitorPanel {
        private Window settingsWindow;
        private Border settingsSurface;
        private Grid settingsHost;
        private TextBlock settingsTitle;
        private Button settingsBack;
        private FrameworkElement settingsView;
        private static readonly int[] RefreshSteps = { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };

        private void OpenSettings() {
            if (settingsWindow != null) {
                if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
                settingsWindow.Activate(); return;
            }
            settingsWindow = BuildSettingsWindow();
            settingsView = BuildSettings();
            settingsHost.Children.Add(settingsView);
            settingsButton.Foreground = AccentBrush;
            settingsWindow.Show(); settingsWindow.Activate();
        }
        // Cancel / Esc / the window's close button: drop unsaved changes and previews.
        private void CloseSettings() {
            if (settingsWindow == null) return;
            Window closing = settingsWindow;
            settingsWindow = null; settingsView = null; customEditor = null; customList = null; settingsHost = null;
            ApplyAppearance(); // drop an unsaved transparency / picture preview
            settingsButton.ClearValue(Control.ForegroundProperty);
            closing.Close();
            if (activeSize != config.DisplaySize) ApplyDisplaySize(config.DisplaySize); else Render();
        }
        private Window BuildSettingsWindow() {
            double s = config.UiScale;
            var w = new Window {
                Title = "codeusagemonit 设置", Width = 460 * s, Height = 720 * s, MinWidth = 400 * s, MinHeight = 480 * s,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResize, Background = Brushes.Transparent, ShowInTaskbar = true,
                FontFamily = window.FontFamily, FontSize = window.FontSize, Foreground = window.Foreground, UseLayoutRounding = true, SnapsToDevicePixels = true,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            try { w.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(Store.Root, "app.ico"))); } catch { }
            System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(7), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
            // Beside the panel when there is room, otherwise centred on the panel's screen.
            Rect area = SystemParameters.WorkArea;
            double left = window.IsVisible ? window.Left - w.Width - 12 : area.Left + (area.Width - w.Width) / 2;
            if (left < area.Left + 8) left = window.IsVisible && window.Left + window.ActualWidth + 12 + w.Width <= area.Right ? window.Left + window.ActualWidth + 12 : area.Left + (area.Width - w.Width) / 2;
            w.Left = Math.Max(area.Left, left); w.Top = Math.Max(area.Top, area.Top + (area.Height - w.Height) / 2);
            if (w.Height > area.Height) w.Height = area.Height;

            settingsSurface = new Border { BorderBrush = Brush("#26FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(WindowFrame.ParseTint(config.SurfaceColor)) };
            var root = new Grid { LayoutTransform = new ScaleTransform(s, s) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); root.RowDefinitions.Add(new RowDefinition());
            var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(10, 0, 8, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsBack = new Button { Style = Styled("IconButton"), Content = "", Visibility = Visibility.Collapsed, ToolTip = "返回（Esc）", Margin = new Thickness(-4, 0, 2, 0) };
            System.Windows.Automation.AutomationProperties.SetName(settingsBack, "返回");
            settingsBack.Click += delegate { BackFromSettings(); };
            head.Children.Add(settingsBack);
            settingsTitle = Label("设置", 12.5, Ink); settingsTitle.FontWeight = FontWeights.SemiBold; settingsTitle.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(settingsTitle, 1); head.Children.Add(settingsTitle);
            var close = new Button { Style = Styled("IconButton"), Content = "", FontSize = 10.5, ToolTip = "关闭（不保存）" };
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭设置");
            close.Click += delegate { CloseSettings(); };
            Grid.SetColumn(close, 2); head.Children.Add(close);
            head.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) { try { w.DragMove(); } catch (InvalidOperationException) { } } };
            root.Children.Add(head);
            root.Children.Add(new Border { Height = 1, Background = Hairline, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0) });
            settingsHost = new Grid(); Grid.SetRow(settingsHost, 1); root.Children.Add(settingsHost);
            settingsSurface.Child = root; w.Content = settingsSurface;
            w.SourceInitialized += delegate { WindowFrame.ApplyBackdrop(w, settingsSurface, "acrylic", config.SurfaceOpacity, WindowFrame.ParseTint(config.SurfaceColor)); };
            w.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape) { BackFromSettings(); e.Handled = true; } };
            // Closed from outside (Alt+F4, taskbar): same as cancel.
            w.Closed += delegate { if (settingsWindow == w) CloseSettings(); };
            return w;
        }
        // Colour / transparency / picture preview until Save or Cancel (the settings window
        // follows the colour and transparency, the picture is shown on the panel only).
        private void PreviewAppearance(double alpha, string picture, string fit, double dim, Color tint) {
            ApplyAppearance(alpha, picture, fit, dim, tint);
            if (settingsWindow != null) WindowFrame.ApplyBackdrop(settingsWindow, settingsSurface, "acrylic", alpha, tint);
        }
        // Five pages share one Save / Cancel bar: 通用 (language, start-up, platforms, window,
        // look), 认证 (accounts, keys, custom platforms), 数据 (devices, SQL, backups, WebDAV),
        // 高级 (refresh, proxy, prices, maintenance) and 关于 (version, updates, links).
        // Buttons on the 数据 page (export, backup, sync…) act at once; everything else on Save.
        private string settingsTab = "general";
        private static readonly string[][] SettingsTabs = { new[] { "general", "通用" }, new[] { "accounts", "认证" }, new[] { "data", "数据" }, new[] { "advanced", "高级" }, new[] { "about", "关于" } };
        private FrameworkElement BuildSettings() {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pages = SettingsTabs.ToDictionary(t => t[0], t => new StackPanel());
            if (!pages.ContainsKey(settingsTab)) settingsTab = "general";
            var scroll = new ScrollViewer { Content = pages[settingsTab], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(14, 2, 8, 8), Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            FrameworkElement tabs = Segmented(SettingsTabs.Select(t => new[] { t[0], I18n.T(t[1]) }).ToArray(), settingsTab, code => { settingsTab = code; scroll.Content = pages[code]; scroll.ScrollToTop(); });
            tabs.Margin = new Thickness(14, 10, 14, 4); root.Children.Add(tabs);
            StackPanel general = pages["general"], accounts = pages["accounts"], dataPage = pages["data"], advanced = pages["advanced"], aboutPage = pages["about"];

            // ── 通用 ── Language and start-up
            general.Children.Add(SectionTitle(I18n.T("语言与启动")));
            var startCard = new StackPanel();
            string language = config.Language == "zh" || config.Language == "en" ? config.Language : "auto";
            var languageRow = Row();
            AddRow(languageRow, FieldLabel(I18n.T("界面语言"), I18n.T("“跟随系统”按 Windows 的显示语言")), Segmented(new[] { new[] { "auto", I18n.T("跟随系统") }, new[] { "zh", "中文" }, new[] { "en", "English" } }, language, code => language = code));
            startCard.Children.Add(languageRow); startCard.Children.Add(Separator());
            CheckBox autoStart = SwitchRow(I18n.T("登录 Windows 后自动运行"), demo ? I18n.T("演示模式下不可更改") : Program.SideBySide ? I18n.T("并行运行的副本不可更改") : I18n.T("启动后只驻留托盘"), !demo && StartupEnabled()); autoStart.IsEnabled = !demo && !Program.SideBySide;
            CheckBox hidden = SwitchRow(I18n.T("隐藏账户邮箱的部分字符"), I18n.T("截图或分享时更安全"), config.HideAccounts);
            startCard.Children.Add(autoStart); startCard.Children.Add(Separator()); startCard.Children.Add(hidden);
            general.Children.Add(Card(startCard));

            // Providers
            general.Children.Add(SectionTitle(I18n.T("显示的平台")));
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
            providers.Children.Add(Hint(I18n.T("关闭的平台不再联网查询，也不出现在标签栏。"), 10));
            providers.Children.Add(Separator());
            CheckBox thirdPartySwitch = SwitchRow(I18n.T("统计第三方 API 用量"), I18n.T("按本机日志统计经中转站的 Token；服务商的限额无法得知"), config.ShowThirdParty);
            providers.Children.Add(thirdPartySwitch);
            general.Children.Add(Card(providers));

            // Window
            general.Children.Add(SectionTitle(I18n.T("窗口")));
            var windowCard = new StackPanel();
            // One window, four sizes: small / medium / large sit on the desktop; full is the panel.
            string displaySize = config.DisplaySize, initialSize = config.DisplaySize;
            var sizeRow = Row();
            AddRow(sizeRow, FieldLabel(I18n.T("显示尺寸"), I18n.T("拖动边缘可调整每种尺寸的大小")), Segmented(DisplaySizes.Select(s => new[] { s, SizeName(s) }).ToArray(), displaySize, code => displaySize = code));
            windowCard.Children.Add(sizeRow); windowCard.Children.Add(Separator());
            CheckBox onTop = SwitchRow(I18n.T("置于其他窗口上方"), I18n.T("默认关闭；关闭时小 / 中 / 大尺寸位于桌面层"), config.AlwaysOnTop);
            CheckBox autoHide = SwitchRow(I18n.T("点击其他窗口时自动收起"), I18n.T("仅完整面板；标题栏的图钉按钮也可以切换"), config.HideOnDeactivate);
            windowCard.Children.Add(onTop); windowCard.Children.Add(Separator()); windowCard.Children.Add(autoHide); windowCard.Children.Add(Separator());
            var zoomRow = Row(); var zoomValue = Label((config.UiScale * 100).ToString("0") + "%", 12, InkDim); Tabular(zoomValue);
            AddRow(zoomRow, FieldLabel(I18n.T("界面缩放"), I18n.T("也可以在面板上按 Ctrl + 滚轮")), zoomValue); windowCard.Children.Add(zoomRow);
            var zoom = new Slider { Minimum = .8, Maximum = 1.4, Value = config.UiScale, TickFrequency = .05, IsSnapToTickEnabled = true, SmallChange = .05, LargeChange = .1, Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(zoom, I18n.T("界面缩放"));
            zoom.ValueChanged += delegate { zoomValue.Text = (zoom.Value * 100).ToString("0") + "%"; };
            windowCard.Children.Add(zoom); windowCard.Children.Add(Separator());
            var placeRow = Row(); var reset = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("重置") };
            reset.Click += delegate { frame.ResetPosition(); };
            AddRow(placeRow, FieldLabel(I18n.T("窗口位置与尺寸"), I18n.T("当前显示尺寸回到默认位置和大小")), reset); windowCard.Children.Add(placeRow);
            general.Children.Add(Card(windowCard));

            // Appearance: surface colour and interface transparency (0 = opaque) — together in
            // the palette, transparency also as a slider — and an optional background picture.
            general.Children.Add(SectionTitle(I18n.T("外观")));
            var look = new StackPanel();
            double transparency = Math.Round((1 - config.SurfaceOpacity) * 100);
            Color tint = WindowFrame.ParseTint(config.SurfaceColor);
            string picture = SavedBackgroundPath(config) ?? "", fit = config.BackgroundFit; double dim = config.BackgroundDim; bool pictureChanged = false;
            Action preview = () => PreviewAppearance(1 - transparency / 100, picture, fit, dim, tint);
            var colorRow = Row();
            var swatchFill = new Border { CornerRadius = new CornerRadius(5) };
            var swatch = new Grid { Width = 40, Height = 22, Margin = new Thickness(0, 0, 8, 0) };
            swatch.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Checkerboard() }); swatch.Children.Add(swatchFill);
            swatch.Children.Add(new Border { CornerRadius = new CornerRadius(5), BorderBrush = Brush("#33FFFFFF"), BorderThickness = new Thickness(1) });
            var swatchText = Label("", 11.5, Ink); Tabular(swatchText); swatchText.VerticalAlignment = VerticalAlignment.Center;
            var swatchContent = new StackPanel { Orientation = Orientation.Horizontal }; swatchContent.Children.Add(swatch); swatchContent.Children.Add(swatchText);
            swatchContent.Children.Add(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 8, Foreground = InkFaint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) });
            var colorButton = new Button { Style = Styled("SecondaryButton"), Content = swatchContent, Padding = new Thickness(6, 4, 10, 4), ToolTip = I18n.T("打开调色盘：颜色与透明度") };
            System.Windows.Automation.AutomationProperties.SetName(colorButton, I18n.T("背景颜色"));
            AddRow(colorRow, FieldLabel(I18n.T("背景颜色"), I18n.T("调色盘选颜色和透明度，深色最清楚")), colorButton);
            look.Children.Add(colorRow);
            var transparencyValue = Label(transparency.ToString("0") + "%", 12, InkDim); Tabular(transparencyValue);
            var transparencyRow = Row(); transparencyRow.Margin = new Thickness(0, 12, 0, 0);
            AddRow(transparencyRow, FieldLabel(I18n.T("界面透明度"), I18n.T("0 为不透明，数值越大越能看到后面的桌面")), transparencyValue);
            look.Children.Add(transparencyRow);
            var transparencySlider = new Slider { Minimum = 0, Maximum = 100, Value = transparency, SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(transparencySlider, I18n.T("界面透明度"));
            Action refreshSwatch = () => { swatchFill.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round((1 - transparency / 100) * 255), tint.R, tint.G, tint.B)); swatchText.Text = Hex(tint); };
            transparencySlider.ValueChanged += delegate { transparency = Math.Round(transparencySlider.Value); transparencyValue.Text = transparency.ToString("0") + "%"; refreshSwatch(); preview(); };
            colorButton.Click += delegate {
                ShowColorPicker(colorButton, tint, transparency, (c, t) => {
                    tint = c;
                    if (Math.Abs(t - transparency) > .1) transparencySlider.Value = t; // previews through the slider
                    else { refreshSwatch(); preview(); }
                });
            };
            refreshSwatch();
            look.Children.Add(transparencySlider); look.Children.Add(Separator());
            var pictureRow = new Grid();
            pictureRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); pictureRow.ColumnDefinitions.Add(new ColumnDefinition()); pictureRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var thumb = new Border { Width = 64, Height = 40, CornerRadius = new CornerRadius(6), BorderBrush = Brush("#26FFFFFF"), BorderThickness = new Thickness(1), Background = Brush("#0AFFFFFF"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var pictureLabel = FieldLabel(I18n.T("背景图片"), picture.Length > 0 ? I18n.T("已设置 · 复制保存在 data 目录") : I18n.T("未设置 · 支持 PNG / JPG / BMP / GIF / WebP"));
            var pictureButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var choose = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("选择图片…") };
            var clear = new Button { Style = Styled("LinkButton"), Content = I18n.T("移除"), Margin = new Thickness(4, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(choose, I18n.T("选择背景图片")); System.Windows.Automation.AutomationProperties.SetName(clear, I18n.T("移除背景图片"));
            pictureButtons.Children.Add(choose); pictureButtons.Children.Add(clear);
            pictureRow.Children.Add(thumb); Grid.SetColumn(pictureLabel, 1); pictureRow.Children.Add(pictureLabel); Grid.SetColumn(pictureButtons, 2); pictureRow.Children.Add(pictureButtons);
            look.Children.Add(pictureRow);
            var fitRow = Row(); fitRow.Margin = new Thickness(0, 12, 0, 0);
            AddRow(fitRow, FieldLabel(I18n.T("填充方式"), null), Segmented(new[] { new[] { "fill", I18n.T("填满") }, new[] { "fit", I18n.T("适应") }, new[] { "tile", I18n.T("平铺") } }, fit, code => { fit = code; preview(); }));
            look.Children.Add(fitRow);
            var dimValue = Label((dim * 100).ToString("0") + "%", 12, InkDim); Tabular(dimValue);
            var dimRow = Row(); dimRow.Margin = new Thickness(0, 12, 0, 0);
            AddRow(dimRow, FieldLabel(I18n.T("图片压暗"), I18n.T("让图片上的文字更清楚")), dimValue); look.Children.Add(dimRow);
            var dimSlider = new Slider { Minimum = 0, Maximum = 85, Value = dim * 100, SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 8, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(dimSlider, I18n.T("图片压暗"));
            dimSlider.ValueChanged += delegate { dim = Math.Round(dimSlider.Value) / 100; dimValue.Text = (dim * 100).ToString("0") + "%"; preview(); };
            look.Children.Add(dimSlider);
            Action refreshPicture = () => {
                var image = LoadPicture(picture);
                thumb.Background = image == null ? Brush("#0AFFFFFF") : (Brush)new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                clear.Visibility = picture.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                fitRow.Visibility = dimRow.Visibility = dimSlider.Visibility = picture.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                ((TextBlock)((StackPanel)pictureLabel).Children[1]).Text = picture.Length == 0 ? I18n.T("未设置 · 支持 PNG / JPG / BMP / GIF / WebP") : pictureChanged ? I18n.T("预览中 · 保存后复制到 data 目录") : I18n.T("已设置 · 复制保存在 data 目录");
            };
            choose.Click += delegate {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = I18n.T("选择背景图片"), Filter = I18n.T("图片") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
                if (dialog.ShowDialog(settingsWindow) != true) return;
                if (LoadPicture(dialog.FileName) == null) { MessageBox.Show(settingsWindow, I18n.T("无法读取这张图片（格式不支持或超过 40 MB）。"), "codeusagemonit"); return; }
                picture = dialog.FileName; pictureChanged = true; refreshPicture(); preview();
            };
            clear.Click += delegate { picture = ""; pictureChanged = true; refreshPicture(); preview(); };
            refreshPicture();
            general.Children.Add(Card(look));

            // ── 认证 ── Accounts and keys
            accounts.Children.Add(SectionTitle(I18n.T("账号与密钥")));
            var keyCard = new StackPanel();
            keyCard.Children.Add(CopilotLoginRow());
            string kimiRegion = config.KimiRegion, zaiRegion = config.ZaiRegion;
            var keyFields = new List<KeyField>();
            foreach (var spec in new[] {
                new { Id = "deepseek", Title = "DeepSeek", Hint = I18n.T("查询 API 账户余额") },
                new { Id = "kimi", Title = "Kimi Code", Hint = I18n.T("在 kimi.com/code/console 创建 API Key") },
                new { Id = "opencode", Title = "OpenCode Go", Hint = I18n.T("OpenCode Go 订阅的 API Key") },
                new { Id = "zcode", Title = I18n.T("ZCode · GLM 编码套餐"), Hint = I18n.T("智谱 / Z.ai 的 API Key") } }) {
                keyCard.Children.Add(Separator());
                FrameworkElement region = null;
                if (spec.Id == "kimi") region = Segmented(new[] { new[] { "china", I18n.T("国内") }, new[] { "international", I18n.T("国际") } }, kimiRegion, code => kimiRegion = code);
                if (spec.Id == "zcode") region = Segmented(new[] { new[] { "china", I18n.T("国内") }, new[] { "global", I18n.T("国际") } }, zaiRegion, code => zaiRegion = code);
                keyFields.Add(KeyRow(keyCard, spec.Id, spec.Title, spec.Hint, region));
            }
            keyCard.Children.Add(Hint(I18n.T("密钥以 Windows DPAPI 加密保存在本机 data 目录，只有当前 Windows 用户能解密；同名环境变量优先。"), 12));
            accounts.Children.Add(Card(keyCard));
            // Custom providers
            accounts.Children.Add(SectionTitle(I18n.T("自定义平台")));
            customList = new StackPanel();
            RenderCustomList();
            accounts.Children.Add(Card(customList));

            // ── 数据 ── devices, SQL, backups, WebDAV (SettingsPages.cs)
            DataFields dataFields = BuildDataPage(dataPage);

            // ── 高级 ── Refresh and network
            advanced.Children.Add(SectionTitle(I18n.T("刷新与网络")));
            var netCard = new StackPanel();
            int minutes = config.RefreshMinutes;
            var intervalRow = Row(); var stepper = new StackPanel { Orientation = Orientation.Horizontal };
            var minus = new Button { Style = Styled("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = I18n.T("更频繁") };
            var value = Label(I18n.T("{0} 分钟", minutes), 12, Ink); value.MinWidth = 58; value.TextAlignment = TextAlignment.Center; Tabular(value);
            var plus = new Button { Style = Styled("IconButton"), Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = I18n.T("更少") };
            List<int> steps = RefreshSteps.Concat(new[] { minutes }).Distinct().OrderBy(n => n).ToList();
            minus.Click += delegate { int i = steps.IndexOf(minutes); if (i > 0) minutes = steps[i - 1]; value.Text = I18n.T("{0} 分钟", minutes); };
            plus.Click += delegate { int i = steps.IndexOf(minutes); if (i < steps.Count - 1) minutes = steps[i + 1]; value.Text = I18n.T("{0} 分钟", minutes); };
            stepper.Children.Add(minus); stepper.Children.Add(value); stepper.Children.Add(plus);
            AddRow(intervalRow, FieldLabel(I18n.T("自动刷新"), I18n.T("每次刷新会查询所有已启用平台的额度")), stepper); netCard.Children.Add(intervalRow); netCard.Children.Add(Separator());
            string proxyMode = config.Proxy == "direct" ? "direct" : config.Proxy == "auto" || String.IsNullOrWhiteSpace(config.Proxy) ? "auto" : "custom";
            var proxyBox = new TextBox { Text = proxyMode == "custom" ? config.Proxy : "", Margin = new Thickness(0, 8, 0, 0), Visibility = proxyMode == "custom" ? Visibility.Visible : Visibility.Collapsed };
            System.Windows.Automation.AutomationProperties.SetName(proxyBox, I18n.T("代理地址"));
            var proxyHint = Hint(ProxyHint(proxyMode), 6);
            var proxyRow = Row();
            AddRow(proxyRow, FieldLabel(I18n.T("网络代理"), null), Segmented(new[] { new[] { "auto", I18n.T("自动") }, new[] { "direct", I18n.T("直连") }, new[] { "custom", I18n.T("自定义") } }, proxyMode, code => {
                proxyMode = code; proxyBox.Visibility = code == "custom" ? Visibility.Visible : Visibility.Collapsed; proxyHint.Text = ProxyHint(code);
                if (code == "custom" && proxyBox.Text.Length == 0) { proxyBox.Text = "http://127.0.0.1:7890"; proxyBox.Focus(); proxyBox.SelectAll(); }
            }));
            netCard.Children.Add(proxyRow); netCard.Children.Add(proxyBox); netCard.Children.Add(proxyHint);
            advanced.Children.Add(Card(netCard));
            // Prices
            advanced.Children.Add(SectionTitle(I18n.T("价目")));
            Pricing.EnsureLoaded();
            CheckBox priceSyncSwitch = SwitchRow(I18n.T("每天同步官方价目"), I18n.T("从本项目 GitHub 仓库取最新 pricing.json，只下载文件、不上传任何数据；当前价目 {0}", Pricing.Day) + (Pricing.Source == "synced" ? I18n.T("（已同步）") : I18n.T("（随软件内置）")), config.PriceSync);
            advanced.Children.Add(Card(priceSyncSwitch));
            // Maintenance
            advanced.Children.Add(SectionTitle(I18n.T("维护")));
            var maintenance = new StackPanel();
            var scanRow = Row(); var scanNow = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("立即扫描"), IsEnabled = !demo };
            scanNow.Click += async delegate { scanNow.IsEnabled = false; await ScanHistory(); scanNow.IsEnabled = true; };
            AddRow(scanRow, FieldLabel(I18n.T("本机用量"), I18n.T("增量读取各平台日志；平时会每 15 分钟自动扫描")), scanNow); maintenance.Children.Add(scanRow); maintenance.Children.Add(Separator());
            var folderRow = Row(); var openData = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("打开") };
            openData.Click += delegate { try { Process.Start("explorer.exe", "\"" + Store.Data + "\""); } catch { } };
            AddRow(folderRow, FieldLabel(I18n.T("数据目录"), Store.Data), openData); maintenance.Children.Add(folderRow);
            advanced.Children.Add(Card(maintenance));

            // ── 关于 ── (SettingsPages.cs)
            CheckBox updateSwitch = BuildAboutPage(aboutPage);

            // Action bar
            var bar = new Grid { Margin = new Thickness(14, 0, 14, 10) };
            bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { Height = 1, Background = Hairline, Margin = new Thickness(-14, 0, -14, 10) }; Grid.SetColumnSpan(line, 3); bar.Children.Add(line);
            var error = new TextBlock { FontSize = 11, Foreground = WarnBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(error, 1); bar.Children.Add(error);
            var cancel = new Button { Style = Styled("SecondaryButton"), Content = I18n.T("取消"), Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += delegate { CloseSettings(); };
            Grid.SetRow(cancel, 1); Grid.SetColumn(cancel, 1); bar.Children.Add(cancel);
            var save = new Button { Style = Styled("PrimaryButton"), Content = I18n.T("保存"), IsDefault = false };
            System.Windows.Automation.AutomationProperties.SetName(save, I18n.T("保存设置"));
            Grid.SetRow(save, 1); Grid.SetColumn(save, 2); bar.Children.Add(save);
            Grid.SetRow(bar, 2); root.Children.Add(bar);

            save.Click += async delegate {
                Action<string, string> fail = (tab, message) => { error.Text = message; if (settingsTab != tab) { settingsTab = tab; scroll.Content = pages[tab]; } };
                if (!choices.Any(p => p.Value.IsChecked == true)) { fail("general", I18n.T("请至少保留一个平台。")); return; }
                string proxyValue = proxyMode == "custom" ? proxyBox.Text.Trim() : proxyMode;
                if (proxyMode == "custom" && proxyValue.Length == 0) { fail("advanced", I18n.T("请填写代理地址，例如 http://127.0.0.1:7890")); return; }
                string davUrl = dataFields.Url.Text.Trim();
                if (davUrl.Length > 0) { string davError = WebDavSync.Validate(davUrl); if (davError != null) { fail("data", davError); return; } }
                try {
                    ProviderService.ResolveProxy(proxyValue);
                    bool languageChanged = I18n.Resolve(language) != I18n.Language;
                    config.Language = language; I18n.Use(language);
                    config.Proxy = proxyValue; config.RefreshMinutes = minutes; config.HideAccounts = hidden.IsChecked == true;
                    config.Enabled = ProviderCatalog.All.Where(id => choices.ContainsKey(id) ? choices[id].IsChecked == true : config.Enabled.Contains(id)).ToArray();
                    config.KimiRegion = kimiRegion; config.ZaiRegion = zaiRegion;
                    config.AlwaysOnTop = onTop.IsChecked == true; config.HideOnDeactivate = autoHide.IsChecked == true; config.SurfaceOpacity = WindowFrame.ClampOpacity(1 - transparency / 100); config.SurfaceColor = Hex(tint);
                    config.BackgroundFit = fit; config.BackgroundDim = dim;
                    if (pictureChanged) { if (picture.Length == 0) { RemoveBackground(); config.BackgroundImage = ""; } else config.BackgroundImage = StoreBackground(picture); }
                    if (displaySize != initialSize) config.DisplaySize = displaySize; // applied by CloseSettings below
                    bool thirdPartyOn = thirdPartySwitch.IsChecked == true && !config.ShowThirdParty; config.ShowThirdParty = thirdPartySwitch.IsChecked == true;
                    config.PriceSync = priceSyncSwitch.IsChecked == true; config.UpdateCheck = updateSwitch.IsChecked == true;
                    config.BackupHours = dataFields.BackupHours(); config.BackupKeep = dataFields.BackupKeep(); config.BackupFolder = dataFields.BackupFolder();
                    config.WebDavUrl = davUrl; config.WebDavUser = dataFields.User.Text.Trim(); config.SyncMinutes = dataFields.SyncMinutes();
                    if (!demo) {
                        if (dataFields.Forget != null && dataFields.Forget.IsChecked == true) Store.SetProviderKey(WebDavSync.KeyId, "");
                        else if (dataFields.Password.Password.Length > 0) Store.SetProviderKey(WebDavSync.KeyId, dataFields.Password.Password);
                        if (dataFields.Device.Text.Trim() != Devices.Self().Name) Devices.Rename(dataFields.Device.Text);
                    }
                    foreach (KeyField field in keyFields) {
                        if (demo) break;
                        if (field.Remove != null && field.Remove.IsChecked == true) { Store.SetProviderKey(field.Id, ""); states[field.Id] = new ProviderState { Id = field.Id }; }
                        else if (!String.IsNullOrWhiteSpace(field.Box.Password)) { Store.SetProviderKey(field.Id, field.Box.Password); states[field.Id] = new ProviderState { Id = field.Id }; }
                    }
                    if (!demo) SetStartup(autoStart.IsChecked == true);
                    frame.SetScale(zoom.Value, false); UpdatePin(); UpdateScaleLabel();
                    Store.Write("settings.json", config); refreshTimer.Interval = TimeSpan.FromMinutes(minutes);
                    CloseSettings();
                    if (languageChanged) LanguageChanged();
                    await Refresh();
                    if (thirdPartyOn) { var scan = ScanHistory(); }
                } catch (Exception ex) { error.Text = ex is ArgumentException ? ex.Message : I18n.T("设置未能保存，请检查数据目录是否可写。"); }
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
                        code.Text = pending.UserCode; device.Visibility = Visibility.Visible; status.Text = "等待你在 GitHub 上授权…";
                        DateTime until = DateTime.UtcNow.AddSeconds(pending.ExpiresIn);
                        while (!cancelled && DateTime.UtcNow < until) {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, pending.Interval)));
                            if (cancelled) break;
                            string token = await service.PollCopilotLogin(pending);
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
            customEditor = null; settingsHost.Children.Clear(); settingsHost.Children.Add(settingsView); settingsTitle.Text = "设置"; settingsBack.Visibility = Visibility.Collapsed; RenderCustomList();
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
            settingsHost.Children.Clear(); settingsHost.Children.Add(root); settingsTitle.Text = "自定义平台"; settingsBack.Visibility = Visibility.Visible;
        }
    }
}
