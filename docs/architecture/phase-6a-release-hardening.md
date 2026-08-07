# Phase 6A — Release Candidate品質基盤

## RC成果物

`build/Publish-ReleaseCandidate.ps1`は`QuickPods.App`、`TaskbarObserver`、`TaskbarHost`、`BluetoothWorker`をRelease／`win-x64`／self-containedで同じpayloadへ発行する。単一ファイル化とReadyToRunは使用せず、子processの実体とruntime configurationを検査可能なside-by-side構成に保つ。通常buildのlock fileはframework-dependent graphを固定しているため変更しない。自己完結runtime packは`global.json`で固定したSDKから、各projectのignored `obj/project.rc.packages.lock.json`へ分離して解決する。NuGet package版は中央の厳密なversion指定を使用する。

成果物には次を含む。

- `QuickPods.exe`と3種類の所有child executable
- self-contained .NET／WPF／Windows runtime files
- 全payload fileの相対path、byte数、SHA-256を持つ`artifact-manifest.json`
- entry時刻を2000-01-01 UTCへ固定し、path順に生成したdeterministic ZIP
- ZIPを検証する`SHA256SUMS.txt`

同じsource、SDK、versionから二回作成したZIPのSHA-256が一致することをRC生成試験とする。code signingとinstallerはPhase 6Bの責務であり、Phase 6A ZIPは署名されていないportable検証成果物である。

## CI

既存のlocked restore、format、Release build、coverage付き全回帰の後にRCを生成する。PRとmainの各runはZIP、checksum、内部manifestを14日間のGitHub Actions artifactとして保持する。RC生成失敗、必須exe欠落、manifest／archive生成失敗はCIを失敗させる。

## Resource計測

`build/Measure-QuickPodsResources.ps1`は指定した本体PIDと開始時刻を実行全体のidentityとして固定し、その時点で所有する全descendant processを一定間隔で集計する。本体の早期終了またはPID再利用は、途中までCSVが存在してもGate失敗とする。supervisor配下のchildがCIM snapshotとmetric取得の間に正常終了した場合だけそのsampleから除外し、replacementは次sampleで再発見する。CSVへUTC時刻、sample番号、process数、累積CPU秒、Working Set、Private Memory、handle、GDI object、USER objectだけを保存する。command line、path、window title、Bluetooth／audio identifierは収集しない。

短時間のtooling smokeは計測器の成立性しか示さない。resource傾向はIssue #48のlocal-console Gate Dで2.01時間、1,391 sampleを観測し、2026-08-07にリリース判断者が短縮範囲で重大な異常なしとして承認した。Explorer実再起動はIssue #34、sleep／RDP transitionはIssue #43で分離して確認する。Bluetooth 50-cycle耐久は、接続／切断／既定化／実音声／外部状態追従の既存実機証拠を十分とする同日の製品判断により追加Gateから除外した。fail-closed、選択外影響、実状態確認の製品契約は維持する。

## Gateの分離

- 実Bluetooth列挙：Issue #38
- 実Bluetooth接続／切断／既定出力：Issue #41
- DPI／High Contrast／sleep／RDP／monitor／trayのlocal-console確認：Issue #43
- Explorer実再起動後の回復：Issue #34

CI artifactの生成成功を、これらの物理受入れ結果へ読み替えない。
