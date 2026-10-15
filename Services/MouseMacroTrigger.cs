using System.Runtime.InteropServices;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class MouseMacroTrigger : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const uint MouseInjected = 0x01;
    private readonly Func<MacroTrigger, MacroDefinition?> _findMacro;
    private readonly MacroPlaybackService _playback;
    private readonly LowLevelMouseProc _callback;
    private readonly HashSet<int> _swallowedButtons = [];
    private IntPtr _hook;

    public MouseMacroTrigger(Func<MacroTrigger, MacroDefinition?> findMacro, MacroPlaybackService playback)
    {
        _findMacro = findMacro;
        _playback = playback;
        _callback = OnMouse;
    }

    public void Enable()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException($"No se pudo activar el botón de macro (Windows {Marshal.GetLastWin32Error()}).");
    }

    public void Disable()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowedButtons.Clear();
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var kind = unchecked((int)message.ToInt64());
            if (kind is WmXButtonDown or WmXButtonUp)
            {
                var input = Marshal.PtrToStructure<MouseHookData>(data);
                var buttonNumber = (input.Flags & MouseInjected) == 0
                    ? (int)((input.MouseData >> 16) & 0xffff)
                    : 0;
                var trigger = buttonNumber == 1 ? MacroTrigger.XButton1 : buttonNumber == 2 ? MacroTrigger.XButton2 : MacroTrigger.None;
                if (trigger != MacroTrigger.None)
                {
                    if (kind == WmXButtonDown)
                    {
                        var macro = _findMacro(trigger);
                        if (macro is not null && macro.Steps.Count > 0)
                        {
                            _swallowedButtons.Add(buttonNumber);
                            _ = _playback.PlayAsync(macro.Steps.ToArray());
                            return (IntPtr)1;
                        }
                    }
                    else if (_swallowedButtons.Remove(buttonNumber))
                    {
                        return (IntPtr)1;
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose() => Disable();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
