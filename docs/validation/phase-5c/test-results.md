# Phase 5C UI fidelity 検証結果

## 2026-08-06 自動検証

- `QuickPods.App` Release win-x64 `-warnaserror` build：警告0、error 0
- focused Foundation回帰：77／77 Pass（既存test数を増やさず、protocol／rendering／native interactionの既存3 testへ必要なassertionだけ追加）
- 変更後sourceから全solutionを再buildした回帰：377／377 Pass（Smoke 1、Foundation 77、DefaultEndpointPolicy 7、CoreAudio 44、TaskbarHost 183、BluetoothKs 65）
- GitHub Actions PR #54 `Windows / .NET 10 / Release`：Pass
- hover session：同一enter世代でarm／previewが各1回、leave後だけ再arm
- protocol：taskbar anchorを含むpreview interactionのJSON往復
- rendering：0～100%正規化、未選択`BTデバイスなし`、選択名・状態のprojection

## 現環境での視覚確認

Releaseのbackground起動でTaskbarHostがnative面へ接続し、タスクバー面はspeaker、slider、`100%`、separator、修正後headphones／status dot、`BTデバイスなし`を表示した。空一覧flyoutはborderless rounded surface、Bluetooth empty-state、再検索、volume、disabled接続、Bluetooth設定導線を表示した。空の診断TextBlock 3個が確保していた不要な下部領域をcollapseし、header refreshをtransparent surfaceへ調整した。

background起動後、実pointerをnative面（`left=662, top=2083, right=1187, bottom=2153` physical px）の中央へ移動してhoverを発生させた。flyoutは`left=573, top=1096, right=1277, bottom=2076`で表示され、surface中心`924.5 px`に対するflyout中心は`925 px`（差`0.5 px`）だった。monitor中央fallbackへの上書きは発生しなかった。

pointerをtaskbar面からflyout内部へ移した後800 ms待っても表示を維持し、外へ移して700 ms後には非表示となった。App／TaskbarHostはいずれも応答状態を維持した。現在はRDPセッションであるため、この結果はhover protocolと相対配置の合格証跡に使い、DPI差の最終合否には使用しない。

ユーザーは2026-08-06に最終hover UIを目視し、slider、mute button、device status、全体の見た目を合格と判定した。同時に`QuickPods設定`展開時、初回位置のままWindowが下方向へ伸びてtaskbar背後へ入り、下部controlを操作できない問題を発見し、Issue [#60](https://github.com/rimtty/QuickPods/issues/60)へ記録した。

修正後は`SizeToContent`による高さ変更をレイアウト確定後に同じtaskbar anchorへ再配置する。175%環境の実測で、折り畳み時は`top=1096, bottom=2076, height=980`、展開後は`top=788, bottom=2076, height=1288` physical pxとなり、増加分308 pxを上方向だけへ拡張した。taskbar上端`2083`に対する下端余白は両状態とも7 pxである。内部scrollを最下部へ移動し、settings combo、checkbox、ログ、診断情報、version表示までtaskbarより上で表示できることを確認した。変更後のRelease buildは警告0／error 0、全回帰377／377 Pass、format verificationと`git diff --check`も成功した。

その後のユーザー再確認で、settingsはhover flyout内に置かず、通知領域アイコンの右クリックメニューから独立windowとして開く仕様へ変更した。flyoutからsettings expanderと全settings controlを削除し、tray menuへ`QuickPods 設定...`を追加した。独立windowは表示、theme、wheel刻み、既定device化、切断確認、自動起動、log、診断copyを引き継ぐ。

hover leaveの不定残留は、dismiss timerがflyout上で一度発火した後に停止し、後続の`MouseLeave`を取りこぼすと再監視されないことが原因だった。preview中はpointerがflyout内ならtimerを再armし、外ならhideするpolling policyへ変更した。policy testは既存の過剰test削減方針に従い1件だけ追加し、全回帰378／378 Pass、Release build警告0／error 0、format verification、`git diff --check`に成功した。

Bluetooth empty-state cardとvolume dividerの間は0 pxから18 pxへ拡張した。自己完結型の確認payload `0.1.0-visual.60`（495 files、SHA-256 `07b244a0845548f0c136108e8efe7b3e9e8271a1f0563ebb7657025ab0c608f9`）を起動し、App／TaskbarHost／TaskbarObserverの全processが同payloadから稼働していることを確認した。

## 2026-08-07 最終UI Gate

自己完結payload `0.1.0-rc.62`（495 files、SHA-256 `214767c21180bd7291b992ea887d6f915d0c42e9d9640b42e456a996299fb5eb`）で、通常起動時はflyoutを表示せずtray常駐し、taskbar面へのhover時だけ同面へanchorされたflyoutを表示することをユーザーが確認した。Startを10秒以上開いた状態でもtaskbar内QuickPodsは残り、Startを閉じた後も重複や遅延再生成を起こさなかった。

flyout外へpointerを移した際のdismiss、empty-state card下18 px余白、slider、mute、主button、境界線、全体の視覚をユーザーが合格と判定した。Windows Settings URIは`Process.Start`がprocess instanceを返さない正常なShell activationを成功として扱うよう修正し、物理pointerとWindow rectによるdismiss、tray-first startup、native continuity retentionをfocused contractへ追加した。

最終stackはRelease build警告0／error 0、全回帰380／380、format、dependency audit、self-contained RC、per-user MSI生成に合格した。PR #54 CIは成功し、2026-08-07にPhase 6BへSquash commit `cc6b99b`として統合した。

### タスクバー内Bluetooth状態表示（visual.74）

選択機器名の後ろへ状態を連結する従来表示では、狭いtaskbar面で末尾省略されると`未接続`だけが見えなくなる問題を修正した。未接続、利用不可、確認中、および処理中は状態を先頭へ固定し、未接続時は`未接続 · AirPods Pro`の順で表示する。通常の接続済み状態は機器名を主表示として維持する。headphones横のstatus dotは未接続時にtrack color、接続中／接続済み時にaccent colorを使用する。

Release solution buildは警告0／error 0、taskbar rendering focused testsは3／3 Pass、format verificationと`git diff --check`も成功した。自己完結payload `0.1.0-visual.74`（495 files、SHA-256 `3eafb5cd473e78b15592691bfb6340b37a12c112ecb4352a9f066df1d3feda75`）を起動し、ユーザーが未接続状態の先頭表示、グレーのstatus dot、および接続時のシアン表示を実機で目視確認して合格と判定した。

## 後続Gate

- local-consoleでtray右クリックの`QuickPods 設定...`から独立settings windowを開き、保存操作ができること（Issue #43）
- 実Bluetooth 1台／複数台でのrow密度、long-name ellipsis、接続状態
- local-consoleでの100／125／150／200%最終描画、keyboard、screen reader、High Contrast（Issue #43）
