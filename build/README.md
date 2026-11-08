# Building GitGator on Ubuntu / Linux

Use .NET SDK 10.0.100 or a newer 10.0 feature band (global.json), Git 2.25.1+,
and the pinned AvaloniaEdit submodule. Package restore requires NuGet access.

```sh
git submodule update --init --recursive
dotnet restore GitGator.slnx
dotnet build GitGator.slnx -c Debug --no-restore
dotnet run --project src/GitGator.csproj --no-build
dotnet build GitGator.slnx -c Release -p:DisableAOT=true
```

Linux desktop runtime dependencies include libx11-6, libice6, libsm6,
libfontconfig1, the distribution's libicu and OpenSSL libraries, and zlib1g.
Install xdg-utils for opening files, folders and links. Configure a Git credential
helper (such as Git Credential Manager or libsecret) for HTTPS remotes as needed.

For a self-contained managed Linux x64 directory:

```sh
dotnet publish src/GitGator.csproj -c Release -r linux-x64 --self-contained true -p:DisableAOT=true -o artifacts/linux-x64
```

Native AOT is inherited from upstream and enabled for Release publishing unless
DisableAOT=true. It additionally requires clang and zlib1g-dev on Ubuntu.
A successful managed build does not validate AOT or a distributable package.

`packaging/linux/io.github.Deftr0y.GitGator.desktop` assumes `GitGator` is on PATH.
It is a template, not installed by these commands. Original scripts under
build/scripts and resources target SourceGit and must be ported before use.
Original release workflows are archived in docs/upstream/workflows and are inert.

## Local desktop entry and icon

Once `GitGator` is on PATH, install the development launcher and its orange
gator icon for your account:

```sh
install -Dm644 packaging/linux/io.github.Deftr0y.GitGator.desktop "$HOME/.local/share/applications/io.github.Deftr0y.GitGator.desktop"
install -Dm644 packaging/linux/gitgator.png "$HOME/.local/share/icons/hicolor/512x512/apps/io.github.Deftr0y.GitGator.png"
install -Dm644 branding/gitgator.svg "$HOME/.local/share/icons/hicolor/scalable/apps/io.github.Deftr0y.GitGator.svg"
```

The [branding source and export instructions](../branding/README.md) describe
how to regenerate the in-app PNG, window ICO, and Linux icon from the same SVG.
