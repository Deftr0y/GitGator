using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     "Connect an account": the choices shown when Git needs to sign in (askpass mode) or when the
    ///     user opens Accounts from the app menu. GitHub signs in through the browser, Bitbucket takes an
    ///     API token created in the browser, SSH keys and plain username/token entry remain available.
    /// </summary>
    public partial class ConnectAccount : ObservableObject
    {
        public enum Step
        {
            Choose,
            GitHubWaiting,
            GitHubSignedIn,
            GitHubMissing,
            Bitbucket,
            Manual,
            Ssh,
            Error,
        }

        public const string BitbucketTokenPage = "https://id.atlassian.com/manage-profile/security/api-tokens";

        /// <summary>Askpass mode: Git is waiting for an answer to <see cref="Prompt"/>.</summary>
        public bool IsAskpass => _prompt != null;
        public Models.GitCredentialPrompt Prompt => _prompt;

        public string Title { get; }
        public string Subtitle { get; }

        public bool ShowGitHubOption => !IsAskpass || _prompt.IsGitHub;
        public bool ShowBitbucketOption => !IsAskpass || _prompt.IsBitbucket;
        public bool ShowManualOption => IsAskpass;
        public bool IsGitHubRecommended => IsAskpass && _prompt.IsGitHub;
        public bool IsBitbucketRecommended => IsAskpass && _prompt.IsBitbucket;
        public bool IsManualRecommended => IsAskpass && !_prompt.IsGitHub && !_prompt.IsBitbucket;
        public bool AsksForUsername => !IsAskpass || _prompt.IsUsername;
        public string HostName => _prompt?.Host ?? "your Git server";

        public Step Page
        {
            get => _page;
            private set
            {
                if (SetProperty(ref _page, value))
                {
                    OnPropertyChanged(nameof(IsChoosePage));
                    OnPropertyChanged(nameof(IsGitHubWaitingPage));
                    OnPropertyChanged(nameof(IsGitHubSignedInPage));
                    OnPropertyChanged(nameof(IsGitHubMissingPage));
                    OnPropertyChanged(nameof(IsBitbucketPage));
                    OnPropertyChanged(nameof(IsManualPage));
                    OnPropertyChanged(nameof(IsSshPage));
                    OnPropertyChanged(nameof(IsErrorPage));
                }
            }
        }

        public bool IsChoosePage => _page == Step.Choose;
        public bool IsGitHubWaitingPage => _page == Step.GitHubWaiting;
        public bool IsGitHubSignedInPage => _page == Step.GitHubSignedIn;
        public bool IsGitHubMissingPage => _page == Step.GitHubMissing;
        public bool IsBitbucketPage => _page == Step.Bitbucket;
        public bool IsManualPage => _page == Step.Manual;
        public bool IsSshPage => _page == Step.Ssh;
        public bool IsErrorPage => _page == Step.Error;

        /// <summary>Menu mode: who GitHub is signed in as, shown on the GitHub option.</summary>
        public string GitHubStatus
        {
            get => _gitHubStatus;
            private set => SetProperty(ref _gitHubStatus, value);
        }

        public string DeviceCode
        {
            get => _deviceCode;
            private set
            {
                if (SetProperty(ref _deviceCode, value))
                    OnPropertyChanged(nameof(HasDeviceCode));
            }
        }

        public bool HasDeviceCode => !string.IsNullOrEmpty(_deviceCode);

        public string DeviceUrl
        {
            get => _deviceUrl;
            private set => SetProperty(ref _deviceUrl, value);
        }

        public string SignedInUser
        {
            get => _signedInUser;
            private set => SetProperty(ref _signedInUser, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Secret
        {
            get => _secret;
            set => SetProperty(ref _secret, value);
        }

        public string BitbucketToken
        {
            get => _bitbucketToken;
            set => SetProperty(ref _bitbucketToken, value);
        }

        public string SshStatus
        {
            get => _sshStatus;
            private set => SetProperty(ref _sshStatus, value);
        }

        public bool HasHostSshSettings => _prompt == null || _prompt.IsGitHub || _prompt.IsBitbucket;

        /// <summary>A code is ready: the view copies it and opens the browser.</summary>
        public event Action<string, string> CodeReady;

        /// <summary>Askpass mode: the line to give Git. The view prints it and exits.</summary>
        public event Action<string> Answered;

        public event Action CloseRequested;

        public ConnectAccount(Models.GitCredentialPrompt prompt)
        {
            _prompt = prompt;
            _username = prompt?.Username ?? string.Empty;

            // A password question with a known username skips straight to the matching form.
            if (prompt is { IsPassword: true, IsBitbucket: true })
                _page = Step.Bitbucket;
            else if (prompt is { IsPassword: true, IsGitHub: false })
                _page = Step.Manual;

            if (prompt == null)
            {
                Title = "Accounts";
                Subtitle = "Connect GitGator to where your repositories live.";
            }
            else if (prompt.IsGitHub)
            {
                Title = "Sign in to GitHub";
                Subtitle = "Git needs your GitHub account to continue.";
            }
            else if (prompt.IsBitbucket)
            {
                Title = "Sign in to Bitbucket";
                Subtitle = "Git needs your Bitbucket account to continue.";
            }
            else
            {
                Title = $"Sign in to {prompt.Host}";
                Subtitle = $"Git needs your account on {prompt.Host} to continue.";
            }
        }

        public async Task LoadStatusAsync()
        {
            if (IsAskpass)
                return;

            if (Models.GitHubCli.Find() == null)
            {
                GitHubStatus = "Opens your browser · needs the GitHub CLI";
                return;
            }

            var user = await Models.GitHubCli.GetSignedInUserAsync();
            GitHubStatus = user switch
            {
                null => "Opens your browser to sign in",
                "" => "Signed in",
                _ => $"Signed in as {user}",
            };
        }

        public async Task SignInWithGitHubAsync()
        {
            if (Models.GitHubCli.Find() == null)
            {
                Page = Step.GitHubMissing;
                return;
            }

            _signIn?.Cancel();
            _signIn = new CancellationTokenSource();
            var token = _signIn.Token;

            DeviceCode = string.Empty;
            DeviceUrl = Models.GitHubCli.DefaultDeviceUrl;
            Page = Step.GitHubWaiting;

            try
            {
                var user = await Models.GitHubCli.SignInAsync((code, url) => Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    DeviceCode = code;
                    DeviceUrl = url;
                    CodeReady?.Invoke(code, url);
                }), token);

                if (token.IsCancellationRequested)
                    return;

                await Models.GitHubCli.SetupGitAsync();

                SignedInUser = string.IsNullOrEmpty(user) ? "your GitHub account" : user;
                GitHubStatus = string.IsNullOrEmpty(user) ? "Signed in" : $"Signed in as {user}";
                Page = Step.GitHubSignedIn;

                if (IsAskpass)
                {
                    // Let the confirmation register before handing control back to Git.
                    await Task.Delay(900);
                    AnswerAfterGitHubSignIn(user);
                }
            }
            catch (OperationCanceledException)
            {
                // Back to the choices; nothing was saved.
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                    ShowError(e.Message);
            }
        }

        public void CancelGitHubSignIn()
        {
            _signIn?.Cancel();
            DeviceCode = string.Empty;
            Page = Step.Choose;
        }

        public void OpenDevicePage()
        {
            Native.OS.OpenBrowser(string.IsNullOrEmpty(_deviceUrl) ? Models.GitHubCli.DefaultDeviceUrl : _deviceUrl);
        }

        public void OpenGitHubCliInstallPage()
        {
            Native.OS.OpenBrowser(Models.GitHubCli.InstallUrl);
        }

        public void ChooseBitbucket()
        {
            Page = Step.Bitbucket;
        }

        public void OpenBitbucketTokenPage()
        {
            Native.OS.OpenBrowser(BitbucketTokenPage);
        }

        public void SubmitBitbucket()
        {
            var token = (_bitbucketToken ?? string.Empty).Trim();
            if (token.Length == 0 || !IsAskpass)
                return;

            // Bitbucket accepts this fixed username with a personal API token.
            if (_prompt.IsUsername)
            {
                Models.GitCredentialPrompt.StashSecret(_prompt.Host, Models.GitCredentialPrompt.BitbucketTokenUser, token);
                Answered?.Invoke(Models.GitCredentialPrompt.BitbucketTokenUser);
            }
            else
            {
                Answered?.Invoke(token);
            }
        }

        public void ChooseManual()
        {
            Page = Step.Manual;
        }

        public void SubmitManual()
        {
            if (!IsAskpass)
                return;

            var secret = _secret ?? string.Empty;
            if (_prompt.IsPassword)
            {
                Answered?.Invoke(secret);
                return;
            }

            var user = (_username ?? string.Empty).Trim();
            if (user.Length == 0)
                return;

            if (secret.Length > 0)
                Models.GitCredentialPrompt.StashSecret(_prompt.Host, user, secret);

            Answered?.Invoke(user);
        }

        public void ChooseSsh()
        {
            SshStatus = string.Empty;
            Page = Step.Ssh;
        }

        public void OpenHostSshSettings()
        {
            if (_prompt is { IsBitbucket: true })
                Native.OS.OpenBrowser("https://bitbucket.org/account/settings/ssh-keys/");
            else
                Native.OS.OpenBrowser("https://github.com/settings/ssh/new");
        }

        /// <summary>
        ///     Askpass runs in the repository Git is working on. Point every remote on this host at the
        ///     SSH address instead (https://host/owner/repo.git becomes git@host:owner/repo.git).
        /// </summary>
        public async Task SwitchRemotesToSshAsync()
        {
            if (!IsAskpass)
                return;

            var git = string.IsNullOrEmpty(Native.OS.GitExecutable) ? "git" : Native.OS.GitExecutable;
            var list = await RunGitAsync(git, "remote -v");
            if (list == null)
            {
                SshStatus = "This window is not running inside a repository. Change the remote in GitGator instead.";
                return;
            }

            var changed = 0;
            var last = string.Empty;
            foreach (var line in list.Split('\n'))
            {
                var match = REG_REMOTE_LINE().Match(line);
                if (!match.Success || !match.Groups[3].Value.Equals(_prompt.Host, StringComparison.OrdinalIgnoreCase))
                    continue;

                var remote = match.Groups[1].Value;
                var ssh = $"git@{_prompt.Host}:{match.Groups[4].Value}";
                if (await RunGitAsync(git, $"remote set-url {remote} {ssh}") != null)
                {
                    changed++;
                    last = $"{remote} now uses {ssh}";
                }
            }

            SshStatus = changed switch
            {
                0 => $"No remote here uses https://{_prompt.Host}.",
                1 => last + ". Close this window and try again.",
                _ => $"{changed} remotes now use SSH. Close this window and try again.",
            };
        }

        public void Back()
        {
            ErrorMessage = string.Empty;
            Page = Step.Choose;
        }

        public void Close()
        {
            _signIn?.Cancel();
            CloseRequested?.Invoke();
        }

        private void AnswerAfterGitHubSignIn(string user)
        {
            if (_prompt.IsUsername)
            {
                // The password run that follows reads the token from gh without a window.
                Answered?.Invoke(string.IsNullOrEmpty(user) ? "x-access-token" : user);
            }
            else if (Models.GitHubCli.TryGetToken(out var token))
            {
                Answered?.Invoke(token);
            }
            else
            {
                ShowError("Signed in, but GitGator could not read the new GitHub token. Try again.");
            }
        }

        private void ShowError(string message)
        {
            ErrorMessage = message;
            Page = Step.Error;
        }

        private static async Task<string> RunGitAsync(string git, string args)
        {
            try
            {
                var start = new ProcessStartInfo(git, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.Environment.Remove("SOURCEGIT_LAUNCH_AS_ASKPASS");

                using var proc = Process.Start(start)!;
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        [GeneratedRegex(@"^(\S+)\s+(https?)://(?:[^@/\s]+@)?([^/:\s]+)(?::\d+)?/(\S+?)\s+\(fetch\)")]
        private static partial Regex REG_REMOTE_LINE();

        private readonly Models.GitCredentialPrompt _prompt;
        private Step _page = Step.Choose;
        private CancellationTokenSource _signIn;
        private string _gitHubStatus = "Opens your browser to sign in";
        private string _deviceCode = string.Empty;
        private string _deviceUrl = Models.GitHubCli.DefaultDeviceUrl;
        private string _signedInUser = string.Empty;
        private string _errorMessage = string.Empty;
        private string _username;
        private string _secret = string.Empty;
        private string _bitbucketToken = string.Empty;
        private string _sshStatus = string.Empty;
    }
}
