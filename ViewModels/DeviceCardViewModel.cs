using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NexoHub.Models;

namespace NexoHub.ViewModels;

public sealed class DeviceCardViewModel : INotifyPropertyChanged
{
    private int _activeLogitechDpiStage;
    private DeviceLocalProfile _localProfile;
    private string _logitechConnectionStatus = "Sin comprobar";

    public DeviceCardViewModel(HardwareDevice device, AdapterInfo adapter, DeviceLocalProfile? localProfile = null,
        IReadOnlyList<DeviceLocalProfile>? localProfiles = null, string? activeProfileId = null)
    {
        Device = device;
        Adapter = adapter;
        Capabilities = new ObservableCollection<CapabilityInfo>(adapter.Capabilities);
        var profiles = localProfiles?.ToList() ?? [];
        if (profiles.Count == 0) profiles.Add(localProfile ?? new DeviceLocalProfile());
        LocalProfiles = new ObservableCollection<DeviceLocalProfile>(profiles);
        _localProfile = profiles.FirstOrDefault(profile => profile.Id == activeProfileId) ?? profiles[0];
        LocalProfile.DpiStageCount = Math.Clamp(LocalProfile.DpiStageCount, 1, 5);
        _activeLogitechDpiStage = Math.Clamp(LocalProfile.ActiveDpiStage, 1, LocalProfile.DpiStageCount);
        LocalProfile.ActiveDpiStage = _activeLogitechDpiStage;
        XboxInput = new XboxInputViewModel();
        AudioEndpoints = new ObservableCollection<AudioEndpointViewModel>((device.AudioEndpoints ?? [])
            .Select(endpoint => new AudioEndpointViewModel(endpoint)));
    }

