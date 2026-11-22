<img src="branding/gitgator.svg" alt="GitGator orange gator logo" width="128" />

# GitGator

A free, open-source Git GUI focused on Ubuntu and Linux, starting from
[SourceGit](https://github.com/sourcegit-scm/sourcegit) under the MIT license.

This is an initial development baseline, version 0.1.0. The Simple Fork workspace
uses PeerBrush's website-inspired warm dark colors, compact navigation and
hierarchical file trees. Repositories open in tabs; there is no workspace
switcher or tab-search interface. The new tab centers Clone, Open and Terminal
actions above repository search and the saved repository list.
Changes, Staged Changes and the commit form share one workspace. File previews
are hidden by default; use **Preview file** when you want to inspect a diff.
Selecting files or folders alone does not load their content. Use Stage/Unstage
or Space/Enter for selected files and folders; double-click a file to move it
between Changes and Staged Changes.
The main commit action commits staged files locally; Pull and Push remain separate.

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
