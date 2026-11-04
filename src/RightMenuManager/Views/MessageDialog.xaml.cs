using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RightMenuManager.Views;

public enum DialogTone
{
    Info,
    Question,
    Warning,
    Error,
    Success
}

public partial class MessageDialog : Window
{
    public MessageDialog(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        DialogTone tone,
        bool dangerPrimary = false)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (string.IsNullOrWhiteSpace(secondaryText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
        }

        if (dangerPrimary)
        {
            PrimaryButton.Style = (Style)FindResource("DangerSolidButton");
        }

        ApplyTone(tone);

        Loaded += (_, _) => PrimaryButton.Focus();
    }

    private void ApplyTone(DialogTone tone)
    {
        (string glyph, string backgroundKey, string foregroundKey) = tone switch
        {
            DialogTone.Question => ("?", "AccentSoftBrush", "AccentBrush"),
            DialogTone.Warning => ("!", "WarningSoftBrush", "WarningBrush"),
            DialogTone.Error => ("!", "DangerSoftBrush", "DangerBrush"),
            DialogTone.Success => ("\u2713", "SuccessSoftBrush", "SuccessBrush"),
            _ => ("i", "AccentSoftBrush", "AccentBrush")
        };

        IconText.Text = glyph;
        IconBadge.Background = (Brush)FindResource(backgroundKey);
        IconText.Foreground = (Brush)FindResource(foregroundKey);
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove 在鼠标已松开时可能抛异常，忽略即可。
        }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
