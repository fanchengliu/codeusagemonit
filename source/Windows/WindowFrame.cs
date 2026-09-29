using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodeUsageMonit {
    public sealed class WindowFrame : IDisposable {
        private readonly Window window;
        private readonly AppConfig config;
        private readonly Action save;
        private readonly DispatcherTimer geometryTimer;
        private bool restored;
        private string mode = "full";
        public bool Compact { get { return mode != "full"; } }
        public string MaterialStatus { get; private set; }
        public WindowFrame(Window window, AppConfig config, Action save) {
            this.window = window; this.config = config; this.save = save;
            window.AllowsTransparency = false; window.ResizeMode = ResizeMode.CanResize;
            window.Topmost = config.AlwaysOnTop;
            WindowChrome.SetWindowChrome(window, new WindowChrome {
                CaptionHeight = 46, ResizeBorderThickness = new Thickness(7), GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false
            });
            window.SourceInitialized += delegate { ApplyMaterial(); };
            geometryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            geometryTimer.Tick += delegate { geometryTimer.Stop(); Capture(); save(); };
            window.LocationChanged += delegate { QueueSave(); };
            window.SizeChanged += delegate { QueueSave(); };
        }
        public static void Normalize(AppConfig c) {
            c.WindowWidth = Bound(c.WindowWidth, 360, 420); c.WindowHeight = Bound(c.WindowHeight, 460, 790);
            c.UiScale = Finite(c.UiScale) ? Math.Max(.8, Math.Min(1.4, c.UiScale)) : 1;
            if (c.Material == "solid") c.SurfaceOpacity = 1;
            c.Material = "acrylic"; c.SurfaceOpacity = ClampOpacity(c.SurfaceOpacity);
            if (c.DisplaySize != "small" && c.DisplaySize != "medium" && c.DisplaySize != "large") c.DisplaySize = "full";
            if (String.IsNullOrEmpty(c.CompactProvider)) c.CompactProvider = "overview";
            c.WindowLeft = Coordinate(c.WindowLeft); c.WindowTop = Coordinate(c.WindowTop);
            c.CompactLeft = Coordinate(c.CompactLeft); c.CompactTop = Coordinate(c.CompactTop);
            if (c.Layouts == null) c.Layouts = new Dictionary<string, PanelPlacement>();
            if (c.UsageRanges == null) c.UsageRanges = new Dictionary<string, UsageRangeChoice>();
            if (c.LayoutStyleVersion < 8) {
                foreach (string size in new[] { "small", "medium", "large" }) {
                    PanelPlacement p; if (!c.Layouts.TryGetValue(size, out p) || p == null) continue;
                    double oldWidth = size == "small" ? 260 : 420, oldHeight = size == "large" ? 540 : 320;
                    if (Math.Abs(p.Width - oldWidth * c.UiScale) < 1 && Math.Abs(p.Height - oldHeight * c.UiScale) < 1) { Size defaults = DefaultSize(size); p.Width = defaults.Width * c.UiScale; p.Height = defaults.Height * c.UiScale; }
                }
                c.LayoutStyleVersion = 8;
            }
            foreach (string size in new[] { "small", "medium", "large", "full" }) {
                PanelPlacement p;
                if (!c.Layouts.TryGetValue(size, out p) || p == null) continue;
                Size min = Minimum(size), defaults = DefaultSize(size);
                p.Width = Bound(p.Width, min.Width * c.UiScale, defaults.Width * c.UiScale); p.Height = Bound(p.Height, min.Height * c.UiScale, defaults.Height * c.UiScale);
                p.Left = Coordinate(p.Left); p.Top = Coordinate(p.Top);
            }
            if (c.UiVersion < 2) { c.UiVersion = 2; c.AlwaysOnTop = false; c.HideOnDeactivate = false; c.PinWindow = false; c.WindowWidth = 420; c.WindowHeight = 790; }
        }
        private static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }
        private static double? Coordinate(double? v) { return v.HasValue && !Finite(v.Value) ? null : v; }
        private static double Bound(double v, double minimum, double fallback) { return Finite(v) ? Math.Max(minimum, Math.Min(4096, v)) : fallback; }
        public static Size DefaultSize(string size) { return size == "small" ? new Size(172, 172) : size == "medium" ? new Size(360, 176) : size == "large" ? new Size(360, 390) : new Size(420, 790); }
        public static Size Minimum(string size) { return size == "small" ? new Size(172, 172) : size == "medium" ? new Size(320, 176) : size == "large" ? new Size(320, 300) : new Size(360, 460); }
        public void SetDisplayMode(string size) {
            geometryTimer.Stop(); Capture(); restored = false; mode = size;
            SetScale(config.UiScale, false); Restore(); window.Topmost = config.AlwaysOnTop;
        }
        public void Restore() {
            if (restored) return;
            Normalize(config); SetScale(config.UiScale, false);
            Size defaults = DefaultSize(mode), min = Minimum(mode);
            PanelPlacement p;
            if (!config.Layouts.TryGetValue(mode, out p) || p == null) p = mode == "full"
                ? new PanelPlacement { Width = config.WindowWidth, Height = config.WindowHeight, Left = config.WindowLeft, Top = config.WindowTop }
                : new PanelPlacement { Width = defaults.Width * config.UiScale, Height = defaults.Height * config.UiScale, Left = config.CompactLeft, Top = config.CompactTop };
            var monitor = Forms.Screen.FromPoint(Forms.Cursor.Position);
            double dpi; using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96;
            var area = monitor.WorkingArea;
            window.Width = Math.Max(window.MinWidth, Math.Min(Bound(p.Width, min.Width * config.UiScale, defaults.Width * config.UiScale), area.Width / dpi - 24));
            window.Height = Math.Max(window.MinHeight, Math.Min(Bound(p.Height, min.Height * config.UiScale, defaults.Height * config.UiScale), area.Height / dpi - 24));
            double left = p.Left ?? area.Right / dpi - window.Width - 16, top = p.Top ?? area.Bottom / dpi - window.Height - 16;
            bool onScreen = false;
            foreach (var screen in Forms.Screen.AllScreens) { var a = screen.WorkingArea; if (left + 80 > a.Left / dpi && left < a.Right / dpi - 80 && top + 45 > a.Top / dpi && top < a.Bottom / dpi - 45) { onScreen = true; break; } }
            if (!onScreen) { left = area.Right / dpi - window.Width - 16; top = area.Bottom / dpi - window.Height - 16; }
            window.Left = left; window.Top = top; restored = true;
        }
        public void Capture() {
            if (!restored || window.WindowState != WindowState.Normal) return;
            config.Layouts[mode] = new PanelPlacement { Width = window.Width, Height = window.Height, Left = window.Left, Top = window.Top };
            if (Compact) { config.CompactLeft = window.Left; config.CompactTop = window.Top; }
            else { config.WindowWidth = window.Width; config.WindowHeight = window.Height; config.WindowLeft = window.Left; config.WindowTop = window.Top; }
        }
        public void ResetPosition() {
            geometryTimer.Stop(); config.Layouts.Remove(mode);
            if (Compact) { config.CompactLeft = null; config.CompactTop = null; }
            else { config.WindowLeft = null; config.WindowTop = null; config.WindowWidth = 420; config.WindowHeight = 790; }
            restored = false; Restore(); Capture(); save();
        }
        private void QueueSave() { if (!restored) return; geometryTimer.Stop(); geometryTimer.Start(); }
        public void SetScale(double scale, bool persist) {
            config.UiScale = Math.Max(.8, Math.Min(1.4, scale));
            foreach (string name in new[] { "ScaleRoot", "CompactRoot" }) { var root = window.FindName(name) as FrameworkElement; if (root != null) root.LayoutTransform = new ScaleTransform(config.UiScale, config.UiScale); }
            Size min = Minimum(mode); window.MinWidth = min.Width * config.UiScale; window.MinHeight = min.Height * config.UiScale;
            var chrome = WindowChrome.GetWindowChrome(window);
            if (chrome != null) { chrome.CaptionHeight = (Compact ? 0 : 46) * config.UiScale; chrome.ResizeBorderThickness = new Thickness(7); }
            window.ResizeMode = ResizeMode.CanResize;
            if (persist) { Capture(); save(); }
        }
        public void ApplyMaterial() { ApplyMaterial("acrylic", config.SurfaceOpacity); }
        public void ApplyMaterial(string material, double opacity) { MaterialStatus = ApplyBackdrop(window, window.FindName("Surface") as Border, "acrylic", opacity); }
        public const double MinOpacity = 0, DefaultOpacity = .66;
        public static double ClampOpacity(double value) { return Finite(value) ? Math.Max(0, Math.Min(1, value)) : DefaultOpacity; }
        public static string ApplyBackdrop(Window target, Border surface, string material, double opacity) {
            IntPtr hwnd = new WindowInteropHelper(target).Handle; if (hwnd == IntPtr.Zero) return "";
            try {
                var source = HwndSource.FromHwnd(hwnd); source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }; DwmExtendFrameIntoClientArea(hwnd, ref margins);
                int dark = 1, corner = 2, backdrop = 3;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4); DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
                bool glass = DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4) == 0;
                if (surface != null) surface.Background = new SolidColorBrush(glass ? Color.FromArgb((byte)Math.Round(ClampOpacity(opacity) * 255), 0x15, 0x17, 0x1B) : Opaque);
                return glass ? "Windows 原生 Acrylic" : "不透明背景";
            } catch { if (surface != null) surface.Background = new SolidColorBrush(Opaque); return "不透明背景"; }
        }
        private static readonly Color Opaque = Color.FromRgb(0x1B, 0x1D, 0x22);
        public void Dispose() { geometryTimer.Stop(); }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    }
}
