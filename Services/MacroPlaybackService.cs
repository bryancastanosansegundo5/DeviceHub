using System.IO;
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

    public async Task PlayAsync(IReadOnlyList<MacroStep> steps, MacroRepeatMode repeatMode = MacroRepeatMode.None,
        int repeatDelayMs = 25, CancellationToken cancellationToken = default)
    {
        if (steps.Count == 0) return;
        if (!Enum.IsDefined(repeatMode) || repeatMode == MacroRepeatMode.Sequence || repeatDelayMs is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(repeatMode));
        await Task.Yield();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lockHeld = false;
        var held = new Dictionary<(MacroInputType Type, int Code), MacroStep>();
        try
        {
            await _playbackLock.WaitAsync(cancellation.Token);
            lockHeld = true;
            _currentPlayback = cancellation;
            do
            {
                await ExecuteStepsAsync(steps, cancellation.Token, held);
                if (repeatMode != MacroRepeatMode.None)
                    await Task.Delay(repeatDelayMs, cancellation.Token);
            } while (repeatMode != MacroRepeatMode.None && !cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var step in held.Values) Release(step);
            if (lockHeld)
            {
                _currentPlayback = null;
                _playbackLock.Release();
            }
        }
    }

    public async Task PlaySequenceAsync(IReadOnlyList<MacroStep> pressSteps, IReadOnlyList<MacroStep> holdSteps,
        IReadOnlyList<MacroStep> releaseSteps, int repeatDelayMs, Task releaseSignal,
        CancellationToken stoppingToken, CancellationToken holdStoppingToken)
    {
        if (repeatDelayMs is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(repeatDelayMs));
        await Task.Yield();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var holdStopping = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, holdStoppingToken);
        var lockHeld = false;
        var held = new Dictionary<(MacroInputType Type, int Code), MacroStep>();
        try
        {
            await _playbackLock.WaitAsync(stopping.Token);
            lockHeld = true;
            _currentPlayback = stopping;
            await ExecuteStepsAsync(pressSteps, stopping.Token, held);
            if (holdSteps.Count == 0)
                await releaseSignal.WaitAsync(stopping.Token);
            else
            {
                while (!releaseSignal.IsCompleted && !stopping.IsCancellationRequested)
                {
                    try
                    {
                        await ExecuteStepsAsync(holdSteps, holdStopping.Token, held);
                        if (!releaseSignal.IsCompleted)
                            await Task.Delay(repeatDelayMs, holdStopping.Token);
                    }
                    catch (OperationCanceledException) when (releaseSignal.IsCompleted)
                    {
                        break;
                    }
                }
            }
            if (releaseSignal.IsCompleted && !stopping.IsCancellationRequested)
                await ExecuteStepsAsync(releaseSteps, stopping.Token, held);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var step in held.Values) Release(step);
            if (lockHeld)
            {
                _currentPlayback = null;
                _playbackLock.Release();
            }
        }
    }

    public void CancelCurrent() => _currentPlayback?.Cancel();

    private static async Task ExecuteStepsAsync(IReadOnlyList<MacroStep> steps, CancellationToken cancellation,
        Dictionary<(MacroInputType Type, int Code), MacroStep> held)
    {
        foreach (var step in steps)
        {
            if (step.DelayMs is < 0 or > 10000) throw new InvalidDataException("La pausa de la macro debe estar entre 0 y 10000 ms.");
            if (step.DelayMs > 0) await Task.Delay(step.DelayMs, cancellation);
            if (step.InputType == MacroInputType.Key)
            {
                if (step.VirtualKey is < 1 or > 255) throw new InvalidDataException("El código de tecla de la macro no es válido.");
                var flags = (step.IsDown ? 0u : KeyUp) | (step.IsExtended ? ExtendedKey : 0u);
                keybd_event((byte)step.VirtualKey, (byte)step.ScanCode, flags, UIntPtr.Zero);
                UpdateHeld(held, step, step.VirtualKey);
            }
            else if (step.InputType == MacroInputType.SystemCommand)
            {
                var virtualKey = GetSystemCommandVirtualKey(step.SystemCommand);
                keybd_event(virtualKey, 0, ExtendedKey, UIntPtr.Zero);
                keybd_event(virtualKey, 0, KeyUp | ExtendedKey, UIntPtr.Zero);
            }
            else if (step.InputType is MacroInputType.MouseButton or MacroInputType.MouseWheel)
            {
                var (flags, data) = MouseEvent(step);
                mouse_event(flags, 0, 0, data, UIntPtr.Zero);
                if (step.InputType == MacroInputType.MouseButton)
                    UpdateHeld(held, step, (int)step.MouseButton);
            }
            else throw new InvalidDataException("El tipo de acción de macro no es válido.");
        }
    }

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

    public static byte GetSystemCommandVirtualKey(MacroSystemCommand command) => command switch
    {
        MacroSystemCommand.VolumeMute => 0xAD,
        MacroSystemCommand.VolumeDown => 0xAE,
        MacroSystemCommand.VolumeUp => 0xAF,
        MacroSystemCommand.NextTrack => 0xB0,
        MacroSystemCommand.PreviousTrack => 0xB1,
        MacroSystemCommand.PlayPause => 0xB3,
        MacroSystemCommand.BrowserBack => 0xA6,
        MacroSystemCommand.BrowserForward => 0xA7,
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
