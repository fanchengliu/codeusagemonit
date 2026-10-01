using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodeUsageMonit {
    // Surface colour palette: presets, a saturation/brightness square, a hue strip, a
    // transparency strip (0 = opaque, the same value as 界面透明度) and a hex field. Every
    // change is reported at once for the live preview; the settings window's Save / Cancel
    // keeps or drops it. Text stays light, so the palette shows the text contrast.
    public sealed partial class MonitorPanel {
        private static readonly string[][] TintPresets = {
            new[] { "#15171B", "石墨（默认）" }, new[] { "#000000", "纯黑" }, new[] { "#262A30", "岩灰" }, new[] { "#0F1B2D", "深海蓝" }, new[] { "#161A36", "午夜蓝" },
            new[] { "#0D2226", "深青" }, new[] { "#0F211C", "墨绿" }, new[] { "#1E1629", "暗紫" }, new[] { "#2A1218", "酒红" }, new[] { "#231A14", "可可" }
        };
        private const double PaletteWidth = 264, SquareHeight = 138, StripHeight = 12;
        private static readonly Color TextInk = Color.FromRgb(0xF2, 0xF3, 0xF5);

        private void ShowColorPicker(FrameworkElement anchor, Color initial, double transparency, Action<Color, double> changed) {
            double hue, sat, val; ToHsv(initial, out hue, out sat, out val);
            Color color = initial; double clear = transparency;
            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 6 };
            var root = new StackPanel { Width = PaletteWidth };

            // Presets.
            var presetRow = new UniformGrid { Columns = TintPresets.Length, Rows = 1, Margin = new Thickness(0, 0, 0, 12) };
            root.Children.Add(Label("预设", 10.5, InkFaint)); ((TextBlock)root.Children[0]).Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add(presetRow);

            // Saturation (x) / brightness (y) square over the current hue.
            var hueLayer = new Border { CornerRadius = new CornerRadius(6) };
            var square = new Grid { Width = PaletteWidth, Height = SquareHeight, Cursor = Cursors.Cross, Focusable = true, Background = Brushes.Transparent, FocusVisualStyle = null };
            square.Children.Add(hueLayer);
            square.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0) });
            square.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), 90), BorderBrush = Brush("#26FFFFFF"), BorderThickness = new Thickness(1) });
            var squareCanvas = new Canvas { IsHitTestVisible = false }; square.Children.Add(squareCanvas);
            var svThumb = new Grid { Width = 16, Height = 16 };
            svThumb.Children.Add(new Ellipse { Stroke = Brush("#99000000"), StrokeThickness = 1 });
            svThumb.Children.Add(new Ellipse { Margin = new Thickness(1), Stroke = Brushes.White, StrokeThickness = 2 });
            squareCanvas.Children.Add(svThumb);
            System.Windows.Automation.AutomationProperties.SetName(square, "饱和度与亮度");
            root.Children.Add(square);

            // Hue and transparency strips.
            var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            for (int i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
            hueBrush.Freeze();
            Border hueThumb, hueTrack, alphaThumb, alphaTrack;
            Action update = null;
            Grid hueStrip = PaletteStrip(hueBrush, false, "色相", () => hue / 360, f => { hue = Math.Min(359.9, f * 360); color = FromHsv(hue, sat, val); update(); }, out hueThumb, out hueTrack);
            Grid alphaStrip = PaletteStrip(null, true, "调色盘透明度", () => clear / 100, f => { clear = Math.Round(f * 100); update(); }, out alphaThumb, out alphaTrack);
            root.Children.Add(hueStrip); root.Children.Add(alphaStrip);

            // Preview (before | after), hex code and transparency value.
            var info = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); info.ColumnDefinitions.Add(new ColumnDefinition()); info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Grid { Width = 44, Height = 32, Margin = new Thickness(0, 0, 12, 0), ClipToBounds = true };
            swatch.ColumnDefinitions.Add(new ColumnDefinition()); swatch.ColumnDefinitions.Add(new ColumnDefinition());
            var checker = new Border { CornerRadius = new CornerRadius(6), Background = Checkerboard() }; Grid.SetColumnSpan(checker, 2); swatch.Children.Add(checker);
            var before = new Border { CornerRadius = new CornerRadius(6, 0, 0, 6), Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round((1 - transparency / 100) * 255), initial.R, initial.G, initial.B)), ToolTip = "原来的颜色" };
            var after = new Border { CornerRadius = new CornerRadius(0, 6, 6, 0), ToolTip = "新的颜色" }; Grid.SetColumn(after, 1);
            swatch.Children.Add(before); swatch.Children.Add(after);
            swatch.Children.Add(new Border { CornerRadius = new CornerRadius(6), BorderBrush = Brush("#33FFFFFF"), BorderThickness = new Thickness(1) }); Grid.SetColumnSpan(swatch.Children[swatch.Children.Count - 1], 2);
            info.Children.Add(swatch);
            var hexBox = new TextBox { Width = 92, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas, Segoe UI"), MaxLength = 7 };
            System.Windows.Automation.AutomationProperties.SetName(hexBox, "颜色代码");
            Grid.SetColumn(hexBox, 1); info.Children.Add(hexBox);
            var clearValue = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            clearValue.Children.Add(Label("透明度", 10, InkFaint));
            var clearText = Label("", 13, Ink); clearText.FontWeight = FontWeights.SemiBold; Tabular(clearText); clearText.HorizontalAlignment = HorizontalAlignment.Right; clearValue.Children.Add(clearText);
            Grid.SetColumn(clearValue, 2); info.Children.Add(clearValue);
            root.Children.Add(info);
            var contrast = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            root.Children.Add(contrast);

            var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var reset = new Button { Style = Styled("LinkButton"), Content = "恢复默认", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(reset, "恢复默认颜色");
            var done = new Button { Style = Styled("PrimaryButton"), Content = "完成" };
            System.Windows.Automation.AutomationProperties.SetName(done, "完成颜色");
            done.Click += delegate { popup.IsOpen = false; };
            actions.Children.Add(reset); Grid.SetColumn(done, 1); actions.Children.Add(done);
            root.Children.Add(actions);

            Action<Color> setColor = c => { color = c; ToHsv(c, out hue, out sat, out val); update(); };
            reset.Click += delegate { clear = Math.Round((1 - WindowFrame.DefaultOpacity) * 100); setColor(WindowFrame.DefaultTint); };
            foreach (string[] preset in TintPresets) {
                Color c = WindowFrame.ParseTint(preset[0]);
                var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(c), BorderThickness = new Thickness(2) };
                var button = new Button { Content = dot, Padding = new Thickness(0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = preset[1] + "  " + preset[0], Tag = c };
                System.Windows.Automation.AutomationProperties.SetName(button, "颜色 " + preset[1]);
                button.Click += delegate { setColor(c); };
                presetRow.Children.Add(button);
            }
            Action<Point> pickSquare = p => {
                sat = Math.Max(0, Math.Min(1, p.X / PaletteWidth)); val = 1 - Math.Max(0, Math.Min(1, p.Y / SquareHeight));
                color = FromHsv(hue, sat, val); update();
            };
            square.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { square.Focus(); square.CaptureMouse(); pickSquare(e.GetPosition(square)); e.Handled = true; };
            square.MouseMove += delegate(object sender, MouseEventArgs e) { if (square.IsMouseCaptured) pickSquare(e.GetPosition(square)); };
            square.MouseLeftButtonUp += delegate { if (square.IsMouseCaptured) square.ReleaseMouseCapture(); };
            square.KeyDown += delegate(object sender, KeyEventArgs e) {
                double step = Keyboard.Modifiers == ModifierKeys.Shift ? .1 : .02;
                if (e.Key == Key.Left) sat = Math.Max(0, sat - step); else if (e.Key == Key.Right) sat = Math.Min(1, sat + step);
                else if (e.Key == Key.Up) val = Math.Min(1, val + step); else if (e.Key == Key.Down) val = Math.Max(0, val - step); else return;
                color = FromHsv(hue, sat, val); update(); e.Handled = true;
            };
            Action applyHex = () => { Color parsed; if (TryParseHex(hexBox.Text, out parsed)) { if (parsed != color) setColor(parsed); } else hexBox.Text = Hex(color); };
            hexBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { applyHex(); e.Handled = true; } };
            hexBox.LostKeyboardFocus += delegate { applyHex(); };

            update = () => {
                hueLayer.Background = new SolidColorBrush(FromHsv(hue, 1, 1));
                Canvas.SetLeft(svThumb, sat * PaletteWidth - 8); Canvas.SetTop(svThumb, (1 - val) * SquareHeight - 8);
                Canvas.SetLeft(hueThumb, hue / 360 * (PaletteWidth - 8)); Canvas.SetLeft(alphaThumb, clear / 100 * (PaletteWidth - 8));
                alphaTrack.Background = new LinearGradientBrush(Color.FromArgb(255, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 0);
                after.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round((1 - clear / 100) * 255), color.R, color.G, color.B));
                if (!hexBox.IsKeyboardFocused) hexBox.Text = Hex(color);
                clearText.Text = clear.ToString("0") + "%";
                foreach (Button b in presetRow.Children) ((Border)b.Content).BorderBrush = (Color)b.Tag == color ? Brushes.White : Brush("#33FFFFFF");
                double ratio = ContrastRatio(TextInk, color);
                contrast.Inlines.Clear();
                contrast.Inlines.Add(new System.Windows.Documents.Run("文字对比度 " + ratio.ToString("0.0", CultureInfo.InvariantCulture) + " : 1 · ") { Foreground = InkFaint });
                contrast.Inlines.Add(new System.Windows.Documents.Run(ratio >= 7 ? "清晰" : ratio >= 4.5 ? "可读" : "偏低，文字可能看不清，建议选深一些的颜色") { Foreground = ratio >= 4.5 ? GoodBrush : WarnBrush });
                if (clear >= 50) contrast.Inlines.Add(new System.Windows.Documents.Run("（透明度较高时还取决于桌面）") { Foreground = InkFaint });
                changed(color, clear);
            };
            update();

            var frameBorder = new Border { Background = PopupSurface(), BorderBrush = Brush("#33FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 14), Child = root };
            System.Windows.Documents.TextElement.SetFontFamily(frameBorder, window.FontFamily);
            frameBorder.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
            popup.Child = frameBorder;
            I18n.Localize(frameBorder);
            popup.IsOpen = true;
        }

        // A horizontal strip with a thumb; click or drag to set the fraction, arrow keys step it.
        private static Grid PaletteStrip(Brush fill, bool checker, string name, Func<double> get, Action<double> set, out Border thumb, out Border track) {
            var strip = new Grid { Width = PaletteWidth, Height = StripHeight + 8, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null, Margin = new Thickness(0, 10, 0, 0) };
            if (checker) strip.Children.Add(new Border { Height = StripHeight, CornerRadius = new CornerRadius(StripHeight / 2), VerticalAlignment = VerticalAlignment.Center, Background = Checkerboard() });
            track = new Border { Height = StripHeight, CornerRadius = new CornerRadius(StripHeight / 2), VerticalAlignment = VerticalAlignment.Center, Background = fill, BorderBrush = Brush("#26FFFFFF"), BorderThickness = new Thickness(1) };
            strip.Children.Add(track);
            var canvas = new Canvas { IsHitTestVisible = false }; strip.Children.Add(canvas);
            thumb = new Border { Width = 8, Height = StripHeight + 8, CornerRadius = new CornerRadius(4), Background = Brushes.White, BorderBrush = Brush("#99000000"), BorderThickness = new Thickness(1) };
            canvas.Children.Add(thumb);
            System.Windows.Automation.AutomationProperties.SetName(strip, name);
            strip.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { strip.Focus(); strip.CaptureMouse(); set(Math.Max(0, Math.Min(1, (e.GetPosition(strip).X - 4) / (PaletteWidth - 8)))); e.Handled = true; };
            strip.MouseMove += delegate(object sender, MouseEventArgs e) { if (strip.IsMouseCaptured) set(Math.Max(0, Math.Min(1, (e.GetPosition(strip).X - 4) / (PaletteWidth - 8)))); };
            strip.MouseLeftButtonUp += delegate { if (strip.IsMouseCaptured) strip.ReleaseMouseCapture(); };
            strip.KeyDown += delegate(object sender, KeyEventArgs e) {
                double step = Keyboard.Modifiers == ModifierKeys.Shift ? .1 : .01;
                if (e.Key == Key.Left) set(Math.Max(0, get() - step)); else if (e.Key == Key.Right) set(Math.Min(1, get() + step)); else return;
                e.Handled = true;
            };
            return strip;
        }
        private static Brush Checkerboard() {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brush("#FF3A3D43"), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            var dark = new GeometryGroup(); dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 4, 4))); dark.Children.Add(new RectangleGeometry(new Rect(4, 4, 4, 4)));
            group.Children.Add(new GeometryDrawing(Brush("#FF24272C"), null, dark));
            var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze(); return brush;
        }

        // ── Colour maths ─────────────────────────────────────────────────
        internal static Color FromHsv(double h, double s, double v) {
            h = ((h % 360) + 360) % 360; double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; } else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
        internal static void ToHsv(Color color, out double h, out double s, out double v) {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0; else if (max == r) h = 60 * (((g - b) / d) % 6); else if (max == g) h = 60 * ((b - r) / d + 2); else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
        internal static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        internal static bool TryParseHex(string text, out Color color) {
            color = Colors.Black; string t = (text ?? "").Trim().TrimStart('#');
            if (t.Length == 3) t = new string(new[] { t[0], t[0], t[1], t[1], t[2], t[2] });
            int value; if (t.Length != 6 || !Int32.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return false;
            color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8 & 255), (byte)(value & 255)); return true;
        }
        // WCAG 2 contrast ratio between two opaque colours (1 … 21).
        internal static double ContrastRatio(Color a, Color b) {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + .05) / (Math.Min(la, lb) + .05);
        }
        private static double Luminance(Color c) {
            Func<byte, double> lin = x => { double s = x / 255.0; return s <= .03928 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); };
            return .2126 * lin(c.R) + .7152 * lin(c.G) + .0722 * lin(c.B);
        }
    }
}
