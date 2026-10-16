using System.Collections.ObjectModel;
using System.ComponentModel;
using NexoHub.Models;

namespace NexoHub.ViewModels;

public sealed class DeviceCardViewModel
{
    public DeviceCardViewModel(HardwareDevice device, AdapterInfo adapter, DeviceLocalProfile? localProfile = null)
    {
        Device = device;
        Adapter = adapter;
        Capabilities = new ObservableCollection<CapabilityInfo>(adapter.Capabilities);
        LocalProfile = localProfile ?? new DeviceLocalProfile();
        XboxInput = new XboxInputViewModel();
        AudioEndpoints = new ObservableCollection<AudioEndpointViewModel>((device.AudioEndpoints ?? [])
            .Select(endpoint => new AudioEndpointViewModel(endpoint)));
    }

    public HardwareDevice Device { get; }
    public AdapterInfo Adapter { get; }
    public ObservableCollection<CapabilityInfo> Capabilities { get; }
    public DeviceLocalProfile LocalProfile { get; }
    public XboxInputViewModel XboxInput { get; }
    public string LocalProfileKey => Device.ModelKey.Length > 0 ? Device.ModelKey : Device.Id;
    public ObservableCollection<AudioEndpointViewModel> AudioEndpoints { get; }
    public string DisplayName => Adapter.DisplayName;
    public string LogitechProfileTitle => $"{Brand} · {DisplayName}";
    public string Brand => Adapter.Brand;
    public string Status => Adapter.Status;
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
    public int LogitechDpiMaximum => 25600;
    public string LogitechDpiRange => $"Cuatro etapas · 100–{LogitechDpiMaximum:N0} DPI";
    public bool IsMicrophone => Device.Kind == DeviceKind.Microphone;
    public bool IsHeadset => Device.Kind == DeviceKind.Headset;
    public bool IsGamepad => Device.Kind == DeviceKind.Gamepad;
    public bool IsKeyboard => Device.Kind == DeviceKind.Keyboard;
    public bool IsAjazzKeyboard => Device.ModelKey == "ajazz-ak820";
    public bool IsXboxElite => Device.ModelKey.StartsWith("xbox-", StringComparison.OrdinalIgnoreCase);
    public bool IsAstroA50 => Device.ModelKey == "astro-a50";
    public bool HasLocalEditor => IsLogitechMouse || IsAjazzKeyboard || IsXboxElite || IsAstroA50;
    public bool HasAudioEndpoints => AudioEndpoints.Count > 0;
    public string SettingsTitle => Device.Kind switch
    {
        DeviceKind.Mouse => "Sensibilidad y memoria integrada",
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
