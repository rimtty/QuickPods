# Validation evidence

Store reproducible technical-spike, integration, hardware, performance, and release evidence under this directory.

Each validation record should identify:

- branch and commit SHA
- date and operator
- Windows build and architecture
- .NET SDK version
- relevant hardware and driver versions
- exact commands or manual steps
- expected and observed results
- measurements and pass/fail decision
- linked GitHub issue or pull request

Do not store credentials, Bluetooth MAC addresses, full PnP IDs, full endpoint IDs, account information, or other private identifiers. Hash identifiers or retain only a short suffix when correlation is required.

Phase 0 evidence will use this structure:

```text
phase-0/
├─ decision.md
├─ core-audio/
├─ bluetooth-ks/
├─ default-endpoint-policy/
└─ taskbar-host/
```

Product-phase evidence follows the implemented slice:

```text
phase-2/
└─ audio-mvp/
   └─ test-results.md
phase-5a/
└─ test-results.md
phase-5b/
└─ test-results.md
```
