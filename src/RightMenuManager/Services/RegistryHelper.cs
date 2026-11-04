using System.Security;
using Microsoft.Win32;

namespace RightMenuManager.Services;

/// <summary>
/// 注册表访问的薄封装。所有路径都使用完整的 HKEY_ 前缀字符串，方便展示、导出备份和恢复。
/// </summary>
public static class RegistryHelper
{
    public const string HkcuClasses = @"HKEY_CURRENT_USER\Software\Classes";
    public const string HklmClasses = @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes";
    public const string Hklm32Classes = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Classes";

    public const string HkcuBlocked =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public const string HklmBlocked =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public const string Hklm32Blocked =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public static readonly string[] BlockedPaths = { HkcuBlocked, HklmBlocked, Hklm32Blocked };

    private static RegistryView View =>
        Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;

    public static RegistryKey? OpenKey(string fullPath, bool writable = false)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;

        var (hive, subPath) = Split(fullPath);
        if (string.IsNullOrEmpty(subPath)) return null;

        using var root = RegistryKey.OpenBaseKey(hive, View);
        return root.OpenSubKey(subPath, writable);
    }

    public static RegistryKey CreateKey(string fullPath)
    {
        var (hive, subPath) = Split(fullPath);
        if (string.IsNullOrEmpty(subPath))
            throw new ArgumentException("无法在注册表根节点上创建子项。", nameof(fullPath));

        using var root = RegistryKey.OpenBaseKey(hive, View);
        return root.CreateSubKey(subPath, writable: true)
               ?? throw new IOException($"无法创建注册表项：{fullPath}");
    }

    public static bool KeyExists(string fullPath)
    {
        using var key = OpenKey(fullPath);
        return key != null;
    }

    public static void DeleteKeyTree(string fullPath)
    {
        var (hive, subPath) = Split(fullPath);
        int index = subPath.LastIndexOf('\\');
        string parentPath = index > 0 ? subPath[..index] : "";
        string keyName = index > 0 ? subPath[(index + 1)..] : subPath;

        using var root = RegistryKey.OpenBaseKey(hive, View);
        if (index <= 0)
        {
            root.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            return;
        }

        using var parent = root.OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
    }

    public static string? GetString(RegistryKey? key, string? valueName)
    {
        if (key == null) return null;

        try
        {
            object? value = key.GetValue(valueName);
            return value switch
            {
                null => null,
                string text => text,
                string[] array => array.FirstOrDefault(),
                _ => value.ToString()
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    public static bool HasValue(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValueNames()
                .Any(name => string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    public static bool TryGetInt(RegistryKey key, string valueName, out int value)
    {
        value = 0;
        try
        {
            object? raw = key.GetValue(valueName);
            switch (raw)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                    value = (int)longValue;
                    return true;
                case string text when int.TryParse(text, out int parsed):
                    value = parsed;
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    public static void SetString(string fullPath, string valueName, string value)
    {
        using var key = CreateKey(fullPath);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public static void SetDword(string fullPath, string valueName, int value)
    {
        using var key = CreateKey(fullPath);
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public static void DeleteValue(string fullPath, string valueName)
    {
        using var key = OpenKey(fullPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public static string? ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(expanded) ? null : expanded;
    }

    private static (RegistryHive Hive, string SubPath) Split(string fullPath)
    {
        int separator = fullPath.IndexOf('\\');
        string rootName = separator < 0 ? fullPath : fullPath[..separator];
        string subPath = separator < 0 ? "" : fullPath[(separator + 1)..];

        RegistryHive hive = rootName.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
            "HKEY_USERS" or "HKU" => RegistryHive.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => RegistryHive.CurrentConfig,
            _ => throw new ArgumentException($"不支持的注册表根节点：{rootName}", nameof(fullPath))
        };

        return (hive, subPath);
    }
}
