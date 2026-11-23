using System;
using System.Security.Cryptography;
using System.Text;

using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class ConnectAccount : ChromelessWindow
    {
        public static readonly FuncValueConverter<bool, string> CancelOrClose =
            new(isAskpass => isAskpass ? "Cancel" : "Close");

        public ConnectAccount()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        public ConnectAccount(ViewModels.ConnectAccount vm) : this()
        {
            DataContext = vm;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_vm != null)
            {
                _vm.CodeReady -= OnCodeReady;
                _vm.Answered -= OnAnswered;
                _vm.CloseRequested -= OnCloseRequested;
            }

            base.OnDataContextChanged(e);

            _vm = DataContext as ViewModels.ConnectAccount;
            if (_vm != null)
            {
                _vm.CodeReady += OnCodeReady;
                _vm.Answered += OnAnswered;
                _vm.CloseRequested += OnCloseRequested;
            }
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Activate();

            if (_vm != null)
                await _vm.LoadStatusAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _vm?.CancelGitHubSignIn();

            // Closing the window without an answer tells Git the sign-in was cancelled.
            if (_vm is { IsAskpass: true } && !_answered && !_quitting)
            {
                _quitting = true;
                Environment.ExitCode = 1;
                App.Quit(1);
            }
        }

        private async void OnCodeReady(string code, string url)
        {
            // Copy first, so the code is ready to paste when the browser appears.
            await this.CopyTextAsync(code);
            _vm?.OpenDevicePage();
        }

        private async void OnCopyCode(object _, RoutedEventArgs e)
        {
            if (_vm is { HasDeviceCode: true })
                await this.CopyTextAsync(_vm.DeviceCode);

            e.Handled = true;
        }

        private void OnAnswered(string answer)
        {
            if (_answered || _vm is not { IsAskpass: true })
                return;

            _answered = true;
            _quitting = true;
            var bytes = Encoding.UTF8.GetBytes(answer ?? string.Empty);
            try
            {
                var stdout = Console.OpenStandardOutput();
                stdout.Write(bytes, 0, bytes.Length);
                stdout.WriteByte((byte)'\n');
                stdout.Flush();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }

            App.Quit(0);
        }

        private void OnCloseRequested()
        {
            if (_vm is { IsAskpass: true })
            {
                if (_quitting)
                    return;

                _quitting = true;
                Environment.ExitCode = 1;
                App.Quit(1);
            }
            else
            {
                Close();
            }
        }

        private async void OnSshOption(object _, RoutedEventArgs e)
        {
            e.Handled = true;

            // From the menu there is no repository waiting, so go straight to the key manager.
            if (_vm is { IsAskpass: true })
                _vm.ChooseSsh();
            else
                await OpenSshKeysAsync();
        }

        private async void OnOpenSshKeys(object _, RoutedEventArgs e)
        {
            e.Handled = true;
            await OpenSshKeysAsync();
        }

        private async System.Threading.Tasks.Task OpenSshKeysAsync()
        {
            var dialog = new SSHKeyHelper() { DataContext = new ViewModels.SSHKeyHelper() };
            await dialog.ShowDialog(this);
        }

        private ViewModels.ConnectAccount _vm;
        private bool _answered;
        private bool _quitting;
    }
}
