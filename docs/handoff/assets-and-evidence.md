# Assets and evidence index

## Authoritative UI references

| Asset | Role |
|---|---|
| [`QuickPods UI Mockup v2.png`](../QuickPods%20UI%20Mockup%20v2.png) | primary Bluetooth selector, volume, action, and taskbar-strip visual reference |
| [`QuickPods UI Mockup v2.md`](../QuickPods%20UI%20Mockup%20v2.md) | states, exact Japanese labels, interaction semantics, and fallback behavior |
| [`QuickPods_Bluetoothオーディオ選択_機能仕様.md`](../QuickPods_Bluetoothオーディオ選択_機能仕様.md) | product behavior and safety contract |
| [`QuickPods UI Mockup v1.png`](../QuickPods%20UI%20Mockup%20v1.png) | historical AirPods-specific concept; not the current Bluetooth-list source |
| [`QuickPods UI Mockup v1.md`](../QuickPods%20UI%20Mockup%20v1.md) | historical prompt and early taskbar concept |

The shipped UI has additional accepted refinements recorded in Phase 5C: separate Sound/Bluetooth settings links, independent tray settings window, Windows glyphs, topmost no-focus flyout, spinner states, live external-state refresh, and DPI-correct taskbar anchoring. Do not regress these merely to match an older bitmap literally.

## Branding assets

The adopted product icon is v2:

| Path | Purpose |
|---|---|
| [`quickpods-icon-v2.png`](../assets/branding/quickpods-icon-v2.png) | final 1024×1024 RGBA reference |
| [`quickpods-icon-v2.ico`](../assets/branding/quickpods-icon-v2.ico) | adopted multi-resolution Windows application icon |
| [`quickpods-icon-v2-alpha-master.png`](../assets/branding/quickpods-icon-v2-alpha-master.png) | transparent master |
| [`quickpods-icon-v2-source.png`](../assets/branding/quickpods-icon-v2-source.png) | original generation/source image |
| [`quickpods-icon-v2-size-preview.png`](../assets/branding/quickpods-icon-v2-size-preview.png) | small-size legibility review |
| [`quickpods-icon-v2-prompt.md`](../assets/branding/quickpods-icon-v2-prompt.md) | design intent and export inventory |

`src/QuickPods.App/QuickPods.App.csproj` uses `docs/assets/branding/quickpods-icon-v2.ico` as `ApplicationIcon`. The non-v2 files in the same directory are retained as historical variants and must not replace v2 accidentally.

## Plans and architecture

- [`QuickPods_実装計画書.md`](../QuickPods_実装計画書.md) — full product plan and AC-001–027.
- [`QuickPods_ブランチ別実装ロードマップ.md`](../QuickPods_ブランチ別実装ロードマップ.md) — branch/phase execution history and current Gate state.
- [`architecture/`](../architecture/) — implemented phase boundaries and ADR-0001.
- [`release/`](../release/) — compatibility, known limitations, update, and uninstall policy.
- [`installer/README.md`](../../installer/README.md) — WiX build and package details.

## Validation evidence map

| Area | Evidence |
|---|---|
| Core Audio and default endpoint | [`phase-0/core-audio`](../validation/phase-0/core-audio/), [`phase-0/default-endpoint-policy`](../validation/phase-0/default-endpoint-policy/), [`phase-2/audio-mvp`](../validation/phase-2/audio-mvp/) |
| Taskbar feasibility and safety | [`phase-0/taskbar-host`](../validation/phase-0/taskbar-host/), [`phase-3a/display-host`](../validation/phase-3a/display-host/), [`phase-3b/ipc-recovery`](../validation/phase-3b/ipc-recovery/) |
| Bluetooth catalog and operations | [`phase-4a`](../validation/phase-4a/), [`phase-4b`](../validation/phase-4b/) |
| Tray, settings, DPI, lifecycle | [`phase-5a`](../validation/phase-5a/), [`phase-5b`](../validation/phase-5b/), [`phase-5c`](../validation/phase-5c/) |
| Retained Start hardening | [`p2-retained-start-provenance`](../validation/p2-retained-start-provenance/) |
| RC, resources, Observer lifecycle | [`phase-6a`](../validation/phase-6a/) |
| MSI and distribution | [`phase-6b`](../validation/phase-6b/) |
| Final acceptance state | [`release-0.1.0/completion-audit.md`](../validation/release-0.1.0/completion-audit.md) |

`docs/validation/README.md` is the general evidence entry point.

## Screenshot policy

Tracked mockups, branding masters, SVG layout evidence, and the sanitized Windows Sound screenshot are sufficient to reproduce design and historical decisions. Temporary `codex-clipboard-*` screenshots were iterative review evidence, not independent source assets; their accepted outcomes are recorded in Phase 4–6 validation documents. Do not add temporary screenshots to Git unless they prove a still-open Gate and contain no private identifiers.

New visual evidence should record the exact commit/candidate, Windows build, resolution, scaling, taskbar alignment, local-versus-RDP session, operation performed, and sanitized result.
