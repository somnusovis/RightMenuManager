using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace RightMenuManager.Services;

public static class AdminService
{
    private static readonly Lazy<bool> IsAdminLazy = new(() =>
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    });

    public static bool IsAdministrator => IsAdminLazy.Value;

    /// <summary>
    /// 以管理员身份重新启动当前程序。调用方应在成功后退出当前进程。
    /// </summary>
    public static void RestartElevated()
    {
        string executable = Environment.ProcessPath
                            ?? throw new InvalidOperationException("无法确定当前程序路径。");

        string arguments = string.Join(" ",
            Environment.GetCommandLineArgs().Skip(1).Select(QuoteIfNeeded));

        if (Path.GetFileNameWithoutExtension(executable)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                string entryAssembly = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
                if (File.Exists(entryAssembly))
                {
                    arguments = $"\"{entryAssembly}\" {arguments}".Trim();
                }
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(startInfo);
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.StartsWith('"') && value.EndsWith('"')) return value;
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
