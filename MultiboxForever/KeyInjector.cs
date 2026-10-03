using System.Runtime.InteropServices;

namespace MultiboxForever;

/// <summary>
/// Injects a single supported key (0-9, Minus, Plus/=) via SendInput — no Ctrl/Alt.
/// </summary>
internal static class KeyInjector
{
    public static void SendKey(char key)
    {
        ushort vk = Protocol.ToVirtualKey(key);

        var inputs = new NativeMethods.INPUT[2];
        inputs[0] = CreateKeyInput(vk, keyUp: false);
        inputs[1] = CreateKeyInput(vk, keyUp: true);

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput injected {sent}/{inputs.Length} events (Win32 error {error}).");
        }
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }
}
