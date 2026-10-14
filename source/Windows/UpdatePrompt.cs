using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace CodeUsageMonit {
    // The update dialog: the newer release, what it brings, and installing it from here
    // (Updates.cs does the work). It opens by itself once per run when the start-up or daily check
    // finds a release the user has not skipped — at once if the panel is open, otherwise a tray
    // note and then when the panel is next opened. About and the status line open it too.
    public sealed partial class MonitorPanel {
        private Window updateWindow;
        private bool updateOffered, updatePending, updateBalloonHooked, updating, statusOpensUpdate;
        private CancellationTokenSource updateCancel;

        private async Task UpdateOnStart() {
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (demo) return;
            if (Program.Updated) {
                try { tray.ShowBalloonTip(6000, "codeusagemonit", I18n.T("已更新到 v{0}", AppInfo.Version), Forms.ToolTipIcon.Info); } catch { }
                // The setup that started this copy may still be finishing; tidy up a little later.
                await Task.Delay(TimeSpan.FromSeconds(30));
                await Task.Run(() => { Updates.CleanUp(Store.Root); Updates.ClearDownloads(); });
            }
            if (Updates.DueOnStart(config, DateTime.UtcNow)) { updateState = await Updates.Check(config, DateTime.UtcNow); UpdateStatus(); }
            OfferUpdate();
        }
        private void OfferUpdate() {
            if (demo || updateOffered || !config.UpdateCheck || !Updates.Offer(updateState, config)) return;
            updateOffered = true;
            if (window.IsVisible) { ShowUpdate(); return; }
            updatePending = true;
            if (!updateBalloonHooked) { updateBalloonHooked = true; tray.BalloonTipClicked += delegate { if (updatePending) Reveal(); }; }
            try { tray.ShowBalloonTip(10000, "codeusagemonit", I18n.T("有新版本 v{0}，点击查看并更新", updateState.Latest), Forms.ToolTipIcon.Info); } catch { }
        }
        // Called when the panel is shown.
        private void ShowPendingUpdate() { if (updatePending) ShowUpdate(); }

        private void ShowUpdate() {
            updatePending = false;
            if (updateWindow != null) {
                if (updateWindow.WindowState == WindowState.Minimized) updateWindow.WindowState = WindowState.Normal;
                updateWindow.Activate(); return;
            }
            updateWindow = BuildUpdateWindow();
            I18n.Localize(updateWindow);
            updateWindow.Show(); updateWindow.Activate();
        }
        private void CloseUpdate() {
            if (updateWindow == null) return;
            if (updateCancel != null) updateCancel.Cancel();
            Window closing = updateWindow; updateWindow = null; closing.Close();
        }

        private Window BuildUpdateWindow() {
            double s = config.UiScale;
            UpdateState u = updateState;
            string kind = Updates.InstallKind(Store.Root);
            var w = new Window {
                Title = "codeusagemonit 更新", Width = 420 * s, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None, Background = Brushes.Transparent, ShowInTaskbar = true,
                FontFamily = window.FontFamily, FontSize = window.FontSize, Foreground = window.Foreground, UseLayoutRounding = true, SnapsToDevicePixels = true,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            ImageSource icon = AppIcon(); if (icon != null) w.Icon = icon;
            System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
            var surface = new Border { BorderBrush = Brush("#26FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(WindowFrame.ParseTint(config.SurfaceColor)) };
            var root = new Grid { LayoutTransform = new ScaleTransform(s, s) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Title bar
            var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(14, 0, 8, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = Label(Updates.Newer(u) ? "发现新版本" : "检查更新", 12.5, Ink); title.FontWeight = FontWeights.SemiBold; head.Children.Add(title);
            var close = new Button { Style = Styled("IconButton"), Content = "", FontSize = 10.5, ToolTip = "关闭" };
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭更新");
            close.Click += delegate { if (!updating) CloseUpdate(); };
            Grid.SetColumn(close, 1); head.Children.Add(close);
            head.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) { try { w.DragMove(); } catch (InvalidOperationException) { } } };
            root.Children.Add(head);
            root.Children.Add(new Border { Height = 1, Background = Hairline, VerticalAlignment = VerticalAlignment.Bottom });

            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
            Grid.SetRow(body, 1); root.Children.Add(body);
            var name = Label("codeusagemonit v" + u.Latest, 16, Ink); name.FontWeight = FontWeights.SemiBold; body.Children.Add(name);
            DateTime published; string when = DateTime.TryParse(u.Published, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out published) ? published.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
            body.Children.Add(Hint(I18n.T("当前 v{0}", AppInfo.Version) + (when.Length > 0 ? " · " + I18n.T("发布于 {0}", when) : ""), 3));

            // What's new
            var notes = new TextBlock { Text = u.Notes.Length > 0 ? u.Notes : I18n.T("这个版本没有附更新说明，可以在发布页查看。"), TextWrapping = TextWrapping.Wrap, Foreground = u.Notes.Length > 0 ? InkDim : InkFaint, FontSize = 12, LineHeight = 19 };
            var notesBox = new Border { Background = CardFill, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 12, 0, 0),
                Child = new ScrollViewer { Content = notes, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            body.Children.Add(notesBox);
            var page = new Button { Style = Styled("LinkButton"), Content = "查看完整发布说明", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 6, 0, 0) };
            page.Click += delegate { OpenUrl(u.Url.Length > 0 ? u.Url : Updates.Releases); };
            body.Children.Add(page);

            // How this copy updates
            string how = kind == "setup" ? "下载新版安装程序，在后台升级当前安装，完成后自动重新打开。设置、密钥和用量数据都会保留。"
                : kind == "portable" ? "下载新版免安装包，替换这个文件夹里的程序文件（data 文件夹不动），完成后自动重新打开。"
                : kind == "scoop" ? "这份是用 Scoop 安装的，请在终端运行下面的命令更新："
                : "这个文件夹没有写入权限，无法在软件内更新；请到发布页下载新版。";
            body.Children.Add(Hint(how, 10));
            const string ScoopCommand = "scoop update codeusagemonit";
            if (kind == "scoop") {
                var command = new TextBox { Text = ScoopCommand, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                body.Children.Add(command);
            }

            // Progress
            var status = new TextBlock { FontSize = 11.5, Foreground = InkDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            var fill = new Border { Background = AccentBrush, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1) };
            var track = new Border { Height = 4, Background = CardLine, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 8, 0, 0), Child = fill, Visibility = Visibility.Collapsed };
            body.Children.Add(status); body.Children.Add(track);

            // Buttons
            var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var skip = new Button { Style = Styled("LinkButton"), Content = "跳过这个版本", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 0, 0, 0), ToolTip = "不再自动提示这个版本；之后在“关于”里仍可手动更新", Visibility = Updates.Newer(u) ? Visibility.Visible : Visibility.Collapsed };
            skip.Click += delegate { config.SkippedVersion = u.Latest; SaveConfig(); CloseUpdate(); };
            buttons.Children.Add(skip);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var later = new Button { Style = Styled("SecondaryButton"), Content = "稍后", Margin = new Thickness(0, 0, 8, 0) };
            later.Click += delegate { if (updating && updateCancel != null) updateCancel.Cancel(); else CloseUpdate(); };
            var primary = new Button { Style = Styled("PrimaryButton"), Content = kind == "scoop" ? "复制命令" : kind == "manual" ? "前往下载" : "立即更新" };
            right.Children.Add(later); right.Children.Add(primary);
            Grid.SetColumn(right, 1); buttons.Children.Add(right);
            body.Children.Add(buttons);

            Action<string, Brush> say = (text, brush) => { status.Text = text; status.Foreground = brush; status.Visibility = Visibility.Visible; };
            primary.Click += async delegate {
                if (kind == "scoop") { try { Clipboard.SetText(ScoopCommand); say(I18n.T("已复制。在终端运行后重新打开软件即可。"), GoodBrush); } catch { } return; }
                if (kind == "manual" || !Updates.Newer(u)) { OpenUrl(u.Url.Length > 0 ? u.Url : Updates.Releases); return; }
                updating = true; updateCancel = new CancellationTokenSource();
                primary.IsEnabled = false; skip.IsEnabled = false; later.Content = I18n.T("取消");
                track.Visibility = Visibility.Visible; ((ScaleTransform)fill.RenderTransform).ScaleX = 0;
                say(I18n.T("正在下载…"), InkDim);
                var progress = new Progress<int>(p => {
                    say(p >= 0 ? I18n.T("正在下载… {0}%", p) : I18n.T("正在下载…"), InkDim);
                    ((ScaleTransform)fill.RenderTransform).ScaleX = p >= 0 ? p / 100.0 : 0.5;
                });
                try {
                    string file = await Updates.Download(config, u.Latest, kind, progress, updateCancel.Token);
                    ((ScaleTransform)fill.RenderTransform).ScaleX = 1;
                    later.IsEnabled = false;
                    say(I18n.T("已下载并通过校验，正在安装，完成后会自动重新打开…"), GoodBrush);
                    await Task.Delay(600);
                    if (kind == "setup") Updates.LaunchSetup(file, Program.SideBySide);
                    else {
                        await Task.Run(() => Updates.ApplyPortable(file, Store.Root));
                        Process.Start(new ProcessStartInfo(Path.Combine(Store.Root, "codeusagemonit.exe"), "--restart " + Process.GetCurrentProcess().Id + " --updated" + (Program.SideBySide ? " --side-by-side" : "")) { UseShellExecute = false });
                    }
                    Quit();
                    return;
                } catch (OperationCanceledException) {
                    // HttpClient reports its own timeout as a cancellation too.
                    say(updateCancel.IsCancellationRequested ? I18n.T("已取消更新。") : I18n.T("下载超时，请检查网络或代理设置。"), updateCancel.IsCancellationRequested ? InkFaint : WarnBrush);
                }
                catch (ProviderException e) { say(e.Message, WarnBrush); }
                catch (Win32Exception e) { say(e.NativeErrorCode == 1223 ? I18n.T("安装没有开始：权限确认被取消。") : I18n.T("无法启动安装程序：{0}", e.Message), WarnBrush); }
                catch (System.Net.Http.HttpRequestException) { say(I18n.T("下载失败，请检查网络或代理设置。"), WarnBrush); }
                catch (Exception e) { say(I18n.T("更新没有完成：{0}", e.Message), WarnBrush); }
                updating = false; primary.IsEnabled = true; skip.IsEnabled = true; later.IsEnabled = true; later.Content = I18n.T("稍后"); track.Visibility = Visibility.Collapsed;
                primary.Content = I18n.T("重试");
            };

            surface.Child = root; w.Content = surface;
            w.SourceInitialized += delegate { WindowFrame.ApplyBackdrop(w, surface, "acrylic", config.SurfaceOpacity, WindowFrame.ParseTint(config.SurfaceColor)); };
            w.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape && !updating) { CloseUpdate(); e.Handled = true; } };
            w.Closed += delegate { if (updateWindow == w) { updateWindow = null; if (updateCancel != null) updateCancel.Cancel(); } };
            // Centred on the panel when it is open, otherwise on the screen (the height is known
            // once laid out, so the first guess is corrected on load).
            Rect area = SystemParameters.WorkArea;
            double cx = window.IsVisible ? window.Left + window.ActualWidth / 2 : area.Left + area.Width / 2, cy = window.IsVisible ? window.Top + window.ActualHeight / 2 : area.Top + area.Height / 2;
            Action place = () => {
                double height = w.ActualHeight > 0 ? w.ActualHeight : 420 * s;
                w.Left = Math.Max(area.Left, Math.Min(area.Right - w.Width, cx - w.Width / 2));
                w.Top = Math.Max(area.Top, Math.Min(area.Bottom - height, cy - height / 2));
            };
            place(); w.Loaded += delegate { place(); };
            return w;
        }
        private static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        // app.ico read into memory once: a BitmapImage from its path keeps the file open, and an
        // update must be able to replace it.
        private static ImageSource appIcon;
        private static ImageSource AppIcon() {
            if (appIcon != null) return appIcon;
            try {
                using (var stream = new FileStream(Path.Combine(Store.Root, "app.ico"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame.Freeze(); appIcon = frame;
                }
            } catch { }
            return appIcon;
        }
    }
}
