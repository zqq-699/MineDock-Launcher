# BlockHelm Launcher Remote Update Manifests

This directory stores release notes and the update manifest template for
BlockHelm Launcher releases. The default branch does not store live
`latest.json` manifests.

The `update` directory is not used as the launcher's local runtime configuration.

## Channels

- `release/notes/`: stable release notes.
- `beta/notes/`: beta release notes.
- `latest.template.json`: manifest schema/template used by release workflows.

Live channel manifests are written by the release workflows to the
`update-manifests` branch in both GitHub and Gitee:

- `update/release/latest.json`: stable release channel for normal users.
- `update/release/latest.json.sig`: Ed25519 signature for the exact manifest bytes.
- `update/beta/latest.json`: beta channel for test builds and early validation.
- `update/beta/latest.json.sig`: Ed25519 signature for the exact manifest bytes.

## Signing key

`latest.json` is written as UTF-8 without a BOM and is signed byte-for-byte with
an Ed25519 PKCS#8 private key. `latest.json.sig` is the 64-byte signature encoded
as canonical Base64 without a BOM or trailing newline. `keyId` is the lowercase
SHA-256 of the public key's SubjectPublicKeyInfo DER bytes.

Local signing material lives only in `.local-secrets/update-signing-private.pem`
and `.local-secrets/update-signing-public.pem`. GitHub Actions uses the
`release-signing` Environment secret `UPDATE_SIGNING_PRIVATE_KEY_BASE64`; its
value is the Base64 encoding of the private PEM file's exact UTF-8 bytes. The
workflow derives and embeds only the public key and removes the private key in
an `always()` cleanup step.

This first version trusts one public key. Key rotation requires first releasing
a transition client that trusts both the old and new keys; replacing the key in
the workflow alone would make existing clients reject future manifests.

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
`Launcher.App/Launcher.App.csproj`. The workflows do not use live `latest.json`
manifests to decide the version being published.

After GitHub and Gitee Releases are created successfully, the workflow writes the
final remote manifest to `update/{channel}/latest.json` on the
`update-manifests` branch in GitHub and Gitee:

- `versionName`: user-facing version, for example `1.2.3`.
- `keyId`: signing public-key identifier described above.
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
fallback. The Gitee repository is not a source-code mirror; its
`update-manifests` branch only stores `update/release/latest.json`,
`update/beta/latest.json`, their `.sig` files, lightweight tags, and Release
attachments. Both providers receive the same local manifest and signature
files; the workflow reads them back with cache-busting URLs and fails unless
both copies are byte-identical.
