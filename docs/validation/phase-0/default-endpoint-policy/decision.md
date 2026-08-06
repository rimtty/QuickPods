# Gate A2 — 既定出力設定判断記録

## 現在の判断

| 項目 | 値 |
|---|---|
| Status | **Go** |
| 対象 | GitHub Issue #21 |
| 対象ブランチ | `codex/phase-0d-default-endpoint-policy-spike` |
| 対象環境 | Windows 11 Pro 25H2 build `26200.8973`、x64、通常権限 |
| 判断 | `IPolicyConfig::SetDefaultEndpoint`を隔離されたWindows互換性境界として採用する |
| 判断日 | 2026-08-06 |
| 判断者 | 実機確認：ユーザー、証跡監査：Codex |

対象ContainerのActive／Render／Stereo Endpointを一意に解決し、Console／Multimediaだけを変更する方式は対象環境で成立した。製品実装は書き込み受理だけを成功にせず、事前に購読した`IMMNotificationClient`通知と`GetDefaultAudioEndpoint`再取得の一致を必須とする。Communicationsは読み取り専用とし、変更を検出した場合は完全成功にしない。

このAPIは一般デスクトップアプリ向けに公開文書化された設定APIではない。COM宣言、CLSIDs、HRESULT変換を`IDefaultAudioEndpointPolicy`配下へ隔離し、生成失敗、呼び出し拒否、通知不一致、対象消失、OS更新による非互換を製品状態として扱う。利用不能時もBluetooth接続は維持し、`接続済み・非既定`と`ms-settings:sound`導線へ縮退する。

## Gate証跡

| 要件 | 証跡 | 結果 |
|---|---|---|
| 通常権限で動作 | 非昇格プロセスでinventory、probe、実変更を実施 | Pass |
| 選択ContainerのステレオEndpointだけを対象 | 接続済みAirPodsをform factor 3／Stereo／Activeとして一意解決 | Pass |
| Console／Multimediaの変更 | AirPods→単一Activeスピーカー→AirPodsの1往復で両ロールの再取得が一致 | Pass |
| 通知と再取得の一致 | 両方向でConsole通知とConsole／Multimedia読戻しを確認 | Pass |
| Capture／Communications非変更 | 一時切替中もCommunicationsはAirPods、復元後も3ロール整合。Captureは候補外 | Pass |
| 既に既定 | 現在のConsole／Multimedia Endpointへ適用し、`AlreadyDefault`、書込み0、通知待機0、読戻し一致 | Pass |
| 途中失敗／対象消失／古い世代 | 部分適用、通知不一致、対象非Active、世代更新を完全成功にしない決定論的試験 | Pass（自動） |
| 生識別子を保存しない | 実行ごとのHMAC aliasだけを表示し、Endpoint／Container／PnP／MACの生値を記録しない | Pass |
| 品質Gate | DefaultEndpointPolicy 7件を含む全300件、Release build、format、CI | Pass |

## 実機経過

- 接続済みAirPodsはWindows自身によりConsole／Multimedia／Communicationsの既定出力になった。
- QuickPodsのガード付き`apply`で単一Activeスピーカーへ一時切替し、Console／Multimediaだけが対象へ移り、CommunicationsはAirPodsのまま維持された。
- 同じ経路でAirPodsへ復元し、Console／Multimedia／Communicationsの3ロールがAirPodsへ揃った。
- AirPods切断後、Windowsが選択した現在のConsole／Multimedia Endpointへ同じ`apply`を実行し、`AlreadyDefault`かつ`accepted-mutation=false`となった。

## 製品実装の必須条件

1. 接続確認済みの同じ物理Containerから、Active／Render／Stereoの一意候補だけを受理する。
2. Hands-Free、Capture、非Active、0件、複数件を対象にしない。
3. 通知購読を各書き込み前に確立し、Console／Multimediaの再取得一致まで成功表示しない。
4. Communicationsは書き込まず、操作前後の変化を監視する。
5. 既定済みなら書き込みも通知待機も行わない。
6. 部分適用とBluetooth接続成功を別状態として保持し、サウンド設定への回復導線を提供する。
7. OSビルド更新後は互換性境界のactivation／readbackを再確認し、失敗時に自動で縮退する。

## 残存リスクと後続

- 非公開COM境界の別Windowsビルド互換性はPhase 6の互換性マトリクスで確認する。
- A→B→Aの長時間反復は機能成立性Gateから外し、Phase 6のリソース／耐久試験へ統合する。
- 実機で対象を操作途中に物理消失させる試験は、決定論的な対象消失・部分適用試験を根拠にGateを阻害しない。Phase 4統合後、UIの部分状態と設定導線を含めて確認する。
- Gate A2のGoはBluetooth KS方式のGate A判断を代替しない。
