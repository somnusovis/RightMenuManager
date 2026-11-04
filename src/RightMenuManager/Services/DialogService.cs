using System.Windows;
using RightMenuManager.Views;

namespace RightMenuManager.Services;

public static class DialogService
{
    public static bool Confirm(
        string title,
        string message,
        string primaryText = "确定",
        string? secondaryText = "取消",
        bool danger = false)
    {
        var dialog = new MessageDialog(
            title,
            message,
            primaryText,
            secondaryText,
            danger ? DialogTone.Warning : DialogTone.Question,
            danger);

        AttachOwner(dialog);
        return dialog.ShowDialog() == true;
    }

    public static void Info(string title, string message, string primaryText = "知道了")
    {
        var dialog = new MessageDialog(title, message, primaryText, null, DialogTone.Info);
        AttachOwner(dialog);
        dialog.ShowDialog();
    }

    public static void Error(string title, string message, string primaryText = "知道了")
    {
        var dialog = new MessageDialog(title, message, primaryText, null, DialogTone.Error);
        AttachOwner(dialog);
        dialog.ShowDialog();
    }

    private static void AttachOwner(Window dialog)
    {
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? Application.Current?.MainWindow;

        if (owner != null && owner.IsVisible && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
    }
}
