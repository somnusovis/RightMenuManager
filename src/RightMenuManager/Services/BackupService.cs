using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RightMenuManager.Models;

namespace RightMenuManager.Services;

/// <summary>
/// 删除前导出 .reg 备份，并负责备份列表、恢复和清理。
/// </summary>
public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RightMenuManager",
        "Backups");

    public BackupEntry BackupAndDelete(ContextMenuItem item)
    {
        Directory.CreateDirectory(RootDirectory);

        string folder = CreateUniqueFolder(item.DisplayName);
        Directory.CreateDirectory(folder);

        string regFile = Path.Combine(folder, "backup.reg");
        ExportKey(item.RegistryPath, regFile);

        var entry = new BackupEntry
        {
            Name = item.DisplayName,
            Kind = item.KindLabel,
            RegistryPath = item.RegistryPath,
            Command = item.Command,
            CreatedAt = DateTime.Now,
            FileName = "backup.reg",
            FolderPath = folder
        };

        File.WriteAllText(
            Path.Combine(folder, "meta.json"),
            JsonSerializer.Serialize(entry, JsonOptions),
            Encoding.UTF8);

        try
        {
            RegistryHelper.DeleteKeyTree(item.RegistryPath);

            if (RegistryHelper.KeyExists(item.RegistryPath))
            {
                throw new IOException("注册表项仍然存在，删除可能被系统或安全软件拦截。");
            }
        }
        catch
        {
            // 删除失败时保留备份，方便用户手动处理。
            throw;
        }

        return entry;
    }

    public List<BackupEntry> ListBackups()
    {
        if (!Directory.Exists(RootDirectory)) return new List<BackupEntry>();

        var entries = new List<BackupEntry>();

        foreach (string folder in Directory.GetDirectories(RootDirectory))
        {
            try
            {
                string metaPath = Path.Combine(folder, "meta.json");
                BackupEntry? entry = null;

                if (File.Exists(metaPath))
                {
                    entry = JsonSerializer.Deserialize<BackupEntry>(
                        File.ReadAllText(metaPath, Encoding.UTF8),
                        JsonOptions);
                }

                entry ??= new BackupEntry
                {
                    Name = Path.GetFileName(folder),
                    CreatedAt = Directory.GetCreationTime(folder)
                };

                entry.FolderPath = folder;
                if (!File.Exists(entry.FilePath))
                {
                    entry.FileName = "backup.reg";
                }

                if (File.Exists(entry.FilePath))
                {
                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"读取备份失败：{folder}", ex);
            }
        }

        return entries.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public void Restore(BackupEntry entry)
    {
        string folderPath = Path.GetFullPath(entry.FolderPath);
        string filePath = Path.GetFullPath(entry.FilePath);
        string folderPrefix = folderPath.TrimEnd(Path.DirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;

        if (!filePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份文件路径无效，已拒绝恢复。");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("备份文件不存在，可能已被移动或删除。", filePath);
        }

        if (entry.RequiresAdmin && !AdminService.IsAdministrator)
        {
            throw new UnauthorizedAccessException("恢复系统级注册表项需要管理员权限。");
        }

        RunReg($"import \"{filePath}\"", "恢复备份");
    }

    public void DeleteBackup(BackupEntry entry)
    {
        string fullPath = Path.GetFullPath(entry.FolderPath);
        string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝删除备份目录之外的文件夹。");
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private string CreateUniqueFolder(string displayName)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string baseName = $"{stamp}_{Sanitize(displayName)}";
        string folder = Path.Combine(RootDirectory, baseName);

        int suffix = 2;
        while (Directory.Exists(folder))
        {
            folder = Path.Combine(RootDirectory, $"{baseName}_{suffix++}");
        }

        return folder;
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();

        foreach (char character in name.Trim())
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
            if (builder.Length >= 48) break;
        }

        string result = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(result) ? "菜单项" : result;
    }

    private static void ExportKey(string registryPath, string filePath)
    {
        if (registryPath.Contains('"'))
        {
            throw new InvalidOperationException(
                "注册表路径包含双引号，无法安全地调用 reg.exe 进行备份。");
        }

        RunReg($"export \"{registryPath}\" \"{filePath}\" /y", "导出注册表备份");

        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
        {
            throw new IOException("注册表导出失败，备份文件为空。");
        }
    }

    private static void RunReg(string arguments, string action)
    {
        var startInfo = new ProcessStartInfo("reg.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动 reg.exe。");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(30000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // 忽略终止失败。
            }

            throw new TimeoutException($"{action}超时，请检查注册表权限或安全软件拦截。");
        }

        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new IOException($"{action}失败（reg.exe 退出码 {process.ExitCode}）。\n{detail.Trim()}");
        }
    }
}
