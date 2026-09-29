using System;
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
        // Must match the first row height of Panel.xaml (the draggable header).
        private const double CaptionHeight = 46;
        public string MaterialStatus { get; private set; }
        public WindowFrame(Window window, AppConfig config, Action save) {
            this.window = window; this.config = config; this.save = save;
            window.AllowsTransparency = false;
            window.ResizeMode = ResizeMode.CanResize;
            window.Topmost = config.AlwaysOnTop;
            WindowChrome.SetWindowChrome(window, new WindowChrome {
                CaptionHeight = CaptionHeight, ResizeBorderThickness = new Thickness(7),
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
        }
        public static void Normalize(AppConfig c) {
            c.WindowWidth = Finite(c.WindowWidth) ? Math.Max(360, Math.Min(4096, c.WindowWidth)) : 420;
            c.WindowHeight = Finite(c.WindowHeight) ? Math.Max(460, Math.Min(4096, c.WindowHeight)) : 790;
            c.UiScale = Finite(c.UiScale) ? Math.Max(.8, Math.Min(1.4, c.UiScale)) : 1;
            // The separate "opaque" material became 0% transparency on Acrylic.
            if (c.Material == "solid") { c.Material = "acrylic"; c.SurfaceOpacity = 1; }
            if (c.Material != "mica") c.Material = "acrylic";
            c.SurfaceOpacity = ClampOpacity(c.SurfaceOpacity);
            if (c.DisplaySize != "small" && c.DisplaySize != "medium" && c.DisplaySize != "large") c.DisplaySize = "full";
            if (String.IsNullOrEmpty(c.CompactProvider)) c.CompactProvider = "overview";
            if (c.CompactLeft.HasValue && !Finite(c.CompactLeft.Value)) c.CompactLeft = null;
            if (c.CompactTop.HasValue && !Finite(c.CompactTop.Value)) c.CompactTop = null;
            if (c.WindowLeft.HasValue && !Finite(c.WindowLeft.Value)) c.WindowLeft = null;
            if (c.WindowTop.HasValue && !Finite(c.WindowTop.Value)) c.WindowTop = null;
            if (c.UiVersion < 2) {
                c.UiVersion = 2; c.AlwaysOnTop = false; c.HideOnDeactivate = false;
                c.PinWindow = false; c.WindowWidth = 420; c.WindowHeight = 790;
            }
        }
        private static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }
        public void Restore() {
            if (restored) return;
            Normalize(config);
            var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            double dpi = 1;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96;
            var area = screen.WorkingArea;
            window.Width = Math.Min(config.WindowWidth, area.Width / dpi - 24);
            window.Height = Math.Min(config.WindowHeight, area.Height / dpi - 24);
            double left = config.WindowLeft ?? area.Right / dpi - window.Width - 16;
            double top = config.WindowTop ?? area.Bottom / dpi - window.Height - 16;
            bool onScreen = false;
            foreach (var monitor in Forms.Screen.AllScreens) {
                var r = monitor.WorkingArea;
                if (left + 80 > r.Left / dpi && left < r.Right / dpi - 80 && top + 45 > r.Top / dpi && top < r.Bottom / dpi - 45) { onScreen = true; break; }
            }
            if (!onScreen) { left = area.Right / dpi - window.Width - 16; top = area.Bottom / dpi - window.Height - 16; }
            window.Left = left; window.Top = top;
            SetScale(config.UiScale, false); restored = true;
        }
        public void ResetPosition() {
            if (Compact) { config.CompactLeft = null; config.CompactTop = null; PlaceCompact(); save(); return; }
            config.WindowLeft = null; config.WindowTop = null; config.WindowWidth = 420; config.WindowHeight = 790; restored = false; Restore(); Capture(); save();
        }
        // Full panel geometry and the compact sizes' position are saved separately.
        public void Capture() {
            if (!restored || window.WindowState != WindowState.Normal) return;
            if (Compact) { config.CompactLeft = window.Left; config.CompactTop = window.Top; return; }
            config.WindowLeft = window.Left; config.WindowTop = window.Top;
            config.WindowWidth = window.Width; config.WindowHeight = window.Height;
        }
        private void QueueSave() { if (!restored) return; geometryTimer.Stop(); geometryTimer.Start(); }
        public void SetScale(double scale, bool persist) {
            config.UiScale = Math.Max(.8, Math.Min(1.4, scale));
            foreach (string name in new[] { "ScaleRoot", "CompactRoot" }) { var root = window.FindName(name) as FrameworkElement; if (root != null) root.LayoutTransform = new ScaleTransform(config.UiScale, config.UiScale); }
            var chrome = WindowChrome.GetWindowChrome(window);
            if (chrome != null) chrome.CaptionHeight = Compact ? 0 : CaptionHeight * config.UiScale;
            if (persist) save();
        }

        // ── Compact sizes (small / medium / large) ─────────────────────────
        // Compact: fixed size, no resize border or caption (dragged from anywhere), and —
        // unless "always on top" — kept at the bottom of the z-order like a desktop widget.
        public bool Compact { get; private set; }
        public void SetCompact(bool compact, Size size) {
            if (compact && !Compact) Capture(); // keep the full panel geometry before shrinking
            else if (Compact) Capture(); // preserve the compact position before returning to full
            Compact = compact;
            SetScale(config.UiScale, false); // also applies saved zoom on a cold compact start
            var chrome = WindowChrome.GetWindowChrome(window);
            if (chrome != null) { chrome.ResizeBorderThickness = new Thickness(compact ? 0 : 7); chrome.CaptionHeight = compact ? 0 : CaptionHeight * config.UiScale; }
            window.ResizeMode = compact ? ResizeMode.NoResize : ResizeMode.CanResize;
            window.MinWidth = compact ? 0 : 360;
            window.MinHeight = compact ? 0 : 460;
            if (compact) { window.Width = size.Width * config.UiScale; window.Height = size.Height * config.UiScale; }
            else { restored = false; Restore(); }
            window.Topmost = config.AlwaysOnTop;
            if (PinnedToDesktop) SendToBottom();
        }
        public bool PinnedToDesktop { get { return Compact && !config.AlwaysOnTop; } }
        public void PlaceCompact() {
            Rect area = SystemParameters.WorkArea;
            double left = config.CompactLeft ?? area.Right - window.Width - 24, top = config.CompactTop ?? area.Top + 24;
            bool visible = left + 60 > SystemParameters.VirtualScreenLeft && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60
                && top + 40 > SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
            if (!visible) { left = area.Right - window.Width - 24; top = area.Top + 24; }
            window.Left = left; window.Top = top; restored = true;
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
        public void ApplyMaterial() { ApplyMaterial(config.Material, config.SurfaceOpacity); }
        // Values other than the saved config are a preview; ApplyMaterial() restores them.
        public void ApplyMaterial(string material) { ApplyMaterial(material, config.SurfaceOpacity); }
        public void ApplyMaterial(string material, double opacity) {
            MaterialStatus = ApplyBackdrop(window, window.FindName("Surface") as Border, material, opacity);
        }
        public const double MinOpacity = 0, DefaultOpacity = .66;
        public static double ClampOpacity(double value) { return Finite(value) ? Math.Max(MinOpacity, Math.Min(1, value)) : DefaultOpacity; }
        // DWM backdrop (Acrylic / Mica) plus a neutral smoke layer whose alpha is the
        // user's opacity: it keeps the blur but stops saturated wallpaper colours from
        // tinting text and progress segments. Shared by all four display sizes.
        public static string ApplyBackdrop(Window target, Border surface, string material, double opacity) {
            IntPtr hwnd = new WindowInteropHelper(target).Handle;
            if (hwnd == IntPtr.Zero) return "";
            try {
                var source = HwndSource.FromHwnd(hwnd); source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                int dark = 1, corner = 2, backdrop = material == "mica" ? 2 : 3;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4); DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
                int result = DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
                bool glass = result == 0 && backdrop != 1;
                byte alpha = (byte)Math.Round(ClampOpacity(opacity) * 255);
                if (surface != null) surface.Background = glass ? new SolidColorBrush(Color.FromArgb(alpha, 0x15, 0x17, 0x1B)) : new SolidColorBrush(Opaque);
                return glass ? (backdrop == 3 ? "Windows 原生 Acrylic" : "Windows 原生 Mica") : "不透明背景";
            } catch { if (surface != null) surface.Background = new SolidColorBrush(Opaque); return "不透明背景"; }
        }
        private static readonly Color Opaque = Color.FromRgb(0x1B, 0x1D, 0x22);
        public void Dispose() { geometryTimer.Stop(); }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    }
}
