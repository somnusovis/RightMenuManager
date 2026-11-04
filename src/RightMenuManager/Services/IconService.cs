using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RightMenuManager.Services;

/// <summary>
/// 从 exe/dll/ico 中提取图标。提取结果会冻结，可跨线程使用并带缓存。
/// </summary>
public static class IconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;

    private static readonly Dictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public static ImageSource? Get(string? path, int index = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded) || expanded.StartsWith('@')) return null;

        string cacheKey = $"{expanded}|{index}";
        lock (Gate)
        {
            if (Cache.TryGetValue(cacheKey, out ImageSource? cached)) return cached;
        }

        ImageSource? icon = Load(expanded, index);

        lock (Gate)
        {
            Cache[cacheKey] = icon;
        }

        return icon;
    }

    private static ImageSource? Load(string path, int index)
    {
        try
        {
            if (!Path.IsPathRooted(path))
            {
                string systemPath = Path.Combine(Environment.SystemDirectory, path);
                if (File.Exists(systemPath))
                {
                    path = systemPath;
                }
                else
                {
                    string windowsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
                    if (File.Exists(windowsPath)) path = windowsPath;
                }
            }

            if (File.Exists(path) && IsBitmapFile(path))
            {
                return LoadBitmap(path);
            }

            if (File.Exists(path))
            {
                ImageSource? extracted = ExtractIcon(path, index);
                if (extracted != null) return extracted;

                // 某些资源 DLL 没有图标组，退回 Shell 图标。
                return GetShellIcon(path);
            }

            return GetShellIcon(path);
        }
        catch (Exception ex)
        {
            LogService.Write($"图标提取失败：{path}", ex);
            return null;
        }
    }

    private static bool IsBitmapFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource? LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.DecodePixelWidth = 32;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? ExtractIcon(string path, int index)
    {
        IntPtr large = IntPtr.Zero;
        IntPtr small = IntPtr.Zero;

        try
        {
            uint count = ExtractIconEx(path, index, out large, out small, 1);
            if (count == 0)
            {
                count = ExtractIconEx(path, 0, out large, out small, 1);
            }

            IntPtr handle = large != IntPtr.Zero ? large : small;
            if (count == 0 || handle == IntPtr.Zero) return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    private static ImageSource? GetShellIcon(string path)
    {
        var info = new ShFileInfo();
        IntPtr result = SHGetFileInfo(
            path,
            FileAttributeNormal,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
