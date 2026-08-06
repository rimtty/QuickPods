# QuickPods 0.1.0 update policy

QuickPods 0.1.0はネットワークへ接続せず、自動更新を行わない。新しいversionはprivate GitHub repositoryの承認済みRelease artifactとして提供し、利用者がchecksumを確認して明示的に置き換える。

更新時はQuickPodsを通知領域から終了し、新しい配布物を導入してから起動する。`%LocalAppData%\QuickPods\settings.json`と`logs`はuser dataとして保持し、schema互換の範囲で再利用する。version downgradeは保証しない。更新失敗時は旧binaryへ戻せるよう、配布工程側で旧packageを保持する。

初期releaseではbackground updater、scheduled task、service、管理者権限、外部telemetryを追加しない。将来自動更新を導入する場合は、署名、rollback、通信先、privacy、partial update失敗を別ADRと脅威modelで承認する。
