using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RightMenuManager.Models;

namespace RightMenuManager.Services;

/// <summary>
/// 扫描注册表中各个场景下的静态右键菜单项和 Shell 扩展。
/// 只读取，不修改任何内容。
/// </summary>
public sealed class ContextMenuScanner
{
    private static readonly Regex ClsidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ProtectedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennew", "edit", "print", "printto", "find", "play",
        "runas", "explore", "preview", "opennewwindow"
    };

    private static readonly Dictionary<string, string> KnownVerbNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = "打开",
            ["opennew"] = "打开",
            ["edit"] = "编辑",
            ["print"] = "打印",
            ["printto"] = "打印到",
            ["find"] = "查找",
            ["play"] = "播放",
            ["runas"] = "以管理员身份运行",
            ["explore"] = "浏览",
            ["preview"] = "预览",
            ["opennewwindow"] = "在新窗口中打开",
            ["cmd"] = "在此处打开命令提示符",
            ["powershell"] = "在此处打开 PowerShell 窗口",
            ["pintohomefile"] = "固定到快速访问",
            ["removeproperties"] = "删除属性",
            ["windows.modernshare"] = "共享"
        };

    private sealed record ClassRoot(string ClassesPath, string SourceLabel, string SourceShort);

    private readonly record struct ComClassInfo(
        string? Display,
        string? FilePath,
        string? IconPath,
        int IconIndex);

    private static readonly ClassRoot[] ClassRoots = BuildClassRoots();

    private static readonly Dictionary<string, string?> FriendlyNameCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object FriendlyNameGate = new();

    private readonly Dictionary<string, ComClassInfo> _comCache =
        new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> _blockedClsids = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<MenuScope, List<ContextMenuItem>> ScanAll()
    {
        // 每次完整扫描都丢弃上一轮的 COM 解析结果。否则程序运行期间新安装的
        // 第三方组件会命中“空缓存”，按 F5 后仍然显示为 CLSID 或没有图标。
        _comCache.Clear();
        _blockedClsids = LoadBlockedClsids();

        var result = new Dictionary<MenuScope, List<ContextMenuItem>>();

        foreach (ScopeDefinition scope in ScopeCatalog.All)
        {
            var items = new List<ContextMenuItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ClassRoot root in ClassRoots)
            {
                string scopeRoot = $@"{root.ClassesPath}\{scope.BasePath}";
                ScanVerbs(scopeRoot, scope.Scope, root, items, seen);
                ScanShellExtensions(scopeRoot, scope.Scope, root, items, seen);
            }

            result[scope.Scope] = items;
        }

        return result;
    }

    private void ScanVerbs(
        string scopeRoot,
        MenuScope scope,
        ClassRoot root,
        List<ContextMenuItem> items,
        HashSet<string> seen)
    {
        string shellPath = scopeRoot + @"\shell";

        try
        {
            using var shell = RegistryHelper.OpenKey(shellPath);
            if (shell == null) return;

            foreach (string verbName in shell.GetSubKeyNames())
            {
                string verbPath = $@"{shellPath}\{verbName}";
                if (!seen.Add(verbPath)) continue;

                try
                {
                    using var verb = RegistryHelper.OpenKey(verbPath);
                    if (verb == null) continue;

                    ContextMenuItem item = CreateVerbItem(verb, verbName, verbPath, scope, root);
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    LogService.Write($"读取右键菜单项失败：{verbPath}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Write($"读取场景失败：{shellPath}", ex);
        }
    }

    private void ScanShellExtensions(
        string scopeRoot,
        MenuScope scope,
        ClassRoot root,
        List<ContextMenuItem> items,
        HashSet<string> seen)
    {
        string handlersPath = scopeRoot + @"\shellex\ContextMenuHandlers";

        try
        {
            using var handlers = RegistryHelper.OpenKey(handlersPath);
            if (handlers == null) return;

            foreach (string handlerName in handlers.GetSubKeyNames())
            {
                string handlerPath = $@"{handlersPath}\{handlerName}";
                if (!seen.Add(handlerPath)) continue;

                try
                {
                    using var handler = RegistryHelper.OpenKey(handlerPath);
                    if (handler == null) continue;

                    string? rawClsid = RegistryHelper.GetString(handler, null)
                                       ?? RegistryHelper.GetString(handler, "CLSID");
                    string? clsid = NormalizeClsid(rawClsid);
                    if (clsid == null) continue;

                    ComClassInfo com = ResolveComClass(clsid, root);
                    string display = ChooseHandlerDisplay(
                        com.Display, com.FilePath, handlerName, clsid);

                    items.Add(new ContextMenuItem
                    {
                        Scope = scope,
                        Kind = MenuItemKind.ShellExtension,
                        DisplayName = display,
                        KeyName = handlerName,
                        RegistryPath = handlerPath,
                        SourceShort = root.SourceShort,
                        SourceLabel = root.SourceLabel,
                        ScopeLabel = ScopeCatalog.Get(scope).Title,
                        Command = com.FilePath ?? "",
                        FilePath = com.FilePath ?? "",
                        IconPath = com.IconPath ?? com.FilePath ?? "",
                        IconIndex = com.IconIndex,
                        Clsid = clsid,
                        IsEnabled = !_blockedClsids.Contains(clsid),
                        IsProtected = false
                    });
                }
                catch (Exception ex)
                {
                    LogService.Write($"读取 Shell 扩展失败：{handlerPath}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Write($"读取场景失败：{handlersPath}", ex);
        }
    }

    private ContextMenuItem CreateVerbItem(
        RegistryKey verb,
        string verbName,
        string verbPath,
        MenuScope scope,
        ClassRoot root)
    {
        string? muiVerb = RegistryHelper.GetString(verb, "MUIVerb");
        string? defaultValue = RegistryHelper.GetString(verb, null);
        string? command = ReadVerbCommand(verb);
        string? iconValue = RegistryHelper.GetString(verb, "Icon")
                            ?? RegistryHelper.GetString(verb, "DefaultIcon");

        string? explorerHandler = RegistryHelper.GetString(verb, "ExplorerCommandHandler");
        string? delegateExecute = RegistryHelper.GetString(verb, "DelegateExecute");
        string? clsid = NormalizeClsid(explorerHandler) ?? NormalizeClsid(delegateExecute);
        ComClassInfo com = ResolveComClass(clsid, root);

        string? commandExecutable = ExtractExecutable(command);
        (string? iconPath, int iconIndex) = ParseIconLocation(iconValue);

        string filePath = commandExecutable
                          ?? com.FilePath
                          ?? iconPath
                          ?? "";

        bool hasExplicitDisplay = !string.IsNullOrWhiteSpace(muiVerb)
                                  || !string.IsNullOrWhiteSpace(defaultValue)
                                  || !string.IsNullOrWhiteSpace(com.Display);

        string display = CleanDisplay(muiVerb)
                         ?? CleanDisplay(defaultValue)
                         ?? com.Display
                         ?? LocalizedVerbName(verbName);

        // 新安装的第三方工具有时只写 GUID 作为键名或显示名，也没有 MUIVerb。
        // 用 exe/dll 的版本信息兜底，避免列表里只能看到一串 GUID。
        bool displayIsUnhelpful = IsGuidLike(display)
                                  || string.Equals(display, clsid, StringComparison.OrdinalIgnoreCase);
        if ((!hasExplicitDisplay && IsGuidLike(verbName)) || displayIsUnhelpful)
        {
            display = ResolveFileDisplayName(filePath) ?? display;
        }

        string resolvedIconPath = iconPath
                                  ?? com.IconPath
                                  ?? commandExecutable
                                  ?? com.FilePath
                                  ?? "";

        int resolvedIconIndex = iconPath != null ? iconIndex : 0;

        bool isProtected = ProtectedVerbs.Contains(verbName);
        string protectionReason = isProtected
            ? "这是 Windows 的系统默认操作。修改或删除它可能影响文件的打开方式和资源管理器行为。"
            : "";

        string detailCommand = command
                               ?? (clsid != null
                                   ? (explorerHandler != null
                                       ? $"ExplorerCommandHandler {clsid}"
                                       : $"COM 委托 {clsid}")
                                   : "");

        return new ContextMenuItem
        {
            Scope = scope,
            Kind = MenuItemKind.Verb,
            DisplayName = display,
            KeyName = verbName,
            RegistryPath = verbPath,
            SourceShort = root.SourceShort,
            SourceLabel = root.SourceLabel,
            ScopeLabel = ScopeCatalog.Get(scope).Title,
            Command = detailCommand,
            FilePath = filePath,
            IconPath = resolvedIconPath,
            IconIndex = resolvedIconIndex,
            Clsid = clsid ?? "",
            IsEnabled = ReadVerbEnabled(verb),
            IsExtended = RegistryHelper.HasValue(verb, "Extended"),
            IsSubMenu = RegistryHelper.HasValue(verb, "SubCommands")
                        || !string.IsNullOrWhiteSpace(
                            RegistryHelper.GetString(verb, "ExtendedSubCommandsKey")),
            IsConditional = RegistryHelper.HasValue(verb, "AppliesTo"),
            HasShield = RegistryHelper.HasValue(verb, "HasLUAShield"),
            IsProtected = isProtected,
            ProtectionReason = protectionReason
        };
    }

    private static string? ReadVerbCommand(RegistryKey verb)
    {
        try
        {
            using var commandKey = verb.OpenSubKey("command");
            string? command = RegistryHelper.GetString(commandKey, null);
            return string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadVerbEnabled(RegistryKey verb)
    {
        if (RegistryHelper.HasValue(verb, "LegacyDisable")
            || RegistryHelper.HasValue(verb, "ProgrammaticAccessOnly"))
        {
            return false;
        }

        if (RegistryHelper.TryGetInt(verb, "HideBasedOnVelocityId", out int velocityId)
            && velocityId == 0x639bc8)
        {
            return false;
        }

        if (RegistryHelper.TryGetInt(verb, "CommandFlags", out int commandFlags)
            && Math.Abs(commandFlags % 16) >= 8)
        {
            return false;
        }

        return true;
    }

    private ComClassInfo ResolveComClass(string? clsid, ClassRoot root)
    {
        if (string.IsNullOrWhiteSpace(clsid)) return default;

        string cacheKey = $"{root.ClassesPath}\\{clsid}";
        if (_comCache.TryGetValue(cacheKey, out ComClassInfo cached)) return cached;

        var searchRoots = new[]
        {
            root.ClassesPath,
            RegistryHelper.HkcuClasses,
            RegistryHelper.HklmClasses,
            RegistryHelper.Hklm32Classes
        };

        foreach (string classesPath in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string keyPath = $@"{classesPath}\CLSID\{clsid}";
            using var key = RegistryHelper.OpenKey(keyPath);
            if (key == null) continue;

            string? display = CleanDisplay(RegistryHelper.GetString(key, null))
                              ?? CleanDisplay(RegistryHelper.GetString(key, "LocalizedString"));

            string? inproc = null;
            string? localServer = null;

            using (var inprocKey = key.OpenSubKey("InprocServer32"))
            {
                inproc = RegistryHelper.ExpandPath(RegistryHelper.GetString(inprocKey, null));
            }

            using (var localKey = key.OpenSubKey("LocalServer32"))
            {
                localServer = ExtractExecutable(RegistryHelper.GetString(localKey, null));
            }

            string? iconValue = null;
            using (var iconKey = key.OpenSubKey("DefaultIcon"))
            {
                iconValue = RegistryHelper.GetString(iconKey, null);
            }

            (string? iconPath, int iconIndex) = ParseIconLocation(iconValue);
            string? filePath = inproc ?? localServer;

            var info = new ComClassInfo(
                display,
                filePath,
                iconPath ?? filePath,
                iconPath != null ? iconIndex : 0);

            _comCache[cacheKey] = info;
            return info;
        }

        _comCache[cacheKey] = default;
        return default;
    }

    private static string ChooseHandlerDisplay(
        string? comDisplay,
        string? filePath,
        string handlerName,
        string clsid)
    {
        if (!string.IsNullOrWhiteSpace(comDisplay)
            && !string.Equals(comDisplay.Trim(), clsid, StringComparison.OrdinalIgnoreCase))
        {
            return comDisplay.Trim();
        }

        if (!string.IsNullOrWhiteSpace(handlerName) && !IsGuidLike(handlerName))
        {
            return StripAccelerators(handlerName);
        }

        return ResolveFileDisplayName(filePath)
               ?? (string.IsNullOrWhiteSpace(handlerName) ? clsid : StripAccelerators(handlerName));
    }

    /// <summary>
    /// 当菜单项没有任何可读名称时，从关联的 exe/dll 版本信息里取一个名称。
    /// 版本信息读取较慢，所以按文件路径缓存。
    /// </summary>
    private static string? ResolveFileDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded)) return null;

        lock (FriendlyNameGate)
        {
            if (FriendlyNameCache.TryGetValue(expanded, out string? cached)) return cached;
        }

        string? result = null;
        try
        {
            if (File.Exists(expanded))
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(expanded);
                result = CleanDisplay(info.FileDescription)
                         ?? CleanDisplay(info.ProductName);
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                result = Path.GetFileNameWithoutExtension(expanded);
            }
        }
        catch
        {
            result = Path.GetFileNameWithoutExtension(expanded);
        }

        if (string.IsNullOrWhiteSpace(result)) result = null;

        lock (FriendlyNameGate)
        {
            FriendlyNameCache[expanded] = result;
        }

        return result;
    }

    private static bool IsGuidLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string text = value.Trim();
        if (text.Length > 2 && text[0] == '{' && text[^1] == '}')
        {
            text = text[1..^1];
        }

        return Guid.TryParse(text, out _);
    }

    private static string? CleanDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        string text = value.Trim();
        if (text.StartsWith('@'))
        {
            string? resolved = MuiResolver.Resolve(text)?.Trim();
            return string.IsNullOrWhiteSpace(resolved) ? null : StripAccelerators(resolved);
        }

        return StripAccelerators(text);
    }

    /// <summary>
    /// 去掉 Win32 菜单文本中的加速键标记。字面量 &amp; 在注册表中会写成 &amp;&amp;。
    /// </summary>
    private static string StripAccelerators(string text)
    {
        if (!text.Contains('&')) return text;

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character != '&')
            {
                builder.Append(character);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                builder.Append('&');
                i++;
            }
            // 单个 & 是加速键标记，直接丢弃。
        }

        return builder.ToString();
    }

    private static string LocalizedVerbName(string keyName)
    {
        return KnownVerbNames.TryGetValue(keyName, out string? name) ? name : keyName;
    }

    public static string? NormalizeClsid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        Match match = ClsidPattern.Match(value);
        return match.Success ? $"{{{match.Value.ToUpperInvariant()}}}" : null;
    }

    private static HashSet<string> LoadBlockedClsids()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in RegistryHelper.BlockedPaths)
        {
            try
            {
                using var key = RegistryHelper.OpenKey(path);
                if (key == null) continue;

                foreach (string valueName in key.GetValueNames())
                {
                    string trimmed = valueName.Trim();
                    if (trimmed.Length > 0) result.Add(trimmed);

                    if (NormalizeClsid(valueName) is { } normalized) result.Add(normalized);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"读取 Shell 扩展屏蔽列表失败：{path}", ex);
            }
        }

        return result;
    }

    public static bool IsBlocked(string? clsid)
    {
        if (string.IsNullOrWhiteSpace(clsid)) return false;
        return LoadBlockedClsids().Contains(clsid);
    }

    /// <summary>
    /// 计算一个轻量指纹，用于判断右键菜单结构是否变化（例如新装了软件）。
    /// 只枚举子项名称，不读取具体值，代价远低于完整扫描。
    /// </summary>
    public int ComputeFingerprint()
    {
        var hash = new HashCode();

        foreach (ScopeDefinition scope in ScopeCatalog.All)
        {
            foreach (ClassRoot root in ClassRoots)
            {
                string scopeRoot = $@"{root.ClassesPath}\{scope.BasePath}";
                AddSubKeyNames(ref hash, scopeRoot + @"\shell");
                AddSubKeyNames(ref hash, scopeRoot + @"\shellex\ContextMenuHandlers");
            }
        }

        return hash.ToHashCode();
    }

    private static void AddSubKeyNames(ref HashCode hash, string keyPath)
    {
        try
        {
            using var key = RegistryHelper.OpenKey(keyPath);
            if (key == null)
            {
                hash.Add(false);
                return;
            }

            hash.Add(true);
            string[] names = key.GetSubKeyNames();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            hash.Add(names.Length);
            foreach (string name in names)
            {
                hash.Add(name, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // 读取失败时把路径计入指纹，至少能在权限或键状态变化后触发刷新。
            hash.Add(keyPath, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        string text = Environment.ExpandEnvironmentVariables(command.Trim());
        string token;

        if (text.StartsWith('"'))
        {
            int closing = text.IndexOf('"', 1);
            token = closing > 0 ? text[1..closing] : text.Trim('"');
        }
        else
        {
            int space = text.IndexOf(' ');
            token = space > 0 ? text[..space] : text;
        }

        token = token.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token) || token.StartsWith('@')) return null;

        if (Path.IsPathRooted(token)) return token;

        string systemCandidate = Path.Combine(Environment.SystemDirectory, token);
        if (File.Exists(systemCandidate)) return systemCandidate;

        string windowsCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), token);
        return File.Exists(windowsCandidate) ? windowsCandidate : null;
    }

    public static (string? Path, int Index) ParseIconLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, 0);

        string text = Environment.ExpandEnvironmentVariables(value.Trim());
        if (text.StartsWith('@')) return (null, 0);

        int comma = text.LastIndexOf(',');
        if (comma > 0)
        {
            string indexText = text[(comma + 1)..].Trim();
            if (int.TryParse(indexText, out int index))
            {
                string path = text[..comma].Trim().Trim('"');
                return (string.IsNullOrWhiteSpace(path) ? null : path, index);
            }
        }

        return (text.Trim('"'), 0);
    }

    private static ClassRoot[] BuildClassRoots()
    {
        var roots = new List<ClassRoot>
        {
            new(RegistryHelper.HklmClasses, "系统", "HKLM")
        };

        if (Environment.Is64BitOperatingSystem)
        {
            roots.Add(new ClassRoot(RegistryHelper.Hklm32Classes, "系统 32 位", "HKLM32"));
        }

        roots.Add(new ClassRoot(RegistryHelper.HkcuClasses, "当前用户", "HKCU"));
        return roots.ToArray();
    }
}
