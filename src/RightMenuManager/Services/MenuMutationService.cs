using System.Security;
using Microsoft.Win32;
using RightMenuManager.Models;

namespace RightMenuManager.Services;

/// <summary>
/// 通过 Windows 官方支持的注册表隐藏方式启用/禁用菜单项，不采用粗暴删除。
/// </summary>
public sealed class MenuMutationService
{
    private const int HideBasedOnVelocityId = 0x639bc8;

    public void SetEnabled(ContextMenuItem item, bool enabled)
    {
        if (item.IsProtected) throw new InvalidOperationException(item.ProtectionReason);

        if (item.Kind == MenuItemKind.ShellExtension)
        {
            SetShellExtensionBlocked(item, blocked: !enabled);
        }
        else
        {
            SetVerbEnabled(item, enabled);
        }
    }

    private static void SetVerbEnabled(ContextMenuItem item, bool enabled)
    {
        using var verb = RegistryHelper.OpenKey(item.RegistryPath, writable: true)
                         ?? throw new InvalidOperationException("注册表项已不存在，可能已被其他程序修改。请刷新后重试。");

        if (enabled)
        {
            DeleteValueIfPresent(verb, "LegacyDisable");
            DeleteValueIfPresent(verb, "ProgrammaticAccessOnly");
            DeleteValueIfPresent(verb, "HideBasedOnVelocityId");

            // 某些菜单项通过 CommandFlags 的第 8 位隐藏，清除该位但保留其他标志。
            if (RegistryHelper.TryGetInt(verb, "CommandFlags", out int flags)
                && Math.Abs(flags % 16) >= 8)
            {
                int cleared = flags & ~8;
                if (cleared == 0)
                {
                    verb.DeleteValue("CommandFlags", throwOnMissingValue: false);
                }
                else
                {
                    verb.SetValue("CommandFlags", cleared, RegistryValueKind.DWord);
                }
            }

            return;
        }

        verb.SetValue("LegacyDisable", "", RegistryValueKind.String);
        verb.SetValue("ProgrammaticAccessOnly", "", RegistryValueKind.String);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
        {
            verb.SetValue("HideBasedOnVelocityId", HideBasedOnVelocityId, RegistryValueKind.DWord);
        }
    }

    private static void SetShellExtensionBlocked(ContextMenuItem item, bool blocked)
    {
        if (string.IsNullOrWhiteSpace(item.Clsid))
        {
            throw new InvalidOperationException("未能解析该 Shell 扩展的 CLSID，无法修改。");
        }

        string clsid = item.Clsid;

        if (blocked)
        {
            using (var key = RegistryHelper.CreateKey(RegistryHelper.HkcuBlocked))
            {
                key.SetValue(clsid, "", RegistryValueKind.String);
            }

            // 当前用户屏蔽已经足够；管理员模式下再同步一份系统级屏蔽。
            if (AdminService.IsAdministrator)
            {
                try
                {
                    using var machineKey = RegistryHelper.CreateKey(RegistryHelper.HklmBlocked);
                    machineKey.SetValue(clsid, "", RegistryValueKind.String);
                }
                catch (Exception ex)
                {
                    LogService.Write($"写入系统级屏蔽列表失败：{clsid}", ex);
                }
            }

            if (!ContextMenuScanner.IsBlocked(clsid))
            {
                throw new InvalidOperationException("写入 Shell 扩展屏蔽列表失败，注册表可能被安全软件拦截。");
            }

            return;
        }

        // 启用：先删除当前用户屏蔽，再处理系统级屏蔽（需要管理员权限）。
        RemoveBlockedValue(RegistryHelper.HkcuBlocked, clsid);

        foreach (string machinePath in new[] { RegistryHelper.HklmBlocked, RegistryHelper.Hklm32Blocked })
        {
            if (!ContainsBlockedValue(machinePath, clsid)) continue;

            if (!AdminService.IsAdministrator)
            {
                throw new UnauthorizedAccessException(
                    "该 Shell 扩展在系统级屏蔽列表中。恢复它需要管理员权限。");
            }

            RemoveBlockedValue(machinePath, clsid);
        }

        if (ContextMenuScanner.IsBlocked(clsid))
        {
            throw new InvalidOperationException("该 Shell 扩展仍处于屏蔽状态，请重启资源管理器后再试。");
        }
    }

    private static bool ContainsBlockedValue(string path, string clsid)
    {
        using var key = RegistryHelper.OpenKey(path);
        if (key == null) return false;

        foreach (string valueName in key.GetValueNames())
        {
            if (valueName.Trim().Equals(clsid, StringComparison.OrdinalIgnoreCase)) return true;
            string? normalized = ContextMenuScanner.NormalizeClsid(valueName);
            if (normalized != null
                && normalized.Equals(clsid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveBlockedValue(string path, string clsid)
    {
        try
        {
            using var key = RegistryHelper.OpenKey(path, writable: true);
            if (key == null) return;

            foreach (string valueName in key.GetValueNames())
            {
                bool matches = valueName.Trim().Equals(clsid, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    string? normalized = ContextMenuScanner.NormalizeClsid(valueName);
                    matches = normalized != null
                              && normalized.Equals(clsid, StringComparison.OrdinalIgnoreCase);
                }

                if (matches)
                {
                    key.DeleteValue(valueName, throwOnMissingValue: false);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new UnauthorizedAccessException(
                "需要管理员权限才能修改系统级 Shell 扩展屏蔽列表。", ex);
        }
    }

    private static void DeleteValueIfPresent(RegistryKey key, string name)
    {
        if (RegistryHelper.HasValue(key, name))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
