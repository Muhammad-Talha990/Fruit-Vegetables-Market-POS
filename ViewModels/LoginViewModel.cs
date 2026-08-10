using System;
using System.Windows;
using System.Windows.Input;
using FruitVegetableMarketPOS.Data;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Services;

namespace FruitVegetableMarketPOS.ViewModels
{
    public class LoginViewModel : BaseViewModel
    {
        private readonly AuthService _authService;
        public event Action? LoginSucceeded;

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _isLoggingIn;
        public bool IsLoggingIn
        {
            get => _isLoggingIn;
            set => SetProperty(ref _isLoggingIn, value);
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel(AuthService authService)
        {
            _authService = authService;
            LoginCommand = new RelayCommand(ExecuteLogin, () => !IsLoggingIn);
        }

        public void Reset()
        {
            Username = string.Empty;
            Password = string.Empty;
            ErrorMessage = string.Empty;
        }

        private void ExecuteLogin()
        {
            try
            {
                ErrorMessage = string.Empty;

                if (string.IsNullOrWhiteSpace(Username))
                {
                    ErrorMessage = "Please enter username.";
                    return;
                }
                if (string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "Please enter password.";
                    return;
                }

                IsLoggingIn = true;
                
                string cleanUsername = Username.Trim();
                
                if (_authService.Login(cleanUsername, Password))
                {
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    ErrorMessage = "Invalid username or password.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Login error. Please try again.";
                AppLogger.Error("Login exception", ex);
            }
            finally
            {
                IsLoggingIn = false;
            }
        }
    }
}
