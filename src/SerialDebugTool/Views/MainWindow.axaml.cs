using Avalonia.Controls;
using SerialDebugTool.ViewModels;

namespace SerialDebugTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Host = this;
                vm.LogAppended += OnLogAppended;
            }
        };

        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.LogAppended -= OnLogAppended;
                vm.SaveAll();
            }
        };
    }

    /// <summary>有新日志时滚动到底部（受“自动滚动”开关控制）。</summary>
    private void OnLogAppended()
    {
        if (DataContext is not MainWindowViewModel vm || !vm.AutoScroll)
        {
            return;
        }

        if (LogList.ItemCount > 0)
        {
            LogList.ScrollIntoView(LogList.ItemCount - 1);
        }
    }
}
