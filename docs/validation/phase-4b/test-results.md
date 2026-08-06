# Phase 4B 検証結果

## Coreオーケストレーション

初回実装ではOS mutation portをfakeへ置換し、次を確認する。

- 接続確認後の既定出力失敗を`ConnectedNotDefault`として保持し、接続を再試行しない。
- 操作送信後に選択が変わった結果を`Superseded`とし、別機器へ既定出力要求を送らない。
- OS要求直前に選択が変わった場合はBluetooth要求を0件にする。
- `SettingsOnly`機器へ変更要求を送らない。
- 同時操作をグローバルに直列化する。

Foundation focused testsは60件（Phase 4B追加5件を含む）でPassしている。物理KS、PolicyConfig、RDP拒否の製品アダプター検証は後続コミットで追加する。DPI、解像度、描画、Floating、クリック、ホイール試験は現在のRDP環境では実施しない。
