<img src="branding/gitgator.svg" alt="GitGator orange gator logo" width="128" />

# GitGator

A free, open-source Git GUI focused on Ubuntu and Linux, starting from
[SourceGit](https://github.com/sourcegit-scm/sourcegit) under the MIT license.

This is an initial development baseline, version 0.1.0. The interface assumes
you work with an AI assistant: it shows what you need to decide and keeps the
technical tools one click away. Colors and shapes follow PeerBrush's warm dark
website and Ubuntu's dark style.

- **Header.** The branch pill shows where you are and switches or creates
  branches. Next to it, one sentence says whether the branch is in sync
  ("2 commits to push", "Up to date", "Only on this computer") and one button
  offers the next step: Pull, Push, Publish branch, Add remote or Fetch. A
  click runs it; Ctrl+click opens the dialog with every option. Fetch, Pull and
  Push with options are also under **⋯**.
- **Sidebar.** Changes, History and your local branches. Stashes, remotes, tags,
  submodules and worktrees sit under **Advanced**, which stays closed until you
  open it. Stashes also appears on its own while something is stashed.
- **Changes.** One list of changed files, one message box, one button. With
  nothing staged the button is **Commit all**. Select files and choose
  **Stage selected** (or Space/Enter, or double-click) to commit only part of
  the work; a **Staged for commit** list appears and the button becomes
  **Commit staged**. **Write with AI** drafts the message from the staged
  changes, staging everything first when nothing is staged; without a provider
  it opens Preferences on the AI page. File previews stay hidden until you
  choose **Preview**. Committing is local; Pull and Push remain separate.
- **History.** Taller rows with message, author and time. The commit ID column
  is off by default and can be turned back on from the column options. The
  detail panel leads with author and message; ID, parents and refs follow as
  reference lines.

Repositories open in tabs. The new tab centers Clone, Open and Terminal actions
above repository search and the saved repository list.

GitGator inherits SourceGit's commit graph, branches, remotes, stash,
merge/rebase, worktrees and optional AI commit-message generation. Less-used
tools are available through menus. The orange gator identity is used throughout
the app. Further AI features are planned.
AI providers are optional and may charge separately; no provider is configured.

## Build and run on Ubuntu

Install Git (2.25.1+) and the .NET 10 SDK. The SDK can be installed locally with
Microsoft's dotnet-install script, or from a supported Ubuntu package source.
Use a terminal where `dotnet --info` reports a 10.0 SDK.

```sh
git submodule update --init --recursive
dotnet restore GitGator.slnx
dotnet build GitGator.slnx -c Debug --no-restore
dotnet run --project src/GitGator.csproj --no-build
```

An existing checkout is already initialized with the pinned editor submodule.
The executable is `src/bin/Debug/net10.0/GitGator`.
For a managed Release build, use `-c Release -p:DisableAOT=true`.
See [build instructions](build/README.md) for publishing and native dependencies.

## GitHub over HTTPS

GitGator uses Git's configured credentials by default. For browser sign-in,
install a current [GitHub CLI](https://github.com/cli/cli/blob/trunk/docs/install_linux.md)
and run:

```sh
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git --hostname github.com
```

Complete the browser authorization and answer Yes if asked to authenticate Git.
For changes to `.github/workflows`, include `--scopes workflow` in the login
command. Git's author name and email are separate settings and must also be set
before creating commits. SSH keys are optional.

## App identity and storage

The app, executable, assembly and solution are named GitGator. Internal C#
namespaces remain SourceGit to reduce upstream merge conflicts.
Linux uses `${XDG_CONFIG_HOME:-~/.config}/GitGator` for settings and
`${XDG_CACHE_HOME:-~/.cache}/GitGator` for cache and logs. Windows and macOS
also use a separate GitGator directory. Existing SourceGit data is not migrated.
Repository-specific settings use `gitgator.*` files inside `.git`.

SourceGit update prompts are disabled. CI only builds on Ubuntu and never
publishes. A development desktop entry is provided in `packaging/linux`;
installing or packaging the app remains a separate step.

## Initial priorities

1. Validate the inherited interface on Ubuntu with X11 and Wayland.
2. Refine onboarding around GitGator's orange gator identity.
3. Port Linux packaging and test a reproducible install.
4. Design opt-in AI assistance with clear controls over submitted repository data.

## License and credits

[MIT](LICENSE). Based on SourceGit v2026.23; see [attribution](NOTICE.md),
[third-party notices](THIRD-PARTY-LICENSES.md), and the retained Git history.
