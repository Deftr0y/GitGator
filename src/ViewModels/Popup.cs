using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Popup : ObservableValidator, Models.ICommandLogReceiver
    {
        public string ConfirmText => this switch
        {
            Clone => "Clone repository",
            OpenLocalRepository => "Open repository",
            Init => App.Text("Init"),
            Fetch => App.Text("Fetch"),
            Pull => App.Text("Pull"),
            Push => App.Text("Push"),
            CreateBranch => App.Text("CreateBranch.Title"),
            CreateTag => App.Text("CreateTag.Title"),
            DeleteBranch => App.Text("DeleteBranch"),
            DeleteTag => App.Text("DeleteTag"),
            Discard => App.Text("Discard"),
            StashChanges => App.Text("Stash"),
            Merge => App.Text("Merge"),
            Rebase => App.Text("Rebase"),
            Revert => App.Text("Revert"),
            _ => App.Text("Sure"),
        };

        /// <summary>Dialogs that set up a tab (clone, open, groups) keep running in the dialog; repository work goes to the queue.</summary>
        public bool RunsInBackground => this is not (Clone or Init or OpenLocalRepository or TrustRepository or ScanRepositories
            or CreateGroup or EditRepositoryNode or MoveRepositoryNode or DeleteRepositoryNode);

        /// <summary>What the operation touches, which decides what it may run next to.</summary>
        public JobNeeds JobNeeds => this switch
        {
            Fetch or Push or PushTag or PushRevision or LFSFetch or LFSPush or PruneRemote => JobNeeds.Remote,
            Checkout or CheckoutDetached or CreateBranch or RenameBranch or Merge or MergeMultiple or Rebase or Reset or Revert
                or CherryPick or StashChanges or ApplyStash or DropStash or ClearStashes or CheckoutBranchFromStash or Discard
                or ViewModels.Cleanup or Apply or AddToIgnore or EditBranchDescription or SetUpstream or Archive or LFSPrune
                or LFSTrackCustomPattern or PruneWorktrees => JobNeeds.Local,
            _ => JobNeeds.All,
        };

        /// <summary>The name shown in the activity list.</summary>
        public string JobTitle => this switch
        {
            Fetch { FetchAllRemotes: true } => "Fetch all remotes",
            Fetch f => $"Fetch {f.SelectedRemote?.Name}".TrimEnd(),
            Pull p => $"Pull {p.SelectedBranch?.FriendlyName ?? p.Current?.Name}".TrimEnd(),
            Push p => p.SelectedRemote != null ? $"Push {p.SelectedLocalBranch?.Name} to {p.SelectedRemote.Name}" : $"Push {p.SelectedLocalBranch?.Name}".TrimEnd(),
            Checkout c => $"Switch to {c.BranchName}",
            CreateBranch c => $"Create branch {c.Name}",
            DeleteBranch d => $"Delete branch {d.Target?.FriendlyName}".TrimEnd(),
            CreateTag t => $"Create tag {t.TagName}",
            Merge m => $"Merge into {m.Into}",
            Discard => "Discard changes",
            StashChanges => "Stash changes",
            _ => Humanize(GetType().Name),
        };

        /// <summary>"https://github.com/owner/repo.git" and "git@github.com:owner/repo.git" both read as "github.com/owner/repo".</summary>
        protected static string DescribeRemoteAddress(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var text = url.Trim();
            var scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                text = text.Substring(scheme + 3);
            }
            else if (!text.StartsWith('/') && !text.StartsWith('.'))
            {
                // scp-like address: user@host:path
                var colon = text.IndexOf(':');
                if (colon > 0 && text.IndexOf('/') > colon)
                    text = text.Substring(0, colon) + "/" + text.Substring(colon + 1);
            }

            var at = text.IndexOf('@');
            var slash = text.IndexOf('/');
            if (at >= 0 && (slash < 0 || at < slash))
                text = text.Substring(at + 1);

            if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 4);

            return text.TrimEnd('/');
        }

        protected static string CountCommits(int count)
        {
            return count == 1 ? "1 commit" : $"{count} commits";
        }

        // "CherryPick" -> "Cherry pick", "LFSFetch" -> "LFS fetch".
        private static string Humanize(string typeName)
        {
            var builder = new System.Text.StringBuilder(typeName.Length + 8);
            for (var i = 0; i < typeName.Length; i++)
            {
                var c = typeName[i];
                var prevLower = i > 0 && char.IsLower(typeName[i - 1]);
                var prevUpper = i > 0 && char.IsUpper(typeName[i - 1]);
                var nextLower = i + 1 < typeName.Length && char.IsLower(typeName[i + 1]);

                if (char.IsUpper(c) && (prevLower || (prevUpper && nextLower)))
                {
                    builder.Append(' ');
                    builder.Append(nextLower ? char.ToLowerInvariant(c) : c);
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        public bool CanTerminate
        {
            get => _canTerminate;
            protected set
            {
                if (OperatingSystem.IsWindows() || Native.OS.SupportSetSid())
                    SetProperty(ref _canTerminate, value);
            }
        }

        public bool InProgress
        {
            get => _inProgress;
            set => SetProperty(ref _inProgress, value);
        }

        public string ProgressDescription
        {
            get => _progressDescription;
            set => SetProperty(ref _progressDescription, value);
        }

        [UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode")]
        public bool Check()
        {
            if (HasErrors)
                return false;
            ValidateAllProperties();
            return !HasErrors;
        }

        public void OnReceiveCommandLog(string data)
        {
            var desc = data.Trim();
            if (!string.IsNullOrEmpty(desc))
                ProgressDescription = desc;
        }

        public void Cleanup()
        {
            _log?.Unsubscribe(this);
        }

        public virtual bool CanStartDirectly()
        {
            return true;
        }

        public virtual Task<bool> Sure()
        {
            return null;
        }

        public virtual void Terminate()
        {
        }

        protected void Use(CommandLog log)
        {
            _log = log;
            _log.Subscribe(this);
        }

        private bool _canTerminate = false;
        private bool _inProgress = false;
        private string _progressDescription = string.Empty;
        private CommandLog _log = null;
    }
}
