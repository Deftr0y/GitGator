using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Browser sign-in for GitHub through the GitHub CLI (gh). gh runs GitHub's device flow:
    ///     it shows a one-time code, the user approves it on github.com, and gh keeps the token.
    ///     <c>gh auth setup-git</c> then lets every Git process use that sign-in.
    /// </summary>
    public static partial class GitHubCli
    {
        public const string Host = "github.com";
        public const string DefaultDeviceUrl = "https://github.com/login/device";
        public const string InstallUrl = "https://github.com/cli/cli#installation";

        public static string Find()
        {
            var dirs = new List<string>();
            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
                dirs.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

            // Desktop launchers do not always carry the login shell's PATH.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add(Path.Combine(home, ".local", "bin"));
            dirs.Add(Path.Combine(home, "bin"));
            dirs.Add("/usr/local/bin");
            dirs.Add("/usr/bin");
            dirs.Add("/snap/bin");
            dirs.Add("/home/linuxbrew/.linuxbrew/bin");
            dirs.Add("/opt/homebrew/bin");

            var exe = OperatingSystem.IsWindows() ? "gh.exe" : "gh";
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>The signed-in GitHub account name, or null when gh is missing or signed out.</summary>
        public static async Task<string> GetSignedInUserAsync()
        {
            var gh = Find();
            if (gh == null)
                return null;

            var rs = await RunAsync(gh, ["auth", "status", "--hostname", Host, "--active"], TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            if (rs.ExitCode != 0)
                return null;

            var match = REG_STATUS_ACCOUNT().Match(rs.Output);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        /// <summary>Synchronous on purpose: used by askpass before any window exists.</summary>
        public static bool TryGetToken(out string token)
        {
            token = null;

            var gh = Find();
            if (gh == null)
                return false;

            var rs = RunAsync(gh, ["auth", "token", "--hostname", Host], TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            var text = rs.StdOut.Trim();
            if (rs.ExitCode != 0 || text.Length == 0 || text.Contains('\n'))
                return false;

            token = text;
            return true;
        }

        /// <summary>Makes Git use the gh sign-in for github.com (writes credential helper entries to the global Git config).</summary>
        public static async Task<bool> SetupGitAsync()
        {
            var gh = Find();
            if (gh == null)
                return false;

            var rs = await RunAsync(gh, ["auth", "setup-git", "--hostname", Host], TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            return rs.ExitCode == 0;
        }

        /// <summary>
        ///     Starts the device flow. <paramref name="onCode"/> receives the one-time code and the page to
        ///     enter it on. Completes with the account name once the user approves, or throws with gh's reason.
        /// </summary>
        public static async Task<string> SignInAsync(Action<string, string> onCode, CancellationToken cancellation)
        {
            var gh = Find() ?? throw new InvalidOperationException("The GitHub CLI (gh) is not installed.");

            // The workflow scope lets pushes include files under .github/workflows.
            var start = CreateStartInfo(gh, ["auth", "login", "--hostname", Host, "--git-protocol", "https", "--web", "--scopes", "workflow"]);
            using var proc = new Process() { StartInfo = start };

            var output = new StringBuilder();
            var codeSent = false;
            var url = DefaultDeviceUrl;
            void OnLine(string line)
            {
                if (line == null)
                    return;

                lock (output)
                    output.AppendLine(line);

                var urlMatch = REG_URL().Match(line);
                if (urlMatch.Success)
                    url = urlMatch.Value;

                var codeMatch = REG_CODE().Match(line);
                if (codeMatch.Success && !codeSent)
                {
                    codeSent = true;
                    var code = codeMatch.Groups[1].Value;

                    // gh prints the code, then the URL on the next line. Give the URL a moment to arrive.
                    _ = Task.Delay(300, CancellationToken.None).ContinueWith(_ => onCode?.Invoke(code, url), TaskScheduler.Default);
                }
            }

            proc.OutputDataReceived += (_, e) => OnLine(e.Data);
            proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
            proc.Start();
            proc.StandardInput.Close();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { /* already gone */ }
                throw;
            }

            string text;
            lock (output)
                text = output.ToString();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(DescribeFailure(text));

            var user = REG_LOGGED_IN().Match(text);
            if (user.Success)
                return user.Groups[1].Value;

            return await GetSignedInUserAsync().ConfigureAwait(false) ?? string.Empty;
        }

        private static string DescribeFailure(string output)
        {
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Contains("timeout", StringComparison.OrdinalIgnoreCase) || line.Contains("dial tcp", StringComparison.OrdinalIgnoreCase))
                    return "GitHub could not be reached. Check your internet connection and try again.";
                if (line.Contains("expired", StringComparison.OrdinalIgnoreCase))
                    return "The code expired before it was approved. Start again to get a new one.";
                if (line.Contains("access_denied", StringComparison.OrdinalIgnoreCase) || line.Contains("denied", StringComparison.OrdinalIgnoreCase))
                    return "The sign-in was declined on GitHub.";
            }

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("failed", StringComparison.OrdinalIgnoreCase) || line.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                    return line;
            }

            return "GitHub sign-in did not finish.";
        }

        private static ProcessStartInfo CreateStartInfo(string gh, string[] args)
        {
            var start = new ProcessStartInfo(gh)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var arg in args)
                start.ArgumentList.Add(arg);

            // Plain, prompt-free output that GitGator can read.
            start.Environment["NO_COLOR"] = "1";
            start.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
            start.Environment["GH_PROMPT_DISABLED"] = "1";
            start.Environment.Remove("SOURCEGIT_LAUNCH_AS_ASKPASS");
            return start;
        }

        private static async Task<(int ExitCode, string StdOut, string Output)> RunAsync(string gh, string[] args, TimeSpan timeout)
        {
            try
            {
                using var proc = new Process() { StartInfo = CreateStartInfo(gh, args) };
                proc.Start();
                proc.StandardInput.Close();

                using var cts = new CancellationTokenSource(timeout);
                var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(true); } catch { /* already gone */ }
                    return (-1, string.Empty, string.Empty);
                }

                var o = await stdout.ConfigureAwait(false);
                var e = await stderr.ConfigureAwait(false);
                return (proc.ExitCode, o, o + "\n" + e);
            }
            catch
            {
                return (-1, string.Empty, string.Empty);
            }
        }

        [GeneratedRegex(@"one-time code:\s*([A-Z0-9]{4}-[A-Z0-9]{4})")]
        private static partial Regex REG_CODE();

        [GeneratedRegex(@"https://[^\s]+/login/device[^\s]*")]
        private static partial Regex REG_URL();

        [GeneratedRegex(@"Logged in as ([A-Za-z0-9-]+)")]
        private static partial Regex REG_LOGGED_IN();

        [GeneratedRegex(@"account ([A-Za-z0-9-]+)")]
        private static partial Regex REG_STATUS_ACCOUNT();
    }
}
