# QuickPods 0.1.0 受入基準 completion audit

## 監査方針

この文書は`docs/QuickPods_実装計画書.md`のAC-001～AC-027を、現在の製品実装と検証証跡へ対応付ける。`Proven`は受入基準と同じ範囲の証拠がある場合だけ使用する。コード、単体試験、Spike、RDP smoke、静的MSI検査のいずれかだけでは、local-console／物理機器／長時間／配布の要件を代替しない。

状態の意味は次のとおり。

- `Proven`：現在の証拠で受入基準を満たす。
- `Partial / Gate pending`：実装または狭い証拠はあるが、同じ範囲の最終Gateが未完了。
- `Pending`：受入基準を直接検証する試験が未実施。

監査日時は2026-08-07。全体進捗は[Roadmap Issue #2](https://github.com/rimtty/QuickPods/issues/2)、本監査の更新は[Issue #57](https://github.com/rimtty/QuickPods/issues/57)で管理する。

## 判定一覧

| AC | 要件要約 | 状態 | 現在の直接証拠 | 残作業／所有Issue |
|---|---|---|---|---|
| AC-001 | 起動時の既定出力名・音量・ミュート | Proven | [Phase 2](../phase-2/audio-mvp/test-results.md)の起動読取とUI表示 | なし |
| AC-002 | 0／50／100%がWindowsと一致 | Proven | [Phase 2](../phase-2/audio-mvp/test-results.md)で独立Core Audio読取と一致 | なし |
| AC-003 | スピーカー操作でミュート切替 | Proven | [Phase 2](../phase-2/audio-mvp/test-results.md)のUI／独立読取、Phase 3Aのnative入力 | なし |
| AC-004 | Windows側変更へ自動追従 | Proven | [Phase 2](../phase-2/audio-mvp/test-results.md)の手動変更・音量キー回帰 | なし |
| AC-005 | 既定デバイス変更へ再起動なしで追従 | Proven | [Phase 2](../phase-2/audio-mvp/test-results.md)のLogitech→Dell→Logitech実往復 | なし |
| AC-006 | デバイスなし／無効化中に非クラッシュ | Proven | [Phase 2](../phase-2/audio-mvp/test-results.md)のbinding retire／再bind、Phase 5C空状態 | なし |
| AC-007 | Bluetoothを物理機器単位で一覧・単一選択 | Partial / Gate pending | [Phase 4A](../phase-4a/test-results.md)の0／1／複数／同名契約、RDPでは安全な空一覧 | 物理列挙とprofile集約：[Issue #38](https://github.com/rimtty/QuickPods/issues/38) |
| AC-008 | 選択・更新だけではOSを変更しない | Proven | [Phase 4A](../phase-4a/test-results.md)と[Phase 5A](../phase-5a/test-results.md)のread-only／fake mutation 0件 | なし |
| AC-009 | 接続・切断後にMMDevice実状態を確認 | Partial / Gate pending | [Phase 4B](../phase-4b/test-results.md)のorchestration／stale拒否。製品RDP経路は要求を安全拒否 | 製品版の物理接続・切断：[Issue #41](https://github.com/rimtty/QuickPods/issues/41) |
| AC-010 | 接続後にConsole／Multimedia既定出力を確認 | Partial / Gate pending | Phase 0 Gate A2と[Phase 4B](../phase-4b/test-results.md)のproduct orchestration／部分成功 | 製品版の物理既定出力化：[Issue #41](https://github.com/rimtty/QuickPods/issues/41) |
| AC-011 | 連打・選択変更・再列挙で古い結果を適用しない | Proven | [Phase 4B](../phase-4b/test-results.md)の固定名gate、直列化、Superseded、generation拒否 | なし |
| AC-012 | 未対応でも停止せず部分状態と代替導線 | Proven | [Phase 4B](../phase-4b/test-results.md)の`SettingsOnly`／`ConnectedNotDefault`／設定launcherとRDP安全縮退 | なし |
| AC-013 | 選択外Bluetooth機器へ影響しない | Partial / Gate pending | Phase 0 Spikeでは参照機器5/5と選択外影響0。製品契約はContainer所有権でfail closed | 製品版の選択外影響確認：[Issue #41](https://github.com/rimtty/QuickPods/issues/41) |
| AC-014 | 安全な空きへタスクバー内表示 | Proven | [Phase 3A](../phase-3a/display-host/test-results.md)のcenter-aligned Native実表示・入力 | なし |
| AC-015 | Windows標準要素を1pxも覆わない | Proven | [Phase 3A](../phase-3a/display-host/test-results.md)のUIA＋native obstacle統合、fail-closed placement | なし |
| AC-016 | 狭い空きでcompact／fallback | Partial / Gate pending | compact／NoFit routing自動契約は合格。左揃えは仕様どおりHidden | center-aligned NoFit Floating目視・入力：[Issue #33](https://github.com/rimtty/QuickPods/issues/33) |
| AC-017 | DPI 100／125／150／200%で表示・入力ずれなし | Partial / Gate pending | Phase 0／3Aの座標契約と過去実機証拠。現行UIのRDP結果は合否に不使用 | 現行製品のlocal-console DPI：[Issue #43](https://github.com/rimtty/QuickPods/issues/43) |
| AC-018 | Explorer再起動後10秒以内に1面だけ復旧 | Partial / Gate pending | Phase 0 Spikeは10/10、[Phase 3B](../phase-3b/ipc-recovery/test-results.md)はsynthetic `TaskbarCreated`とHost／Observer復旧、Phase 5CはStart 10秒超でnative面維持 | 製品版Explorer restart／resource：[Issue #34](https://github.com/rimtty/QuickPods/issues/34)、[Issue #48](https://github.com/rimtty/QuickPods/issues/48) |
| AC-019 | 終了後に残骸ウィンドウなし | Proven | [Phase 3A](../phase-3a/display-host/test-results.md)、[Phase 3B](../phase-3b/ipc-recovery/test-results.md)、[Phase 5A](../phase-5a/test-results.md)で残留0 | なし |
| AC-020 | native失敗時も音量・Bluetoothを利用可能 | Partial / Gate pending | [Phase 3B](../phase-3b/ipc-recovery/test-results.md)のHost失敗時App生存、通知領域／Hidden policy | center-aligned NoFitのoperator-visible fallback：[Issue #33](https://github.com/rimtty/QuickPods/issues/33) |
| AC-021 | Explorerをクラッシュ／ハングさせない | Partial / Gate pending | 別process境界、Phase 0の10 restart、Phase 3A／3Bのbounded live runs | 製品版Explorer反復と24時間観測：[Issue #34](https://github.com/rimtty/QuickPods/issues/34)、[Issue #48](https://github.com/rimtty/QuickPods/issues/48) |
| AC-022 | 管理者権限を要求しない | Partial / Gate pending | 通常権限のAudio／Bluetooth／policy実績、PR #51のper-user／non-elevated MSI静的検査 | clean standard-user MSI lifecycle：[Issue #50](https://github.com/rimtty/QuickPods/issues/50) |
| AC-023 | 設定を再起動後も保持 | Proven | [Phase 5A](../phase-5a/test-results.md)の全設定JSON round-trip、破損時はPhase 5Bで隔離再生成 | なし |
| AC-024 | 主要API失敗を分類済みログへ記録 | Proven | [Phase 5A](../phase-5a/test-results.md)のsanitized JSONL、[Phase 5B](../phase-5b/test-results.md)のlifecycle／interaction例外境界 | なし |
| AC-025 | 24時間で継続的resource増加なし | Pending | [Phase 6A](../phase-6a/test-results.md)は3秒tooling smokeのみで合否対象外 | 24時間Gate D：[Issue #48](https://github.com/rimtty/QuickPods/issues/48) |
| AC-026 | フライアウトの主要操作をkeyboardで実行 | Partial / Gate pending | [Phase 5B](../phase-5b/test-results.md)のAutomation metadata／keyboard確定経路、[Phase 5C](../phase-5c/test-results.md)の統合UI・目視合格 | local-console focus／keyboard／screen reader：[Issue #43](https://github.com/rimtty/QuickPods/issues/43) |
| AC-027 | Ceiling由来コードがあればMIT同梱 | Partial / Gate pending | PR #51で`ThirdPartyNotices.txt`と配布payload静的検査を実装 | Phase 6B統合・最終配布物確認：[Issue #50](https://github.com/rimtty/QuickPods/issues/50) |

## 集計

| 状態 | 件数 | AC |
|---|---:|---|
| Proven | 14 | 001～006、008、011、012、014、015、019、023、024 |
| Partial / Gate pending | 12 | 007、009、010、013、016～018、020～022、026、027 |
| Pending | 1 | 025 |

`14 / 27 Proven`は現時点の完成率を表す数値ではなく、同じ範囲の証拠が揃った受入項目数である。実装済み行を自動的に`Proven`へ昇格しない。

## 残作業の最小Gateセット

| Gate | 閉じるAC | 実施内容 |
|---|---|---|
| #38 | 007 | local-consoleで物理Bluetooth一覧、profile集約、単一選択 |
| #41 | 009、010、013 | 接続→実状態→既定出力、切断、選択外影響 |
| #33 | 016、020 | center-aligned NoFit Floatingのvisibility、z-order、input、cleanup |
| #34／#48 | 018、021 | 製品版Explorer restart、重複0、10秒以内、resource反復 |
| #43 | 017、026 | local-console DPI、focus、keyboard、High Contrast、tray settings |
| #48 | 025 | 24時間resource Gate D |
| #50／PR #51 | 022、027 | clean standard-user MSI lifecycle、法務payload、署名済み最終成果物 |

すべての行が`Proven`になるまでRoadmap #2のcompletion auditとRelease branch B16を完了扱いにしない。
