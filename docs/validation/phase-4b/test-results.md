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

`visual.72`の目視では白い無効面と外部状態追従は解消した一方、Boolean値を汎用`Tag` triggerで比較したため回転indicatorがCollapsedのままとなる不具合を検出した。`visual.73`ではBooleanを直接visibilityへ変換し、読み込み時にrepeat storyboardを開始する。また、接続処理はStereo Render endpointのActiveだけを成立条件としCapture／マイクを待っていないことを再確認した。一覧生成時に同じinventory generationでDirectControlを判定済みにもかかわらず、操作直前に全KS Basic Support probeを重複実行していたためこれを除去した。adapter所有権、実mutation worker、Stereo Active観測、Console／Multimedia既定出力の通知とread-backは維持する。

`visual.73`の実機ログでは接続2回が13.936秒／14.401秒、切断2回が5.646秒／5.555秒ですべて成功した。接続中の最初のCore Audio topology通知は開始から2.226秒／7.103秒だった一方、Stereo Render Active後の`SettingDefault → Succeeded`は0.053秒／0.020秒だった。重複probe除去後も接続全体は短縮せず、WindowsがAirPodsのStereo Render endpointをActiveにする区間が支配的である。QuickPodsの固定待機やマイク取得待ちを短縮して解決できる遅延ではなく、Stereoが利用可能になる前に既定出力成功を返すことも行わない。

同じ`visual.73`で、操作者が接続中／切断中の対象行と主操作ボタンにシアンのindicatorが回転表示されることを目視確認した。白い無効面を再発させず、状態文字列とanimationを同時に表示できたため処理中表示の視覚Gateは合格とする。Issue #41は所定の接続5回／切断5回、選択外機器への影響0、idempotent操作、adapter／driver情報を同一証跡へ揃えるまでopenのまま維持する。

`visual.75`／`visual.76`の追加試行を含め、15秒以内に完了した接続成功は13.936秒、14.401秒、12.402秒、13.396秒、3.895秒の5回となった。切断成功は5回を超え、いずれも約5.5～5.9秒で5秒の安定windowを満たした。AirPodsがWindowsから切断された直後にペアリング済みiPhoneへ接続するmulti-host環境では、接続観測が15.122秒で2回timeoutした。試験的に接続期限だけ18秒へ延長しても18.136秒でtimeoutし、直後の手動retryが3.895秒で成功したため、固定期限延長は待ち時間だけを増やし根因を解消しないと判断して不採用とした。製品sourceは15秒、one-shot、自動retryなしを維持する。

同じendpointがすでにActiveの状態で受けたConnect要求は、preflightから0.028秒で既定状態へ収束し、Bluetooth reconnect mutationを送らないidempotent経路を実機で通過した。Windows 11 Pro build 26200、MediaTek Bluetooth Adapter driver 1.1147.0.610、MediaTek Bluetooth Audio Device driver 1.6.0.48をsanitized環境証跡として記録した。multi-host timeout後の案内と遅延状態収束は[Issue #65](https://github.com/rimtty/QuickPods/issues/65)で追跡する。Issue #41は、選択外Bluetooth機器への影響0の操作者確認と、disconnected状態へのidempotent Disconnectを製品境界で確認するまでopenのまま維持する。
