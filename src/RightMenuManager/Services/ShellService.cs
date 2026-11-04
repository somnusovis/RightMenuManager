using System.Diagnostics;
using Microsoft.Win32;

namespace RightMenuManager.Services;

public static class ShellService
{
    public static void OpenRegeditAt(string keyPath)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit",
                   writable: true))
        {
            key?.SetValue("LastKey", keyPath, RegistryValueKind.String);
        }

        Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
    }

    public static void OpenFileLocation(string path)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (File.Exists(expanded))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{expanded}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        if (Directory.Exists(expanded))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{expanded}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        throw new FileNotFoundException($"找不到文件或目录：{expanded}");
    }

    public static void OpenFolder(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"")
        {
            UseShellExecute = true
        });
    }

    public static async Task RestartExplorerAsync()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(2000);
            }
            catch
            {
                // 资源管理器可能已被系统重启，忽略单个进程的错误。
            }
            finally
            {
                process.Dispose();
            }
        }

        await Task.Delay(1400);

        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
    }
}
