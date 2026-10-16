using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class MouseMacroTrigger : IDisposable
{
    private sealed class SequencePlaybackState(MacroTrigger trigger) : IDisposable
    {
        public MacroTrigger Trigger { get; } = trigger;
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Stop { get; } = new();
        public CancellationTokenSource HoldStop { get; } = new();
        public void Release()
        {
            Released.TrySetResult();
            Cancel(HoldStop);
        }
        public void Cancel()
        {
            Cancel(Stop);
            Cancel(HoldStop);
        }
        public void Dispose() { Stop.Dispose(); HoldStop.Dispose(); }
        private static void Cancel(CancellationTokenSource source)
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private const int WhMouseLl = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonDown = 0x0204;
    private const int WmRightButtonUp = 0x0205;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMiddleButtonUp = 0x0208;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const uint MouseInjected = 0x01;
    private readonly Func<MacroTrigger, bool, MacroDefinition?> _findMacro;
    private readonly Func<MacroTrigger> _getGShiftTrigger;
    private readonly MacroPlaybackService _playback;
    private readonly LowLevelMouseProc _callback;
    private readonly HashSet<MacroTrigger> _swallowedButtons = [];
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _playbackCancellations = new();
    private readonly object _stateLock = new();
    private IntPtr _hook;
    private bool _gShiftHeld;
    private MacroTrigger _gShiftHeldTrigger;
    private MacroTrigger _repeatTrigger;
    private MacroRepeatMode _repeatMode;
    private CancellationTokenSource? _repeatCancellation;
    private SequencePlaybackState? _sequence;

    public MouseMacroTrigger(Func<MacroTrigger, bool, MacroDefinition?> findMacro,
        Func<MacroTrigger> getGShiftTrigger, MacroPlaybackService playback)
    {
        _findMacro = findMacro;
        _getGShiftTrigger = getGShiftTrigger;
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
        lock (_stateLock)
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            StopRepeating();
            foreach (var cancellation in _playbackCancellations.Keys.ToArray())
            {
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            _swallowedButtons.Clear();
            _gShiftHeld = false;
            _gShiftHeldTrigger = MacroTrigger.None;
        }
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var kind = unchecked((int)message.ToInt64());
            var input = Marshal.PtrToStructure<MouseHookData>(data);
            if ((input.Flags & MouseInjected) == 0 && TryMapButtonMessage(kind, input.MouseData, out var trigger))
            {
                var isDown = kind is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown;
                var isUp = kind is WmLeftButtonUp or WmRightButtonUp or WmMiddleButtonUp or WmXButtonUp;
                if ((isDown || isUp) && HandleInput(trigger, isDown, consume: true)) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void ProcessExternalInput(MacroTrigger trigger, bool isDown) => HandleInput(trigger, isDown, consume: false);

    private bool HandleInput(MacroTrigger trigger, bool isDown, bool consume)
    {
        lock (_stateLock)
        {
            if (isDown && trigger == _getGShiftTrigger() && trigger != MacroTrigger.None)
            {
                _gShiftHeld = true;
                _gShiftHeldTrigger = trigger;
                if (consume) _swallowedButtons.Add(trigger);
                return consume;
            }

            if (!isDown)
            {
                if (_sequence?.Trigger == trigger)
                {
                    if (consume) _swallowedButtons.Remove(trigger);
                    _sequence.Release();
                    return consume;
                }

                var swallowed = consume && _swallowedButtons.Remove(trigger);
                if (_gShiftHeldTrigger == trigger)
                {
                    _gShiftHeld = false;
                    _gShiftHeldTrigger = MacroTrigger.None;
                }
                if (_repeatTrigger == trigger && _repeatMode == MacroRepeatMode.WhileHeld) StopRepeating();
                return swallowed;
            }

            var macro = _findMacro(trigger, _gShiftHeld);
            if (macro is null || !macro.HasActions) return false;
            if (_repeatMode == MacroRepeatMode.Toggle && _repeatTrigger == trigger &&
                macro.RepeatMode == MacroRepeatMode.Toggle)
            {
                StopRepeating();
                if (consume) _swallowedButtons.Add(trigger);
                return consume;
            }

            if (_repeatMode != MacroRepeatMode.None) StopRepeating();
            if (consume) _swallowedButtons.Add(trigger);
            if (macro.RepeatMode == MacroRepeatMode.Sequence)
            {
                var sequence = new SequencePlaybackState(trigger);
                _sequence = sequence;
                _repeatCancellation = sequence.Stop;
                _repeatTrigger = trigger;
                _repeatMode = MacroRepeatMode.Sequence;
                _playbackCancellations.TryAdd(sequence.Stop, 0);
                _ = PlaySequenceAsync(macro, sequence);
            }
            else
            {
                var cancellation = new CancellationTokenSource();
                _playbackCancellations.TryAdd(cancellation, 0);
                if (macro.RepeatMode != MacroRepeatMode.None)
                {
                    _repeatCancellation = cancellation;
                    _repeatTrigger = trigger;
                    _repeatMode = macro.RepeatMode;
                }
                _ = PlayMacroAsync(macro.Steps.ToArray(), macro.RepeatMode, macro.RepeatDelayMs, cancellation);
            }
            return consume;
        }
    }

    private async Task PlaySequenceAsync(MacroDefinition macro, SequencePlaybackState sequence)
    {
        try
        {
            await _playback.PlaySequenceAsync(macro.Steps.ToArray(), macro.HoldSteps.ToArray(),
                macro.ReleaseSteps.ToArray(), macro.RepeatDelayMs, sequence.Released.Task,
                sequence.Stop.Token, sequence.HoldStop.Token);
        }
        finally
        {
            _playbackCancellations.TryRemove(sequence.Stop, out _);
            lock (_stateLock)
            {
                if (ReferenceEquals(_sequence, sequence)) _sequence = null;
                if (ReferenceEquals(_repeatCancellation, sequence.Stop))
                {
                    _repeatCancellation = null;
                    _repeatTrigger = MacroTrigger.None;
                    _repeatMode = MacroRepeatMode.None;
                }
            }
            sequence.Dispose();
        }
    }

    private async Task PlayMacroAsync(IReadOnlyList<MacroStep> steps, MacroRepeatMode mode, int delayMs,
        CancellationTokenSource cancellation)
    {
        try { await _playback.PlayAsync(steps, mode, delayMs, cancellation.Token); }
        finally
        {
            _playbackCancellations.TryRemove(cancellation, out _);
            lock (_stateLock)
            {
                cancellation.Dispose();
                if (ReferenceEquals(_repeatCancellation, cancellation))
                {
                    _repeatCancellation = null;
                    _repeatTrigger = MacroTrigger.None;
                    _repeatMode = MacroRepeatMode.None;
                }
            }
        }
    }

    private void StopRepeating()
    {
        lock (_stateLock)
        {
            try { _repeatCancellation?.Cancel(); }
            catch (ObjectDisposedException) { }
            _sequence?.Cancel();
            _sequence = null;
            _repeatCancellation = null;
            _repeatTrigger = MacroTrigger.None;
            _repeatMode = MacroRepeatMode.None;
        }
    }

    public static bool TryMapButtonMessage(int message, uint mouseData, out MacroTrigger trigger)
    {
        trigger = message switch
        {
            WmLeftButtonDown or WmLeftButtonUp => MacroTrigger.LeftButton,
            WmRightButtonDown or WmRightButtonUp => MacroTrigger.RightButton,
            WmMiddleButtonDown or WmMiddleButtonUp => MacroTrigger.MiddleButton,
            WmXButtonDown or WmXButtonUp when ((mouseData >> 16) & 0xffff) == 1 => MacroTrigger.XButton1,
            WmXButtonDown or WmXButtonUp when ((mouseData >> 16) & 0xffff) == 2 => MacroTrigger.XButton2,
            _ => MacroTrigger.None
        };
        return trigger != MacroTrigger.None;
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
