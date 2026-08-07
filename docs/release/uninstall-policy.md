# QuickPods 0.1.0 uninstall policy

ユーザー単位MSIは更新／uninstall時に実行中の`QuickPods.exe`へ終了messageを送り、5秒待っても終了しない場合だけprocessを終了させてからfileを更新／削除する。

完全uninstall時だけ、file削除前にinstalled copyの`QuickPods.exe --unregister-startup`を通常user権限で実行する。このcommandはQuickPods所有のHKCU Run値を削除し、既存設定がある場合は`StartWithWindows=false`へ原子的に保存して、再導入時に自動起動が意図せず復活しないようにする。他のRun値は変更しない。Major Upgradeによる旧version削除では実行せず、利用者のstartup設定を保持する。cleanup commandが実行不能でもMSI自体はuninstallを継続し、残骸の有無をclean-environment gateで確認する。

既定のuninstallでは次のuser dataを保持する。

- `%LocalAppData%\QuickPods\settings.json`
- 隔離済みの`settings.corrupt-*.json`
- `%LocalAppData%\QuickPods\logs`

完全なuser data削除はMSIの責務外とし、利用者が明示的に上記`QuickPods` directoryを削除する。binary directory、Component marker、shortcut、uninstall registrationはMSIが所有し、管理者権限なしで除去できなければならない。実際のinstall／update／uninstall／残骸確認はclean-environment gateで行う。
