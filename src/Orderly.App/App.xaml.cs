using System.Windows;
using Orderly.App.ViewModels;
using Orderly.App.Views;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Infrastructure.Hotkeys;
using Orderly.Infrastructure.Services;
using Orderly.Infrastructure.Tray;

namespace Orderly.App;

public partial class App : System.Windows.Application
{
    private const int MainHotkeyId = 1001;
    private const int FloatingHotkeyId = 1002;

    private TrayIconService? _trayIconService;
    private GlobalHotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;
    private MainViewModel? _mainViewModel;
    private FloatingWindowViewModel? _floatingViewModel;
    private bool _isLoginCompleted;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Console.WriteLine("App starting");

        var loginViewModel = new LoginViewModel();
        var loginView = new LoginView(loginViewModel);

        loginViewModel.LoginSucceeded += async (_, _) => await CompleteLoginAsync(loginView);
        loginView.Closed += (_, _) =>
        {
            if (!_isLoginCompleted && !IsExiting)
            {
                ExitApplication();
            }
        };

        Console.WriteLine("LoginView showing");
        loginView.WindowState = WindowState.Normal;
        loginView.ShowInTaskbar = true;
        loginView.Opacity = 1;
        loginView.Show();
        loginView.Activate();
    }

    private async Task CompleteLoginAsync(LoginView loginView)
    {
        if (_isLoginCompleted)
        {
            return;
        }

        _isLoginCompleted = true;
        loginView.Close();

        try
        {
            await InitializeWorkspaceAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"启动工作台失败：{ex.Message}",
                "Orderly",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            ExitApplication();
        }
    }

    private async Task InitializeWorkspaceAsync()
    {
        var databasePath = DatabasePaths.GetDefaultDatabasePath();
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        var initializer = new DatabaseInitializer(connectionFactory);
        await initializer.InitializeAsync();
        Console.WriteLine("Database initialized");

        ICustomerRepository customerRepository = new CustomerRepository(connectionFactory);
        IOrderRepository orderRepository = new OrderRepository(connectionFactory);
        IDealRepository dealRepository = new DealRepository(connectionFactory);
        IFollowUpRepository followUpRepository = new FollowUpRepository(connectionFactory);
        ICustomerNoteRepository noteRepository = new CustomerNoteRepository(connectionFactory);
        IActivityLogRepository activityLogRepository = new ActivityLogRepository(connectionFactory);
        IPriceAdjustmentRepository priceAdjustmentRepository = new PriceAdjustmentRepository(connectionFactory);
        IReplyTemplateRepository replyTemplateRepository = new ReplyTemplateRepository(connectionFactory);
        IAppSettingRepository settingRepository = new AppSettingRepository(connectionFactory);
        IClipboardService clipboardService = new DesktopClipboardService();

        IDealService dealService = new DealService(dealRepository, activityLogRepository);
        IFollowUpService followUpService = new FollowUpService(followUpRepository, activityLogRepository);
        INoteService noteService = new NoteService(noteRepository, activityLogRepository);
        IActivityLogService activityLogService = new ActivityLogService(activityLogRepository);
        IPriceAdjustmentService priceAdjustmentService = new PriceAdjustmentService(priceAdjustmentRepository, activityLogRepository);

        var preferences = await settingRepository.GetPreferencesAsync();

        _mainViewModel = new MainViewModel(
            customerRepository,
            orderRepository,
            dealService,
            followUpService,
            noteService,
            activityLogService,
            priceAdjustmentService,
            replyTemplateRepository,
            settingRepository,
            clipboardService,
            databasePath);
        await _mainViewModel.LoadAsync();

        _floatingViewModel = new FloatingWindowViewModel(orderRepository, replyTemplateRepository, clipboardService);
        await _floatingViewModel.LoadAsync();

        _mainWindow = new MainWindow(_mainViewModel);
        MainWindow = _mainWindow;
        _floatingWindow = new FloatingWindow(_floatingViewModel);

        _trayIconService = new TrayIconService();
        _trayIconService.OpenMainRequested += (_, _) => ShowMainWindow();
        _trayIconService.ToggleFloatingRequested += (_, _) => ToggleFloatingWindow();
        _trayIconService.ExitRequested += (_, _) => ExitApplication();

        _hotkeyService = new GlobalHotkeyService();
        _hotkeyService.HotkeyPressed += (_, hotkey) =>
        {
            if (string.Equals(hotkey, preferences.MainHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ShowMainWindow();
            }
            else if (string.Equals(hotkey, preferences.FloatingHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ToggleFloatingWindow();
            }
        };

        _mainWindow.SourceInitialized += (_, _) =>
        {
            _hotkeyService.Attach(_mainWindow);
            _hotkeyService.Register(MainHotkeyId, preferences.MainHotkey);
            _hotkeyService.Register(FloatingHotkeyId, preferences.FloatingHotkey);
        };

        Console.WriteLine("MainWindow showing");
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Opacity = 1;
        _mainWindow.Show();
        _mainWindow.Activate();

        if (preferences.ShowFloatingWindowOnStartup)
        {
            _floatingWindow.Show();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ToggleFloatingWindow()
    {
        if (_floatingWindow is null)
        {
            return;
        }

        if (_floatingWindow.IsVisible)
        {
            _floatingWindow.Hide();
        }
        else
        {
            _floatingWindow.Show();
            _floatingWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _floatingWindow?.Close();
        _mainWindow?.Close();
        Shutdown();
    }
}
