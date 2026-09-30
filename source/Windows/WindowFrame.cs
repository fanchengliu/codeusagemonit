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
    // Window chrome, geometry and backdrop for the one window that shows all four display
    // sizes. Every size can be resized; each keeps its own size and position.
    public sealed class WindowFrame : IDisposable {
        private readonly Window window;
        private readonly AppConfig config;
        private readonly Action save;
        private readonly DispatcherTimer geometryTimer;
        private bool restored;
        private string mode = "full";
        public string MaterialStatus { get; private set; }
        public WindowFrame(Window window, AppConfig config, Action save) {
            this.window = window; this.config = config; this.save = save;
            window.AllowsTransparency = false;
            window.ResizeMode = ResizeMode.CanResize;
            window.Topmost = config.AlwaysOnTop;
            // No native caption: the panel header and the compact sizes are dragged with
            // DragMove, so a right-click there opens our menu instead of the system menu.
            WindowChrome.SetWindowChrome(window, new WindowChrome {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(7),
                GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(8),
                UseAeroCaptionButtons = false
            });
            window.SourceInitialized += delegate {
                ApplyMaterial();
                HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook(Hook);
            };
            geometryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            geometryTimer.Tick += delegate { geometryTimer.Stop(); Capture(); save(); };
            window.LocationChanged += delegate { QueueSave(); };
            window.SizeChanged += delegate { QueueSave(); };
            ApplyLimits();
        }
        public static void Normalize(AppConfig c) {
            c.WindowWidth = Finite(c.WindowWidth) ? Math.Max(360, Math.Min(4096, c.WindowWidth)) : 420;
            c.WindowHeight = Finite(c.WindowHeight) ? Math.Max(460, Math.Min(4096, c.WindowHeight)) : 790;
            c.UiScale = Finite(c.UiScale) ? Math.Max(.8, Math.Min(1.4, c.UiScale)) : 1;
            // Materials are no longer a choice: "opaque" became 0% transparency; Mica / Acrylic
            // both map to the system blur under the interface transparency.
            if (c.Material == "solid") c.SurfaceOpacity = 1;
            c.Material = "acrylic";
            if (c.BackgroundFit != "fit" && c.BackgroundFit != "tile") c.BackgroundFit = "fill";
            c.BackgroundDim = Finite(c.BackgroundDim) ? Math.Max(0, Math.Min(.85, c.BackgroundDim)) : .35;
            if (c.BackgroundImage == null) c.BackgroundImage = "";
            c.SurfaceOpacity = ClampOpacity(c.SurfaceOpacity);
            Color tint; c.SurfaceColor = MonitorPanel.TryParseHex(c.SurfaceColor, out tint) ? MonitorPanel.Hex(tint) : MonitorPanel.Hex(DefaultTint);
            if (c.DisplaySize != "small" && c.DisplaySize != "medium" && c.DisplaySize != "large") c.DisplaySize = "full";
            if (String.IsNullOrEmpty(c.CompactProvider)) c.CompactProvider = "overview";
            if (c.CompactLeft.HasValue && !Finite(c.CompactLeft.Value)) c.CompactLeft = null;
            if (c.CompactTop.HasValue && !Finite(c.CompactTop.Value)) c.CompactTop = null;
            if (c.WindowLeft.HasValue && !Finite(c.WindowLeft.Value)) c.WindowLeft = null;
            if (c.WindowTop.HasValue && !Finite(c.WindowTop.Value)) c.WindowTop = null;
            if (c.Layouts == null) c.Layouts = new Dictionary<string, WindowGeometry>();
            foreach (string key in new List<string>(c.Layouts.Keys)) {
                WindowGeometry g = c.Layouts[key];
                if (g == null || (key != "small" && key != "medium" && key != "large") || !Finite(g.Width) || !Finite(g.Height)) { c.Layouts.Remove(key); continue; }
                if (g.Left.HasValue && !Finite(g.Left.Value)) g.Left = null;
                if (g.Top.HasValue && !Finite(g.Top.Value)) g.Top = null;
            }
            if (c.UiVersion < 2) {
                c.UiVersion = 2; c.AlwaysOnTop = false; c.HideOnDeactivate = false;
                c.PinWindow = false; c.WindowWidth = 420; c.WindowHeight = 790;
            }
        }
        private static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }

        // ── Display sizes ─────────────────────────────────────────────────
        // Sizes are at 100% interface scale; Ctrl + wheel scales them together with the content.
        public static Size DefaultSize(string size) {
            return size == "small" ? new Size(172, 172) : size == "medium" ? new Size(360, 180) : size == "large" ? new Size(360, 430) : new Size(420, 790);
        }
        public static Size MinSize(string size) {
            return size == "small" ? new Size(160, 160) : size == "medium" ? new Size(300, 160) : size == "large" ? new Size(320, 340) : new Size(360, 460);
        }
        public static Size MaxSize(string size) {
            return size == "small" ? new Size(360, 360) : size == "medium" ? new Size(720, 360) : size == "large" ? new Size(720, 1000) : new Size(4096, 4096);
        }
        public string Mode { get { return mode; } }
        public bool Compact { get { return mode != "full"; } }
        // Compact sizes sit on the desktop layer unless "always on top" is on.
        public bool PinnedToDesktop { get { return Compact && !config.AlwaysOnTop; } }
        public void SetMode(string next) {
            if (restored) Capture(); // keep the geometry of the size being left
            mode = next;
            var chrome = WindowChrome.GetWindowChrome(window);
            if (chrome != null) chrome.ResizeBorderThickness = new Thickness(Compact ? 6 : 7);
            ApplyLimits();
            restored = false; Restore();
            window.Topmost = config.AlwaysOnTop;
            if (PinnedToDesktop) SendToBottom();
        }
        private void ApplyLimits() {
            double s = config.UiScale; Size min = MinSize(mode), max = MaxSize(mode);
            window.MinWidth = min.Width * s; window.MinHeight = min.Height * s;
            window.MaxWidth = Compact ? max.Width * s : Double.PositiveInfinity; window.MaxHeight = Compact ? max.Height * s : Double.PositiveInfinity;
        }
        public void Restore() {
            if (restored) return;
            Normalize(config);
            var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            double dpi = 1;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96;
            var area = screen.WorkingArea;
            double s = config.UiScale, width, height, left, top;
            if (Compact) {
                WindowGeometry saved; config.Layouts.TryGetValue(mode, out saved);
                Size design = DefaultSize(mode), min = MinSize(mode), max = MaxSize(mode);
                width = saved != null ? saved.Width : design.Width * s; height = saved != null ? saved.Height : design.Height * s;
                width = Math.Max(min.Width * s, Math.Min(Math.Min(max.Width * s, area.Width / dpi - 24), width));
                height = Math.Max(min.Height * s, Math.Min(Math.Min(max.Height * s, area.Height / dpi - 24), height));
                left = (saved != null ? saved.Left : null) ?? config.CompactLeft ?? area.Right / dpi - width - 24;
                top = (saved != null ? saved.Top : null) ?? config.CompactTop ?? area.Top / dpi + 24;
            } else {
                width = Math.Min(config.WindowWidth, area.Width / dpi - 24); height = Math.Min(config.WindowHeight, area.Height / dpi - 24);
                left = config.WindowLeft ?? area.Right / dpi - width - 16; top = config.WindowTop ?? area.Bottom / dpi - height - 16;
            }
            bool onScreen = false;
            foreach (var monitor in Forms.Screen.AllScreens) {
                var r = monitor.WorkingArea;
                if (left + 80 > r.Left / dpi && left < r.Right / dpi - 80 && top + 45 > r.Top / dpi && top < r.Bottom / dpi - 45) { onScreen = true; break; }
            }
            if (!onScreen) { left = area.Right / dpi - width - (Compact ? 24 : 16); top = Compact ? area.Top / dpi + 24 : area.Bottom / dpi - height - 16; }
            window.Width = width; window.Height = height; window.Left = left; window.Top = top;
            SetScale(config.UiScale, false); restored = true;
        }
        public void ResetPosition() {
            if (Compact) { config.Layouts.Remove(mode); config.CompactLeft = null; config.CompactTop = null; }
            else { config.WindowLeft = null; config.WindowTop = null; config.WindowWidth = 420; config.WindowHeight = 790; }
            restored = false; Restore(); Capture(); save();
        }
        // Each display size saves its own geometry.
        public void Capture() {
            if (!restored || window.WindowState != WindowState.Normal) return;
            if (Compact) { config.Layouts[mode] = new WindowGeometry { Left = window.Left, Top = window.Top, Width = window.Width, Height = window.Height }; return; }
            config.WindowLeft = window.Left; config.WindowTop = window.Top;
            config.WindowWidth = window.Width; config.WindowHeight = window.Height;
        }
        private void QueueSave() { if (!restored) return; geometryTimer.Stop(); geometryTimer.Start(); }
        public void SetScale(double scale, bool persist) {
            double previous = config.UiScale;
            config.UiScale = Math.Max(.8, Math.Min(1.4, scale));
            foreach (string name in new[] { "ScaleRoot", "CompactRoot" }) { var root = window.FindName(name) as FrameworkElement; if (root != null) root.LayoutTransform = new ScaleTransform(config.UiScale, config.UiScale); }
            ApplyLimits();
            // A compact size grows and shrinks with its content; the full panel keeps its size.
            if (Compact && restored && Math.Abs(previous - config.UiScale) > 1e-6) { double ratio = config.UiScale / previous; window.Width *= ratio; window.Height *= ratio; }
            if (persist) save();
        }

        public void SendToBottom() { IntPtr hwnd = new WindowInteropHelper(window).Handle; if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
        // WM_WINDOWPOSCHANGING: while pinned, any z-order change goes to the bottom, so a
        // click on the compact window never raises it above other windows.
        private IntPtr Hook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
            if (message == 0x0046 && PinnedToDesktop && lParam != IntPtr.Zero) {
                var pos = (WindowPos)Marshal.PtrToStructure(lParam, typeof(WindowPos));
                if ((pos.Flags & 0x0004) == 0) { pos.InsertAfter = new IntPtr(1); Marshal.StructureToPtr(pos, lParam, false); }
            }
            return IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct WindowPos { public IntPtr Hwnd, InsertAfter; public int X, Y, Cx, Cy; public uint Flags; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // ── Backdrop ──────────────────────────────────────────────────────
        public void ApplyMaterial() { ApplyMaterial(config.Material, config.SurfaceOpacity, ParseTint(config.SurfaceColor)); }
        // Values other than the saved config are a preview; ApplyMaterial() restores them.
        public void ApplyMaterial(string material, double opacity, Color tint) {
            MaterialStatus = ApplyBackdrop(window, window.FindName("Surface") as Border, material, opacity, tint);
        }
        public const double MinOpacity = 0, DefaultOpacity = .66;
        public static readonly Color DefaultTint = Color.FromRgb(0x15, 0x17, 0x1B);
        public static Color ParseTint(string hex) { Color tint; return MonitorPanel.TryParseHex(hex, out tint) ? tint : DefaultTint; }
        public static double ClampOpacity(double value) { return Finite(value) ? Math.Max(MinOpacity, Math.Min(1, value)) : DefaultOpacity; }
        // DWM backdrop (Acrylic / Mica) plus a tint layer (the chosen surface colour) whose
        // alpha is 1 − the interface transparency: it keeps the blur but stops saturated
        // wallpaper colours from tinting text and meters. Shared by the panel and settings.
        public static string ApplyBackdrop(Window target, Border surface, string material, double opacity, Color tint) {
            IntPtr hwnd = new WindowInteropHelper(target).Handle;
            if (hwnd == IntPtr.Zero) return "";
            try {
                var source = HwndSource.FromHwnd(hwnd); source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                int dark = 1, corner = 2, backdrop = material == "mica" ? 2 : 3;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4); DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
                int result = DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
                bool glass = result == 0;
                byte alpha = (byte)Math.Round(ClampOpacity(opacity) * 255);
                if (surface != null) surface.Background = glass ? new SolidColorBrush(Color.FromArgb(alpha, tint.R, tint.G, tint.B)) : new SolidColorBrush(tint);
                return glass ? (backdrop == 3 ? "Windows 原生 Acrylic" : "Windows 原生 Mica") : "不透明背景（系统不支持背景材质）";
            } catch { if (surface != null) surface.Background = new SolidColorBrush(tint); return "不透明背景（系统不支持背景材质）"; }
        }
        public void Dispose() { geometryTimer.Stop(); }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    }
}
