using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using Orderly.App.ViewModels;

namespace Orderly.App.Views
{
    public partial class LoginView : Window
    {
        private readonly LoginViewModel _viewModel;

        public LoginView(LoginViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();
            txtAccount.Focus(); // 默认聚焦账号输入框，提升体验
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void TxtPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExecuteLogin();
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            ExecuteLogin();
        }

        private async void ExecuteLogin()
        {
            txtError.Visibility = Visibility.Collapsed;
            string account = txtAccount.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                ShowError("请输入账号和密码");
                return;
            }

            btnLogin.IsEnabled = false;
            gridLoading.Visibility = Visibility.Visible;

            try
            {
                // TODO: 替换为实际 AuthService 逻辑
                await Task.Delay(1200); 
                EnterWorkspace();
            }
            catch (Exception)
            {
                ShowError("登录失败，请检查网络或账号密码。");
            }
            finally
            {
                btnLogin.IsEnabled = true;
                gridLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnLocalMode_Click(object sender, RoutedEventArgs e)
        {
            EnterWorkspace();
        }

        private void EnterWorkspace()
        {
            _viewModel.LoginCommand.Execute(null);
        }

        private void ShowError(string msg)
        {
            txtError.Inlines.Clear();

            if (msg == "请输入账号和密码")
            {
                txtError.Inlines.Add(new Run("请输入")
                {
                    FontWeight = FontWeights.Bold
                });
                txtError.Inlines.Add(new Run("账号和密码"));
            }
            else
            {
                txtError.Text = msg;
            }

            txtError.Visibility = Visibility.Visible;
        }
    }
}
