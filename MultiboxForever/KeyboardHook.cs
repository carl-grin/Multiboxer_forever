using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultiboxForever;

/// <summary>
/// Global low-level keyboard hook that raises when Ctrl+Alt+{0-9, Minus, Plus} is pressed.
/// Keys are not swallowed — CallNextHookEx always runs so Windows keeps normal behavior.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly HashSet<uint> _chordKeysDown = new();
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    public event Action<char>? ChordPressed;

    public KeyboardHook()
    {
        // Keep a strong reference so the GC cannot collect the callback delegate.
        _proc = HookCallback;
    }

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule
            ?? throw new InvalidOperationException("Could not resolve the process main module for the keyboard hook.");

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetWindowsHookEx failed (Win32 error {error}).");
        }
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                _chordKeysDown.Remove(data.vkCode);
            }
            else if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                if (IsCtrlDown()
                    && IsAltDown()
                    && Protocol.TryMapVirtualKeyToKey(data.vkCode, out var key)
                    && _chordKeysDown.Add(data.vkCode))
                {
                    try
                    {
                        ChordPressed?.Invoke(key);
                    }
                    catch
                    {
                        // Never let subscriber exceptions break the hook chain.
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsCtrlDown() =>
        IsKeyDown(NativeMethods.VK_CONTROL)
        || IsKeyDown(NativeMethods.VK_LCONTROL)
        || IsKeyDown(NativeMethods.VK_RCONTROL);

    private static bool IsAltDown() =>
        IsKeyDown(NativeMethods.VK_MENU)
        || IsKeyDown(NativeMethods.VK_LMENU)
        || IsKeyDown(NativeMethods.VK_RMENU);

    private static bool IsKeyDown(int vKey) =>
        (NativeMethods.GetAsyncKeyState(vKey) & NativeMethods.KEY_PRESSED) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Uninstall();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
