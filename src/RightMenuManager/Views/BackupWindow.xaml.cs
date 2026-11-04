using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RightMenuManager.Models;
using RightMenuManager.Services;

namespace RightMenuManager.Views;

public partial class BackupWindow : Window
{
    private readonly BackupService _service;
    private readonly Func<Task>? _onRestored;

    public ObservableCollection<BackupEntry> Backups { get; } = new();

    public BackupWindow(BackupService service, Func<Task>? onRestored = null)
    {
        InitializeComponent();

        _service = service;
        _onRestored = onRestored;

        WindowThemeService.UseLightTitleBar(this);

        BackupList.ItemsSource = Backups;
        Loaded += (_, _) => LoadBackups();
    }

    private void LoadBackups()
    {
        Backups.Clear();
        foreach (BackupEntry entry in _service.ListBackups())
        {
            Backups.Add(entry);
        }

        bool empty = Backups.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BackupList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        FooterText.Text = empty
            ? $"备份目录：{_service.RootDirectory}"
            : $"共 {Backups.Count} 份备份 · {_service.RootDirectory}";
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BackupEntry entry) return;

        if (entry.RequiresAdmin && !AdminService.IsAdministrator)
        {
            bool elevate = DialogService.Confirm(
                "需要管理员权限",
                $"恢复「{entry.Name}」需要写入 HKEY_LOCAL_MACHINE。\n\n" +
                "可以以管理员身份重新启动右键管家后再恢复。",
                "以管理员身份重启",
                "取消");

            if (elevate)
            {
                try
                {
                    AdminService.RestartElevated();
                    Application.Current.Shutdown();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // 用户取消了 UAC。
                }
                catch (Exception ex)
                {
                    DialogService.Error("无法提升权限", ex.Message);
                }
            }

            return;
        }

        bool confirmed = DialogService.Confirm(
            "恢复备份",
            $"即将把「{entry.Name}」写回注册表：\n\n{entry.RegistryPath}\n\n" +
            "同名的现有注册表项会被覆盖。是否继续？",
            "恢复",
            "取消",
            danger: true);

        if (!confirmed) return;

        try
        {
            await Task.Run(() => _service.Restore(entry));

            if (_onRestored != null)
            {
                await _onRestored();
            }

            LoadBackups();
            DialogService.Info(
                "恢复完成",
                $"「{entry.Name}」已恢复到注册表。\n\n部分菜单项可能需要重启资源管理器后才会重新出现。");
        }
        catch (Exception ex)
        {
            LogService.Write($"恢复备份失败：{entry.FilePath}", ex);
            DialogService.Error("恢复失败", ex.Message);
        }
    }

    private void OnOpenEntryFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BackupEntry entry) return;

        try
        {
            ShellService.OpenFolder(entry.FolderPath);
        }
        catch (Exception ex)
        {
            DialogService.Error("无法打开文件夹", ex.Message);
        }
    }

    private void OnDeleteBackupClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BackupEntry entry) return;

        bool confirmed = DialogService.Confirm(
            "删除备份",
            $"将永久删除「{entry.Name}」的备份文件，此操作不可撤销。\n\n{entry.FolderPath}",
            "删除备份",
            "取消",
            danger: true);

        if (!confirmed) return;

        try
        {
            _service.DeleteBackup(entry);
            LoadBackups();
        }
        catch (Exception ex)
        {
            LogService.Write($"删除备份失败：{entry.FolderPath}", ex);
            DialogService.Error("删除失败", ex.Message);
        }
    }

    private void OnOpenRootFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellService.OpenFolder(_service.RootDirectory);
        }
        catch (Exception ex)
        {
            DialogService.Error("无法打开备份目录", ex.Message);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
