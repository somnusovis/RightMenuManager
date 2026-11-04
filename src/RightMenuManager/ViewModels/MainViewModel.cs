using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RightMenuManager.Models;
using RightMenuManager.Services;
using RightMenuManager.Views;

namespace RightMenuManager.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ContextMenuScanner _scanner = new();
    private readonly MenuMutationService _mutation = new();
    private readonly BackupService _backup = new();
    private readonly Dictionary<MenuScope, List<ContextMenuItem>> _cache = new();
    private readonly bool _isAdmin = AdminService.IsAdministrator;

    private readonly AsyncRelayCommand _refreshCommand;
    private readonly RelayCommand _enableSelectedCommand;
    private readonly RelayCommand _disableSelectedCommand;
    private readonly AsyncRelayCommand _deleteSelectedCommand;
    private readonly RelayCommand _openRegistryCommand;
    private readonly RelayCommand _openFileLocationCommand;
    private readonly RelayCommand _copyCommandCommand;
    private readonly RelayCommand _clearSearchCommand;
    private readonly RelayCommand _elevateCommand;
    private readonly AsyncRelayCommand _restartExplorerCommand;
    private readonly RelayCommand _openBackupsCommand;

    private CancellationTokenSource? _iconCts;
    private bool _disposed;
    private bool _initialized;
    private bool _changeCheckRunning;
    private int? _lastFingerprint;
    private DateTime _lastChangeCheckUtc = DateTime.MinValue;
    private string[] _searchTerms = Array.Empty<string>();

    private ScopeNavItem _selectedScope;
    private ContextMenuItem? _selectedItem;
    private string _searchText = "";
    private FilterMode _filter = FilterMode.All;
    private bool _isLoading;
    private string _statusMessage = "更改会立即写入注册表，删除前自动备份。";

    public MainViewModel()
    {
        Scopes = new ObservableCollection<ScopeNavItem>(
            ScopeCatalog.All.Select(definition => new ScopeNavItem(definition)));

        _selectedScope = Scopes[0];

        _refreshCommand = new AsyncRelayCommand(
            () => ReloadAsync(),
            () => !IsLoading);

        _enableSelectedCommand = new RelayCommand(
            () => SetEnabled(SelectedItem!, true),
            () => !IsLoading && SelectedItem is { IsEnabled: false, CanToggle: true });

        _disableSelectedCommand = new RelayCommand(
            () => SetEnabled(SelectedItem!, false),
            () => !IsLoading && SelectedItem is { IsEnabled: true, CanToggle: true });

        _deleteSelectedCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            () => !IsLoading && SelectedItem != null);

        _openRegistryCommand = new RelayCommand(
            OpenRegistry,
            () => SelectedItem != null);

        _openFileLocationCommand = new RelayCommand(
            OpenFileLocation,
            () => SelectedItem != null);

        _copyCommandCommand = new RelayCommand(
            CopyCommand,
            () => SelectedItem != null);

        _clearSearchCommand = new RelayCommand(
            () => SearchText = "",
            () => !string.IsNullOrEmpty(SearchText));

        _elevateCommand = new RelayCommand(Elevate, () => !IsAdmin);

        _restartExplorerCommand = new AsyncRelayCommand(RestartExplorerAsync);

        _openBackupsCommand = new RelayCommand(OpenBackups);
    }

    public ObservableCollection<ScopeNavItem> Scopes { get; }

    public ObservableCollection<ContextMenuItem> Items { get; } = new();

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand EnableSelectedCommand => _enableSelectedCommand;
    public ICommand DisableSelectedCommand => _disableSelectedCommand;
    public ICommand DeleteSelectedCommand => _deleteSelectedCommand;
    public ICommand OpenRegistryCommand => _openRegistryCommand;
    public ICommand OpenFileLocationCommand => _openFileLocationCommand;
    public ICommand CopyCommandCommand => _copyCommandCommand;
    public ICommand ClearSearchCommand => _clearSearchCommand;
    public ICommand ElevateCommand => _elevateCommand;
    public ICommand RestartExplorerCommand => _restartExplorerCommand;
    public ICommand OpenBackupsCommand => _openBackupsCommand;

    public ScopeNavItem SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (SetProperty(ref _selectedScope, value) && value != null)
            {
                ApplyScope();
                OnPropertyChanged(nameof(TotalCount));
            }
        }
    }

    public ContextMenuItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasFilePath));
            RaiseCommandStates();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;

            _searchTerms = string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            ApplyScope();
            _clearSearchCommand.RaiseCanExecuteChanged();
        }
    }

    public FilterMode Filter
    {
        get => _filter;
        private set
        {
            if (!SetProperty(ref _filter, value)) return;

            OnPropertyChanged(nameof(IsAllFilter));
            OnPropertyChanged(nameof(IsEnabledFilter));
            OnPropertyChanged(nameof(IsDisabledFilter));
            OnPropertyChanged(nameof(IsExtensionFilter));
            ApplyScope();
        }
    }

    public bool IsAllFilter
    {
        get => _filter == FilterMode.All;
        set
        {
            if (value) Filter = FilterMode.All;
        }
    }

    public bool IsEnabledFilter
    {
        get => _filter == FilterMode.Enabled;
        set
        {
            if (value) Filter = FilterMode.Enabled;
        }
    }

    public bool IsDisabledFilter
    {
        get => _filter == FilterMode.Disabled;
        set
        {
            if (value) Filter = FilterMode.Disabled;
        }
    }

    public bool IsExtensionFilter
    {
        get => _filter == FilterMode.Extensions;
        set
        {
            if (value) Filter = FilterMode.Extensions;
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            RaiseCommandStates();
        }
    }

    public bool IsAdmin => _isAdmin;

    public string AdminStatusText => IsAdmin ? "管理员模式" : "普通权限";

    public string AdminSubText => IsAdmin
        ? "可修改系统级菜单项"
        : "系统级项目需要提升权限";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasSelection => SelectedItem != null;

    public bool HasFilePath => SelectedItem is { FilePath.Length: > 0 };

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public int TotalCount
    {
        get
        {
            var source = CurrentScopeItems;
            return source.Count;
        }
    }

    public string CountSummary
    {
        get
        {
            var source = CurrentScopeItems;
            int total = source.Count;
            int enabled = source.Count(item => item.IsEnabled);

            string summary = $"共 {total} 项 · 已启用 {enabled} · 已禁用 {total - enabled}";
            if (Items.Count != total)
            {
                summary += $" · 当前显示 {Items.Count} 项";
            }

            return summary;
        }
    }

    private List<ContextMenuItem> CurrentScopeItems =>
        _cache.TryGetValue(SelectedScope.Scope, out var items)
            ? items
            : new List<ContextMenuItem>();

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await ReloadAsync();
    }

    public async Task ReloadAsync(bool preserveSelection = true)
    {
        if (_disposed) return;

        string? selectedId = preserveSelection ? SelectedItem?.Id : null;
        IsLoading = true;

        try
        {
            var scan = await Task.Run(() => (
                Data: _scanner.ScanAll(),
                Fingerprint: _scanner.ComputeFingerprint()));

            if (_disposed) return;

            _cache.Clear();
            foreach (var pair in scan.Data)
            {
                _cache[pair.Key] = pair.Value;
            }

            UpdateScopeCounts();
            ApplyScope();

            if (selectedId != null)
            {
                SelectedItem = Items.FirstOrDefault(item =>
                    string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            }

            OnPropertyChanged(nameof(CountSummary));
            OnPropertyChanged(nameof(TotalCount));

            // 等界面数据真正更新成功后再记录指纹，失败时下次激活会重试。
            _lastFingerprint = scan.Fingerprint;

            _ = LoadIconsAsync();
        }
        catch (Exception ex)
        {
            LogService.Write("扫描注册表失败", ex);
            DialogService.Error("读取注册表失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 窗口重新激活时调用。只有注册表结构真的发生变化才重新扫描，
    /// 这样新安装的第三方软件会被自动发现，普通切换窗口又不会反复刷新列表。
    /// </summary>
    public async Task CheckForChangesAsync()
    {
        if (_disposed || IsLoading || _changeCheckRunning || _lastFingerprint == null) return;

        DateTime now = DateTime.UtcNow;
        if (now - _lastChangeCheckUtc < TimeSpan.FromSeconds(2)) return;
        _lastChangeCheckUtc = now;

        _changeCheckRunning = true;
        try
        {
            int fingerprint = await Task.Run(() => _scanner.ComputeFingerprint());

            if (_disposed || IsLoading || fingerprint == _lastFingerprint.Value) return;

            int previousCount = _cache.Values.Sum(list => list.Count);
            await ReloadAsync();
            int added = Math.Max(0, _cache.Values.Sum(list => list.Count) - previousCount);

            StatusMessage = added > 0
                ? $"检测到 {added} 个新的右键菜单项，已自动刷新"
                : "检测到右键菜单变化，已自动刷新";
        }
        catch (Exception ex)
        {
            LogService.Write("自动检测右键菜单变化失败", ex);
        }
        finally
        {
            _changeCheckRunning = false;
        }
    }

    public void SetEnabled(ContextMenuItem item, bool enabled)
    {
        if (IsLoading || _disposed) return;

        if (item.IsProtected)
        {
            DialogService.Info("受保护的菜单项", item.ProtectionReason);
            return;
        }

        try
        {
            _mutation.SetEnabled(item, enabled);
            item.IsEnabled = enabled;

            // Shell 扩展的屏蔽是全局的，同步更新其他场景中的同一 CLSID。
            if (item.Kind == MenuItemKind.ShellExtension
                && !string.IsNullOrWhiteSpace(item.Clsid))
            {
                foreach (ContextMenuItem related in _cache.Values
                             .SelectMany(list => list)
                             .Where(other => other.Kind == MenuItemKind.ShellExtension
                                             && string.Equals(other.Clsid, item.Clsid,
                                                 StringComparison.OrdinalIgnoreCase)))
                {
                    related.IsEnabled = enabled;
                }
            }

            StatusMessage = $"已{(enabled ? "启用" : "禁用")}「{item.DisplayName}」";

            // 只有在“已启用/已禁用”筛选下，开关状态才可能改变项目是否可见。
            if (_filter is FilterMode.Enabled or FilterMode.Disabled)
            {
                ApplyScope();
            }
            else
            {
                OnPropertyChanged(nameof(CountSummary));
            }

            RaiseCommandStates();
        }
        catch (UnauthorizedAccessException ex)
        {
            PromptElevate(item, ex.Message);
        }
        catch (Exception ex)
        {
            LogService.Write($"修改菜单项失败：{item.RegistryPath}", ex);
            DialogService.Error("操作失败", ex.Message);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (IsLoading || _disposed || SelectedItem is not { } item) return;

        if (item.IsProtected)
        {
            DialogService.Info("受保护的菜单项", item.ProtectionReason);
            return;
        }

        if (item.RequiresAdmin && !IsAdmin)
        {
            PromptElevate(item, "删除系统级菜单项需要对 HKEY_LOCAL_MACHINE 的写入权限。");
            return;
        }

        bool confirmed = DialogService.Confirm(
            "删除菜单项",
            $"即将删除「{item.DisplayName}」\n\n注册表路径：\n{item.RegistryPath}\n\n" +
            "删除前会自动导出 .reg 备份，可在「备份与恢复」中还原。删除后可能需要重启资源管理器才会生效。",
            "备份并删除",
            "取消",
            danger: true);

        if (!confirmed) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在备份并删除…";

            BackupEntry entry = await Task.Run(() => _backup.BackupAndDelete(item));

            StatusMessage = $"已删除「{item.DisplayName}」，备份已保存";
            LogService.Write($"已删除注册表项：{item.RegistryPath}；备份：{entry.FolderPath}");

            await ReloadAsync(preserveSelection: false);
        }
        catch (UnauthorizedAccessException ex)
        {
            PromptElevate(item, ex.Message);
        }
        catch (Exception ex)
        {
            LogService.Write($"删除菜单项失败：{item.RegistryPath}", ex);
            DialogService.Error("删除失败", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenRegistry()
    {
        if (SelectedItem is not { } item) return;

        try
        {
            ShellService.OpenRegeditAt(item.RegistryPath);
            StatusMessage = "已打开注册表编辑器。若它已经在运行，请按 F5 刷新。";
        }
        catch (Exception ex)
        {
            LogService.Write("打开注册表失败", ex);
            DialogService.Error("无法打开注册表", ex.Message);
        }
    }

    private void OpenFileLocation()
    {
        if (SelectedItem is not { } item) return;

        if (string.IsNullOrWhiteSpace(item.FilePath))
        {
            DialogService.Info(
                "没有可定位的文件",
                "这个菜单项没有解析出关联的 exe/dll 路径。\n可以改为查看它的注册表项。");
            return;
        }

        try
        {
            ShellService.OpenFileLocation(item.FilePath);
            StatusMessage = "已在资源管理器中定位文件";
        }
        catch (Exception ex)
        {
            LogService.Write($"打开文件位置失败：{item.FilePath}", ex);
            DialogService.Error("无法定位文件", ex.Message);
        }
    }

    private void CopyCommand()
    {
        if (SelectedItem is not { } item) return;

        if (string.IsNullOrWhiteSpace(item.Command))
        {
            DialogService.Info("没有命令行", "这个菜单项没有可复制的命令行，可以改为查看它的注册表项。");
            return;
        }

        try
        {
            Clipboard.SetText(item.Command);
            StatusMessage = "命令已复制到剪贴板";
        }
        catch (Exception ex)
        {
            LogService.Write("复制命令失败", ex);
            DialogService.Error("复制失败", ex.Message);
        }
    }

    private void OpenBackups()
    {
        var window = new BackupWindow(_backup, () => ReloadAsync())
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void PromptElevate(ContextMenuItem item, string message)
    {
        bool elevate = DialogService.Confirm(
            "需要管理员权限",
            $"{message}\n\n目标项目：{item.DisplayName}\n\n" +
            "可以以管理员身份重新启动右键管家，然后重试这次操作。",
            "以管理员身份重启",
            "取消");

        if (elevate) Elevate();
    }

    private void Elevate()
    {
        if (IsAdmin)
        {
            DialogService.Info("权限状态", "当前已经是管理员模式，无需再次提升权限。");
            return;
        }

        try
        {
            AdminService.RestartElevated();
            Application.Current.Shutdown();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusMessage = "已取消权限提升";
        }
        catch (Exception ex)
        {
            LogService.Write("提升权限失败", ex);
            DialogService.Error("无法提升权限", ex.Message);
        }
    }

    private async Task RestartExplorerAsync()
    {
        bool confirmed = DialogService.Confirm(
            "重启资源管理器",
            "将关闭所有资源管理器窗口并重新启动 Windows 外壳。\n\n" +
            "正在运行的程序不会关闭，但未保存的文件浏览位置可能会丢失。是否继续？",
            "重启",
            "取消");

        if (!confirmed) return;

        try
        {
            StatusMessage = "正在重启资源管理器…";
            await ShellService.RestartExplorerAsync();
            StatusMessage = "资源管理器已重启";
        }
        catch (Exception ex)
        {
            LogService.Write("重启资源管理器失败", ex);
            DialogService.Error("重启失败", ex.Message);
        }
    }

    private void ApplyScope()
    {
        if (SelectedScope == null) return;

        string? selectedId = SelectedItem?.Id;

        List<ContextMenuItem> source = CurrentScopeItems;
        var filtered = source.Where(Matches).ToList();

        Items.Clear();
        foreach (ContextMenuItem item in filtered)
        {
            Items.Add(item);
        }

        SelectedItem = selectedId == null
            ? null
            : Items.FirstOrDefault(item =>
                string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CountSummary));
        RaiseCommandStates();
    }

    private bool Matches(ContextMenuItem item)
    {
        switch (_filter)
        {
            case FilterMode.Enabled when !item.IsEnabled:
            case FilterMode.Disabled when item.IsEnabled:
                return false;
            case FilterMode.Extensions when item.Kind != MenuItemKind.ShellExtension:
                return false;
        }

        if (_searchTerms.Length == 0) return true;

        return _searchTerms.All(term =>
            item.SearchIndex.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateScopeCounts()
    {
        foreach (ScopeNavItem scope in Scopes)
        {
            scope.Count = _cache.TryGetValue(scope.Scope, out var items)
                ? items.Count
                : 0;
        }
    }

    private async Task LoadIconsAsync()
    {
        _iconCts?.Cancel();
        var cts = new CancellationTokenSource();
        _iconCts = cts;

        List<ContextMenuItem> items = _cache.Values
            .SelectMany(list => list)
            .Where(item => item.Icon == null)
            .ToList();

        foreach (ContextMenuItem item in items)
        {
            if (_disposed || cts.IsCancellationRequested) return;

            string? path = !string.IsNullOrWhiteSpace(item.IconPath)
                ? item.IconPath
                : item.FilePath;

            if (string.IsNullOrWhiteSpace(path)) continue;

            int index = item.IconIndex;
            ImageSource? icon = await Task.Run(() => IconService.Get(path, index));

            if (_disposed || cts.IsCancellationRequested) return;
            if (icon != null) item.Icon = icon;
        }
    }

    private void RaiseCommandStates()
    {
        _refreshCommand.RaiseCanExecuteChanged();
        _enableSelectedCommand.RaiseCanExecuteChanged();
        _disableSelectedCommand.RaiseCanExecuteChanged();
        _deleteSelectedCommand.RaiseCanExecuteChanged();
        _openRegistryCommand.RaiseCanExecuteChanged();
        _openFileLocationCommand.RaiseCanExecuteChanged();
        _copyCommandCommand.RaiseCanExecuteChanged();
        _clearSearchCommand.RaiseCanExecuteChanged();
        _elevateCommand.RaiseCanExecuteChanged();
        _restartExplorerCommand.RaiseCanExecuteChanged();
        _openBackupsCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        _iconCts?.Cancel();
        _iconCts?.Dispose();
    }
}
