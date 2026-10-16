using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace NexoHub.Models;

public enum MacroTrigger
{
    None,
    XButton1,
    XButton2,
    LeftButton,
    RightButton,
    MiddleButton,
    XboxDpadUp,
    XboxDpadDown,
    XboxDpadLeft,
    XboxDpadRight,
    XboxStart,
    XboxView,
    XboxLeftStickClick,
    XboxRightStickClick,
    XboxLeftShoulder,
    XboxRightShoulder,
    XboxLeftTrigger,
    XboxRightTrigger,
    XboxA,
    XboxB,
    XboxX,
    XboxY,
    XboxLeftStickUp,
    XboxLeftStickUpRight,
    XboxLeftStickRight,
    XboxLeftStickDownRight,
    XboxLeftStickDown,
    XboxLeftStickDownLeft,
    XboxLeftStickLeft,
    XboxLeftStickUpLeft,
    XboxRightStickUp,
    XboxRightStickUpRight,
    XboxRightStickRight,
    XboxRightStickDownRight,
    XboxRightStickDown,
    XboxRightStickDownLeft,
    XboxRightStickLeft,
    XboxRightStickUpLeft
}

public enum MacroInputType
{
    Key,
    MouseButton,
    MouseWheel,
    SystemCommand
}

public enum MacroMouseButton
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

public enum MacroSystemCommand
{
    VolumeMute,
    VolumeDown,
    VolumeUp,
    PlayPause,
    NextTrack,
    PreviousTrack,
    BrowserBack,
    BrowserForward
}

public enum MacroRepeatMode
{
    None,
    WhileHeld,
    Toggle,
    Sequence
}

public enum MacroSequenceSection
{
    Press,
    Hold,
    Release
}

public sealed record MacroBindingOption(MacroTrigger Trigger, string Label)
{
    public static IReadOnlyList<MacroBindingOption> All { get; } =
    [
        new(MacroTrigger.None, "Sin asignar"),
        new(MacroTrigger.LeftButton, "Botón izquierdo"),
        new(MacroTrigger.RightButton, "Botón derecho"),
        new(MacroTrigger.MiddleButton, "Botón central"),
        new(MacroTrigger.XButton1, "Botón lateral 4"),
        new(MacroTrigger.XButton2, "Botón lateral 5"),
        new(MacroTrigger.XboxDpadUp, "Mando · Cruceta ↑"),
        new(MacroTrigger.XboxDpadDown, "Mando · Cruceta ↓"),
        new(MacroTrigger.XboxDpadLeft, "Mando · Cruceta ←"),
        new(MacroTrigger.XboxDpadRight, "Mando · Cruceta →"),
        new(MacroTrigger.XboxStart, "Mando · Menú"),
        new(MacroTrigger.XboxView, "Mando · Ver"),
        new(MacroTrigger.XboxLeftStickClick, "Mando · Pulsar stick izq."),
        new(MacroTrigger.XboxRightStickClick, "Mando · Pulsar stick der."),
        new(MacroTrigger.XboxLeftShoulder, "Mando · LB"),
        new(MacroTrigger.XboxRightShoulder, "Mando · RB"),
        new(MacroTrigger.XboxLeftTrigger, "Mando · LT"),
        new(MacroTrigger.XboxRightTrigger, "Mando · RT"),
        new(MacroTrigger.XboxA, "Mando · A"),
        new(MacroTrigger.XboxB, "Mando · B"),
        new(MacroTrigger.XboxX, "Mando · X"),
        new(MacroTrigger.XboxY, "Mando · Y"),
        new(MacroTrigger.XboxLeftStickUp, "Mando · Stick izq. ↑"),
        new(MacroTrigger.XboxLeftStickUpRight, "Mando · Stick izq. ↗"),
        new(MacroTrigger.XboxLeftStickRight, "Mando · Stick izq. →"),
        new(MacroTrigger.XboxLeftStickDownRight, "Mando · Stick izq. ↘"),
        new(MacroTrigger.XboxLeftStickDown, "Mando · Stick izq. ↓"),
        new(MacroTrigger.XboxLeftStickDownLeft, "Mando · Stick izq. ↙"),
        new(MacroTrigger.XboxLeftStickLeft, "Mando · Stick izq. ←"),
        new(MacroTrigger.XboxLeftStickUpLeft, "Mando · Stick izq. ↖"),
        new(MacroTrigger.XboxRightStickUp, "Mando · Stick der. ↑"),
        new(MacroTrigger.XboxRightStickUpRight, "Mando · Stick der. ↗"),
        new(MacroTrigger.XboxRightStickRight, "Mando · Stick der. →"),
        new(MacroTrigger.XboxRightStickDownRight, "Mando · Stick der. ↘"),
        new(MacroTrigger.XboxRightStickDown, "Mando · Stick der. ↓"),
        new(MacroTrigger.XboxRightStickDownLeft, "Mando · Stick der. ↙"),
        new(MacroTrigger.XboxRightStickLeft, "Mando · Stick der. ←"),
        new(MacroTrigger.XboxRightStickUpLeft, "Mando · Stick der. ↖")
    ];

