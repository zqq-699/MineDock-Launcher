# MineDock Launcher Remote Update Manifests

This directory stores remote update manifest files for MineDock Launcher releases.
These files are source-controlled release metadata and are intended to be uploaded
to a static update source such as OSS, COS, GitCode, Gitee, GitHub raw, or GitHub
Release assets.

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

After a GitHub Release is created successfully, the workflow writes the final
remote manifest back to `update/{channel}/latest.json` on the default branch:

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
- `urls`: download mirrors, tried from lower `priority` to higher `priority`.

Keep placeholder download URLs empty until the final OSS, GitCode, Gitee, or
GitHub Release URLs are ready.
