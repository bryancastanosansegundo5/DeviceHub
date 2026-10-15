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
    XButton2
}

public enum MacroInputType
{
    Key,
    MouseButton,
    MouseWheel
}

public enum MacroMouseButton
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

public sealed record MacroBindingOption(MacroTrigger Trigger, string Label)
{
    public static IReadOnlyList<MacroBindingOption> All { get; } =
    [
        new(MacroTrigger.None, "Sin asignar"),
        new(MacroTrigger.XButton1, "Botón lateral 4"),
        new(MacroTrigger.XButton2, "Botón lateral 5")
    ];

    public override string ToString() => Label;
}

public sealed class MacroDefinition : INotifyPropertyChanged
{
    private string _name = "Nueva macro";
    private MacroTrigger _binding;
    private ObservableCollection<MacroStep> _steps = [];

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
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
        }
    }

    [JsonIgnore]
    public MacroBindingOption BindingOption
    {
        get => MacroBindingOption.All.First(option => option.Trigger == Binding);
        set { if (value is not null) Binding = value.Trigger; }
    }

    [JsonIgnore]
    public string Summary => $"{Steps.Count} acciones · {BindingOption.Label}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void StepsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(Summary));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class MacroStep
{
    public MacroInputType InputType { get; set; } = MacroInputType.Key;
    public int VirtualKey { get; set; }
    public uint ScanCode { get; set; }
    public bool IsDown { get; set; }
    public bool IsExtended { get; set; }
    public int DelayMs { get; set; }
    public MacroMouseButton MouseButton { get; set; }
    public int WheelDelta { get; set; }

    [JsonIgnore]
    public string Index { get; set; } = "";
    [JsonIgnore]
    public string Description
    {
        get
        {
            if (InputType == MacroInputType.MouseWheel)
                return $"Rueda · {(WheelDelta > 0 ? "arriba" : "abajo")}";
            if (InputType == MacroInputType.MouseButton)
                return $"{(IsDown ? "Pulsar" : "Soltar")} · Ratón {MouseButtonName(MouseButton)}";
            var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
            var name = key == Key.None ? $"Tecla 0x{VirtualKey:X2}" : key.ToString();
            return $"{(IsDown ? "Pulsar" : "Soltar")} · {name}";
        }
    }
    [JsonIgnore]
    public string DelayLabel => $"{DelayMs} ms";

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
