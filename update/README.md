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

When publishing a new version, update the corresponding channel's `latest.json`:

- `versionName`: user-facing version, for example `1.2.3`.
- `versionCode`: numeric version used for comparisons, for example `10203`.
- `publishedAt`: publish time in ISO 8601 format.
- `mandatory`: whether this update is mandatory.
- `minSupportedVersionCode`: local versions below this value must update.
- `releaseNotes`: short user-facing release notes.
- `assets`: Windows x64 single-file `exe` package metadata.
- `fileName`: published executable file name.
- `size`: executable size in bytes.
- `sha256`: SHA-256 checksum used for download integrity verification.
- `urls`: download mirrors, tried from lower `priority` to higher `priority`.

Keep placeholder download URLs empty until the final OSS, GitCode, Gitee, or
GitHub Release URLs are ready.
