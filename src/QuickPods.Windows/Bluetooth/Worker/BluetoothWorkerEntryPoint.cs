using System.Runtime.InteropServices;
using System.Text.Json;
using QuickPods.Windows.Audio.Interop;

namespace QuickPods.Windows.Bluetooth.Worker;

internal static class BluetoothWorkerEntryPoint
{
    private const uint PropertyTypeGet = 0x00000001;
    private const uint PropertyTypeBasicSupport = 0x00000200;
    private const uint OneShotReconnect = 0;
    private const uint OneShotDisconnect = 1;

    private static readonly Guid BluetoothAudioPropertySet =
        new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

    internal static int Run(
        BluetoothWorkerOperation operation,
        string? authorizationToken,
        bool parentVerified,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (!parentVerified)
        {
            error.WriteLine("Bluetooth worker parent validation failed.");
            return 2;
        }

        BluetoothWorkerRequest request;
        try
        {
            request = BluetoothWorkerRequest.DeserializeAndValidate(
                input.ReadLine() ?? string.Empty,
                authorizationToken,
                operation);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException)
        {
            error.WriteLine("Bluetooth worker request validation failed.");
            return 2;
        }

        try
        {
            using var worker = new MtaAudioWorker();
            BluetoothWorkerResponse response = worker.Invoke(() => Execute(request));
            output.WriteLine(response.Serialize(request));
            return 0;
        }
        catch
        {
            error.WriteLine("Bluetooth worker failed before producing a sanitized response.");
            return 1;
        }
    }

    private static BluetoothWorkerResponse Execute(BluetoothWorkerRequest request)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? activatedInterface = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            int result = enumerator.GetDevice(request.AdapterDeviceId, out device);
            if (result < 0)
            {
                return new(result, 0, null);
            }

            Guid ksControlInterfaceId = typeof(IKsControl).GUID;
            result = device.Activate(
                ref ksControlInterfaceId,
                ComClassContext.All,
                nint.Zero,
                out activatedInterface);
            if (result < 0)
            {
                return new(result, 0, null);
            }

            var ksControl = (IKsControl)activatedInterface;
            return ExecuteKsProperty(ksControl, request.Operation);
        }
        finally
        {
            ReleaseComObject(activatedInterface);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    private static BluetoothWorkerResponse ExecuteKsProperty(
        IKsControl ksControl,
        BluetoothWorkerOperation operation)
    {
        bool basicSupport = operation is
            BluetoothWorkerOperation.BasicSupportReconnect or
            BluetoothWorkerOperation.BasicSupportDisconnect;
        uint propertyId = operation is
            BluetoothWorkerOperation.BasicSupportReconnect or BluetoothWorkerOperation.Reconnect
                ? OneShotReconnect
                : OneShotDisconnect;
        var property = new KsProperty(
            BluetoothAudioPropertySet,
            propertyId,
            basicSupport ? PropertyTypeBasicSupport : PropertyTypeGet);
        nint buffer = nint.Zero;
        try
        {
            uint bufferLength = 0;
            if (basicSupport)
            {
                bufferLength = sizeof(uint);
                buffer = Marshal.AllocHGlobal((int)bufferLength);
                Marshal.WriteInt32(buffer, 0);
            }

            int result = ksControl.KsProperty(
                ref property,
                (uint)Marshal.SizeOf<KsProperty>(),
                buffer,
                bufferLength,
                out uint bytesReturned);
            uint? supportFlags = basicSupport && result == 0 && bytesReturned >= sizeof(uint)
                ? unchecked((uint)Marshal.ReadInt32(buffer))
                : null;
            return new(result, bytesReturned, supportFlags);
        }
        finally
        {
            if (buffer != nint.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }
}
