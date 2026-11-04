using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RightMenuManager.Services;

/// <summary>
/// 让窗口标题栏跟随应用的浅色主题，避免深色系统主题下出现突兀的黑色标题栏。
/// </summary>
public static class WindowThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int size);

    public static void UseLightTitleBar(Window window)
    {
        if (window.IsLoaded)
        {
            Apply(window);
            return;
        }

        window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int light = 0;
            int result = DwmSetWindowAttribute(
                handle,
                DwmwaUseImmersiveDarkMode,
                ref light,
                sizeof(int));

            if (result != 0)
            {
                DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkModeBefore20H1,
                    ref light,
                    sizeof(int));
            }
        }
        catch
        {
            // 旧版 Windows 或禁用 DWM 时忽略，不影响功能。
        }
    }
}
