# Phase 6A 検証結果

## RC発行

2026-08-06、`build/Publish-ReleaseCandidate.ps1 -OutputDirectory artifacts/rc-local -Version 0.1.0-rc.local`を同じcheckoutで二回実行した。

- RID：`win-x64`
- self-contained：true
- payload：492 files（内部manifestを含む）
- PDB：0 files
- ZIP：83,548,544 bytes
- 1回目SHA-256：`e92684275a251968f3b072bbe98974b7a59dc7323bef79bfb7e23be927a80f5b`
- 2回目SHA-256：`e92684275a251968f3b072bbe98974b7a59dc7323bef79bfb7e23be927a80f5b`
- 必須exe：本体、TaskbarHost、TaskbarObserver、BluetoothWorkerすべて存在
- `artifact-manifest.json`のfile数と実payloadが一致

このhashはversion文字列`0.1.0-rc.local`のlocal検証成果物に対する値であり、CI run番号を含むRCとは異なる。再現性は同じsource／SDK／versionで判定する。

## 自動回帰

Phase 6Aは既存の376件へ重複testを追加しない。RC生成は必須file、manifest、deterministic archive、checksumの生成失敗をscript／CIのexit codeで検出する。通常の自動回帰baselineはPhase 5Bのcoverage付き376／376 Pass、Release build警告0／error 0である。PR CIではこれらの後にRC artifactを発行する。

## 自己完結RCのprocess／resource tooling smoke

RDP中にRCの`QuickPods.exe --background`を起動し、3秒間、1秒間隔で計測した。

- samples：3
- primary生存：true
- max process count：3
- max summed Working Set：214,319,104 bytes
- max summed Private Memory：63,623,168 bytes
- max handles：1,300
- max GDI objects：32
- max USER objects：47
- cleanup後のRC process：0

この結果はself-contained RCが起動し、resource CSVが取得できることだけを示す。期間が短くRDPでもあるため、memory目標、leak、idle CPUの合否は出さない。

## 2026-08-07 Gate D計測器のfail-closed修正

24時間Gate準備監査で、指定した本体processが期間途中に終了しても、1 sample以上のCSVがあれば計測scriptが成功終了する欠陥を検出した。また、supervisor配下のchild processがCIM snapshot直後に正常終了すると、metric取得raceで全計測を中断する可能性があった。

`Measure-QuickPodsResources.ps1`は本体PIDと開始時刻を固定し、早期終了またはPID再利用をGate失敗とする。本体以外のchildがsnapshotとmetric取得の間に終了した場合だけそのsampleから除外し、次sampleでreplacementを再発見する。途中CSVは診断用に保持するが、要求期間を完走していない実行を成功とは返さない。

focused smokeは次のとおり。

- PowerShell parse：error 0
- 10秒生存するdummy processを2秒計測：2 samples、成功
- 600msで終了するdummy processを5秒計測：`exited before the requested duration completed`でfail closed
- 稼働中の自己完結rc.62を3秒計測：3 samples、process count 2、本体継続
- `git diff --check`：成功

これは計測器の正否だけを確認した短時間試験であり、24時間resource GateとExplorer反復の合否には使用しない。

## Gate D CSV解析の再現性

