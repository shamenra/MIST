# Supported Games & Validation

[← M.I.S.T. home](../README.md) · [Roadmap](ROADMAP.md) · [Changelog](../CHANGELOG.md)

Updated: **2026-11-05**. This list distinguishes inherited game integrations from functionality **actually validated in M.I.S.T.** Support varies by storefront, game edition, and mod format.

| Game | Integration status | Runtime evidence | Known constraints / next tests |
| --- | --- | --- | --- |
| Cyberpunk 2077 | **Locally validated** on Steam | Steam detection, public version 2.5 on tested install, original-file validation, and locally cached cover art | More mod deployment formats and historical versions require validation |
| The Elder Scrolls IV: Oblivion Remastered | **Locally validated** on Steam | Steam detection, public version 1.2 on tested install, original-file validation, receipt-based test-file deploy/removal, and locally cached cover art | General mod compatibility and other builds need further tests |
| Stardew Valley | **Inherited supported integration** | Listed by the existing app as supported; not yet independently revalidated in this fork | Test Steam/GOG installations and SMAPI handling |

The displayed public versions above are observations from specific user installations, **not claims that all installations run those versions**. Steam game artwork is sourced from local user caches and not included in the AppImage.

**Candidate games are not supported until validated.** See the [roadmap](ROADMAP.md) and [game requests](https://github.com/shamenra/MIST/issues) for planned work.

## Linux GOG status — community validation needed

**Not validated in M.I.S.T. on Linux.** The project inherits some GOG-related locators, including Heroic integration, but native Linux GOG discovery and end-to-end mod operations have not been verified by the maintainer. Depending on launcher and install layout, parts of discovery may already work; this is **not** a support guarantee. Windows-registry-only discovery does not provide a native Linux installation source.

We plan to investigate Heroic, Lutris, Minigalaxy, and offline GOG installs. See the [MIST-006-10 roadmap](ROADMAP.md#linux-gog-discovery--mist-006-10). If you have a GOG game on Linux, please file a [success or failure report](https://github.com/shamenra/MIST/issues/new?template=gog-linux-report.yml). Reports should include launcher, distribution, game/product ID when available, and reproducible steps, with sensitive paths and tokens removed.

### How a game graduates to validated

A successful scan or a visible thumbnail is insufficient. We also require correct installation identity, safe writable paths, reversible mod operations, and a testable regression/smoke-test record. Known limitations must be documented.
