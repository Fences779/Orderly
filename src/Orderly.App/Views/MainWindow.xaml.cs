using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Orderly.App.ViewModels;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        System.Windows.Application.Current.MainWindow = this;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    // ——— 弹窗入口（code-behind 转发，不含业务逻辑）———

    private void AddCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteViewModelCommand(viewModel => viewModel.AddCustomerCommand);
    }

    private void AddOrderButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteViewModelCommand(viewModel => viewModel.AddOrderCommand);
    }

    // ——— 快捷筛选 Chip（code-behind 设值，不含业务逻辑）———

    private void QuickFilter_All_Click(object sender, RoutedEventArgs e)
    {
        SetQuickFilter(QuickFilterKind.All);
    }

    private void QuickFilter_Today_Click(object sender, RoutedEventArgs e)
    {
        SetQuickFilter(QuickFilterKind.TodayFollowUp);
    }

    private void QuickFilter_Overdue_Click(object sender, RoutedEventArgs e)
    {
        SetQuickFilter(QuickFilterKind.OverdueFollowUp);
    }

    private void QuickFilter_Tomorrow_Click(object sender, RoutedEventArgs e)
    {
        SetQuickFilter(QuickFilterKind.TomorrowFollowUp);
    }

    private void QuickFilter_Pending_Click(object sender, RoutedEventArgs e)
    {
        SetQuickFilter(QuickFilterKind.PendingOrders);
    }

    private void QuickFilter_Won_Click(object sender, RoutedEventArgs e)
    {
        SetQuickFilter(QuickFilterKind.WonOrders);
    }

    private void SetQuickFilter(QuickFilterKind kind)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var option = viewModel.QuickFilterOptions.FirstOrDefault(opt => opt.Kind == kind);
        if (option is not null)
        {
            viewModel.SelectedQuickFilter = option;
        }
    }

    // ——— 通用 ViewModel Command 执行器 ———

    private void ExecuteViewModelCommand(Func<MainViewModel, ICommand> commandSelector)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var command = commandSelector(viewModel);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
