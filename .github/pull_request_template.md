## Summary

- What changed:
- Why it changed:
- User or maintainer impact:

## Related issue

Closes #

## Validation

- [ ] `./build/Test-RepositoryPublicReadiness.ps1`
- [ ] `dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn`
- [ ] `dotnet build QuickPods.sln -c Release --no-restore`
- [ ] `dotnet test QuickPods.sln -c Release --no-build --no-restore -- RunConfiguration.TreatNoTestsAsError=true`
- [ ] Hardware, DPI, Explorer, startup, or installer validation is attached when applicable

## Documentation and privacy

- [ ] User-visible and maintainer-facing documentation is updated
- [ ] Logs and screenshots were reviewed for credentials, account data, and raw device identifiers
- [ ] No generated artifacts, dumps, certificates, keys, or machine-specific files are included

## Remaining risk

- Known limitations:
- Follow-up work:
