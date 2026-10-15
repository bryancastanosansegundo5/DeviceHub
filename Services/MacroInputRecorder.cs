using System.Diagnostics;
using System.Runtime.InteropServices;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class MacroInputRecorder : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLeftDown = 0x0201;
    private const int WmLeftUp = 0x0202;
    private const int WmRightDown = 0x0204;
    private const int WmRightUp = 0x0205;
    private const int WmMiddleDown = 0x0207;
    private const int WmMiddleUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const uint KeyboardInjected = 0x10;
    private const uint MouseInjected = 0x01;
    private readonly LowLevelInputProc _keyboardCallback;
    private readonly LowLevelInputProc _mouseCallback;
    private readonly List<MacroStep> _steps = [];
    private readonly Dictionary<(MacroInputType Type, int Code), MacroStep> _heldInputs = [];
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private long _lastTimestamp;
    private readonly uint _processId = (uint)Environment.ProcessId;

    public MacroInputRecorder()
    {
        _keyboardCallback = OnKeyboard;
        _mouseCallback = OnMouse;
    }

    public bool IsRecording => _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;

    public void Start()
    {
        if (IsRecording) throw new InvalidOperationException("Ya hay una grabación activa.");
        _steps.Clear();
        _heldInputs.Clear();
        _lastTimestamp = Stopwatch.GetTimestamp();
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            throw new InvalidOperationException($"No se pudo iniciar la grabación de teclas (Windows {Marshal.GetLastWin32Error()}).");
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseCallback, module, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            throw new InvalidOperationException($"No se pudo iniciar la grabación del ratón (Windows {error}).");
        }
    }

    public IReadOnlyList<MacroStep> Stop()
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
        foreach (var held in _heldInputs.Values.ToArray())
            AddStep(held.InputType, held.VirtualKey, held.ScanCode, held.MouseButton,
                held.WheelDelta, false, held.IsExtended);
        _heldInputs.Clear();
        return _steps.Select(step => new MacroStep
        {
            InputType = step.InputType,
            VirtualKey = step.VirtualKey,
            ScanCode = step.ScanCode,
            IsDown = step.IsDown,
            IsExtended = step.IsExtended,
            DelayMs = step.DelayMs,
            MouseButton = step.MouseButton,
            WheelDelta = step.WheelDelta
        }).ToArray();
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsOutsideNexo())
        {
            var kind = unchecked((int)message.ToInt64());
            if (kind is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)
            {
                var key = Marshal.PtrToStructure<KeyboardHookData>(data);
                if ((key.Flags & KeyboardInjected) == 0 && key.VirtualKey is > 0 and <= 255)
                    RecordHeldInput(MacroInputType.Key, (int)key.VirtualKey, (int)key.VirtualKey,
                        (int)key.ScanCode, default, 0, (kind is WmKeyDown or WmSysKeyDown), (key.Flags & 1) != 0);
            }
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsOutsideNexo())
        {
            var kind = unchecked((int)message.ToInt64());
            var mouse = Marshal.PtrToStructure<MouseHookData>(data);
            if ((mouse.Flags & MouseInjected) == 0)
            {
                var button = kind switch
                {
                    WmLeftDown or WmLeftUp => MacroMouseButton.Left,
                    WmRightDown or WmRightUp => MacroMouseButton.Right,
                    WmMiddleDown or WmMiddleUp => MacroMouseButton.Middle,
                    WmXButtonDown or WmXButtonUp when ((mouse.MouseData >> 16) & 0xffff) == 1 => MacroMouseButton.XButton1,
                    WmXButtonDown or WmXButtonUp when ((mouse.MouseData >> 16) & 0xffff) == 2 => MacroMouseButton.XButton2,
                    _ => (MacroMouseButton?)null
                };
                if (button is MacroMouseButton selectedButton)
                {
                    var isDown = kind is WmLeftDown or WmRightDown or WmMiddleDown or WmXButtonDown;
                    RecordHeldInput(MacroInputType.MouseButton, (int)selectedButton, 0, 0,
                        selectedButton, 0, isDown, false);
                }
                else if (kind == WmMouseWheel)
                {
                    var delta = unchecked((short)(mouse.MouseData >> 16));
                    if (delta != 0)
                        AddStep(MacroInputType.MouseWheel, 0, 0, default, delta, true, false);
                }
            }
        }
        return CallNextHookEx(_mouseHook, code, message, data);
    }

    private void RecordHeldInput(MacroInputType type, int code, int virtualKey, int scanCode,
        MacroMouseButton mouseButton, int wheelDelta, bool isDown, bool isExtended)
    {
        var key = (type, code);
        if (isDown)
        {
            if (!_heldInputs.ContainsKey(key))
            {
                var step = AddStep(type, virtualKey, (uint)scanCode, mouseButton, wheelDelta, true, isExtended);
                _heldInputs[key] = step;
            }
        }
        else if (_heldInputs.Remove(key))
        {
            AddStep(type, virtualKey, (uint)scanCode, mouseButton, wheelDelta, false, isExtended);
        }
    }

    private MacroStep AddStep(MacroInputType type, int virtualKey, uint scanCode,
        MacroMouseButton mouseButton, int wheelDelta, bool isDown, bool isExtended)
    {
        var now = Stopwatch.GetTimestamp();
        var delay = _steps.Count == 0 ? 0 : (int)Math.Clamp((now - _lastTimestamp) * 1000 / Stopwatch.Frequency, 0, 10000);
        var step = new MacroStep
        {
            InputType = type,
            VirtualKey = virtualKey,
            ScanCode = scanCode,
            MouseButton = mouseButton,
            WheelDelta = wheelDelta,
            IsDown = isDown,
            IsExtended = isExtended,
            DelayMs = delay
        };
        _steps.Add(step);
        _lastTimestamp = now;
        return step;
    }

    private bool IsOutsideNexo()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var processId);
        return processId != _processId;
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
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
    private delegate IntPtr LowLevelInputProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelInputProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
