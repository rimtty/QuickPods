# Release guide

This document describes the repository's release mechanics. It does not authorize publication or signing on behalf of a maintainer.

## Release artifacts

QuickPods can produce:

- a self-contained `win-x64` portable ZIP for diagnostics and testing;
- an elevation-free, per-user x64 MSI;
- SHA-256 checksum files and JSON manifests for both packages.

End users do not need a separate .NET Desktop Runtime.

## Versioning

Use a three-part semantic version, optionally followed by a prerelease suffix:

```text
0.1.0
0.1.0-rc.1
0.1.0-ci.123
```

Update `VersionPrefix`, `AssemblyVersion`, and `FileVersion` in `Directory.Build.props` for a stable release and add the release notes to `CHANGELOG.md`.

## Build locally

```powershell
dotnet restore QuickPods.sln --locked-mode
dotnet restore installer/QuickPods.Setup/QuickPods.Setup.wixproj --locked-mode
./build/Test-DependencyVulnerabilities.ps1
dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn
dotnet build QuickPods.sln -c Release --no-restore
dotnet test QuickPods.sln -c Release --no-build --no-restore -- RunConfiguration.TreatNoTestsAsError=true
./build/Test-RepositoryPublicReadiness.ps1
./build/Publish-ReleaseCandidate.ps1 -Version 0.1.0-rc.1
./build/Publish-Installer.ps1 -Version 0.1.0-rc.1
```

Generated files are placed below ignored `artifacts/` directories.

## Signing

Do not distribute an unsigned MSI as a stable release. Unsigned artifacts must be labeled clearly as test builds.

The `Signed release` GitHub Actions workflow uses the protected `release-signing` environment and these secrets:

- `QUICKPODS_SIGNING_PFX_BASE64`
- `QUICKPODS_SIGNING_PFX_PASSWORD`

The workflow restores the PFX only in runner temporary storage, signs with SHA-256 and a timestamp, validates Authenticode status, and deletes the temporary certificate material. Never commit a certificate or password.

## Publication checklist

Before changing repository visibility, complete the separate [public repository checklist](public-repository-checklist.md).

- [ ] Version and changelog are final.
- [ ] Locked restore, dependency audit, format, Release build, and all tests pass.
- [ ] Public-readiness scan passes.
- [ ] Hardware-dependent changes have current local-console validation.
- [ ] Portable and MSI manifests list the expected version and files.
- [ ] Checksums match the files from the same workflow run.
- [ ] MSI signature is `Valid` and timestamped.
- [ ] Upgrade and uninstall behavior was tested from the previous supported version.
- [ ] Release notes include compatibility, known limitations, and migration information.
- [ ] Git tag and GitHub Release use the same semantic version.

## Dependency and legal review

Review NuGet vulnerability output and every third-party license before release. QuickPods includes its own `LICENSE`, `ThirdPartyNotices.txt`, and the .NET license/notices in self-contained payloads. WiX is a build-time dependency; maintainers must review the toolchain's current terms independently.
