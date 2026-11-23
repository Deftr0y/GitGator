using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RepositoryToolbar : UserControl
    {
        public RepositoryToolbar()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_repo != null)
                _repo.PropertyChanged -= OnRepositoryPropertyChanged;

            _repo = DataContext as ViewModels.Repository;
            if (_repo != null)
                _repo.PropertyChanged += OnRepositoryPropertyChanged;

            UpdateSyncState();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_repo != null)
            {
                _repo.PropertyChanged -= OnRepositoryPropertyChanged;
                _repo = null;
            }

            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (_repo == null && DataContext is ViewModels.Repository repo)
            {
                _repo = repo;
                _repo.PropertyChanged += OnRepositoryPropertyChanged;
            }

            UpdateSyncState();
        }

        private void OnRepositoryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ViewModels.Repository.CurrentBranch)
                or nameof(ViewModels.Repository.Remotes)
                or nameof(ViewModels.Repository.Branches))
            {
                if (Dispatcher.UIThread.CheckAccess())
                    UpdateSyncState();
                else
                    Dispatcher.UIThread.Post(UpdateSyncState);
            }
        }

        // Turns ahead/behind bookkeeping into one sentence and the buttons that matter right now:
        // Fetch (or Pull once there is something to pull), plus Push while there is something to push.
        private void UpdateSyncState()
        {
            if (GetButton == null)
                return;

            var repo = _repo;
            var branch = repo?.CurrentBranch;
            var ahead = branch?.Ahead?.Count ?? 0;
            var behind = branch?.Behind?.Count ?? 0;

            var hasRemote = repo?.Remotes is { Count: > 0 };
            var onBranch = branch is { IsDetachedHead: false };
            var published = onBranch && !string.IsNullOrEmpty(branch.Upstream) && !branch.IsUpstreamGone;
            var canPull = hasRemote && published && behind > 0 && !repo.IsBare;
            var showPush = hasRemote && onBranch && !repo.IsBare && (!published || ahead > 0);

            string status, dot;
            if (repo == null)
                (status, dot) = (string.Empty, "Brush.FG3");
            else if (!hasRemote)
                (status, dot) = ("Only on this computer", "Brush.FG3");
            else if (!onBranch)
                (status, dot) = (branch == null ? "No branch checked out" : "Not on a branch", "Brush.FG3");
            else if (!published)
                (status, dot) = ("This branch is not online yet", "Brush.Warning");
            else if (behind > 0)
                (status, dot) = (ahead > 0 ? $"{Count(behind)} to pull · {Count(ahead)} to push" : $"{Count(behind)} to pull", "Brush.Warning");
            else if (ahead > 0)
                (status, dot) = ($"{Count(ahead)} to push", "Brush.Accent");
            else
                (status, dot) = ("Up to date", "Brush.Success");

            SyncStatus.Text = status;
            SyncDot.Fill = this.FindResource(dot) as IBrush ?? Brushes.Gray;

            AddRemoteButton.IsVisible = repo != null && !hasRemote;

            // Fetch or Pull.
            _getIsPull = canPull;
            GetGroup.IsVisible = hasRemote;
            GetLabel.Text = canPull ? $"{App.Text("Pull")} {behind}" : App.Text("Fetch");
            GetIcon.Data = canPull ? s_pullIcon : this.FindResource("Icons.Fetch") as Geometry;
            GetTip.Text = canPull
                ? $"Bring {Count(behind)} from the remote into {branch.Name}"
                : "Check the remote for new commits";
            GetOptionsTip.Text = canPull ? "Pull options…" : "Fetch options…";
            GetButton.Classes.Set("primary", canPull);
            GetOptionsButton.Classes.Set("primary", canPull);

            // Push, or Publish for a branch the remote has not seen. Pull comes first when both apply.
            PushGroup.IsVisible = showPush;
            PushLabel.Text = published ? $"{App.Text("Push")} {ahead}" : "Publish branch";
            PushIcon.Data = s_pushIcon;
            PushTip.Text = published
                ? $"Send {Count(ahead)} to the remote"
                : "Choose where to put this branch online";
            PushButton.Classes.Set("primary", !canPull);
            PushOptionsButton.Classes.Set("primary", !canPull);

            // Publishing always asks where, so it needs no separate options arrow.
            PushOptionsButton.IsVisible = published;
            PushButton.Classes.Set("alone", !published);
        }

        private static string Count(int commits)
        {
            return commits == 1 ? "1 commit" : $"{commits} commits";
        }

        // A click on a label just does it with the usual settings; the arrow beside it (or Ctrl+click) asks first.
        private async void OnGet(object sender, TappedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            e.Handled = true;
            var direct = e.KeyModifiers is not KeyModifiers.Control;
            if (_getIsPull)
                await repo.PullAsync(direct);
            else
                await repo.FetchAsync(direct);
        }

        private async void OnGetOptions(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            e.Handled = true;
            if (_getIsPull)
                await repo.PullAsync(false);
            else
                await repo.FetchAsync(false);
        }

        private async void OnPush(object sender, TappedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            // A branch that is not online yet still opens the dialog: Git needs to know where to put it.
            e.Handled = true;
            await repo.PushAsync(e.KeyModifiers is not KeyModifiers.Control);
        }

        private async void OnPushOptions(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            e.Handled = true;
            await repo.PushAsync(false);
        }

        private void OnAddRemote(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                repo.AddRemote();

            e.Handled = true;
        }

        private void OnCancelJob(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ViewModels.BackgroundJob job } && DataContext is ViewModels.Repository repo)
                repo.Jobs.Cancel(job);

            e.Handled = true;
        }

        private void OpenBranchMenu(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not ViewModels.Repository repo)
                return;

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

            var locals = new List<Models.Branch>();
            foreach (var b in repo.Branches ?? [])
            {
                if (b.IsLocal && !b.IsDetachedHead)
                    locals.Add(b);
            }

            locals.Sort((l, r) => l.IsCurrent != r.IsCurrent ? (l.IsCurrent ? -1 : 1) : r.CommitterDate.CompareTo(l.CommitterDate));

            const int maxVisible = 12;
            for (var i = 0; i < locals.Count && i < maxVisible; i++)
            {
                var branch = locals[i];
                var item = new MenuItem();
                item.Header = branch.Name;
                item.Icon = this.CreateMenuIcon(branch.IsCurrent ? "Icons.Check" : "Icons.Branch");
                item.IsEnabled = !branch.IsCurrent;
                item.Click += async (_, ev) =>
                {
                    await repo.CheckoutBranchAsync(branch);
                    ev.Handled = true;
                };
                menu.Items.Add(item);
            }

            if (locals.Count > maxVisible)
                menu.Items.Add(new MenuItem() { Header = $"{locals.Count - maxVisible} more in the sidebar", IsEnabled = false });

            if (locals.Count > 0)
                menu.Items.Add(new MenuItem() { Header = "-" });

            var create = new MenuItem();
            create.Header = App.Text("Repository.NewBranch");
            create.Icon = this.CreateMenuIcon("Icons.Branch.Add");
            create.Tag = OperatingSystem.IsMacOS() ? "⌘+B" : "Ctrl+B";
            create.Click += (_, ev) =>
            {
                repo.CreateNewBranch();
                ev.Handled = true;
            };
            menu.Items.Add(create);

            menu.Open(button);
            e.Handled = true;
        }

        private static readonly Geometry s_pullIcon = StreamGeometry.Parse("M5 0H7V9L10 6L11.4 7.4L6 12.8L0.6 7.4L2 6L5 9Z");
        private static readonly Geometry s_pushIcon = StreamGeometry.Parse("M5 13H7V4L10 7L11.4 5.6L6 0.2L0.6 5.6L2 7L5 4Z");

        private ViewModels.Repository _repo;
        private bool _getIsPull = false;

        private void OpenExternalToolsFromMore(object sender, RoutedEventArgs e)
        {
            OpenWithExternalTools(MoreButton, e);
        }

        private void OpenGitFlowFromMore(object sender, RoutedEventArgs e)
        {
            OpenGitFlowMenu(MoreButton, e);
        }

        private void OpenGitLFSFromMore(object sender, RoutedEventArgs e)
        {
            OpenGitLFSMenu(MoreButton, e);
        }

        private void OpenCustomActionsFromMore(object sender, RoutedEventArgs e)
        {
            OpenCustomActionMenu(MoreButton, e);
        }

        private async void StashFromMore(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
                await repo.StashAllAsync(false);

            e.Handled = true;
        }

        private void OpenWithExternalTools(object sender, RoutedEventArgs ev)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var fullpath = repo.FullPath;
                if (!Directory.Exists(fullpath))
                    return;

                var isMacOS = OperatingSystem.IsMacOS();
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                RenderOptions.SetBitmapInterpolationMode(menu, BitmapInterpolationMode.HighQuality);
                RenderOptions.SetEdgeMode(menu, EdgeMode.Antialias);
                RenderOptions.SetTextRenderingMode(menu, TextRenderingMode.SubpixelAntialias);

                var explore = new MenuItem();
                explore.Header = App.Text("Repository.Explore");
                explore.Icon = this.CreateMenuIcon("Icons.Explore");
                explore.Tag = isMacOS ? "⌘+E" : "Ctrl+E";
                explore.Click += (_, e) =>
                {
                    Native.OS.OpenInFileManager(fullpath);
                    e.Handled = true;
                };

                var terminal = new MenuItem();
                terminal.Header = App.Text("Repository.Terminal");
                terminal.Icon = this.CreateMenuIcon("Icons.Terminal");
                terminal.Tag = isMacOS ? "Λ+`" : "Ctrl+`";
                terminal.Click += (_, e) =>
                {
                    Native.OS.OpenTerminal(fullpath);
                    e.Handled = true;
                };

                menu.Items.Add(explore);
                menu.Items.Add(terminal);

                var tools = Native.OS.ExternalTools;
                if (tools.Count > 0)
                {
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    foreach (var tool in tools)
                    {
                        var dupTool = tool;

                        var item = new MenuItem();
                        item.Header = App.Text("Repository.OpenIn", dupTool.Name);
                        item.Icon = new Image { Width = 16, Height = 16, Source = dupTool.IconImage };

                        var options = dupTool.MakeLaunchOptions(fullpath);
                        var count = (dupTool.SupportOpenFolder ? 1 : 0) + (options?.Count ?? 0);
                        if (count == 0)
                            continue;

                        if (count == 1)
                        {
                            var args = fullpath.Quoted();
                            if (options is { Count: 1 })
                                args = options[0].Args;

                            item.Click += (_, e) =>
                            {
                                dupTool.Launch(args);
                                e.Handled = true;
                            };
                        }
                        else
                        {
                            foreach (var opt in options)
                            {
                                var subItem = new MenuItem();
                                subItem.Header = opt.Title;
                                subItem.Click += (_, e) =>
                                {
                                    dupTool.Launch(opt.Args);
                                    e.Handled = true;
                                };

                                item.Items.Add(subItem);
                            }

                            if (dupTool.SupportOpenFolder)
                            {
                                var open = new MenuItem();
                                open.Header = App.Text("Repository.OpenAsFolder");
                                open.Click += (_, e) =>
                                {
                                    dupTool.Launch(fullpath.Quoted());
                                    e.Handled = true;
                                };
                                item.Items.Add(new MenuItem() { Header = "-" });
                                item.Items.Add(open);
                            }
                        }

                        menu.Items.Add(item);
                    }
                }

                var urls = new Dictionary<string, string>();
                foreach (var r in repo.Remotes)
                {
                    if (r.TryGetVisitURL(out var visit))
                        urls.Add(r.Name, visit);
                }

                if (urls.Count > 0)
                {
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    foreach (var (name, addr) in urls)
                    {
                        var dupUrl = addr;

                        var item = new MenuItem();
                        item.Header = App.Text("Repository.Visit", name);
                        item.Icon = this.CreateMenuIcon("Icons.Remotes");
                        item.Click += (_, e) =>
                        {
                            Native.OS.OpenBrowser(dupUrl);
                            e.Handled = true;
                        };

                        menu.Items.Add(item);
                    }
                }

                menu.Open(button);
                ev.Handled = true;
            }
        }

        private async void OpenStatistics(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await this.ShowDialogAsync(new ViewModels.Statistics(repo.FullPath));
                e.Handled = true;
            }
        }

        private async void FetchWithOptions(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.FetchAsync(false);
                e.Handled = true;
            }
        }

        private async void FetchDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (App.GetLauncher() is { CommandPalette: { } } launcher)
                return;

            if (DataContext is ViewModels.Repository repo)
            {
                await repo.FetchAsync(true);
                e.Handled = true;
            }
        }

        private async void PullWithOptions(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PullAsync(false);
                e.Handled = true;
            }
        }

        private async void PullDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (App.GetLauncher() is { CommandPalette: { } } launcher)
                return;

            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PullAsync(true);
                e.Handled = true;
            }
        }

        private async void PushWithOptions(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PushAsync(false);
                e.Handled = true;
            }
        }

        private async void PushDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (App.GetLauncher() is { CommandPalette: { } } launcher)
                return;

            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PushAsync(true);
                e.Handled = true;
            }
        }

        private async void StashAll(object _, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.StashAllAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private void OpenGitFlowMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                if (repo.IsGitFlowEnabled())
                {
                    var startFeature = new MenuItem();
                    startFeature.Header = App.Text("GitFlow.StartFeature");
                    startFeature.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Feature));
                        e.Handled = true;
                    };

                    var startRelease = new MenuItem();
                    startRelease.Header = App.Text("GitFlow.StartRelease");
                    startRelease.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Release));
                        e.Handled = true;
                    };

                    var startHotfix = new MenuItem();
                    startHotfix.Header = App.Text("GitFlow.StartHotfix");
                    startHotfix.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Hotfix));
                        e.Handled = true;
                    };

                    menu.Items.Add(startFeature);
                    menu.Items.Add(startRelease);
                    menu.Items.Add(startHotfix);

                    var type = repo.CurrentBranch != null ? repo.GetGitFlowType(repo.CurrentBranch) : Models.GitFlowBranchType.None;
                    if (type != Models.GitFlowBranchType.None)
                    {
                        var finish = new MenuItem();
                        finish.Header = App.Text("GitFlow.Finish", repo.CurrentBranch.Name);
                        finish.Icon = this.CreateMenuIcon("Icons.GitFlow.Finish");
                        finish.Click += (_, e) =>
                        {
                            if (repo.CanCreatePopup())
                                repo.ShowPopup(new ViewModels.GitFlowFinish(repo, repo.CurrentBranch, type));
                            e.Handled = true;
                        };
                        menu.Items.Add(new MenuItem() { Header = "-" });
                        menu.Items.Add(finish);
                    }
                }
                else
                {
                    var init = new MenuItem();
                    init.Header = App.Text("GitFlow.Init");
                    init.Icon = this.CreateMenuIcon("Icons.Init");
                    init.Click += (_, e) =>
                    {
                        if (repo.CurrentBranch == null)
                            repo.SendNotification("Git flow init failed: No branch found!!!", true);
                        else if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.InitGitFlow(repo));

                        e.Handled = true;
                    };
                    menu.Items.Add(init);
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private void OpenGitLFSMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                if (repo.IsLFSEnabled())
                {
                    var addPattern = new MenuItem();
                    addPattern.Header = App.Text("GitLFS.AddTrackPattern");
                    addPattern.Icon = this.CreateMenuIcon("Icons.File.Add");
                    addPattern.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.LFSTrackCustomPattern(repo));

                        e.Handled = true;
                    };
                    menu.Items.Add(addPattern);
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    var fetch = new MenuItem();
                    fetch.Header = App.Text("GitLFS.Fetch");
                    fetch.Icon = this.CreateMenuIcon("Icons.Fetch");
                    fetch.IsEnabled = repo.Remotes.Count > 0;
                    fetch.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                        {
                            if (repo.Remotes.Count == 1)
                                await repo.ShowAndStartPopupAsync(new ViewModels.LFSFetch(repo));
                            else
                                repo.ShowPopup(new ViewModels.LFSFetch(repo));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(fetch);

                    var pull = new MenuItem();
                    pull.Header = App.Text("GitLFS.Pull");
                    pull.Icon = this.CreateMenuIcon("Icons.Pull");
                    pull.IsEnabled = repo.Remotes.Count > 0;
                    pull.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                        {
                            if (repo.Remotes.Count == 1)
                                await repo.ShowAndStartPopupAsync(new ViewModels.LFSPull(repo));
                            else
                                repo.ShowPopup(new ViewModels.LFSPull(repo));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(pull);

                    var push = new MenuItem();
                    push.Header = App.Text("GitLFS.Push");
                    push.Icon = this.CreateMenuIcon("Icons.Push");
                    push.IsEnabled = repo.Remotes.Count > 0;
                    push.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                        {
                            if (repo.Remotes.Count == 1)
                                await repo.ShowAndStartPopupAsync(new ViewModels.LFSPush(repo));
                            else
                                repo.ShowPopup(new ViewModels.LFSPush(repo));
                        }

                        e.Handled = true;
                    };
                    menu.Items.Add(push);

                    var prune = new MenuItem();
                    prune.Header = App.Text("GitLFS.Prune");
                    prune.Icon = this.CreateMenuIcon("Icons.Clean");
                    prune.Click += async (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            await repo.ShowAndStartPopupAsync(new ViewModels.LFSPrune(repo));

                        e.Handled = true;
                    };
                    menu.Items.Add(new MenuItem() { Header = "-" });
                    menu.Items.Add(prune);

                    var locks = new MenuItem();
                    locks.Header = App.Text("GitLFS.Locks");
                    locks.Icon = this.CreateMenuIcon("Icons.Lock");
                    locks.IsEnabled = repo.Remotes.Count > 0;
                    if (repo.Remotes.Count == 1)
                    {
                        locks.Click += async (_, e) =>
                        {
                            await this.ShowDialogAsync(new ViewModels.LFSLocks(repo, repo.Remotes[0].Name));
                            e.Handled = true;
                        };
                    }
                    else
                    {
                        foreach (var remote in repo.Remotes)
                        {
                            var remoteName = remote.Name;
                            var lockRemote = new MenuItem();
                            lockRemote.Header = remoteName;
                            lockRemote.Click += async (_, e) =>
                            {
                                await this.ShowDialogAsync(new ViewModels.LFSLocks(repo, remoteName));
                                e.Handled = true;
                            };
                            locks.Items.Add(lockRemote);
                        }
                    }

                    menu.Items.Add(new MenuItem() { Header = "-" });
                    menu.Items.Add(locks);
                }
                else
                {
                    var install = new MenuItem();
                    install.Header = App.Text("GitLFS.Install");
                    install.Icon = this.CreateMenuIcon("Icons.Init");
                    install.Click += async (_, e) =>
                    {
                        await repo.InstallLFSAsync();
                        e.Handled = true;
                    };
                    menu.Items.Add(install);
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private async void StartBisect(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository { IsBisectCommandRunning: false, InProgressContext: null } repo &&
                repo.CanCreatePopup())
            {
                if (repo.LocalChangesCount > 0)
                    repo.SendNotification("You have un-committed local changes. Please discard or stash them first.", true);
                else if (repo.IsBisectCommandRunning || repo.BisectState != Models.BisectState.None)
                    repo.SendNotification("Bisect is running! Please abort it before starting a new one.", true);
                else
                    await repo.ExecBisectCommandAsync("start");
            }

            e.Handled = true;
        }

        private void OpenCustomActionMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                var actions = repo.GetCustomActions(Models.CustomActionScope.Repository);
                if (actions.Count > 0)
                {
                    foreach (var action in actions)
                    {
                        var (dup, label) = action;
                        var item = new MenuItem();
                        item.Icon = this.CreateMenuIcon("Icons.Action");
                        item.Header = label;
                        item.Click += async (_, e) =>
                        {
                            await repo.ExecCustomActionAsync(dup, null);
                            e.Handled = true;
                        };

                        menu.Items.Add(item);
                    }
                }
                else
                {
                    menu.Items.Add(new MenuItem() { Header = App.Text("Repository.CustomActions.Empty") });
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private async void OpenGitLogs(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await this.ShowDialogAsync(new ViewModels.ViewLogs(repo));
                e.Handled = true;
            }
        }

        private async void Cleanup(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.CleanupAsync();
                e.Handled = true;
            }
        }

        private void NavigateToHead(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository { CurrentBranch: { } head } repo)
            {
                var repoView = TopLevel.GetTopLevel(this)?.FindDescendantOfType<Repository>();
                repoView?.LocalBranchTree?.Select(head);

                repo.NavigateToCommit(head.Head);
                e.Handled = true;
            }
        }
    }
}
