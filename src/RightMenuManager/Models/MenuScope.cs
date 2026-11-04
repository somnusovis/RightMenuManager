namespace RightMenuManager.Models;

public enum MenuScope
{
    File,
    Folder,
    FolderBackground,
    Desktop,
    Drive,
    AllObjects
}

public enum MenuItemKind
{
    Verb,
    ShellExtension
}

public enum FilterMode
{
    All,
    Enabled,
    Disabled,
    Extensions
}
