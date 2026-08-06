# Phase 4B 検証結果

## Coreオーケストレーション

初回実装ではOS mutation portをfakeへ置換し、次を確認する。

- 接続確認後の既定出力失敗を`ConnectedNotDefault`として保持し、接続を再試行しない。
- 操作送信後に選択が変わった結果を`Superseded`とし、別機器へ既定出力要求を送らない。
- OS要求直前に選択が変わった場合はBluetooth要求を0件にする。
- `SettingsOnly`機器へ変更要求を送らない。
- 同時操作をグローバルに直列化する。

Foundation focused testsは64件（Phase 4B追加9件を含む）でPassしている。追加したWindows binding試験では、最新generationだけを公開し、古いgenerationの操作targetを解決しないことを確認した。worker protocol試験は認証済みread-only request、mutation capability拒否、応答相関拒否を対象とする。

Release App出力にworkerのexe／dll／deps／runtimeconfigが配置されることを確認した。workerをPowerShellから直接起動した非正規親テストは、KS要求を読む前にparent validationで終了コード2となった。物理KS mutationとPolicyConfig検証は後続コミットおよびローカルコンソールGateで扱う。DPI、解像度、描画、Floating、クリック、ホイール試験は現在のRDP環境では実施しない。
