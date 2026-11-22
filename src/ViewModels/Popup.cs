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
