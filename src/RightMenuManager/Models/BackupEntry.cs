using System.Text.Json.Serialization;

namespace RightMenuManager.Models;

public sealed class BackupEntry
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RegistryPath { get; set; } = "";
    public string Command { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string FileName { get; set; } = "backup.reg";

    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    [JsonIgnore]
    public string FilePath => Path.Combine(FolderPath, FileName);

    [JsonIgnore]
    public string CreatedText => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public bool RequiresAdmin =>
        RegistryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
}
