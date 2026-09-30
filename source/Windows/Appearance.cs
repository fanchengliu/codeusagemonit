using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodeUsageMonit {
    // Window surface: a colour and an "interface transparency" (0 = opaque) over the system
    // blur, plus an optional background picture. The picture is a copy in data/ (background.*); nothing
    // else is stored and no service is involved.
    public sealed partial class MonitorPanel {
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
        private const long MaxImageBytes = 40L * 1024 * 1024;

        private static string SavedBackgroundPath(AppConfig c) {
            if (String.IsNullOrEmpty(c.BackgroundImage)) return null;
            string path = Path.Combine(Store.Data, Path.GetFileName(c.BackgroundImage));
            return File.Exists(path) ? path : null;
        }
        // Decoded at most ~2560 px wide and fully loaded, so the file is not kept open.
        public static BitmapSource LoadPicture(string path) {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try {
                if (new FileInfo(path).Length > MaxImageBytes) return null;
                var image = new BitmapImage();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.DecodePixelWidth = 2560; image.StreamSource = stream; image.EndInit();
                }
                image.Freeze(); return image;
            } catch { return null; }
        }
        private void ApplyAppearance() { ApplyAppearance(config.SurfaceOpacity, SavedBackgroundPath(config), config.BackgroundFit, config.BackgroundDim, WindowFrame.ParseTint(config.SurfaceColor)); }
        // The last decoded picture, so dragging a slider or the palette does not decode again.
        private string cachedPicturePath; private DateTime cachedPictureStamp; private BitmapSource cachedPicture;
        private BitmapSource CachedPicture(string path) {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            DateTime stamp; try { stamp = File.GetLastWriteTimeUtc(path); } catch { return null; }
            if (path != cachedPicturePath || stamp != cachedPictureStamp) { cachedPicture = LoadPicture(path); cachedPicturePath = path; cachedPictureStamp = stamp; }
            return cachedPicture;
        }
        // alpha = 1 − transparency. The picture fades with the same transparency; the dim
        // layer (in a darker shade of the surface colour) keeps text readable on busy pictures.
        private void ApplyAppearance(double alpha, string picture, string fit, double dim, Color tint) {
            frame.ApplyMaterial("acrylic", alpha, tint);
            var layer = window.FindName("BackdropImage") as Border; var scrim = window.FindName("BackdropScrim") as Border;
            if (layer == null || scrim == null) return;
            BitmapSource image = CachedPicture(picture);
            if (image == null) { layer.Visibility = Visibility.Collapsed; scrim.Visibility = Visibility.Collapsed; layer.Background = null; return; }
            var brush = new ImageBrush(image) { AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            if (fit == "fit") brush.Stretch = Stretch.Uniform;
            else if (fit == "tile") { brush.Stretch = Stretch.None; brush.TileMode = TileMode.Tile; brush.ViewportUnits = BrushMappingMode.Absolute; brush.Viewport = new Rect(0, 0, image.Width, image.Height); }
            else brush.Stretch = Stretch.UniformToFill;
            brush.Freeze();
            layer.Background = brush; layer.Opacity = Math.Max(0, Math.Min(1, alpha)); layer.Visibility = Visibility.Visible;
            scrim.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Max(0, Math.Min(.85, dim)) * 255), (byte)(tint.R * .6), (byte)(tint.G * .6), (byte)(tint.B * .6)));
            scrim.Opacity = layer.Opacity; scrim.Visibility = Visibility.Visible;
        }
        // Opaque background for popups (period picker, palette): the surface colour, a shade lighter.
        private Brush PopupSurface() {
            Color c = WindowFrame.ParseTint(config.SurfaceColor);
            Func<byte, byte> lift = x => (byte)Math.Round(x + (255 - x) * .03);
            return new SolidColorBrush(Color.FromRgb(lift(c.R), lift(c.G), lift(c.B)));
        }
        // Copies the chosen picture into data/ and removes an older one.
        private static string StoreBackground(string source) {
            string ext = (Path.GetExtension(source) ?? "").ToLowerInvariant(); if (!ImageExtensions.Contains(ext)) ext = ".png";
            string name = "background" + ext, target = Path.Combine(Store.Data, name);
            if (!String.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, true);
            foreach (string old in Directory.GetFiles(Store.Data, "background.*")) if (!String.Equals(old, target, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(old); } catch { } }
            return name;
        }
        private static void RemoveBackground() { try { foreach (string old in Directory.GetFiles(Store.Data, "background.*")) File.Delete(old); } catch { } }
    }
}
