# M.I.S.T. v0.6.0-dev.2 — Development preview

**Date:** 2026-11-05

**Platform:** Linux x86_64 AppImage (preview)

**Project:** Independent GPL-3.0 fork of Nexus Mods App; not affiliated with or endorsed by Nexus Mods.

## UI, branding and navigation

- M.I.S.T.-first GitHub README and project documentation; preserved upstream attribution and archived original documentation.
- Installed-game cards use locally cached Steam cover artwork, with bundled fallbacks.
- Main game sidebar and Downloads game list use Steam-cached small icons where available, or game logos composed over locally cached backgrounds; fallback remains safe for uncached and non-Steam games.
- Settings Save button uses M.I.S.T.'s cyan palette; inherited Nexus-specific Storage Location help link replaced with independent help text and a M.I.S.T. storage guide.
- My Games **View roadmap** opens M.I.S.T.'s GitHub roadmap rather than the inherited Nexus Trello board; other project/changelog links point to M.I.S.T. resources.

## Related improvements and verification

- Independent settings/log presentation and navigation cleanup from the 006-series development work are included **where present in the committed source**.
- Builds and regression tests are run by the local release publisher before tagging. Review GitHub Actions for the AppImage build result; a successful local source build does not by itself prove that the packaged AppImage works on another system.
- Prior file-operation safety mechanisms (installation-specific original-file verification and receipt-based reversal) remain critical and must not be replaced with recursive directory deletion.

## On the roadmap (not shipped in this release)

- **Manual game support:** choose and validate a game directory when automatic detection fails; unsupported games initially remain catalog-only until safely managed.
- **Recognition of already-installed Nexus mods:** read-only inventory, evidence-backed identification, confidence labels, and optional user-confirmed adoption without taking ownership of unknown files.
- **Linux GOG discovery:** research for Heroic, Lutris, Minigalaxy and offline installs; still unverified without community test installations.

## Links

- [Roadmap](ROADMAP.md)
- [Supported games](SUPPORTED-GAMES.md)
- [Changelog](../CHANGELOG.md)
- [GitHub release](https://github.com/shamenra/MIST/releases/tag/v0.6.0-dev.2)
- [Report a bug](https://github.com/shamenra/MIST/issues)

**Preview warning:** Back up game data and saves. Finding a game or mod does not by itself mean it is safe to install or remove files. Nexus account authentication still depends on the local `nxm://` callback handler being properly registered.
