using System.Windows.Media;

namespace RightMenuManager.Models;

/// <summary>
/// 表示右键菜单中的一个可管理项。每一项对应一处确定的注册表位置。
/// </summary>
public sealed class ContextMenuItem : ObservableObject
{
    public MenuScope Scope { get; init; }
    public MenuItemKind Kind { get; init; }

    /// <summary>菜单显示名称（已尝试解析 MUI 字符串）。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>注册表子项名称，例如 "open" 或 "7-Zip"。</summary>
    public string KeyName { get; init; } = "";

    /// <summary>完整的注册表路径，例如 HKEY_LOCAL_MACHINE\SOFTWARE\Classes\*\shell\7-Zip。</summary>
    public string RegistryPath { get; init; } = "";

    /// <summary>来源短标签：HKCU / HKLM / HKLM32。</summary>
    public string SourceShort { get; init; } = "";

    /// <summary>来源说明：当前用户 / 系统 / 系统 32 位。</summary>
    public string SourceLabel { get; init; } = "";

    public string ScopeLabel { get; init; } = "";
    public string Command { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string IconPath { get; init; } = "";
    public int IconIndex { get; init; }
    public string Clsid { get; init; } = "";

    public bool IsExtended { get; init; }
    public bool IsSubMenu { get; init; }
    public bool IsConditional { get; init; }
    public bool HasShield { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = "";

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool CanToggle => !IsProtected;

    public bool IsExtension => Kind == MenuItemKind.ShellExtension;

    public string KindLabel => IsExtension ? "外壳扩展" : "菜单项";

    public string StateLabel => IsEnabled ? "已启用" : "已禁用";

    public string SecondaryText =>
        !string.IsNullOrWhiteSpace(Command)
            ? Command
            : !string.IsNullOrWhiteSpace(FilePath)
                ? FilePath
                : "（无命令行信息）";

    public string FilePathDisplay =>
        string.IsNullOrWhiteSpace(FilePath) ? "未解析到关联文件" : FilePath;

    public bool RequiresAdmin =>
        RegistryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);

    public string Id => RegistryPath;

    private string? _searchIndex;

    public string SearchIndex =>
        _searchIndex ??=
            $"{DisplayName} {KeyName} {Command} {FilePath} {RegistryPath} {Clsid} {SourceLabel}";

    public string AdminHint => RequiresAdmin ? "系统级项目，修改需要管理员权限" : "当前用户项目";
}
