using System.Collections.Immutable;
using QuickPods.Core.Models;

namespace QuickPods.Presentation;

public enum ProductPrimaryActionKind
{
    None,
    Connect,
    Disconnect,
    MakeDefault,
    OpenBluetoothSettings,
}

public sealed record BluetoothDeviceRowPresentation(
    BluetoothDeviceKey DeviceKey,
    string DisplayName,
    BluetoothAudioKind Kind,
    string IconGlyph,
    string StatusText,
    string AccessibleName,
    bool IsSelected,
    bool IsEnabled);

public sealed record BluetoothProductPresentation(
    long CatalogGeneration,
    long CatalogRevision,
    long OperationRevision,
    BluetoothDeviceKey? SelectedDeviceKey,
    ImmutableArray<BluetoothDeviceRowPresentation> Devices,
    bool IsRefreshing,
    bool IsBusy,
    ProductPrimaryActionKind PrimaryAction,
    string PrimaryActionText,
    bool IsPrimaryActionEnabled,
    bool ShowSoundRecovery,
    string? ErrorMessage)
{
    public bool HasDevices => !Devices.IsDefaultOrEmpty;
}

public static class BluetoothProductPresenter
{
    public static BluetoothProductPresentation Project(
        BluetoothAudioCatalogSnapshot catalog,
        BluetoothOperationSnapshot operation,
        bool isRefreshing,
        string? catalogError = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(operation);

        bool isBusy = operation.Outcome == BluetoothOperationOutcome.InProgress;
        ImmutableArray<BluetoothDeviceRowPresentation> devices = [.. catalog.Devices.Select(device =>
        {
            (BluetoothConnectionState connection, DefaultOutputState output) =
                ResolveEffectiveState(device, catalog.InventoryGeneration, operation);
            string status = CreateStatus(
                device,
                connection,
                output,
                catalog.InventoryGeneration,
                operation);
            return new BluetoothDeviceRowPresentation(
                device.DeviceKey,
                device.DisplayName,
                device.Kind,
                CreateIconGlyph(device.Kind),
                status,
                $"{device.DisplayName}、ペアリング済み、{status}",
                device.IsSelected,
                !isRefreshing && !isBusy);
        })];

        BluetoothAudioDeviceDescriptor? selected = catalog.Devices.FirstOrDefault(
            device => device.IsSelected);
        (ProductPrimaryActionKind action, string actionText, bool actionEnabled) =
            ResolvePrimaryAction(catalog, selected, operation, isRefreshing, isBusy);
        bool matchingOperation = selected is not null && IsMatching(
            selected,
            catalog.InventoryGeneration,
            operation);
        (BluetoothConnectionState selectedConnection, DefaultOutputState selectedOutput) =
            selected is null
                ? (BluetoothConnectionState.Unknown, DefaultOutputState.NotApplicable)
                : ResolveEffectiveState(selected, catalog.InventoryGeneration, operation);
        bool showSoundRecovery = selectedConnection == BluetoothConnectionState.Connected &&
            selectedOutput is DefaultOutputState.NotDefault or DefaultOutputState.Failed;
        string? error = catalogError ?? (matchingOperation
            ? CreateErrorMessage(operation.Error)
            : null);

        return new(
            catalog.InventoryGeneration,
            catalog.Revision,
            operation.Revision,
            catalog.SelectedDeviceKey,
            devices,
            isRefreshing,
            isBusy,
            action,
            actionText,
            actionEnabled,
            showSoundRecovery,
            error);
    }

    private static (
        ProductPrimaryActionKind Action,
        string Text,
        bool Enabled) ResolvePrimaryAction(
        BluetoothAudioCatalogSnapshot catalog,
        BluetoothAudioDeviceDescriptor? selected,
        BluetoothOperationSnapshot operation,
        bool isRefreshing,
        bool isBusy)
    {
        if (selected is null || !catalog.SelectedDevicePresent)
        {
            return (ProductPrimaryActionKind.None, "デバイスを選択", false);
        }

        if (isBusy)
        {
            string operationText = operation.Operation switch
            {
                QuickPodsOperation.Connecting => "接続中…",
                QuickPodsOperation.SettingDefault => "既定の出力へ切替中…",
                QuickPodsOperation.Disconnecting => "切断中…",
                _ => "処理中…",
            };
            return (ProductPrimaryActionKind.None, operationText, false);
        }

        if (isRefreshing)
        {
            return (ProductPrimaryActionKind.None, "更新中…", false);
        }

        if (selected.Capability is
            BluetoothDeviceCapability.SettingsOnly or
            BluetoothDeviceCapability.OwnershipUnknown)
        {
            return (
                ProductPrimaryActionKind.OpenBluetoothSettings,
                "Bluetooth設定を開く",
                true);
        }

        if (selected.Capability != BluetoothDeviceCapability.DirectControl)
        {
            return (ProductPrimaryActionKind.None, "現在は操作できません", false);
        }

        (BluetoothConnectionState connection, DefaultOutputState output) =
            ResolveEffectiveState(selected, catalog.InventoryGeneration, operation);
        if (connection == BluetoothConnectionState.Disconnected)
        {
            return (ProductPrimaryActionKind.Connect, "接続", true);
        }

        if (connection == BluetoothConnectionState.Connected)
        {
            return output == DefaultOutputState.Default
                ? (ProductPrimaryActionKind.Disconnect, "切断", true)
                : (ProductPrimaryActionKind.MakeDefault, "既定の出力に設定", true);
        }

        return (ProductPrimaryActionKind.None, "状態を更新してください", false);
    }

