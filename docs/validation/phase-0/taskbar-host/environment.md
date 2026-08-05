# Phase 0C 検証環境

## 自動取得対象

検証時に、個人情報やウィンドウタイトルを保存せず次を記録する。

- Windows edition、version、OS build
- プロセスarchitectureとintegrity（管理者権限を使用しない）
- .NET SDK／Runtime
- プライマリタスクバーの寸法、DPI、向き
- Start／WidgetsのAutomation ID有無とサニタイズ済み矩形
- Explorer世代は実行中比較だけに使用し、生PIDは証跡へ保存しない
- 検証コミットSHA

## 現在値

| 項目 | 値 |
|---|---|
| 検証日 | 2026-08-05 |
| ブランチ | `codex/phase-0c-taskbar-host-spike` |
| 検証対象 | 本ブランチのPhase 0C作業ツリー（コミット前検証） |
| Windows | Windows 11 Pro 10.0.26200（build 26200） |
| OS／プロセスarchitecture | x64／x64 |
| integrity | 非管理者（elevated=false） |
| .NET SDK | 10.0.302 |
| Windows Desktop Runtime | 10.0.10 |
| プライマリタスクバー | 3840×72 physical px、horizontal、DPI 144（150%） |
| Start | 一意に取得、相対矩形 `(1091, 0, 68, 72)` |
| Widgets | 一意に取得、相対矩形 `(9, 0, 228, 72)` |
| UIA button数 | 30 |
| native critical child観測数 | 5（既知構造要素を含む。未知の可視要素は障害物化） |
| Compact配置 | 相対矩形 `(249, 6, 299, 60)` |
| 初回探索時間 | Child 102.439 ms／Popup 98.565 ms（各30秒live runの初回scan） |

矩形はタスクバー左上を原点とする相対physical pxだけを記録した。生HWND、Explorer PID、ウィンドウタイトル、通知内容、一般ボタンのAutomation ID、外部要素のclass名は保存していない。UIA無効化の記録もevent kindと`Owned`／`External`／`Unknown`分類だけに限定した。別の可視タスクバー要素を検出したため、これを未知障害物として安全領域から除外した。実画面スクリーンショットは保存していない。
