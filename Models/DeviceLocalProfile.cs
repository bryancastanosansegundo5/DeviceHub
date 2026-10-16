using System.ComponentModel;

namespace NexoHub.Models;

/// <summary>Editable Nexo-side values; these are kept separate from verified device writes.</summary>
public sealed class DeviceLocalProfile : INotifyPropertyChanged
{
    private string _name = "Perfil principal";
    private string _applicationExecutable = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
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
