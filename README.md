<div align="center">
  <img src="src/NexusMods.App.UI/Assets/mist-logo.svg" alt="M.I.S.T. logo" width="112" />

  # M.I.S.T.

  **Mod Integration and Sorting Technology**

  *A Linux-first, open-source game mod manager built from the Nexus Mods App codebase.*

  **Linux x86_64 · AppImage preview · GPL-3.0**

  [**Download preview**](https://github.com/shamenra/MIST/releases) ·
  [**Roadmap**](docs/ROADMAP.md) ·
  [**Report an issue**](https://github.com/shamenra/MIST/issues)
</div>

---

## What is M.I.S.T.?

**M.I.S.T.** is an independent fork of [Nexus Mods App](https://github.com/Nexus-Mods/NexusMods.App), focused on making game discovery, version-aware mod management, and safe, reversible loadouts work well on **Linux**—including systems running Steam through Flatpak and Windows games through Proton.

It keeps useful Nexus Mods integration, including account authentication and supported mod downloads, while developing its own Linux-focused fixes, game support, interface, and release process. M.I.S.T. is **not an official Nexus Mods application** and is not affiliated with or endorsed by Nexus Mods.

> **Development preview:** This is an early release, not a claim that every game, mod, launcher, or Linux distribution is verified. Back up valuable saves and game data before trying mod-management operations.

## Start here

| Resource | What's inside |
| --- | --- |
| [**Download M.I.S.T.**](https://github.com/shamenra/MIST/releases) | Linux x86_64 AppImage builds and release notes |
| [**Game & feature roadmap**](docs/ROADMAP.md) | Games we want to add, upcoming features, and validation criteria |
| [**Supported games**](docs/SUPPORTED-GAMES.md) | What we've tested, what we inherited, and what remains experimental |
| [**M.I.S.T. changelog**](CHANGELOG.md) | Important fixes, game additions, and user-facing changes |
| [**Development history**](https://github.com/shamenra/MIST/commits/mist/development) | Every commit pushed to the development branch |
| [**Issues & game requests**](https://github.com/shamenra/MIST/issues) | Bug reports, game requests, and community test results |
| [**Credits and licensing**](docs/MIST-CREDITS.md) | Upstream contributors, attribution, and licensing information |
| [**Original Nexus Mods App README**](docs/upstream/README.md) | Archived upstream landing page for reference |
| [**Original Nexus Mods App changelog**](docs/upstream/CHANGELOG.md) | Archived upstream release history |

## What works today?

- **Steam game discovery**, including common Linux and Flatpak Steam layouts.
- **Steam build identity** and local depot-manifest acquisition for version-aware game recognition.
- **Verified original-file detection:** compare local files against trusted Steam depot information before classifying them as original game files.
- **Known public game-version mappings**, where sufficient verified data exists; unknown versions remain unknown rather than being guessed.
- **Mod/loadout management** with receipt-based file reversal designed to avoid deleting unrelated game directories.
- **Nexus Mods account login** and existing supported download integrations.
- **Local Steam cover art** on installed game cards, using Steam's artwork cache rather than shipping game images inside M.I.S.T. (implemented and tested locally after the first AppImage preview).
- **M.I.S.T. branding** with the cyan/magenta interface and human-readable version/build identifiers.

These are capabilities of the project, **not universal compatibility guarantees**. Exact game, storefront, mod format, and Linux-environment support varies; see [Supported Games](docs/SUPPORTED-GAMES.md).

### Current game validation

| Game | Current evidence |
| --- | --- |
| **Cyberpunk 2077** | Locally tested on Steam, including public version **2.5**, installation identification, and original-file verification |
| **The Elder Scrolls IV: Oblivion Remastered** | Locally tested on Steam, including public version **1.2** and a controlled mod-file deployment/removal test |
| **Stardew Valley** | Supported by inherited upstream integration; M.I.S.T.-specific Linux validation is still pending |
| **GOG on Linux** | **Experimental/research roadmap**; we need community installations and bug reports to validate launchers and paths |

See the [full supported-games tracker](docs/SUPPORTED-GAMES.md) for qualifications and [the roadmap](docs/ROADMAP.md) for proposed additions. Discovering a game does *not* automatically mean it's safe to modify.

## Install the AppImage

Get the current preview asset from [M.I.S.T. Releases](https://github.com/shamenra/MIST/releases). On an x86_64 Linux desktop:

```bash
chmod +x MIST-*.AppImage
./MIST-*.AppImage
```

The AppImage is the **only packaged application format** published for this Linux preview (GitHub also provides automatic source-code archives). No Windows, DEB, or RPM release is implied.

**Nexus login note:** Your desktop must route the `nxm://` authorization callback to M.I.S.T. Standalone AppImage desktop integration can differ by distribution, browser, and launcher configuration; please report failures with your Linux setup, without sharing authorization URLs or tokens.

## Why does M.I.S.T. exist? The Cyberpunk story

It started with **Cyberpunk 2077**, not a plan to launch a new mod manager.

After a Steam game update, the Linux Nexus Mods App setup couldn't reliably recognize the updated **Cyberpunk 2077 2.5** files. The game appeared with **“Version: Unknown”**, and outdated official-file hashes could cause a backup-limit error during mod deployment. Reinstalling the game or throwing away an otherwise working mod setup was not an appealing solution.

The first fix was a focused **Linux HashFix**: use the installed Steam build and depot manifest, verify original files, and rebuild the missing official-file hash records. In the tested Cyberpunk installation, that recovered **117 missing hash entries covering roughly 66.7 GB of game files** and restored a usable mod-management path without wiping the game or its mods. Other game-launch issues, including outdated mods after an update, were investigated separately.

That one repair uncovered bigger opportunities. We needed more reliable Steam version detection, support for historical builds, safer file reversal, better Linux integration, and clearer game compatibility. The work grew from a one-game workaround into **M.I.S.T.—Mod Integration and Sorting Technology**.

Early milestones included adding **Oblivion Remastered** integration, correcting a dangerous inherited directory-cleanup path, preserving independent build metadata, replacing inherited application branding, and producing the first **Linux AppImage preview**. Local Steam-library artwork detection followed soon afterward.

The goal is still the same as that first Cyberpunk fix: **make the tools work with the games people already have, and avoid damaging a working installation while doing it.**

## Where we're going

The [living roadmap](docs/ROADMAP.md) tracks game support, version recognition, Linux launcher discovery, OAuth integration, and other improvements. We'd like to work toward **a few game additions or substantial validation milestones per week**, but that's a stretch goal—not a release promise. Safe testing and correct file handling come first.

**GOG discovery on Linux** is an example of a planned community-tested area. We can develop Heroic/Lutris/Minigalaxy/offline-install detection, but without a real GOG installation to test locally, it must remain labelled unverified until users supply reproducible results. Both successful detections and bug reports are welcome.

## Build and contribute

The active development branch is [`mist/development`](https://github.com/shamenra/MIST/tree/mist/development). The project currently builds on .NET and uses Avalonia for its desktop interface.

```bash
dotnet restore src/NexusMods.App/NexusMods.App.csproj
dotnet build src/NexusMods.App/NexusMods.App.csproj -c Release
dotnet test tests/NexusMods.DataModel.Tests/NexusMods.DataModel.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~MIST'
```

The project still contains upstream `NexusMods.*` source, project, and namespace names. Those are implementation details inherited from the fork, not a claim that this repository is the original Nexus Mods App.

When reporting a bug, please include the M.I.S.T. version/build, game and edition, storefront/launcher, Linux distribution, and reproducible steps. Redact personal paths and logs as needed; **never post OAuth callback URLs, account tokens, or credentials**.

## Credits, upstream history, and license

M.I.S.T. is built on the work of the [**Nexus Mods App** developers and contributors](https://github.com/Nexus-Mods/NexusMods.App). Their original project, licenses, and notices remain important parts of this fork.

- **Upstream source:** [Nexus-Mods/NexusMods.App](https://github.com/Nexus-Mods/NexusMods.App)
- **Archived upstream docs:** [README](docs/upstream/README.md) · [Changelog](docs/upstream/CHANGELOG.md)
- **Fork credits:** [M.I.S.T. Credits](docs/MIST-CREDITS.md)
- **License:** [GNU GPL-3.0](LICENSE.md) (see the repository and third-party notices)

Nexus Mods and game trademarks belong to their respective owners. Local Steam artwork is read from the user's Steam installation and is **not bundled** into the M.I.S.T. AppImage.
