# Phase 4B — Bluetooth操作オーケストレーション

## Core境界

`BluetoothOperationController`は、明示選択済みの物理デバイスに対する接続／切断と、接続確認後の既定出力変更を直列化する。OS固有のKS、MMDevice、PolicyConfig型はCoreへ公開しない。

操作開始時に次の値を固定する。

- opaque `BluetoothDeviceKey`
- 読み取り専用カタログの`InventoryGeneration`
- 要求種別（Connect／Disconnect）

OS要求の直前と結果反映前に、選択キー、inventory generation、選択機器の存在を再検証する。変更されていれば`Superseded`として古い結果を現在行へ適用しない。送信済みOS要求の取り消しや補償操作は行わない。

## Port契約

`IBluetoothDeviceOperationPort`は一回の検証済み物理デバイス操作だけを実行する。実装は送信済みKS要求を自動再試行してはならない。送信後は呼び出し元のキャンセルを「未送信」の証明に使わず、固定期限内の実状態観測を終えて結果を返す。

`IDefaultOutputOperationPort`は接続済みの同一物理デバイスに対してだけ既定出力を変更・検証する。失敗時にBluetoothを切断せず、無関係な過去の既定Endpointへ強制復元しない。

## 状態と部分成功

接続確認と既定出力確認は別結果として保持する。Bluetoothが`Connected`になった後でPolicyConfigまたは検証が失敗した場合は`ConnectedNotDefault`とし、接続失敗へ読み替えない。

操作結果は`Succeeded`、`ConnectedNotDefault`、`Unsupported`、`SelectionStale`、`Superseded`、`TimedOut`、`Rejected`、`ContainmentFailed`、`Faulted`、`Cancelled`へ正規化する。一つの対象の失敗を別機器やマスター音量へ波及させない。

## Windows実装で必須となる条件

- Phase 4Aのgeneration付き内部bindingからだけContainer所有のEndpoint／KS filterを解決する。
- Reconnectと全対象Disconnect候補のBasic Supportを短命workerで確認する。
- 生のPnP／Endpoint／Container識別子はworker標準入力だけで渡し、コマンドライン、UI、通常ログへ出さない。
- workerをJob Objectへ収容し、timeout／crash後にprocess treeが空であることを証明する。
- RDPではローカル所有権を証明できないため、KSと既定出力の変更要求を拒否する。

物理AirPods操作はローカルコンソールGateまで実行しない。

初期Windows binding実装は、列挙時に取得した生のContainer ID、MMDevice Endpoint ID、DeviceTopology接続先IDを`QuickPods.Windows`内部のregistryだけへ保存する。公開にはopaque keyだけを使い、最新generationと完全一致しない操作targetは解決しない。古い並行列挙はregistryを上書きできない。DeviceTopologyを取得できないEndpointもカタログから消さず、候補なしとして後続の能力判定を`OwnershipUnknown`へ縮退させる。
