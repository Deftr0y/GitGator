using System;
using System.Threading.Tasks;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>What a job touches. Two jobs run side by side only when these do not overlap.</summary>
    [Flags]
    public enum JobNeeds
    {
        None = 0,

        /// <summary>The working tree, the index, HEAD or local branches (stage, commit, checkout, merge ...).</summary>
        Local = 1,

        /// <summary>The network and remote-tracking branches (fetch, push ...).</summary>
        Remote = 2,

        All = Local | Remote,
    }

    public enum JobState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled,
    }

    /// <summary>One Git operation in a repository's queue.</summary>
    public class BackgroundJob : ObservableObject
    {
        public string Title { get; }
        public JobNeeds Needs { get; }

        /// <summary>Small housekeeping (stage, unstage, auto-fetch) leaves the list as soon as it succeeds.</summary>
        public bool IsQuiet { get; }

        public JobState State
        {
            get => _state;
            internal set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(IsQueued));
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsSucceeded));
                    OnPropertyChanged(nameof(IsFailed));
                    OnPropertyChanged(nameof(IsCancelled));
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(CanCancel));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool IsQueued => _state == JobState.Queued;
        public bool IsRunning => _state == JobState.Running;
        public bool IsSucceeded => _state == JobState.Succeeded;
        public bool IsFailed => _state == JobState.Failed;
        public bool IsCancelled => _state == JobState.Cancelled;
        public bool IsActive => _state is JobState.Queued or JobState.Running;

        /// <summary>The latest progress line from Git while running.</summary>
        public string Detail
        {
            get => _detail;
            set
            {
                if (SetProperty(ref _detail, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>While queued: the job this one is waiting behind.</summary>
        public string WaitingFor
        {
            get => _waitingFor;
            internal set
            {
                if (SetProperty(ref _waitingFor, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => _state switch
        {
            JobState.Queued => string.IsNullOrEmpty(_waitingFor) ? "Queued" : $"Waiting for: {_waitingFor}",
            JobState.Running => string.IsNullOrEmpty(_detail) ? "Running…" : _detail,
            JobState.Succeeded => "Done",
            JobState.Failed => "Did not finish",
            _ => "Cancelled",
        };

        /// <summary>A waiting job can always be removed; a running one only when Git can be stopped safely.</summary>
        public bool CanCancel => _state == JobState.Queued || (_state == JobState.Running && Terminate != null);

        /// <summary>Completes with true when the job succeeded.</summary>
        public Task<bool> Completion => _completion.Task;

        internal Func<BackgroundJob, Task<bool>> Run { get; init; }
        internal Action Terminate { get; init; }
        internal bool CancelRequested { get; set; }

        internal BackgroundJob(string title, JobNeeds needs, bool quiet)
        {
            Title = title;
            Needs = needs;
            IsQuiet = quiet;
        }

        internal void Complete(bool succeeded)
        {
            _completion.TrySetResult(succeeded);
        }

        private readonly TaskCompletionSource<bool> _completion = new();
        private JobState _state = JobState.Queued;
        private string _detail = string.Empty;
        private string _waitingFor = string.Empty;
    }

    /// <summary>
    ///     Runs a repository's Git operations in the background, in the order they were asked for.
    ///     A job starts at once when nothing running or waiting ahead of it needs the same thing;
    ///     otherwise it waits its turn. Use it from the UI thread only.
    /// </summary>
    public class JobQueue : ObservableObject
    {
        /// <summary>How long a finished job stays visible, so its result can be read.</summary>
        public static TimeSpan SuccessLinger { get; set; } = TimeSpan.FromSeconds(2.5);
        public static TimeSpan FailureLinger { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>Running, waiting and just-finished jobs, oldest first.</summary>
        public AvaloniaList<BackgroundJob> Jobs { get; } = [];

        public bool HasJobs => Jobs.Count > 0;
        public bool IsActive => _runningCount + _queuedCount > 0;
        public bool IsRunning => _runningCount > 0;
        public int RunningCount => _runningCount;
        public int QueuedCount => _queuedCount;

        /// <summary>The one job the top bar names: what is running now, else what just finished.</summary>
        public string HeadlineTitle => _headline?.Title ?? string.Empty;
        public string HeadlineStatus => _headline?.StatusText ?? string.Empty;
        public bool ShowSuccess => !IsActive && _headline is { IsSucceeded: true };
        public bool ShowFailure => !IsActive && _headline is { IsFailed: true };

        /// <summary>Everything active besides the headline job.</summary>
        public int OtherCount => Math.Max(0, _runningCount + _queuedCount - 1);
        public string OtherText => $"+{OtherCount}";
        public string OtherTip => OtherCount == 1 ? "1 more in the queue" : $"{OtherCount} more in the queue";

        public BackgroundJob Enqueue(string title, JobNeeds needs, Func<BackgroundJob, Task<bool>> run, Action terminate = null, bool quiet = false)
        {
            var job = new BackgroundJob(title, needs, quiet) { Run = run, Terminate = terminate };
            Jobs.Add(job);
            Pump();
            return job;
        }

        public Task<bool> RunAsync(string title, JobNeeds needs, Func<BackgroundJob, Task<bool>> run, Action terminate = null, bool quiet = false)
        {
            return Enqueue(title, needs, run, terminate, quiet).Completion;
        }

        public void Cancel(BackgroundJob job)
        {
            if (job == null)
                return;

            if (job.State == JobState.Queued)
            {
                job.State = JobState.Cancelled;
                job.Complete(false);
                Jobs.Remove(job);
                Pump();
            }
            else if (job.State == JobState.Running && job.Terminate != null)
            {
                job.CancelRequested = true;
                job.Detail = "Stopping…";
                job.Terminate();
            }
        }

        /// <summary>Drops everything that has not started yet (the repository is being closed).</summary>
        public void CancelWaiting()
        {
            foreach (var job in new System.Collections.Generic.List<BackgroundJob>(Jobs))
            {
                if (job.State == JobState.Queued)
                {
                    job.State = JobState.Cancelled;
                    job.Complete(false);
                    Jobs.Remove(job);
                }
            }

            Refresh();
        }

        /// <summary>Removes a finished job from the list before its time is up.</summary>
        public void Dismiss(BackgroundJob job)
        {
            if (job is { IsActive: false } && Jobs.Remove(job))
                Refresh();
        }

        // Starts every waiting job whose needs are free, without letting it overtake an earlier job that needs the same thing.
        private void Pump()
        {
            var blocked = JobNeeds.None;
            foreach (var job in Jobs)
            {
                if (job.State == JobState.Running)
                    blocked |= job.Needs;
            }

            foreach (var job in new System.Collections.Generic.List<BackgroundJob>(Jobs))
            {
                if (job.State != JobState.Queued)
                    continue;

                if ((job.Needs & blocked) == JobNeeds.None)
                    Start(job);
                else
                    job.WaitingFor = FindBlocker(job);

                blocked |= job.Needs;
            }

            Refresh();
        }

        private string FindBlocker(BackgroundJob waiting)
        {
            foreach (var job in Jobs)
            {
                if (job == waiting)
                    break;

                if (job.IsActive && (job.Needs & waiting.Needs) != JobNeeds.None)
                    return job.Title;
            }

            return string.Empty;
        }

        private async void Start(BackgroundJob job)
        {
            job.WaitingFor = string.Empty;
            job.State = JobState.Running;

            var succeeded = false;
            try
            {
                succeeded = await job.Run(job);
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }

            job.State = job.CancelRequested ? JobState.Cancelled : succeeded ? JobState.Succeeded : JobState.Failed;
            job.Complete(succeeded && !job.CancelRequested);
            Pump();

            if (!(job.IsQuiet && job.IsSucceeded))
                await Task.Delay(job.IsSucceeded ? SuccessLinger : FailureLinger);

            if (Jobs.Remove(job))
                Refresh();
        }

        private void Refresh()
        {
            var running = 0;
            var queued = 0;
            BackgroundJob firstRunning = null;
            BackgroundJob lastFinished = null;

            foreach (var job in Jobs)
            {
                if (job.State == JobState.Running)
                {
                    running++;
                    firstRunning ??= job;
                }
                else if (job.State == JobState.Queued)
                {
                    queued++;
                }
                else
                {
                    lastFinished = job;
                }
            }

            _runningCount = running;
            _queuedCount = queued;

            var headline = firstRunning ?? lastFinished;
            if (!ReferenceEquals(_headline, headline))
            {
                if (_headline != null)
                    _headline.PropertyChanged -= OnHeadlineChanged;

                _headline = headline;

                if (_headline != null)
                    _headline.PropertyChanged += OnHeadlineChanged;
            }

            OnPropertyChanged(nameof(HasJobs));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(RunningCount));
            OnPropertyChanged(nameof(QueuedCount));
            OnPropertyChanged(nameof(HeadlineTitle));
            OnPropertyChanged(nameof(HeadlineStatus));
            OnPropertyChanged(nameof(ShowSuccess));
            OnPropertyChanged(nameof(ShowFailure));
            OnPropertyChanged(nameof(OtherCount));
            OnPropertyChanged(nameof(OtherText));
            OnPropertyChanged(nameof(OtherTip));
        }

        private void OnHeadlineChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BackgroundJob.StatusText))
                OnPropertyChanged(nameof(HeadlineStatus));
        }

        private int _runningCount = 0;
        private int _queuedCount = 0;
        private BackgroundJob _headline = null;
    }
}
