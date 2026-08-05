using System.Runtime.InteropServices;
using System.Text.Json;
using QuickPods.Spike.BluetoothKs.Interop;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal static class KsChildEntryPoint
{
    public static int Run(
        KsChildOperation operation,
        string? expectedAuthorizationToken,
        bool sameExecutableParent,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        KsChildRequest request;
        try
        {
            request = KsChildRequest.DeserializeAndValidate(
                input.ReadLine() ?? string.Empty,
                expectedAuthorizationToken,
                operation);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException)
        {
            error.WriteLine("KS child capability validation failed.");
            return 2;
        }

        bool simulation = operation is
            KsChildOperation.SimulatedHang or
            KsChildOperation.SimulatedFault;
        if (!simulation && !sameExecutableParent)
        {
            error.WriteLine("KS child parent validation failed.");
            return 2;
        }

        if (operation == KsChildOperation.SimulatedHang)
        {
            Thread.Sleep(System.Threading.Timeout.Infinite);
            return 1;
        }

        if (operation == KsChildOperation.SimulatedFault)
        {
            error.WriteLine("KS child simulated a sanitized failure.");
            return 1;
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? activatedInterface = null;
        using var apartment = ComApartmentScope.EnterMultithreaded();
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            int result = enumerator.GetDevice(request.AdapterDeviceId, out device);
            if (result < 0)
            {
                WriteResponse(
                    new KsChildResponse(result, 0, SupportFlags: null),
                    request,
                    output);
                return 0;
            }

            Guid ksControlInterfaceId = KernelStreamingContracts.KsControlInterfaceId;
            result = device.Activate(
                in ksControlInterfaceId,
                NativeConstants.ClsContextAll,
                nint.Zero,
                out activatedInterface);
            if (result < 0)
            {
                WriteResponse(
                    new KsChildResponse(result, 0, SupportFlags: null),
                    request,
                    output);
                return 0;
            }

            var ksControl = (IKsControl)activatedInterface;
            KsChildResponse response = Execute(ksControl, operation);
            WriteResponse(response, request, output);
            return 0;
        }
        catch
        {
            error.WriteLine("KS child failed before producing a sanitized response.");
            return 1;
        }
        finally
        {
            ComObject.Release(activatedInterface);
            ComObject.Release(device);
            ComObject.Release(enumerator);
        }
    }

    private static void WriteResponse(
        KsChildResponse response,
        KsChildRequest request,
        TextWriter output)
    {
        output.WriteLine(response.Serialize(
            request.RequestNonce,
            request.TargetHash,
            request.Operation));
    }

    private static KsChildResponse Execute(IKsControl ksControl, KsChildOperation operation)
    {
        (bool basicSupport, KsProperty property) = operation switch
        {
            KsChildOperation.BasicSupportReconnect => (
                true,
                KernelStreamingContracts.CreateBasicSupportDescriptor(
                    KernelStreamingContracts.OneShotReconnect)),
            KsChildOperation.BasicSupportDisconnect => (
                true,
                KernelStreamingContracts.CreateBasicSupportDescriptor(
                    KernelStreamingContracts.OneShotDisconnect)),
            KsChildOperation.Reconnect => (
                false,
                KernelStreamingContracts.CreateReconnectDescriptor()),
            KsChildOperation.Disconnect => (
                false,
                KernelStreamingContracts.CreateDisconnectDescriptor()),
            _ => throw new InvalidOperationException(
                "The requested operation is not a KS device operation."),
        };

        nint supportBuffer = nint.Zero;
        try
        {
            uint bufferLength = 0;
            if (basicSupport)
            {
                bufferLength = sizeof(uint);
                supportBuffer = Marshal.AllocHGlobal((int)bufferLength);
                Marshal.WriteInt32(supportBuffer, 0);
            }

            int result = ksControl.KsProperty(
                ref property,
                (uint)KernelStreamingContracts.PropertySize,
                supportBuffer,
                bufferLength,
                out uint bytesReturned);
            uint? supportFlags = basicSupport && result == 0 && bytesReturned >= sizeof(uint)
                ? unchecked((uint)Marshal.ReadInt32(supportBuffer))
                : null;
            return new KsChildResponse(result, bytesReturned, supportFlags);
        }
        finally
        {
            if (supportBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(supportBuffer);
            }
        }
    }
}
