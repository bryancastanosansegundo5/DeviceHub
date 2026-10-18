using NexoHub.Models;

namespace NexoHub.Services;

public readonly record struct XboxMacroInputState(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftStickX,
    short LeftStickY,
    short RightStickX,
    short RightStickY);

public readonly record struct XboxMacroInputTransition(MacroTrigger Trigger, bool IsDown);

public sealed class XboxMacroInputTracker
{
    private readonly HashSet<MacroTrigger> _pressed = [];

    public IReadOnlyList<XboxMacroInputTransition> Update(XboxMacroInputState? state, DeviceLocalProfile? profile = null)
    {
        var current = state is XboxMacroInputState input
            ? GetPressedTriggers(input, profile?.LeftTriggerStart ?? 0, profile?.LeftTriggerEnd ?? 10,
                profile?.RightTriggerStart ?? 0, profile?.RightTriggerEnd ?? 10,
                profile?.LeftStickMacroThresholdPercent ?? 48.83,
                profile?.RightStickMacroThresholdPercent ?? 48.83)
            : [];
        var transitions = _pressed.Except(current).Order()
            .Select(trigger => new XboxMacroInputTransition(trigger, false))
            .Concat(current.Except(_pressed).Order()
                .Select(trigger => new XboxMacroInputTransition(trigger, true)))
            .ToArray();
        _pressed.Clear();
        _pressed.UnionWith(current);
        return transitions;
    }

    public static HashSet<MacroTrigger> GetPressedTriggers(XboxMacroInputState input,
        int leftTriggerStart = 0, int leftTriggerEnd = 10, int rightTriggerStart = 0, int rightTriggerEnd = 10,
        double leftStickMacroThresholdPercent = 48.83, double rightStickMacroThresholdPercent = 48.83)
    {
        var pressed = new HashSet<MacroTrigger>();
        AddButton(0x0001, MacroTrigger.XboxDpadUp);
        AddButton(0x0002, MacroTrigger.XboxDpadDown);
        AddButton(0x0004, MacroTrigger.XboxDpadLeft);
        AddButton(0x0008, MacroTrigger.XboxDpadRight);
        AddButton(0x0010, MacroTrigger.XboxStart);
        AddButton(0x0020, MacroTrigger.XboxView);
        AddButton(0x0040, MacroTrigger.XboxLeftStickClick);
        AddButton(0x0080, MacroTrigger.XboxRightStickClick);
        AddButton(0x0100, MacroTrigger.XboxLeftShoulder);
        AddButton(0x0200, MacroTrigger.XboxRightShoulder);
        AddButton(0x1000, MacroTrigger.XboxA);
        AddButton(0x2000, MacroTrigger.XboxB);
        AddButton(0x4000, MacroTrigger.XboxX);
        AddButton(0x8000, MacroTrigger.XboxY);
        if (input.LeftTrigger >= GetTriggerThreshold(leftTriggerStart, leftTriggerEnd)) pressed.Add(MacroTrigger.XboxLeftTrigger);
        if (input.RightTrigger >= GetTriggerThreshold(rightTriggerStart, rightTriggerEnd)) pressed.Add(MacroTrigger.XboxRightTrigger);
        AddStick(input.LeftStickX, input.LeftStickY, left: true, thresholdPercent: leftStickMacroThresholdPercent);
        AddStick(input.RightStickX, input.RightStickY, left: false, thresholdPercent: rightStickMacroThresholdPercent);
        return pressed;

        void AddButton(ushort mask, MacroTrigger trigger)
        {
            if ((input.Buttons & mask) != 0) pressed.Add(trigger);
        }

        void AddStick(short x, short y, bool left, double thresholdPercent)
        {
            var horizontal = Math.Abs((int)x);
            var vertical = Math.Abs((int)y);
            var threshold = (int)Math.Round(Math.Clamp(thresholdPercent, 10, 90) * short.MaxValue / 100);
            if ((long)x * x + (long)y * y < (long)threshold * threshold) return;

            var direction = horizontal * 2 <= vertical
                ? y > 0 ? 0 : 4
                : vertical * 2 <= horizontal
                    ? x > 0 ? 2 : 6
                    : x > 0
                        ? y > 0 ? 1 : 3
                        : y > 0 ? 7 : 5;
            var trigger = (left, direction) switch
            {
                (true, 0) => MacroTrigger.XboxLeftStickUp,
                (true, 1) => MacroTrigger.XboxLeftStickUpRight,
                (true, 2) => MacroTrigger.XboxLeftStickRight,
                (true, 3) => MacroTrigger.XboxLeftStickDownRight,
                (true, 4) => MacroTrigger.XboxLeftStickDown,
                (true, 5) => MacroTrigger.XboxLeftStickDownLeft,
                (true, 6) => MacroTrigger.XboxLeftStickLeft,
                (true, _) => MacroTrigger.XboxLeftStickUpLeft,
                (false, 0) => MacroTrigger.XboxRightStickUp,
                (false, 1) => MacroTrigger.XboxRightStickUpRight,
                (false, 2) => MacroTrigger.XboxRightStickRight,
                (false, 3) => MacroTrigger.XboxRightStickDownRight,
                (false, 4) => MacroTrigger.XboxRightStickDown,
                (false, 5) => MacroTrigger.XboxRightStickDownLeft,
                (false, 6) => MacroTrigger.XboxRightStickLeft,
                _ => MacroTrigger.XboxRightStickUpLeft
            };
            pressed.Add(trigger);
        }
    }

    private static byte GetTriggerThreshold(int start, int end)
    {
        start = Math.Clamp(start, 0, 10);
        end = Math.Clamp(end, start, 10);
        return (byte)Math.Max(1, Math.Round((start + end) * 255d / 20d, MidpointRounding.AwayFromZero));
    }
}
