using System.Runtime.InteropServices;
using System.Text;

namespace RightMenuManager.Services;

/// <summary>
/// 解析 "@C:\Windows\system32\shell32.dll,-8506" 这类 MUI 字符串资源。
/// </summary>
public static class MuiResolver
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        int cchOutBuf,
        IntPtr ppvReserved);

    public static string? Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        string key = source.Trim();
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out string? cached)) return cached;
        }

        string? result = null;
        try
        {
            var buffer = new StringBuilder(1024);
            if (SHLoadIndirectString(key, buffer, buffer.Capacity, IntPtr.Zero) == 0)
            {
                result = buffer.ToString();
            }
        }
        catch
        {
            // 资源不存在或格式异常时返回 null，由调用方使用后备名称。
        }

        lock (Gate)
        {
            Cache[key] = result;
        }

        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