    public override string ToString() => Label;
}

public sealed record MacroRepeatOption(MacroRepeatMode Mode, string Label)
{
    public static IReadOnlyList<MacroRepeatOption> All { get; } =
    [
        new(MacroRepeatMode.None, "Sin repetición"),
        new(MacroRepeatMode.WhileHeld, "Mientras se mantiene"),
        new(MacroRepeatMode.Toggle, "Alternar hasta volver a pulsar"),
        new(MacroRepeatMode.Sequence, "Secuencia: pulsar / mantener / soltar")
    ];
}

public sealed record MacroSequenceSectionOption(MacroSequenceSection Section, string Label)
{
    public static IReadOnlyList<MacroSequenceSectionOption> All { get; } =
    [
        new(MacroSequenceSection.Press, "Al pulsar"),
        new(MacroSequenceSection.Hold, "Mientras se mantiene"),
        new(MacroSequenceSection.Release, "Al soltar")
    ];
}

public sealed record MacroSystemCommandOption(MacroSystemCommand Command, string Label)
{
    public static IReadOnlyList<MacroSystemCommandOption> All { get; } =
    [
        new(MacroSystemCommand.VolumeMute, "Silenciar volumen"),
        new(MacroSystemCommand.VolumeDown, "Bajar volumen"),
        new(MacroSystemCommand.VolumeUp, "Subir volumen"),
        new(MacroSystemCommand.PlayPause, "Reproducir / pausar"),
        new(MacroSystemCommand.NextTrack, "Pista siguiente"),
        new(MacroSystemCommand.PreviousTrack, "Pista anterior"),
        new(MacroSystemCommand.BrowserBack, "Atrás del navegador"),
        new(MacroSystemCommand.BrowserForward, "Adelante del navegador")
    ];
}

public sealed class MacroDefinition : INotifyPropertyChanged
{
    private string _name = "Nueva macro";
    private string _applicationExecutable = "";
    private MacroTrigger _binding;
    private MacroTrigger _shiftBinding;
    private MacroRepeatMode _repeatMode;
    private int _repeatDelayMs = 25;
    private ObservableCollection<MacroStep> _steps = [];
    private ObservableCollection<MacroStep> _holdSteps = [];
    private ObservableCollection<MacroStep> _releaseSteps = [];

