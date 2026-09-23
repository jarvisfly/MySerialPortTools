using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SerialDebugTool.ViewModels;
using SerialDebugTool.Views;

namespace SerialDebugTool;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainWindowViewModel();

            var window = new MainWindow
            {
                DataContext = _viewModel
            };

            // 关闭窗口 / 退出程序时落盘
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _viewModel?.SaveAll();
            window.Closing += (_, _) => _viewModel?.SaveAll();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
