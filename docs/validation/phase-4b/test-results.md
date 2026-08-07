# Phase 4B 検証結果

## Coreオーケストレーション

初回実装ではOS mutation portをfakeへ置換し、次を確認する。

- 接続確認後の既定出力失敗を`ConnectedNotDefault`として保持し、接続を再試行しない。
- 操作送信後に選択が変わった結果を`Superseded`とし、別機器へ既定出力要求を送らない。
- OS要求直前に選択が変わった場合はBluetooth要求を0件にする。
- `SettingsOnly`機器へ変更要求を送らない。
- 同時操作をグローバルに直列化する。

Foundation focused testsは66件でPassしている。追加したWindows binding試験では、最新generationだけを公開して古いgenerationの操作targetを解決しないこと、および同じKS候補を複数Containerが参照した場合に関係機器を所有権不明へ縮退させることを確認した。worker protocol試験は認証済みread-only request、mutation capability拒否、応答相関拒否を対象とする。製品の固定名ゲートは、非同期の複合操作が完了するまで所有権を保持し、その間の第二要求を実行しない回帰試験を追加した。

Release solutionは警告0／エラー0でビルド成功した。全ソリューション回帰は366件（Foundation 66、Bluetooth KS 65、Core Audio 44、Default Endpoint Policy 7、Taskbar Host 183、Smoke 1）がすべてPassした。Release App出力にworkerのexe／dll／deps／runtimeconfigが配置されることを確認した。workerをPowerShellから直接起動した非正規親テストは、KS要求を読む前にparent validationで終了コード2となった。

既定出力変更が部分成功となった場合の回復経路として、Windows境界の設定ランチャーと現行画面の「サウンド設定を開く」導線を追加した。Bluetooth設定URIも同じ差し替え可能な境界に保持し、Phase 5の機器別非対応表示から利用できる。

## RDP安全縮退

2026-08-06に現在の製品ポートを一時プローブから直接呼び、次を確認した。

```text
RemoteSession=True
CatalogDevices=0
Connect Submitted=False Failure=OwnershipUnknown State=Unknown
Default Submitted=False Failure=OwnershipUnknown State=Failed Capability=VerificationUnavailable
```

この結果は、RDP中にBluetooth KS要求とPolicyConfig書き込みを送信しない安全境界だけを示す。物理デバイス列挙は[Issue #38](https://github.com/rimtty/QuickPods/issues/38)で追跡し、物理KS mutationと既定出力変更は別のローカルコンソールGateで扱う。DPI、解像度、描画、Floating、クリック、ホイール試験は現在のRDP環境では実施しない。

## local-console予備確認（2026-08-07）

`visual.71`とAirPods Proを使い、製品UIから接続、既定出力化、切断が成功した。JSONLには`Connecting → SettingDefault → Succeeded`と`Disconnecting → Succeeded`が記録され、Windows設定でも接続／切断状態を操作者が確認した。複数の予備試行は成功したが、現行UIで定めた反復数、選択外Bluetooth機器への影響0、外部操作後の状態追従を同じrunで確認していないためIssue #41の合格証拠にはまだ使用しない。

操作中にListBox全体をWPFのdisabled状態へ移したため白い選択面が現れる問題を`visual.72`で修正した。行の描画状態は維持して入力だけを抑止し、対象行と主操作ボタンへシアンの回転indicatorを表示する。Windows側の外部接続／切断はCore Audio topology通知と表示直前更新の二経路でカタログへ反映する。処理中表示と外部状態追従の目視確認後、Issue #41の接続／既定化／切断反復Gateへ進む。
