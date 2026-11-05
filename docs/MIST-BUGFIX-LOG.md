# M.I.S.T. — Fix & Regression Log

Maintained development history; see [changelog](../CHANGELOG.md).

## v0.6.0-dev.2 — 2026-11-05 (MIST-006-07A through MIST-006-12E)

- **Game cards:** read-only Steam artwork discovery and reliable cover fallback.
- **Main game sidebar:** replace hardcoded game thumbnails with Steam icon or rendered logo/background when cached.
- **Downloads sidebar:** fix separate per-game image binding and `SelectAwait`/`ValueTask<Bitmap?>` compilation mismatch; preserve bundled fallback.
- **Settings:** replace remaining orange Save state with cyan; retire misleading Nexus storage-help hyperlink.
- **Project navigation:** repair My Games roadmap action and verify the compiled UI points to M.I.S.T. instead of the Nexus Trello board.
- **Documentation:** own README/changelog/roadmap, original upstream history retained, Linux GOG status documented as unverified.
- **Release process:** bump display/product version, PupNet arguments and tag-triggered AppImage workflow together; regenerate local build identifier after commit.

These are development milestones; see the [changelog](../CHANGELOG.md) and [release notes](RELEASE-NOTES-v0.6.0-dev.2.md) for user-facing details. The release publisher runs the local regression suite before publication. Actual artifact packaging must be checked in GitHub Actions.
