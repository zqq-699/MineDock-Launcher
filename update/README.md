# MineDock Launcher Remote Update Manifests

This directory stores remote update manifest files for MineDock Launcher releases.
These files are source-controlled release metadata used by the GitHub raw and
Gitee raw update sources, with matching executable assets published through
GitHub and Gitee Releases.

The `update` directory is not used as the launcher's local runtime configuration.

## Channels

- `release/latest.json`: stable release channel for normal users.
- `beta/latest.json`: beta channel for test builds and early validation.

Both channels use the same JSON schema. The only required channel difference is
the top-level `channel` field.

## Release Checklist

When preparing a new version, update the build metadata in
`Launcher.App/Launcher.App.csproj` and add a matching release notes file:

- Release notes:
  - `release/notes/{versionName}.md`, for example `release/notes/1.2.3.md`.
  - `beta/notes/{versionName}.md`, for example `beta/notes/1.2.3-beta.1.md`.
- `LauncherVersionCode` uses `MMmmppbb` semantics and is stored as a JSON number.
  - Release builds use `bb = 99`, for example `0.9.1` -> `90199`.
  - Beta builds use the beta revision for `bb`, for example `0.9.1-beta.1` -> `90101`.

The release workflows parse `versionName` from the Git tag and verify
`InformationalVersion`, `LauncherBuildChannel`, and `LauncherVersionCode` from
`Launcher.App/Launcher.App.csproj`. The workflows do not use `latest.json` to
decide the version being published.

After GitHub and Gitee Releases are created successfully, the workflow writes the
final remote manifest back to `update/{channel}/latest.json` on the default
branch and mirrors both channel manifests to the lightweight Gitee update
repository:

- `versionName`: user-facing version, for example `1.2.3`.
- `versionCode`: numeric version used for comparisons, for example `1020399`.
- `publishedAt`: publish time in ISO 8601 format.
- `mandatory`: whether this update is mandatory.
- `minSupportedVersionCode`: local versions below this value must update.
- `releaseNotes`: Markdown copied from the matching notes file.
- `assets`: Windows x64 single-file `exe` package metadata.
- `fileName`: published executable file name.
- `size`: executable size in bytes.
- `sha256`: SHA-256 checksum used for download integrity verification.
- `urls`: Gitee and GitHub download mirrors, tried from lower `priority` to higher `priority`.

Final manifests contain only `gitee` and `github` download URLs. Gitee is the
first update manifest source and the first download mirror; GitHub is the
fallback. The Gitee repository is not a source-code mirror; it only stores
`update/release/latest.json`, `update/beta/latest.json`, lightweight tags, and
Release attachments.
