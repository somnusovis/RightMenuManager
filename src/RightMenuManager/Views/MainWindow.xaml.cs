using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RightMenuManager.Models;
using RightMenuManager.Services;
using RightMenuManager.ViewModels;

namespace RightMenuManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        WindowThemeService.UseLightTitleBar(this);
        ClampToWorkingArea();

        Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void ClampToWorkingArea()
    {
        Rect workArea = SystemParameters.WorkArea;
        double maxWidth = Math.Max(MinWidth, workArea.Width - 24);
        double maxHeight = Math.Max(MinHeight, workArea.Height - 24);

        if (Width > maxWidth) Width = maxWidth;
        if (Height > maxHeight) Height = maxHeight;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        await _viewModel.CheckForChangesAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not ContextMenuItem item) return;

        bool desired = box.IsChecked == true;
        if (desired == item.IsEnabled) return;

        _viewModel.SetEnabled(item, desired);

        // 操作失败时（例如没有管理员权限）把开关恢复到实际状态。
        if (box.IsChecked != item.IsEnabled)
        {
            box.IsChecked = item.IsEnabled;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin)
        {
            _viewModel.SearchText = "";
            e.Handled = true;
        }
    }
}
