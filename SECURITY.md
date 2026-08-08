# Security policy

## Supported versions

Security fixes are applied to the latest release and the current `main` branch. Pre-release and historical test artifacts are not supported after a newer build is available.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability.

Use GitHub's private vulnerability reporting feature on the repository's **Security** tab. If private reporting is unavailable, contact the repository owner through the public GitHub profile without including exploit details, credentials, or private device identifiers in the initial message.

Please include:

- the affected commit or release;
- Windows version and architecture;
- a concise impact description;
- reproducible steps or a minimal proof of concept;
- whether the issue requires local access, a paired device, or elevated privileges;
- sanitized logs only.

You should receive an acknowledgement when the report has been reviewed. Public disclosure and credit will be coordinated after a fix or mitigation is available.

## Sensitive diagnostics

QuickPods diagnostics are designed to omit raw Bluetooth addresses, Container IDs, Endpoint IDs, PnP IDs, and account names. Reporters must still review every attachment and screenshot before sharing it. Never upload signing certificates, private keys, environment files, memory dumps, or registry exports containing unrelated user data.
