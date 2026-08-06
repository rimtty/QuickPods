using System.Runtime.InteropServices;

namespace QuickPods.Windows.Audio.Interop;

internal enum AudioDataFlow
{
    Render,
    Capture,
    All,
}

internal enum AudioRole
{
    Console,
    Multimedia,
    Communications,
}

[Flags]
internal enum ComClassContext : uint
{
    InprocServer = 0x1,
    InprocHandler = 0x2,
    LocalServer = 0x4,
    RemoteServer = 0x10,
    All = InprocServer | InprocHandler | LocalServer | RemoteServer,
}

[Flags]
internal enum AudioDeviceState : uint
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, uint propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly uint PropertyId = propertyId;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)]
    public ushort VariantType;

    [FieldOffset(8)]
    public nint PointerValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;

    [MarshalAs(UnmanagedType.Bool)]
    public bool IsMuted;

    public float MasterVolume;
    public uint ChannelCount;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(AudioDataFlow dataFlow, AudioDeviceState stateMask, out nint devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(AudioDataFlow dataFlow, AudioRole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        ComClassContext classContext,
        nint activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);

    [PreserveSig]
    int OpenPropertyStore(uint storageAccessMode, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out AudioDeviceState state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        AudioDeviceState newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(
        AudioDataFlow dataFlow,
        AudioRole role,
        [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        PropertyKey propertyKey);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback client);

    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback client);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channelNumber, float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channelNumber, out float levelDb);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channelNumber, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);

    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float minimumDb, out float maximumDb, out float incrementDb);
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(nint notificationData);
}

internal static partial class CoreAudioNativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int PropVariantClear(ref PropVariant variant);
}

internal static class HResult
{
    public static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new CoreAudioInteropException(operation, result);
        }
    }
}

internal sealed class CoreAudioInteropException : Exception
{
    public CoreAudioInteropException(string operation, int nativeHResult)
        : base($"{operation} failed with HRESULT 0x{nativeHResult:X8}.")
    {
        NativeHResult = nativeHResult;
        HResult = nativeHResult;
    }

    public int NativeHResult { get; }
}
