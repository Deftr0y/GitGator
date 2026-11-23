using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    /// <summary>
    ///     A credential question Git asks through GitGator's askpass, such as
    ///     <c>Username for 'https://github.com': </c>.
    /// </summary>
    public partial class GitCredentialPrompt
    {
        public const string BitbucketTokenUser = "x-bitbucket-api-token-auth";

        public bool IsUsername { get; private init; }
        public bool IsPassword => !IsUsername;
        public string Scheme { get; private init; }
        public string Host { get; private init; }
        public string Username { get; private init; } = string.Empty;
        public string RawPrompt { get; private init; }

        public bool IsGitHub => Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        public bool IsBitbucket => Host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns null for anything that is not an HTTP(S) username/password prompt (SSH passphrases, for example).</summary>
        public static GitCredentialPrompt Parse(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return null;

            var match = REG_PROMPT().Match(prompt);
            if (!match.Success)
                return null;

            return new GitCredentialPrompt()
            {
                IsUsername = match.Groups[1].Value == "Username",
                Scheme = match.Groups[2].Value.ToLowerInvariant(),
                Username = Uri.UnescapeDataString(match.Groups[3].Value),
                Host = match.Groups[4].Value.ToLowerInvariant(),
                RawPrompt = prompt,
            };
        }

        /// <summary>
        ///     Answers a password prompt without a window when the answer is already known:
        ///     a secret entered a moment ago for the username prompt, or the GitHub CLI's token.
        /// </summary>
        public static bool TryAnswerSilently(string prompt, out string answer)
        {
            answer = null;

            var parsed = Parse(prompt);
            if (parsed is not { IsPassword: true })
                return false;

            answer = TakeStashedSecret(parsed.Host, parsed.Username);
            if (answer != null)
                return true;

            if (parsed.IsGitHub && GitHubCli.TryGetToken(out var token))
            {
                answer = token;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Git asks for the username and the password in two separate askpass runs. When both are
        ///     entered in one window, the secret waits here (a 0600 file in the per-user runtime
        ///     directory) for the password run, which deletes it at once. Unclaimed secrets expire.
        /// </summary>
        public static void StashSecret(string host, string username, string secret)
        {
            try
            {
                var file = GetStashFile(host, username);
                if (file == null)
                    return;

                var options = new FileStreamOptions()
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                };

                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

                using var stream = new FileStream(file, options);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(secret);
            }
            catch
            {
                // Git will simply ask for the password again.
            }
        }

        private static string TakeStashedSecret(string host, string username)
        {
            try
            {
                var file = GetStashFile(host, username);
                if (file == null || !File.Exists(file))
                    return null;

                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                var secret = age < StashLifetime ? File.ReadAllText(file) : null;
                File.Delete(file);
                return secret;
            }
            catch
            {
                return null;
            }
        }

        private static string GetStashFile(string host, string username)
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            var key = Encoding.UTF8.GetBytes($"{host.ToLowerInvariant()}\n{username}");
            var name = Convert.ToHexString(SHA256.HashData(key))[..24].ToLowerInvariant();
            return Path.Combine(dir, $"gitgator-askpass-{name}");
        }

        private static readonly TimeSpan StashLifetime = TimeSpan.FromMinutes(2);

        [GeneratedRegex(@"^(Username|Password) for '(https?)://(?:([^@'/]*)@)?([^/:']+)(?::\d+)?[/']")]
        private static partial Regex REG_PROMPT();
    }
}
