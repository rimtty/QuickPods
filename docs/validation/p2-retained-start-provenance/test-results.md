# P2 retained Start provenance 検証結果

## 対象

2026-08-07、`codex/p2-retained-start-provenance`（最新検証base `2294b80`）で Issue [#15](https://github.com/rimtty/QuickPods/issues/15) の保持した Start landmark の証拠化、verified factoryの出所検証、順序付きcontinuity attemptのfail-closed境界を実装した。

- 完全観測済み `TaskbarContinuityAnchor` からだけ private proof を生成する
- `DirectExpected`、同一taskbar geometry、`StartButtonMissing`だけの不完全観測、fresh Start不在を生成時に要求する
- HWND、Explorer PID、DPI、monitor／work-area、taskbar bounds、合成後button列をproof内だけに保持する
- verified factoryでproofの有無、route、Explorer generation、geometry、button列の完全一致を再検証する
- 診断ログへは限定されたautomation originだけを記録し、生の識別子や座標を出さない
- fresh complete観測だけが次のcontinuity anchorを更新できる
- scan前後のUIA generation、invalidation generation、native／automation／floating invalidation、continuity再要求を一つのfenceで比較する
- retained Startまたはretained notification areaを使うscanでは、fresh障害物と直前の完全観測をunionし、片方だけに存在する障害物も捨てない
- fresh完全観測だけはobsoleteな保守履歴を破棄し、新しいanchorと障害物集合へ置き換える
- Settings／Display／DPIを含むscan中invalidationは、結果を待たずnative surfaceを即時hideしてscanをcancelする
- discovery結果、障害物安全性、identity／bounds、native attachment、race fenceを`TaskbarContinuityAttemptPolicy`へ集約し、Runnerが同じ決定境界を実使用する

自動化可能なIssue #15の契約は完了した。Startを開いたままtaskbar／Search／Widgetsレイアウトを変更する物理negative Gateだけは同Issueに残す。

## 自動検証

過剰なテスト増加を避け、既存のretained Start／factory／evidence policyテストを証拠オブジェクトの検証へ置き換えた。テスト実行数は増やしていない。

2026-08-07の結果：

- `dotnet test tests/QuickPods.Spike.TaskbarHost.Tests/QuickPods.Spike.TaskbarHost.Tests.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true`：183／183 Pass
- `dotnet build QuickPods.sln -c Release --no-restore -warnaserror`：警告0、error 0
- `dotnet test QuickPods.sln -c Release --no-build --no-restore`：398／398 Pass
  - Smoke 1
  - Default Endpoint Policy 7
  - Core Audio 44
  - Foundation 98
  - Taskbar Host 183
  - Bluetooth KS 65
- `dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn --verbosity minimal`：差分なし
- `git diff --check`：問題なし
- .NET SDK：10.0.302

テスト実行数は183件のまま増やしていない。既存のadapter／watchdogテストへ代表シナリオを統合し、retained Start＋notification area、過去障害物、新規障害物、fresh完全復旧、watcher generation race、Settings／Display／DPI in-flight invalidationを検証する。非Direct route、追加fault、duplicate、timeout、property／bounds failure、route変化は既存のcontinuity discovery契約で検証する。

全体回帰は今回変更したTaskbar Host projectをReleaseで再buildした後の`--no-build`実行である。PR CIではsolution全体をclean buildして再検証する。

## 判定境界

自動検証は、不正な呼び出し元が任意のStart snapshotやbooleanを渡してverified結果を成立させられず、順序付きscanの途中状態がvisible継続へ入れないコード境界を確認する。Explorer切替中の実際のタイミング、Start/Search表示中の合成描画、物理taskbarでのnegative caseの合否には代用しない。
