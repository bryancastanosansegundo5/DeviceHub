using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NexoHub.Models;

/// <summary>Editable Nexo-side values; these are kept separate from verified device writes.</summary>
public sealed class DeviceLocalProfile : INotifyPropertyChanged
{
    private string _name = "Perfil principal";
    private bool _isXboxAccessoriesSlotPlaceholder;
    private string _applicationExecutable = "";
    private double _leftStickMacroThresholdPercent = 48.83;
    private double _rightStickMacroThresholdPercent = 48.83;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }
    [JsonIgnore]
    public string DisplayName
    {
        get => IsXboxAccessoriesSlotPlaceholder ? "Predeterminado" : Name;
        set { if (!IsXboxAccessoriesSlotPlaceholder) Name = value; }
    }
    public string StorageLabel { get; set; } = "Perfil Nexo local";
    public int? XboxAccessoriesSlot { get; set; }
    public bool IsXboxAccessoriesSlotPlaceholder
    {
        get => _isXboxAccessoriesSlotPlaceholder;
        set
        {
            if (_isXboxAccessoriesSlotPlaceholder == value) return;
            _isXboxAccessoriesSlotPlaceholder = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsXboxAccessoriesSlotPlaceholder)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }
    public string? XboxAccessoriesSourceName { get; set; }
    public string ApplicationExecutable
    {
        get => _applicationExecutable;
        set
        {
            if (_applicationExecutable == value) return;
            _applicationExecutable = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplicationExecutable)));
        }
    }
    public int Dpi1 { get; set; } = 400;
    public int Dpi2 { get; set; } = 800;
    public int Dpi3 { get; set; } = 1300;
    public int Dpi4 { get; set; } = 2000;
    public int Dpi5 { get; set; } = 6400;
    public int ShiftDpi { get; set; } = 1300;
    public int DpiStageCount { get; set; } = 4;
    public int ActiveDpiStage { get; set; } = 1;
    public int ReportRate { get; set; } = 1000;
    public int LeftTriggerStart { get; set; }
    public int LeftTriggerEnd { get; set; } = 10;
    public int RightTriggerStart { get; set; }
    public int RightTriggerEnd { get; set; } = 10;
    public Dictionary<string, string> XboxButtonAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool XboxSwapSticks { get; set; }
    public bool XboxInvertLeftStickY { get; set; }
    public bool XboxInvertRightStickY { get; set; }
    public bool XboxSwapTriggers { get; set; }
    public string XboxLeftStickCurve { get; set; } = "Instantáneo";
    public int XboxLeftStickCurveAdjustment { get; set; } = 10;
    public bool XboxLeftStickRadialCalculation { get; set; }
    public string XboxRightStickCurve { get; set; } = "Instantáneo";
    public int XboxRightStickCurveAdjustment { get; set; } = 10;
    public bool XboxRightStickRadialCalculation { get; set; }
    public int XboxLeftTriggerDeadzoneStart { get; set; }
    public int XboxLeftTriggerDeadzoneEnd { get; set; } = 10;
    public int XboxRightTriggerDeadzoneStart { get; set; }
    public int XboxRightTriggerDeadzoneEnd { get; set; } = 10;
    public bool XboxMirrorTriggers { get; set; } = true;
    public int XboxLeftTriggerVibration { get; set; } = 100;
    public int XboxLeftMainVibration { get; set; } = 100;
    public int XboxRightTriggerVibration { get; set; } = 100;
    public int XboxRightMainVibration { get; set; } = 100;
    public int XboxButtonBrightness { get; set; } = 100;
    public double LeftStickMacroThresholdPercent
    {
        get => _leftStickMacroThresholdPercent;
        set
        {
            value = Math.Clamp(value, 10, 90);
            if (Math.Abs(_leftStickMacroThresholdPercent - value) < 0.001) return;
            _leftStickMacroThresholdPercent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftStickMacroThresholdPercent)));
        }
    }
    public double RightStickMacroThresholdPercent
    {
        get => _rightStickMacroThresholdPercent;
        set
        {
            value = Math.Clamp(value, 10, 90);
            if (Math.Abs(_rightStickMacroThresholdPercent - value) < 0.001) return;
            _rightStickMacroThresholdPercent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightStickMacroThresholdPercent)));
        }
    }
    public string LightingEffect { get; set; } = "Arcoíris";
    public string LightingColor { get; set; } = "53D6C2";
    public int LightingBrightness { get; set; } = 70;
    public bool MouseLightingEnabled { get; set; }
    public int LightingCycleDurationMs { get; set; } = 8000;
    public Dictionary<string, string> KeyboardKeyColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int EqBass { get; set; }
    public int EqLowMid { get; set; }
    public int EqMid { get; set; }
    public int EqHighMid { get; set; }
    public int EqTreble { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
