using System.Runtime.InteropServices;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class MacroPlaybackService
{
    private const uint KeyUp = 0x0002;
    private const uint ExtendedKey = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseXDown = 0x0080;
    private const uint MouseXUp = 0x0100;
    private const uint MouseWheel = 0x0800;
    private readonly SemaphoreSlim _playbackLock = new(1, 1);
    private CancellationTokenSource? _currentPlayback;

    public async Task PlayAsync(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0) return;
        await _playbackLock.WaitAsync();
        using var cancellation = new CancellationTokenSource();
        _currentPlayback = cancellation;
        var held = new Dictionary<(MacroInputType Type, int Code), MacroStep>();
        try
        {
            foreach (var step in steps)
            {
                if (step.DelayMs > 0) await Task.Delay(step.DelayMs, cancellation.Token);
                if (step.InputType == MacroInputType.Key)
                {
                    var flags = (step.IsDown ? 0u : KeyUp) | (step.IsExtended ? ExtendedKey : 0u);
                    keybd_event((byte)step.VirtualKey, (byte)step.ScanCode, flags, UIntPtr.Zero);
                    UpdateHeld(held, step, step.VirtualKey);
                }
                else
                {
                    var (flags, data) = MouseEvent(step);
                    mouse_event(flags, 0, 0, data, UIntPtr.Zero);
                    if (step.InputType == MacroInputType.MouseButton)
                        UpdateHeld(held, step, (int)step.MouseButton);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var step in held.Values) Release(step);
            _currentPlayback = null;
            _playbackLock.Release();
        }
    }

    public void CancelCurrent() => _currentPlayback?.Cancel();

    private static void UpdateHeld(Dictionary<(MacroInputType Type, int Code), MacroStep> held,
        MacroStep step, int code)
    {
        var id = (step.InputType, code);
        if (step.IsDown) held[id] = step;
        else held.Remove(id);
    }

    private static void Release(MacroStep step)
    {
        if (step.InputType == MacroInputType.Key)
        {
            keybd_event((byte)step.VirtualKey, (byte)step.ScanCode,
                KeyUp | (step.IsExtended ? ExtendedKey : 0u), UIntPtr.Zero);
            return;
        }
        var release = new MacroStep { InputType = step.InputType, MouseButton = step.MouseButton, IsDown = false };
        var (flags, data) = MouseEvent(release);
        mouse_event(flags, 0, 0, data, UIntPtr.Zero);
    }

    private static (uint Flags, uint Data) MouseEvent(MacroStep step)
    {
        if (step.InputType == MacroInputType.MouseWheel)
            return (MouseWheel, unchecked((uint)step.WheelDelta));
        var flags = step.MouseButton switch
        {
            MacroMouseButton.Left => step.IsDown ? MouseLeftDown : MouseLeftUp,
            MacroMouseButton.Right => step.IsDown ? MouseRightDown : MouseRightUp,
            MacroMouseButton.Middle => step.IsDown ? MouseMiddleDown : MouseMiddleUp,
            MacroMouseButton.XButton1 or MacroMouseButton.XButton2 => step.IsDown ? MouseXDown : MouseXUp,
            _ => 0u
        };
        var data = step.MouseButton switch
        {
            MacroMouseButton.XButton1 => 1u,
            MacroMouseButton.XButton2 => 2u,
            _ => 0u
        };
        return (flags, data);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
