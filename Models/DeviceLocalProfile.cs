namespace NexoHub.Models;

/// <summary>Editable Nexo-side values; these are kept separate from verified device writes.</summary>
public sealed class DeviceLocalProfile
{
    public string Name { get; set; } = "Perfil principal";
    public int Dpi1 { get; set; } = 400;
    public int Dpi2 { get; set; } = 800;
    public int Dpi3 { get; set; } = 1300;
    public int Dpi4 { get; set; } = 2000;
    public int ReportRate { get; set; } = 1000;
    public int LeftTriggerStart { get; set; }
    public int LeftTriggerEnd { get; set; } = 10;
    public int RightTriggerStart { get; set; }
    public int RightTriggerEnd { get; set; } = 10;
    public string LightingEffect { get; set; } = "Arcoíris";
    public string LightingColor { get; set; } = "53D6C2";
    public int LightingBrightness { get; set; } = 70;
    public Dictionary<string, string> KeyboardKeyColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int EqBass { get; set; }
    public int EqLowMid { get; set; }
    public int EqMid { get; set; }
    public int EqHighMid { get; set; }
    public int EqTreble { get; set; }
}
