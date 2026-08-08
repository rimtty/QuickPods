<p align="center">
  <img src="docs/assets/branding/quickpods-icon-v2.png" width="112" alt="QuickPods アイコン">
</p>

<h1 align="center">QuickPods</h1>

<p align="center">
  Windows 11のタスクバーから、音量とペアリング済みBluetoothオーディオをすばやく操作するアプリです。
</p>

<p align="center">
  <a href="README.md">English</a> ·
  <a href="docs/user-guide.md">ユーザーガイド</a> ·
  <a href="docs/development.md">開発ガイド</a> ·
  <a href="CONTRIBUTING.md">コントリビューション</a>
</p>

> [!IMPORTANT]
> QuickPodsは開発中です。現時点では署名済みの一般公開版はありません。CIやローカルで生成した未署名インストーラーでは、Windowsの警告が表示される場合があります。

## 主な機能

- タスクバーのコンパクトな操作面から、既定出力の音量とミュートを操作
- ペアリング済みBluetoothヘッドホン、イヤホン、ヘッドセット、スピーカーを一覧表示
- Windowsドライバーが対応する場合に、選択した機器を接続または切断
- 接続した機器をConsole／Multimediaの既定出力へ設定
- Windows側で行われた音声・Bluetooth状態変更をアプリ再起動なしで追従
- 安全なタスクバー配置を確認できない場合は通知領域へフォールバック
- 日本語Windowsでは日本語、それ以外では英語を使用し、設定画面から明示的に変更可能
- 管理者権限を要求せず、設定と診断ログをユーザー単位で保存

## 動作要件

- Windows 11 x64、build 22000以降
- タスクバー内表示には中央揃えのタスクバー
- 直接接続・切断には対応したBluetoothオーディオドライバー

左揃えのタスクバーや、安全な配置を証明できない環境でも、QuickPodsは通知領域から利用できます。配布パッケージは自己完結型のため、利用者が.NETを別途導入する必要はありません。

## ビルドして実行する

最初の署名済み公開版までは、ソースからビルドしてください。

```powershell
git clone https://github.com/rimtty/QuickPods.git
Set-Location QuickPods
dotnet restore QuickPods.sln --locked-mode
dotnet build src/QuickPods.App/QuickPods.App.csproj -c Release --no-restore
./src/QuickPods.App/bin/Release/net10.0-windows10.0.26100.0/QuickPods.exe
```

パッケージ作成と署名要件は[リリースガイド](docs/release/README.md)を参照してください。

## プライバシーと安全性

QuickPodsはローカルで動作し、テレメトリやネットワークサービスを含みません。ログにはBluetoothアドレス、Container ID、PnP ID、Endpoint ID、アカウント名などの生の識別子を記録しない設計です。Issueへ診断情報を添付する前に[プライバシー情報](docs/privacy.md)を確認してください。

## 開発・コントリビューション

ビルド、テスト、ハードウェア依存検証については[開発ガイド](docs/development.md)を参照してください。不具合報告やPull Requestを作成する前に、[CONTRIBUTING.md](CONTRIBUTING.md)、[SUPPORT.md](SUPPORT.md)、[SECURITY.md](SECURITY.md)を確認してください。

## ライセンス

QuickPodsは[MIT License](LICENSE)で公開します。第三者ライセンスは[ThirdPartyNotices.txt](ThirdPartyNotices.txt)に記載しています。
