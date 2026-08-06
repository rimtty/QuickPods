# QuickPods 0.1.0 uninstall policy

配布形式にかかわらず、uninstallerは最初にQuickPodsを終了し、installed copyの`QuickPods.exe --unregister-startup`を通常user権限で実行する。このcommandはQuickPods所有のHKCU Run値を削除し、既存設定がある場合は`StartWithWindows=false`へ原子的に保存して、再導入時に自動起動が意図せず復活しないようにする。他のRun値は変更しない。

既定のuninstallでは次のuser dataを保持する。

- `%LocalAppData%\QuickPods\settings.json`
- 隔離済みの`settings.corrupt-*.json`
- `%LocalAppData%\QuickPods\logs`

完全削除は利用者の明示選択時だけ上記`QuickPods` directoryを削除する。binary directory、shortcut、uninstall registrationは選択したinstaller形式が所有し、管理者権限なしで除去できなければならない。実際のinstall／update／uninstall／残骸確認は配布形式決定後のclean-environment gateで行う。
