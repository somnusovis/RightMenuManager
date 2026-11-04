using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using RightMenuManager.Services;

namespace RightMenuManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 统一使用中文区域设置，保证日期、排序等行为一致。
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("zh-CN")));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Write("DispatcherUnhandledException", e.Exception);
        try
        {
            MessageBox.Show(
                $"程序遇到未处理的错误：\n\n{e.Exception.Message}\n\n详细信息已写入日志。",
                "右键管家",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 忽略错误提示本身的异常
        }

        e.Handled = true;
    }
}