    private static string CreateStatus(
        BluetoothAudioDeviceDescriptor device,
        BluetoothConnectionState connection,
        DefaultOutputState output,
        long inventoryGeneration,
        BluetoothOperationSnapshot operation)
    {
        if (IsMatching(device, inventoryGeneration, operation))
        {
            string? activeStatus = operation.Operation switch
            {
                QuickPodsOperation.Connecting => "接続中",
                QuickPodsOperation.SettingDefault => "既定の出力へ切替中",
                QuickPodsOperation.Disconnecting => "切断中",
                _ => null,
            };
            if (activeStatus is not null)
            {
                return activeStatus;
            }
        }

        if (device.Capability is
            BluetoothDeviceCapability.SettingsOnly or
            BluetoothDeviceCapability.OwnershipUnknown)
        {
            return connection == BluetoothConnectionState.Connected
                ? "接続済み・直接操作は未対応"
                : "直接操作は未対応";
        }

        if (device.Capability == BluetoothDeviceCapability.TemporarilyUnavailable)
        {
            return "一時的に利用不可";
        }

        return connection switch
        {
            BluetoothConnectionState.Connected when output == DefaultOutputState.Default =>
                "接続済み・既定",
            BluetoothConnectionState.Connected => "接続済み・非既定",
            BluetoothConnectionState.Disconnected => "未接続",
            BluetoothConnectionState.Unavailable => "利用不可",
            _ => "状態不明",
        };
    }

    private static (BluetoothConnectionState Connection, DefaultOutputState Output)
        ResolveEffectiveState(
            BluetoothAudioDeviceDescriptor device,
            long inventoryGeneration,
            BluetoothOperationSnapshot operation)
    {
        if (!IsMatching(device, inventoryGeneration, operation))
        {
            return (device.ConnectionState, device.DefaultOutputState);
        }

        BluetoothConnectionState connection = operation.ConnectionState is
            BluetoothConnectionState.Unknown
                ? device.ConnectionState
                : operation.ConnectionState;
        DefaultOutputState output = operation.DefaultOutputState == DefaultOutputState.NotApplicable
            ? device.DefaultOutputState
            : operation.DefaultOutputState;
        return (connection, output);
    }

    private static bool IsMatching(
        BluetoothAudioDeviceDescriptor device,
        long inventoryGeneration,
        BluetoothOperationSnapshot operation) =>
        operation.Target is { } target &&
        target.DeviceKey == device.DeviceKey &&
        target.InventoryGeneration == inventoryGeneration;

    private static string? CreateErrorMessage(QuickPodsErrorCode? error) =>
        error switch
        {
            QuickPodsErrorCode.BluetoothDriverUnsupported =>
                "このデバイスはQuickPodsから直接操作できません。",
            QuickPodsErrorCode.BluetoothTimeout =>
                "接続状態を期限内に確認できませんでした。状態を更新してから再試行してください。",
            QuickPodsErrorCode.BluetoothSelectionStale =>
                "デバイス一覧が更新されました。選択状態を確認してください。",
            QuickPodsErrorCode.BluetoothDeviceUnavailable =>
                "選択したデバイスを利用できません。距離や装着状態を確認してください。",
            QuickPodsErrorCode.BluetoothContainmentFailed =>
                "Bluetooth操作の安全な終了を確認できないため、操作を停止しました。",
            QuickPodsErrorCode.DefaultOutputSwitchFailed =>
                "Bluetooth接続は完了しましたが、Windowsの既定出力を変更できませんでした。",
            QuickPodsErrorCode.BluetoothOperationRejected =>
                "Bluetooth操作を完了できませんでした。状態を更新してから再試行してください。",
            _ => null,
        };

    private static string CreateIconGlyph(BluetoothAudioKind kind) =>
        kind switch
        {
            BluetoothAudioKind.Speaker => "\uE7F5",
            BluetoothAudioKind.Earbuds => "\uE95B",
            _ => "\uE7F6",
        };
}
