# P2 retained Start provenance 検証結果

## 対象

2026-08-06、`codex/p2-retained-start-provenance`（base `bca36c4`）で Issue [#15](https://github.com/rimtty/QuickPods/issues/15) のうち、保持した Start landmark の証拠化と verified factory の出所検証を実装した。

- 完全観測済み `TaskbarContinuityAnchor` からだけ private proof を生成する
- `DirectExpected`、同一taskbar geometry、`StartButtonMissing`だけの不完全観測、fresh Start不在を生成時に要求する
- HWND、Explorer PID、DPI、monitor／work-area、taskbar bounds、合成後button列をproof内だけに保持する
- verified factoryでproofの有無、route、Explorer generation、geometry、button列の完全一致を再検証する
- 診断ログへは限定されたautomation originだけを記録し、生の識別子や座標を出さない
- fresh complete観測だけが次のcontinuity anchorを更新できる

この変更は Issue #15 全体の完了ではない。deterministic obstacle/race matrix、runner seam、および物理環境でのnegative verificationは同Issueに残す。

## 自動検証

過剰なテスト増加を避け、既存のretained Start／factory／evidence policyテストを証拠オブジェクトの検証へ置き換えた。テスト実行数は増やしていない。

2026-08-06の結果：

- `dotnet test tests/QuickPods.Spike.TaskbarHost.Tests/QuickPods.Spike.TaskbarHost.Tests.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true`：183／183 Pass
- `dotnet test QuickPods.sln -c Release --no-build --no-restore -- RunConfiguration.TreatNoTestsAsError=true`：377／377 Pass
  - Smoke 1
  - Default Endpoint Policy 7
  - Core Audio 44
  - Foundation 77
  - Taskbar Host 183
  - Bluetooth KS 65
- `dotnet format QuickPods.sln --verify-no-changes --no-restore --severity warn --verbosity minimal`：差分なし
- `git diff --check`：問題なし
- .NET SDK：10.0.302

全体回帰は今回変更したTaskbar Host projectをReleaseで再buildした後の`--no-build`実行である。PR CIではsolution全体をclean buildして再検証する。

## 判定境界

自動検証は、不正な呼び出し元が任意のStart snapshotやbooleanを渡してverified結果を成立させられないコード境界を確認する。Explorer切替中の実際のタイミング、Start/Search表示中の合成描画、物理taskbarでのnegative caseの合否には代用しない。