24時間計測の完走後に人手だけでresource推移を集計すると、同じCSVでも判断根拠を再現しにくいため、Issue [#75](https://github.com/rimtty/QuickPods/issues/75)で`build/Summarize-QuickPodsResources.ps1`を追加した。解析器は読み取り専用であり、QuickPods、Bluetooth、Windows設定、計測中のCSVを変更しない。

解析器は次を行う。

- schema、0起点の連続sample番号、単調増加timestamp、非負metric、観測時間、最大sample gapを検証する
- CPU、Working Set、Private Memory、Handle、GDI、USERとprocess countの範囲を集計する
- 単発peakやGCだけをleakと扱わないよう、先頭／末尾10%のmedian差と全期間の最小二乗slopeを併記する
- JSONとMarkdownを同時に生成する
- capture integrityとperformance目標を分離し、AC-025を自動Passにはしない

安定series、意図的に増加するseries、短縮series、sample番号を壊したseriesでfocused smokeを行った。結果は次のとおり。

- 25時間の安定series：growth signalなし、final capture integrity Pass
- 25時間の増加series：Private Memory、Working Set、Handle、GDI、USERをsignalとして検出
- 45分の短縮series：要求時間不足としてfail closed
- sample番号欠落series：連続性違反としてfail closed
- Windows PowerShell 5.1：安定seriesを正常解析
- 進行中の`visual.84` CSV：`-Preview`で解析でき、capture integrityはPreview、AC-025はReviewRequiredを維持

growth signalはreview補助であり、自動的なleak判定ではない。共有self-contained .NET pageをprocessごとに重複計上し得るWorking Setは単純合算の絶対値だけで判定せず、時間推移とPrivate Memoryを併記する。Bluetooth 50-cycle耐久は、既存の実機機能証拠を十分とする2026-08-07の製品判断により追加Gateから除外した。

## 2026-08-07 Gate D短縮エージング判定

利用者判断により24時間から短縮し、local-console `visual.84`を2.01時間、1,391 sample観測した。公式samplerのstderrは空で、QuickPods App、TaskbarHost、Observerの監督対象treeを追跡した。AppとTaskbarHostは全期間同一processとして継続し、TaskbarObserverだけが一度世代交代した。Explorerの再起動およびWindows Application log上のApplication Error、.NET Runtime、Windows Error Reporting、Application Hangは確認されなかった。元のQuickPods JSONLにもWarning／Errorはなかった。

開始時の合算値はPrivate Memory 101.6 MB、Working Set 248.5 MB、Handle 1,278、GDI 32、USER 48だった。Observer世代交代直前はPrivate Memory 121.66 MB、Working Set約273 MB、Handle 1,316、GDI 32、USER 50だった。その後のBluetooth catalog更新／UI表示を含む区間で段階的なallocation増加があり、終盤はPrivate Memory約252.67 MB、Working Set約384.42 MB、Handle 1,611、GDI 49、USER 58だった。5分medianはPrivate Memory 227.93→242.02→246.05 MB、Working Set 361.62→374.15→378.60 MB、Handle 1,594→1,598→1,597、GDI 49、USER 59→58となった。

この短いseriesには段階的allocationと終盤の緩やかな増加があるため、24時間相当の「leakなし」を証明するものではない。一方、Handle／GDI／USERの継続増加、process treeの崩壊、App／Hostの再起動、stderr、OS crash記録、アプリWarning／Errorはなく、利用者が短縮範囲で重大なresource異常なしと判定した。Issue #48のresource観測はこの証跡で完了とする。

Observer世代交代の履歴原因は旧protocolでは記録されず断定不能だったため、Issue #77でprotocol version 3のsanitized lifecycle診断を追加した。`visual.85`では正常な`TaskbarCreated`がInformation／generation 0、Observer単体停止がWarningの`Disconnected`／generation 1として記録され、どちらもApp／TaskbarHostを維持してObserver一体へ回復した。詳細は[observer-lifecycle-diagnostics.md](observer-lifecycle-diagnostics.md)を参照する。
## 後続Gateの解決

- local-console表示／DPI／通常theme／real-login：[#43](https://github.com/rimtty/QuickPods/issues/43)で完了
- Explorer実再起動後の回復／generation resource：[#34](https://github.com/rimtty/QuickPods/issues/34)／[#18](https://github.com/rimtty/QuickPods/issues/18)で完了
- clean standard-user MSI lifecycle：[#50](https://github.com/rimtty/QuickPods/issues/50)で合格

Phase 6AのCIをこれらの代替にせず、各local-console証拠で判定した。High Contrast追加追試とコード署名はオーナー判断で現在の最終Gateから除外した。