    public HardwareDevice Device { get; }
    public AdapterInfo Adapter { get; }
    public ObservableCollection<CapabilityInfo> Capabilities { get; }
    public ObservableCollection<DeviceLocalProfile> LocalProfiles { get; }
    public DeviceLocalProfile LocalProfile
    {
        get => _localProfile;
        set
        {
            if (value is null || ReferenceEquals(_localProfile, value) || !LocalProfiles.Contains(value)) return;
            _localProfile = value;
            _activeLogitechDpiStage = Math.Clamp(value.ActiveDpiStage, 1, Math.Clamp(value.DpiStageCount, 1, 5));
            value.ActiveDpiStage = _activeLogitechDpiStage;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDeleteLocalProfile));
            OnPropertyChanged(nameof(CanAddLocalProfile));
            OnPropertyChanged(nameof(LogitechDpiStageCount));
            OnPropertyChanged(nameof(HasLogitechDpiStage2));
            OnPropertyChanged(nameof(HasLogitechDpiStage3));
            OnPropertyChanged(nameof(HasLogitechDpiStage4));
            OnPropertyChanged(nameof(HasLogitechDpiStage5));
            OnPropertyChanged(nameof(CanAddLogitechDpiStage));
            OnPropertyChanged(nameof(CanRemoveLogitechDpiStage));
            OnPropertyChanged(nameof(LogitechDpiRange));
            OnPropertyChanged(nameof(ActiveLogitechDpiStage));
            OnPropertyChanged(nameof(LogitechDpiStageLabel));
            OnPropertyChanged(nameof(LogitechDpiStage1Value));
            OnPropertyChanged(nameof(LogitechDpiStage2Value));
            OnPropertyChanged(nameof(LogitechDpiStage3Value));
            OnPropertyChanged(nameof(LogitechDpiStage4Value));
            OnPropertyChanged(nameof(LogitechDpiStage5Value));
            OnPropertyChanged(nameof(SelectedLogitechDpiValue));
            OnPropertyChanged(nameof(IsLogitechDpiStage1Selected));
            OnPropertyChanged(nameof(IsLogitechDpiStage2Selected));
            OnPropertyChanged(nameof(IsLogitechDpiStage3Selected));
            OnPropertyChanged(nameof(IsLogitechDpiStage4Selected));
            OnPropertyChanged(nameof(IsLogitechDpiStage5Selected));
        }
    }
    public bool CanDeleteLocalProfile => LocalProfiles.Count > 1;
    public bool CanAddLocalProfile => LocalProfiles.Count < 50;
    public XboxInputViewModel XboxInput { get; }
    public string LocalProfileKey => Device.ModelKey.Length > 0 ? Device.ModelKey : Device.Id;
    public ObservableCollection<AudioEndpointViewModel> AudioEndpoints { get; }
    public string DisplayName => Adapter.DisplayName;
    public string LogitechProfileTitle => $"{Brand} · {DisplayName}";
    public string Brand => Adapter.Brand;
    public string Status => Adapter.Status;
    public string LogitechConnectionStatus => _logitechConnectionStatus;
    public string Description => Adapter.Description;
    public string Transport => Device.Transport;
    public string HardwareId => Device.XInputSlot is int slot
        ? $"VID_{Device.VendorId:X4} · XInput {slot + 1}"
        : $"VID_{Device.VendorId:X4} · PID_{Device.ProductId:X4}";
    public string InterfaceSummary => Device.InterfaceCount switch
    {
        0 => "Entrada de mando",
        1 => "1 interfaz HID",
        _ => $"{Device.InterfaceCount} interfaces HID"
    };
    public string Detail => Device.Detail;
    public string IdentificationNote => Device.IdentificationNote;
    public string NoAudioEndpointsMessage => Device.Kind switch
    {
        DeviceKind.Mouse => "Los valores DPI quedan guardados como perfil Nexo; aplicarlos al ratón requiere validar el protocolo del modelo.",
        DeviceKind.Keyboard => "La iluminación se guarda como perfil Nexo. El envío al teclado espera la identificación exacta de esta variante.",
        DeviceKind.Gamepad => "La lectura XInput está disponible. El perfil Nexo aún no se escribe en el mando.",
        DeviceKind.Headset => "Windows no ha publicado endpoints de audio activos para este auricular.",
        DeviceKind.Microphone => "Windows no ha publicado endpoints de audio activos para este micrófono.",
        _ => "Este dispositivo no tiene controles del sistema disponibles; consulta el estado del adaptador antes de guardar cambios."
    };
    public string VisualKind => Device.Kind.ToString();
    public bool IsMouse => Device.Kind == DeviceKind.Mouse;
    public bool IsLogitechMouse => Device.ModelKey is "logitech-pro-wireless" or "logitech-pro-x-superlight";
    public bool IsLogitechProWireless => Device.ModelKey == "logitech-pro-wireless" && Device.VendorId == 0x046D && Device.ProductId == 0xC539;
    public bool IsLogitechProXSuperlight => Device.ModelKey == "logitech-pro-x-superlight" && Device.VendorId == 0x046D &&
        Device.ProductId == 0xC547 && Device.IdentificationNote.Contains("HID++ 0x0005", StringComparison.OrdinalIgnoreCase);
    public bool IsLogitechDpiWritable => IsLogitechProWireless || IsLogitechProXSuperlight;
    public bool IsLogitechHidppCandidate => Device.VendorId == 0x046D && (Device.HidInterfaces ?? []).Any(item =>
        item.UsagePage == 0xFF00 && item.Usage == 0x0002 && item.InputReportBytes == 20 && item.OutputReportBytes == 20);

    public void SetLogitechConnectionStatus(string status)
    {
        if (_logitechConnectionStatus == status) return;
        _logitechConnectionStatus = status;
        OnPropertyChanged(nameof(LogitechConnectionStatus));
    }
    public int LogitechDpiMaximum => 25600;
    public int LogitechDpiStageCount => LocalProfile.DpiStageCount;
    public bool HasLogitechDpiStage2 => LogitechDpiStageCount >= 2;
    public bool HasLogitechDpiStage3 => LogitechDpiStageCount >= 3;
    public bool HasLogitechDpiStage4 => LogitechDpiStageCount >= 4;
    public bool HasLogitechDpiStage5 => LogitechDpiStageCount >= 5;
    public bool CanAddLogitechDpiStage => IsLogitechMouse && LogitechDpiStageCount < 5;
    public bool CanRemoveLogitechDpiStage => IsLogitechMouse && LogitechDpiStageCount > 1;
    public string LogitechDpiRange => $"{LogitechDpiStageCount} etapas · 100–{LogitechDpiMaximum:N0} DPI";
    public int ActiveLogitechDpiStage => _activeLogitechDpiStage;
    public string LogitechDpiStageLabel => $"ETAPA {_activeLogitechDpiStage} · PERFIL LOCAL";
    public int LogitechDpiStage1Value => LocalProfile.Dpi1;
    public int LogitechDpiStage2Value => LocalProfile.Dpi2;
    public int LogitechDpiStage3Value => LocalProfile.Dpi3;
    public int LogitechDpiStage4Value => LocalProfile.Dpi4;
    public int LogitechDpiStage5Value => LocalProfile.Dpi5;
    public double SelectedLogitechDpiValue
    {
        get => GetActiveLogitechDpiValue();
        set
        {
            var dpi = (int)Math.Round(Math.Clamp(value, 100d, LogitechDpiMaximum), MidpointRounding.AwayFromZero);
            if (GetActiveLogitechDpiValue() == dpi) return;
            SetLogitechDpiStageValue(ActiveLogitechDpiStage, dpi);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LogitechDpiStage1Value));
            OnPropertyChanged(nameof(LogitechDpiStage2Value));
            OnPropertyChanged(nameof(LogitechDpiStage3Value));
            OnPropertyChanged(nameof(LogitechDpiStage4Value));
            OnPropertyChanged(nameof(LogitechDpiStage5Value));
        }
    }
    public bool IsLogitechDpiStage1Selected { get => ActiveLogitechDpiStage == 1; set { if (value) SetActiveLogitechDpiStage(1); } }
    public bool IsLogitechDpiStage2Selected { get => ActiveLogitechDpiStage == 2; set { if (value) SetActiveLogitechDpiStage(2); } }
    public bool IsLogitechDpiStage3Selected { get => ActiveLogitechDpiStage == 3; set { if (value) SetActiveLogitechDpiStage(3); } }
    public bool IsLogitechDpiStage4Selected { get => ActiveLogitechDpiStage == 4; set { if (value) SetActiveLogitechDpiStage(4); } }
    public bool IsLogitechDpiStage5Selected { get => ActiveLogitechDpiStage == 5; set { if (value) SetActiveLogitechDpiStage(5); } }
    public bool IsMicrophone => Device.Kind == DeviceKind.Microphone;
    public bool IsHeadset => Device.Kind == DeviceKind.Headset;
    public bool IsGamepad => Device.Kind == DeviceKind.Gamepad;
    public bool IsKeyboard => Device.Kind == DeviceKind.Keyboard;
    public bool IsAjazzKeyboard => Device.ModelKey == "ajazz-ak820";
    public bool IsXboxElite => Device.ModelKey.StartsWith("xbox-", StringComparison.OrdinalIgnoreCase);
    public bool IsAstroA50 => Device.ModelKey == "astro-a50";
    public bool HasLocalEditor => IsLogitechMouse || IsAjazzKeyboard || IsXboxElite || IsAstroA50;
    public bool HasAudioEndpoints => AudioEndpoints.Count > 0;
    public event PropertyChangedEventHandler? PropertyChanged;

    private int GetActiveLogitechDpiValue() => GetLogitechDpiStageValue(ActiveLogitechDpiStage);

    private int GetLogitechDpiStageValue(int stage) => stage switch
    {
        1 => LocalProfile.Dpi1,
        2 => LocalProfile.Dpi2,
        3 => LocalProfile.Dpi3,
        4 => LocalProfile.Dpi4,
        _ => LocalProfile.Dpi5
    };

    private void SetLogitechDpiStageValue(int stage, int value)
    {
        switch (stage)
        {
            case 1: LocalProfile.Dpi1 = value; break;
            case 2: LocalProfile.Dpi2 = value; break;
            case 3: LocalProfile.Dpi3 = value; break;
            case 4: LocalProfile.Dpi4 = value; break;
            default: LocalProfile.Dpi5 = value; break;
        }
    }

    private void SetActiveLogitechDpiStage(int stage)
    {
        if (stage < 1 || stage > LogitechDpiStageCount || _activeLogitechDpiStage == stage) return;
        _activeLogitechDpiStage = LocalProfile.ActiveDpiStage = stage;
        OnPropertyChanged(nameof(ActiveLogitechDpiStage));
        OnPropertyChanged(nameof(LogitechDpiStageLabel));
        OnPropertyChanged(nameof(SelectedLogitechDpiValue));
        OnPropertyChanged(nameof(IsLogitechDpiStage1Selected));
        OnPropertyChanged(nameof(IsLogitechDpiStage2Selected));
        OnPropertyChanged(nameof(IsLogitechDpiStage3Selected));
        OnPropertyChanged(nameof(IsLogitechDpiStage4Selected));
        OnPropertyChanged(nameof(IsLogitechDpiStage5Selected));
    }

    public void AddLogitechDpiStage()
    {
        if (!CanAddLogitechDpiStage) return;
        var stage = LogitechDpiStageCount + 1;
        SetLogitechDpiStageValue(stage, Math.Min(LogitechDpiMaximum, GetLogitechDpiStageValue(stage - 1) + 100));
        LocalProfile.DpiStageCount = stage;
        OnPropertyChanged(nameof(LogitechDpiStageCount));
        OnPropertyChanged(nameof(LogitechDpiRange));
        OnPropertyChanged(nameof(HasLogitechDpiStage2));
        OnPropertyChanged(nameof(HasLogitechDpiStage3));
        OnPropertyChanged(nameof(HasLogitechDpiStage4));
        OnPropertyChanged(nameof(HasLogitechDpiStage5));
        OnPropertyChanged(nameof(CanAddLogitechDpiStage));
        OnPropertyChanged(nameof(CanRemoveLogitechDpiStage));
        OnPropertyChanged($"LogitechDpiStage{stage}Value");
        SetActiveLogitechDpiStage(stage);
    }

    public void RemoveLogitechDpiStage()
    {
        if (!CanRemoveLogitechDpiStage) return;
        LocalProfile.DpiStageCount--;
        OnPropertyChanged(nameof(LogitechDpiStageCount));
        OnPropertyChanged(nameof(LogitechDpiRange));
        OnPropertyChanged(nameof(HasLogitechDpiStage2));
        OnPropertyChanged(nameof(HasLogitechDpiStage3));
        OnPropertyChanged(nameof(HasLogitechDpiStage4));
        OnPropertyChanged(nameof(HasLogitechDpiStage5));
        OnPropertyChanged(nameof(CanAddLogitechDpiStage));
        OnPropertyChanged(nameof(CanRemoveLogitechDpiStage));
        if (ActiveLogitechDpiStage > LogitechDpiStageCount)
            SetActiveLogitechDpiStage(LogitechDpiStageCount);
    }

    public void AddLocalProfile()
    {
        if (!CanAddLocalProfile) return;
        var profile = JsonSerializer.Deserialize<DeviceLocalProfile>(JsonSerializer.Serialize(LocalProfile))!;
        profile.Id = Guid.NewGuid().ToString("N");
        profile.ApplicationExecutable = "";
        var index = LocalProfiles.Count + 1;
        while (LocalProfiles.Any(item => item.Name.Equals($"Perfil Nexo {index}", StringComparison.OrdinalIgnoreCase))) index++;
        profile.Name = $"Perfil Nexo {index}";
        LocalProfiles.Add(profile);
        LocalProfile = profile;
    }

    public void RemoveLocalProfile()
    {
        if (!CanDeleteLocalProfile) return;
        LocalProfiles.Remove(LocalProfile);
        LocalProfile = LocalProfiles[0];
    }

    public bool SelectProfileForApplication(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        var profile = LocalProfiles.FirstOrDefault(item => item.ApplicationExecutable.Length > 0 &&
            item.ApplicationExecutable.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        if (profile is null && LocalProfiles.Any(item => item.ApplicationExecutable.Length > 0))
            profile = LocalProfiles.FirstOrDefault(item => item.ApplicationExecutable.Length == 0);
        if (profile is null || ReferenceEquals(LocalProfile, profile)) return false;
        LocalProfile = profile;
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void SetLogitechHidppStatus(CapabilityInfo status)
    {
        var index = Capabilities.ToList().FindIndex(item => item.Name == "HID++ / OMM");
        if (index >= 0) Capabilities[index] = status;
    }

    public void SetLogitechIdentityStatus(CapabilityInfo status)
    {
        var index = Capabilities.ToList().FindIndex(item => item.Name == "Identidad HID++");
        if (index >= 0) Capabilities[index] = status;
        else Capabilities.Add(status);
    }

    public string SettingsTitle => Device.Kind switch
    {
        DeviceKind.Mouse => "Sensibilidad y perfiles",
        DeviceKind.Microphone => "Entrada de sonido",
        DeviceKind.Headset => "Audio del ASTRO A50",
        DeviceKind.Gamepad => "Prueba del mando y perfiles",
        DeviceKind.Keyboard => "Teclas, iluminación y macros",
        DeviceKind.Receiver => "Receptor USB",
        _ => "Ajustes del dispositivo"
    };
    public string SettingsSummary => Device.Kind switch
    {
        DeviceKind.Mouse when IsLogitechMouse => $"{DisplayName} · el perfil Nexo se guarda localmente hasta validar la aplicación al ratón.",
        DeviceKind.Mouse => "Modelo no identificado; los controles específicos del ratón están desactivados.",
        DeviceKind.Microphone => "Cambia el nivel de entrada de Windows y comprueba que el micrófono Yeti Classic esté activo.",
        DeviceKind.Headset => "Ajusta los endpoints Game/Voice en Windows. EQ, sidetone y perfiles de memoria requieren el protocolo ASTRO validado.",
        DeviceKind.Gamepad => "Modelo Xbox One Elite leído de Windows. XInput permite probar botones y ejes; los perfiles locales no se escriben al mando.",
        DeviceKind.Keyboard => "El paquete AJAZZ admite ajustes por USB (8009/800A) o receptor 2.4 GHz (FDFD); Windows anuncia PID 8006. AJAZZ indica que Bluetooth no admite ajustes. Los cambios de Nexo son locales.",
        DeviceKind.Receiver => "Receptor identificado en el inventario de Windows.",
        _ => "Se muestra el nombre de Windows y el identificador de hardware para crear un adaptador por modelo."
    };
}

public sealed class AudioEndpointViewModel : INotifyPropertyChanged
{
    private int _level;

    public AudioEndpointViewModel(AudioEndpointInfo endpoint)
    {
        Id = endpoint.Id;
        Name = endpoint.Name;
        Flow = endpoint.Flow;
        _level = endpoint.Level;
    }

    public string Id { get; }
    public string Name { get; }
    public AudioFlow Flow { get; }
    public string FlowLabel => Flow == AudioFlow.Capture ? "ENTRADA · WINDOWS" : "SALIDA · WINDOWS";
    public string VolumeLabel => $"{Level}%";
    public int Level
    {
        get => _level;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (_level == value) return;
            _level = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VolumeLabel)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
