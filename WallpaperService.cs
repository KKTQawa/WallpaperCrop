using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperCrop;

internal static class WallpaperService
{
    // The desktop compositor is the only capture source that consistently includes
    // GPU-rendered live wallpapers. Reading Wallpaper Engine's HWND/DC directly can
    // succeed while returning an all-black bitmap (a common DirectX surface behavior).
    // RegionSelector is hidden before this method is called, so the composed desktop
    // contains the wallpaper rather than the selection overlay.
    public static Bitmap CaptureDesktopRegion(Rectangle screenRegion)
    {
        try { return CaptureComposedDesktopRegion(screenRegion); }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            // Keep the static-wallpaper fallback for unusual desktops where the
            // screen DC cannot be read (for example, a disconnected remote session).
            return CropStaticWallpaperFill(screenRegion);
        }
    }

    private static Bitmap CaptureComposedDesktopRegion(Rectangle screenRegion)
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        var crop = Rectangle.Intersect(screenRegion, screen);
        if (crop.Width < 1 || crop.Height < 1)
            throw new ArgumentException("选择区域不在主显示器内。", nameof(screenRegion));

        var result = new Bitmap(crop.Width, crop.Height);
        using var graphics = Graphics.FromImage(result);
        graphics.CopyFromScreen(crop.Location, Point.Empty, crop.Size, CopyPixelOperation.SourceCopy);
        return result;
    }

    // Wallpaper Engine owns a fullscreen child window of the desktop host. Capturing
    // that window's device context retains its live DirectX scene instead of falling
    // through to the static Windows wallpaper file.
    private static IntPtr FindWallpaperEngineRenderWindow(Rectangle target)
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        NativeMethods.EnumChildWindows(NativeMethods.GetDesktopWindow(), (window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) || !NativeMethods.GetWindowRect(window, out var rect)) return true;
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < screen.Width * .75 || bounds.Height < screen.Height * .75) return true;
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            try
            {
                var name = Process.GetProcessById((int)processId).ProcessName;
                if (!name.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase)) return true;
                var area = (long)bounds.Width * bounds.Height;
                if (area > bestArea) { bestArea = area; best = window; }
            }
            catch (ArgumentException) { }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static Bitmap CropWindowDc(IntPtr window, Rectangle screenRegion)
    {
        if (!NativeMethods.GetWindowRect(window, out var rect)) throw new InvalidOperationException("无法读取 Wallpaper Engine 的渲染窗口位置。");
        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var crop = Rectangle.Intersect(screenRegion, bounds);
        if (crop.Width < 1 || crop.Height < 1) throw new InvalidOperationException("选择区域不在 Wallpaper Engine 渲染范围内。");
        var output = new Bitmap(crop.Width, crop.Height);
        using var g = Graphics.FromImage(output);
        var destinationDc = g.GetHdc();
        var sourceDc = NativeMethods.GetWindowDC(window);
        try
        {
            if (sourceDc == IntPtr.Zero || !NativeMethods.BitBlt(destinationDc, 0, 0, crop.Width, crop.Height, sourceDc, crop.Left - bounds.Left, crop.Top - bounds.Top, 0x00CC0020))
                throw new InvalidOperationException("无法读取 Wallpaper Engine 的实时渲染画面。");
        }
        finally
        {
            if (sourceDc != IntPtr.Zero) NativeMethods.ReleaseDC(window, sourceDc);
            g.ReleaseHdc(destinationDc);
        }
        return output;
    }

    private static Bitmap CropDesktopCapture(Rectangle screenRegion)
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        using var desktop = CaptureWallpaperHost(screen);
        var crop = Rectangle.Intersect(screenRegion, screen);
        if (crop.Width < 1 || crop.Height < 1) throw new InvalidOperationException("选择区域不在主显示器内。");
        var result = new Bitmap(crop.Width, crop.Height);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(desktop, new Rectangle(0, 0, result.Width, result.Height), new Rectangle(crop.Left - screen.Left, crop.Top - screen.Top, crop.Width, crop.Height), GraphicsUnit.Pixel);
        return result;
    }

    // Fallback for a normal static Windows wallpaper when Wallpaper Engine is not running.
    private static Bitmap CropStaticWallpaperFill(Rectangle screenRegion)
    {
        var path = new StringBuilder(260);
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDESKWALLPAPER, path.Capacity, path, 0);
        if (path.Length == 0 || !File.Exists(path.ToString()))
            throw new InvalidOperationException("未找到 Wallpaper Engine 的桌面层，且当前 Windows 壁纸不是可读取的静态图片。\n请启动 Wallpaper Engine 后重试。");
        using var source = new Bitmap(path.ToString());
        var screen = Screen.PrimaryScreen!.Bounds;
        var crop = Rectangle.Intersect(screenRegion, screen);
        if (crop.Width < 1 || crop.Height < 1) throw new InvalidOperationException("选择区域不在主显示器内。");
        var scale = Math.Max((float)screen.Width / source.Width, (float)screen.Height / source.Height);
        var renderedWidth = source.Width * scale;
        var renderedHeight = source.Height * scale;
        var offsetX = (screen.Width - renderedWidth) / 2f;
        var offsetY = (screen.Height - renderedHeight) / 2f;
        var sourceRect = RectangleF.FromLTRB((crop.Left - screen.Left - offsetX) / scale, (crop.Top - screen.Top - offsetY) / scale, (crop.Right - screen.Left - offsetX) / scale, (crop.Bottom - screen.Top - offsetY) / scale);
        sourceRect = RectangleF.Intersect(sourceRect, new RectangleF(0, 0, source.Width, source.Height));
        if (sourceRect.Width < 1 || sourceRect.Height < 1) throw new InvalidOperationException("选择区域不在壁纸范围内。");
        var result = new Bitmap(Math.Max(1, (int)Math.Round(sourceRect.Width)), Math.Max(1, (int)Math.Round(sourceRect.Height)));
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height), sourceRect, GraphicsUnit.Pixel);
        return result;
    }

    private static Bitmap CaptureWallpaperHost(Rectangle screen)
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            NativeMethods.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 500, out _);

        IntPtr worker = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            if (NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                worker = NativeMethods.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return worker == IntPtr.Zero;
            }
            return true;
        }, IntPtr.Zero);

        // Windows 11 and Wallpaper Engine can omit the classic SHELLDLL_DefView
        // sibling. In that layout, use a full-screen WorkerW discovered directly.
        if (worker == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((top, _) =>
            {
                var className = new System.Text.StringBuilder(256);
                NativeMethods.GetClassName(top, className, className.Capacity);
                if (className.ToString() == "WorkerW" && NativeMethods.GetWindowRect(top, out var rect) && rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom)
                {
                    worker = top;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (worker == IntPtr.Zero || !NativeMethods.GetWindowRect(worker, out var bounds))
            throw new InvalidOperationException("找不到 Wallpaper Engine 的桌面渲染层。请确认 Wallpaper Engine 正在运行。");

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < screen.Width || height < screen.Height)
            throw new InvalidOperationException("Wallpaper Engine 的桌面渲染层尺寸异常。");

        using var hostImage = new Bitmap(width, height);
        using (var g = Graphics.FromImage(hostImage))
        {
            var hdc = g.GetHdc();
            var printed = NativeMethods.PrintWindow(worker, hdc, 2);
            g.ReleaseHdc(hdc);
            if (!printed) throw new InvalidOperationException("无法读取 Wallpaper Engine 的桌面画面。");
        }
        var output = new Bitmap(screen.Width, screen.Height);
        using (var g = Graphics.FromImage(output))
            g.DrawImage(hostImage, new Rectangle(0, 0, output.Width, output.Height), new Rectangle(screen.Left - bounds.Left, screen.Top - bounds.Top, screen.Width, screen.Height), GraphicsUnit.Pixel);
        return output;
    }

    public static Rectangle Denormalize(SavedRegion region)
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        return Rectangle.FromLTRB(screen.Left + (int)Math.Round(region.X * screen.Width), screen.Top + (int)Math.Round(region.Y * screen.Height), screen.Left + (int)Math.Round((region.X + region.Width) * screen.Width), screen.Top + (int)Math.Round((region.Y + region.Height) * screen.Height));
    }

    public static SavedRegion Normalize(Rectangle rectangle, int slot, string name)
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        return new SavedRegion { Name = name, Slot = slot, X = (float)(rectangle.Left - screen.Left) / screen.Width, Y = (float)(rectangle.Top - screen.Top) / screen.Height, Width = (float)rectangle.Width / screen.Width, Height = (float)rectangle.Height / screen.Height };
    }
}
