namespace RightMenuManager.Models;

public sealed record ScopeDefinition(
    MenuScope Scope,
    string Title,
    string Description,
    string Glyph,
    string BasePath);

public static class ScopeCatalog
{
    public static IReadOnlyList<ScopeDefinition> All { get; } = new[]
    {
        new ScopeDefinition(
            MenuScope.File,
            "文件",
            "右键点击任意文件时显示的菜单项",
            "\uE7C3",
            "*"),
        new ScopeDefinition(
            MenuScope.Folder,
            "文件夹",
            "右键点击文件夹时显示的菜单项",
            "\uE8B7",
            "Directory"),
        new ScopeDefinition(
            MenuScope.FolderBackground,
            "文件夹空白处",
            "在文件夹空白区域右键时显示的菜单项",
            "\uE8A9",
            @"Directory\Background"),
        new ScopeDefinition(
            MenuScope.Desktop,
            "桌面",
            "在桌面空白处右键时显示的菜单项",
            "\uE7F4",
            "DesktopBackground"),
        new ScopeDefinition(
            MenuScope.Drive,
            "驱动器",
            "右键点击磁盘分区时显示的菜单项",
            "\uEDA2",
            "Drive"),
        new ScopeDefinition(
            MenuScope.AllObjects,
            "所有对象",
            "文件和文件夹共用的右键菜单项（AllFilesystemObjects）",
            "\uE8F1",
            "AllFilesystemObjects")
    };

    public static ScopeDefinition Get(MenuScope scope) => All.First(x => x.Scope == scope);
}
