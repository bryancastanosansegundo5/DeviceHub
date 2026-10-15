using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NexoHub.ViewModels;

public sealed class XboxInputViewModel : INotifyPropertyChanged
{
    private bool _isConnected;
    private int _leftTrigger;
    private int _rightTrigger;
    private string _buttonsLabel = "Esperando datos del mando…";
    private string _leftStickLabel = "X 0% · Y 0%";
    private string _rightStickLabel = "X 0% · Y 0%";

    public bool IsConnected
    {
        get => _isConnected;
        private set => Set(ref _isConnected, value);
    }

    public int LeftTrigger
    {
        get => _leftTrigger;
        private set => Set(ref _leftTrigger, value);
    }

    public int RightTrigger
    {
        get => _rightTrigger;
        private set => Set(ref _rightTrigger, value);
    }

    public string ButtonsLabel
    {
        get => _buttonsLabel;
        private set => Set(ref _buttonsLabel, value);
    }

    public string LeftStickLabel
    {
        get => _leftStickLabel;
        private set => Set(ref _leftStickLabel, value);
    }

    public string RightStickLabel
    {
        get => _rightStickLabel;
        private set => Set(ref _rightStickLabel, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh(int slot)
    {
        try
        {
            if (XInputGetState((uint)slot, out var state) != 0)
            {
                IsConnected = false;
                ButtonsLabel = "Sin datos XInput · revisa el cable o la conexión del mando.";
                LeftTrigger = 0;
                RightTrigger = 0;
                LeftStickLabel = "X 0% · Y 0%";
                RightStickLabel = "X 0% · Y 0%";
                return;
            }

            IsConnected = true;
            ButtonsLabel = DescribeButtons(state.Gamepad.Buttons);
            LeftTrigger = ToPercent(state.Gamepad.LeftTrigger);
            RightTrigger = ToPercent(state.Gamepad.RightTrigger);
            LeftStickLabel = DescribeStick(state.Gamepad.LeftThumbX, state.Gamepad.LeftThumbY);
            RightStickLabel = DescribeStick(state.Gamepad.RightThumbX, state.Gamepad.RightThumbY);
        }
        catch (DllNotFoundException)
        {
            IsConnected = false;
            ButtonsLabel = "XInput no está disponible en esta versión de Windows.";
        }
        catch (EntryPointNotFoundException)
        {
            IsConnected = false;
            ButtonsLabel = "La API XInput de este sistema no ofrece lectura de estado.";
        }
    }

    private static string DescribeButtons(ushort buttons)
    {
        var pressed = new List<string>(12);
        Add(0x0001, "↑"); Add(0x0002, "↓"); Add(0x0004, "←"); Add(0x0008, "→");
        Add(0x0010, "Start"); Add(0x0020, "View"); Add(0x0040, "LS"); Add(0x0080, "RS");
        Add(0x0100, "LB"); Add(0x0200, "RB"); Add(0x1000, "A"); Add(0x2000, "B");
        Add(0x4000, "X"); Add(0x8000, "Y");
        return pressed.Count == 0 ? "Botones: ninguno · pulsa un control para probarlo" : $"Botones: {string.Join(" · ", pressed)}";

        void Add(ushort mask, string label)
        {
            if ((buttons & mask) == mask) pressed.Add(label);
        }
    }

    private static int ToPercent(byte value) => (int)Math.Round(value * 100d / 255d);

    private static string DescribeStick(short x, short y)
    {
        static int Percent(short value) => (int)Math.Round(value * (value < 0 ? 100d / 32768d : 100d / 32767d));
        return string.Format(CultureInfo.InvariantCulture, "X {0:+0;-0;0}% · Y {1:+0;-0;0}%", Percent(x), Percent(y));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }
}
