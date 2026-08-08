using System.Collections.Immutable;
using QuickPods.Contracts;
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
    ImmutableArray<byte> IconPng,
    string IconGlyph,
    string StatusText,
    bool IsProcessing,
    string AccessibleName,
    bool IsSelected,
    bool IsEnabled)
{
    public bool HasDeviceIcon => !IconPng.IsDefaultOrEmpty;
}

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
        string? catalogError = null,
        ProductLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(operation);
        localizer ??= new ProductLocalizer(ProductLanguage.English);

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
                operation,
                localizer);
            bool isProcessing = isBusy && IsMatching(
                device,
                catalog.InventoryGeneration,
                operation);
            return new BluetoothDeviceRowPresentation(
                device.DeviceKey,
                device.DisplayName,
                device.Kind,
                device.IconPng,
                CreateIconGlyph(device.Kind),
                status,
                isProcessing,
                localizer.Format("PairedAccessible", device.DisplayName, status),
                device.IsSelected,
                !isRefreshing && !isBusy);
        })];

        BluetoothAudioDeviceDescriptor? selected = catalog.Devices.FirstOrDefault(
            device => device.IsSelected);
        (ProductPrimaryActionKind action, string actionText, bool actionEnabled) =
            ResolvePrimaryAction(catalog, selected, operation, isRefreshing, isBusy, localizer);
        (BluetoothConnectionState selectedConnection, DefaultOutputState selectedOutput) =
            selected is null
                ? (BluetoothConnectionState.Unknown, DefaultOutputState.NotApplicable)
                : ResolveEffectiveState(selected, catalog.InventoryGeneration, operation);
        bool showSoundRecovery = selectedConnection == BluetoothConnectionState.Connected &&
            selectedOutput is DefaultOutputState.NotDefault or DefaultOutputState.Failed;
        string? error = catalogError ?? (selected is not null &&
            ShouldShowOperationError(selected, operation)
                ? CreateErrorMessage(operation.Error, localizer)
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
        bool isBusy,
        ProductLocalizer localizer)
    {
        if (selected is null || !catalog.SelectedDevicePresent)
        {
            return (ProductPrimaryActionKind.None, localizer["Connect"], false);
        }

        if (isBusy)
        {
            string operationText = operation.Operation switch
            {
                QuickPodsOperation.Connecting => localizer["ConnectingEllipsis"],
                QuickPodsOperation.SettingDefault => localizer["SettingDefaultEllipsis"],
                QuickPodsOperation.DisconnectingOtherDevices => localizer["DisconnectingOthersEllipsis"],
                QuickPodsOperation.Disconnecting => localizer["DisconnectingEllipsis"],
                _ => localizer["WorkingEllipsis"],
            };
            return (ProductPrimaryActionKind.None, operationText, false);
        }

        if (isRefreshing)
        {
            return (ProductPrimaryActionKind.None, localizer["RefreshingEllipsis"], false);
        }

        if (selected.Capability is
            BluetoothDeviceCapability.SettingsOnly or
            BluetoothDeviceCapability.OwnershipUnknown)
        {
            return (
                ProductPrimaryActionKind.OpenBluetoothSettings,
                localizer["OpenBluetoothSettings"],
                true);
        }

        if (selected.Capability != BluetoothDeviceCapability.DirectControl)
        {
            return (ProductPrimaryActionKind.None, localizer["CurrentlyUnavailable"], false);
        }

        (BluetoothConnectionState connection, DefaultOutputState output) =
            ResolveEffectiveState(selected, catalog.InventoryGeneration, operation);
        if (connection == BluetoothConnectionState.Disconnected)
        {
            return (ProductPrimaryActionKind.Connect, localizer["Connect"], true);
        }

        if (connection == BluetoothConnectionState.Connected)
        {
            return output == DefaultOutputState.Default
                ? (ProductPrimaryActionKind.Disconnect, localizer["Disconnect"], true)
                : (ProductPrimaryActionKind.MakeDefault, localizer["MakeDefault"], true);
        }

        return (ProductPrimaryActionKind.None, localizer["RefreshState"], false);
    }

    private static string CreateStatus(
        BluetoothAudioDeviceDescriptor device,
        BluetoothConnectionState connection,
        DefaultOutputState output,
        long inventoryGeneration,
        BluetoothOperationSnapshot operation,
        ProductLocalizer localizer)
    {
        if (IsMatching(device, inventoryGeneration, operation))
        {
            string? activeStatus = operation.Operation switch
            {
                QuickPodsOperation.Connecting => localizer["Connecting"],
                QuickPodsOperation.SettingDefault => localizer["SettingDefault"],
                QuickPodsOperation.DisconnectingOtherDevices => localizer["DisconnectingOthers"],
                QuickPodsOperation.Disconnecting => localizer["Disconnecting"],
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
                ? localizer["ConnectedUnsupported"]
                : localizer["Unsupported"];
        }

        if (device.Capability == BluetoothDeviceCapability.TemporarilyUnavailable)
        {
            return localizer["TemporarilyUnavailable"];
        }

        return connection switch
        {
            BluetoothConnectionState.Connected when output == DefaultOutputState.Default =>
                localizer["ConnectedDefault"],
            BluetoothConnectionState.Connected => localizer["ConnectedNotDefault"],
            BluetoothConnectionState.Disconnected => localizer["Disconnected"],
            BluetoothConnectionState.Unavailable => localizer["Unavailable"],
            _ => localizer["UnknownStatus"],
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

    private static bool ShouldShowOperationError(
        BluetoothAudioDeviceDescriptor selected,
        BluetoothOperationSnapshot operation)
    {
        if (operation.Error is null ||
            operation.Target is not { } target ||
            target.DeviceKey != selected.DeviceKey)
        {
            return false;
        }

        if (operation.Error == QuickPodsErrorCode.OtherBluetoothDevicesStillConnected)
        {
            return true;
        }

        bool requestedStateReached = operation.Error == QuickPodsErrorCode.DefaultOutputSwitchFailed
            ? selected.ConnectionState == BluetoothConnectionState.Connected &&
                selected.DefaultOutputState == DefaultOutputState.Default
            : operation.RequestedAction switch
            {
                BluetoothRequestedAction.Connect =>
                    selected.ConnectionState == BluetoothConnectionState.Connected,
                BluetoothRequestedAction.Disconnect =>
                    selected.ConnectionState == BluetoothConnectionState.Disconnected,
                _ => false,
            };
        return !requestedStateReached;
    }

    private static string? CreateErrorMessage(
        QuickPodsErrorCode? error,
        ProductLocalizer localizer) =>
        error switch
        {
            QuickPodsErrorCode.BluetoothDriverUnsupported =>
                localizer["DirectControlUnavailable"],
            QuickPodsErrorCode.BluetoothTimeout =>
                localizer["BluetoothTimeout"],
            QuickPodsErrorCode.BluetoothSelectionStale =>
                localizer["SelectionStale"],
            QuickPodsErrorCode.BluetoothDeviceUnavailable =>
                localizer["DeviceUnavailable"],
            QuickPodsErrorCode.BluetoothContainmentFailed =>
                localizer["ContainmentFailed"],
            QuickPodsErrorCode.DefaultOutputSwitchFailed =>
                localizer["DefaultSwitchFailed"],
            QuickPodsErrorCode.OtherBluetoothDevicesStillConnected =>
                localizer["OtherDevicesStillConnected"],
            QuickPodsErrorCode.BluetoothOperationRejected =>
                localizer["OperationRejected"],
            _ => null,
        };

    private static string CreateIconGlyph(BluetoothAudioKind kind) =>
        kind switch
        {
            BluetoothAudioKind.Speaker => "\uE767",
            _ => FluentAudioGlyphs.Headphones,
        };
}
