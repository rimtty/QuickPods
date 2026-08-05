using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuickPods.Spike.CoreAudio;

internal sealed partial class SystemVolumeKeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VolumeDown = 0xAE;
    private const ushort VolumeUp = 0xAF;

    internal static unsafe int NativeInputSize => sizeof(Input);

    public unsafe void SendStep(bool increase)
    {
        ushort virtualKey = increase ? VolumeUp : VolumeDown;
        Input* inputs = stackalloc Input[2];
        inputs[0] = CreateInput(virtualKey, flags: 0);
        inputs[1] = CreateInput(virtualKey, KeyEventKeyUp);

        uint sent = NativeMethods.SendInput(2, inputs, NativeInputSize);
        if (sent != 2)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SendInput could not deliver the bounded system-volume key step.");
        }
    }

    private static Input CreateInput(ushort virtualKey, uint flags)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags,
                },
            },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        // INPUT's native union is sized by MOUSEINPUT (32 bytes on x64),
        // even when the active member is KEYBDINPUT (24 bytes on x64).
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static unsafe partial uint SendInput(uint inputCount, Input* inputs, int inputSize);
    }
}
