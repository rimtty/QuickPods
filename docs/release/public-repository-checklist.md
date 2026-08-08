# Public repository checklist

Use this checklist immediately before changing the GitHub repository from private to public. The visibility change is a separate maintainer action and is not implied by completing the code changes in a pull request.

## Repository content

- [ ] `./build/Test-RepositoryPublicReadiness.ps1` passes on the final commit.
- [ ] The complete Git history has been reviewed for credentials, personal data, private device identifiers, and private file paths.
- [ ] The approved commit author identity is intentional; no unrelated author email addresses remain.
- [ ] Generated binaries, logs, screenshots, test results, certificates, and local configuration files are not tracked.
- [ ] `README.md`, `README.ja.md`, `LICENSE`, `SECURITY.md`, `SUPPORT.md`, `CONTRIBUTING.md`, and `CHANGELOG.md` describe the current project.
- [ ] All local links in Markdown documents resolve.

## GitHub settings

- [ ] Repository description, website, social preview, and topics are suitable for a public audience.
- [ ] Default branch and branch protection or rulesets are configured for `main`.
- [ ] Required CI checks are enabled before merge.
- [ ] Collaborator, team, deploy-key, GitHub App, and environment permissions have been reviewed.
- [ ] Actions permissions and approval requirements for workflows from forks are appropriate.
- [ ] Dependabot alerts, security updates, secret scanning, and push protection are enabled where available.
- [ ] Private vulnerability reporting is enabled so `SECURITY.md` has a private reporting path.
- [ ] No code-signing secrets or certificate material are stored in the repository or unprotected Actions configuration.
- [ ] Old branches, releases, tags, issues, pull requests, discussions, wiki pages, attachments, and project boards have been reviewed for publishable content.
- [ ] Issue forms and support links work after the visibility change.

## First public release

- [ ] The release version, tag, changelog entry, package manifests, and release title agree.
- [ ] Public downloads are checksummed, clearly distinguish stable and prerelease builds, and disclose their signing state.
- [ ] Compatibility and known limitations are linked from the release notes.
- [ ] Upgrade and uninstall behavior has been tested from the previous supported version.
- [ ] A maintainer has explicitly approved the final visibility change and release publication.