    public MacroDefinition()
    {
        _steps.CollectionChanged += StepsChanged;
        _holdSteps.CollectionChanged += StepsChanged;
        _releaseSteps.CollectionChanged += StepsChanged;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }
    public string ApplicationExecutable
    {
        get => _applicationExecutable;
        set
        {
            if (_applicationExecutable == value) return;
            _applicationExecutable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }
    public MacroTrigger Binding
    {
        get => _binding;
        set
        {
            if (_binding == value) return;
            _binding = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BindingOption));
            OnPropertyChanged(nameof(Summary));
        }
    }
    public MacroTrigger ShiftBinding
    {
        get => _shiftBinding;
        set
        {
            if (_shiftBinding == value) return;
            _shiftBinding = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShiftBindingOption));
            OnPropertyChanged(nameof(Summary));
        }
    }
    public MacroRepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode == value) return;
            _repeatMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RepeatModeOption));
            OnPropertyChanged(nameof(IsSequence));
            OnPropertyChanged(nameof(Summary));
        }
    }
    public int RepeatDelayMs
    {
        get => _repeatDelayMs;
        set { if (_repeatDelayMs == value) return; _repeatDelayMs = value; OnPropertyChanged(); }
    }
    public ObservableCollection<MacroStep> Steps
    {
        get => _steps;
        set
        {
            _steps.CollectionChanged -= StepsChanged;
            _steps = value ?? [];
            _steps.CollectionChanged += StepsChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasActions));
        }
    }
    public ObservableCollection<MacroStep> HoldSteps
    {
        get => _holdSteps;
        set
        {
            _holdSteps.CollectionChanged -= StepsChanged;
            _holdSteps = value ?? [];
            _holdSteps.CollectionChanged += StepsChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasActions));
        }
    }
    public ObservableCollection<MacroStep> ReleaseSteps
    {
        get => _releaseSteps;
        set
        {
            _releaseSteps.CollectionChanged -= StepsChanged;
            _releaseSteps = value ?? [];
            _releaseSteps.CollectionChanged += StepsChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasActions));
        }
    }

    [JsonIgnore]
    public MacroBindingOption BindingOption
    {
        get => MacroBindingOption.All.First(option => option.Trigger == Binding);
        set { if (value is not null) Binding = value.Trigger; }
    }

    [JsonIgnore]
    public MacroBindingOption ShiftBindingOption
    {
        get => MacroBindingOption.All.First(option => option.Trigger == ShiftBinding);
        set { if (value is not null) ShiftBinding = value.Trigger; }
    }

    [JsonIgnore]
    public MacroRepeatOption RepeatModeOption
    {
        get => MacroRepeatOption.All.First(option => option.Mode == RepeatMode);
        set { if (value is not null) RepeatMode = value.Mode; }
    }

    [JsonIgnore]
    public bool IsSequence => RepeatMode == MacroRepeatMode.Sequence;

    [JsonIgnore]
    public bool HasActions => Steps.Count + HoldSteps.Count + ReleaseSteps.Count > 0;

    [JsonIgnore]
    public string Summary => $"{Steps.Count + HoldSteps.Count + ReleaseSteps.Count} acciones · {BindingOption.Label}" +
        (ShiftBinding == MacroTrigger.None ? "" : $" · G-Shift: {ShiftBindingOption.Label}") +
        (string.IsNullOrWhiteSpace(ApplicationExecutable) ? "" : $" · {Path.GetFileName(ApplicationExecutable)}") +
        (RepeatMode == MacroRepeatMode.None ? "" : $" · {RepeatModeOption.Label}");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void StepsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasActions));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class MacroStep : INotifyPropertyChanged
{
    private string _index = "";
    private int _delayMs;

    public MacroInputType InputType { get; set; } = MacroInputType.Key;
    public int VirtualKey { get; set; }
    public uint ScanCode { get; set; }
    public bool IsDown { get; set; }
    public bool IsExtended { get; set; }
    public int DelayMs
    {
        get => _delayMs;
        set { if (_delayMs == value) return; _delayMs = value; OnPropertyChanged(); }
    }
    public MacroMouseButton MouseButton { get; set; }
    public int WheelDelta { get; set; }
    public MacroSystemCommand SystemCommand { get; set; }

    [JsonIgnore]
    public string Index
    {
        get => _index;
        set { if (_index == value) return; _index = value; OnPropertyChanged(); }
    }
    [JsonIgnore]
    public string Description
    {
        get
        {
            if (InputType == MacroInputType.MouseWheel)
                return $"Rueda · {(WheelDelta > 0 ? "arriba" : "abajo")}";
            if (InputType == MacroInputType.SystemCommand)
                return MacroSystemCommandOption.All.First(option => option.Command == SystemCommand).Label;
            if (InputType == MacroInputType.MouseButton)
                return $"{(IsDown ? "Pulsar" : "Soltar")} · Ratón {MouseButtonName(MouseButton)}";
            var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
            var name = key == Key.None ? $"Tecla 0x{VirtualKey:X2}" : key.ToString();
            return $"{(IsDown ? "Pulsar" : "Soltar")} · {name}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private static string MouseButtonName(MacroMouseButton button) => button switch
    {
        MacroMouseButton.Left => "izquierdo",
        MacroMouseButton.Right => "derecho",
        MacroMouseButton.Middle => "central",
        MacroMouseButton.XButton1 => "lateral 4",
        MacroMouseButton.XButton2 => "lateral 5",
        _ => "desconocido"
    };
}
