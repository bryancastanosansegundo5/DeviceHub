using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NexoHub.Adapters;
using NexoHub.Models;
using NexoHub.Services;

namespace NexoHub.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DeviceDiscoveryService _discovery = new();
    private readonly DeviceAdapterRegistry _adapters = new();
    private readonly ProfileLibrary _profiles = new();
    private readonly DeviceLocalProfileLibrary _deviceProfiles = new();
    private readonly WindowsAudioEndpointService _audioEndpoints = new();
    private readonly LogitechHidppCapabilityReader _logitechHidpp = new();
    private readonly Dictionary<string, CapabilityInfo> _logitechProbeResults = [];
    private List<DeviceCardViewModel> _allDevices = [];
    private DeviceCardViewModel? _selectedDevice;
    private string _scanStatus = "Buscando dispositivos…";
    private string _profileStatus = "Los perfiles importados se guardan en este PC.";
    private bool _isScanning;
    private ProfileFile? _selectedProfile;
    private bool _showAuxiliary;
    private string _configurationStatus = "";

    public MainViewModel() => RefreshProfiles();

    public ObservableCollection<DeviceCardViewModel> Devices { get; } = [];
    public ObservableCollection<ProfileFile> Profiles { get; } = [];
    public MacroManagerViewModel MacroManager { get; } = new();
    public IReadOnlyList<int> ReportRates { get; } = [125, 250, 500, 1000];
    public IReadOnlyList<string> LightingEffects { get; } = ["Colorido", "Arcoíris", "Arcoíris inverso", "Degradado", "Ciclo de espectro", "Apagado"];
    public DeviceCardViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value)) return;
            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedDevice));
            OnPropertyChanged(nameof(HasNoDevices));
            OnPropertyChanged(nameof(CanProbeLogitech));
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;
    public bool HasNoDevices => Devices.Count == 0;
    public bool CanProbeLogitech => SelectedDevice?.IsLogitechProWireless == true && !IsScanning && !IsProbingLogitech;
    public bool HasAuxiliaryDevices => AuxiliaryCount > 0;
    public int AuxiliaryCount => _allDevices.Count(device => device.Device.IsAuxiliary);
    public string AuxiliaryButtonLabel => ShowAuxiliary ? $"Ocultar otros ({AuxiliaryCount})" : $"Mostrar otros ({AuxiliaryCount})";
    public bool ShowAuxiliary
    {
        get => _showAuxiliary;
        set
        {
            if (_showAuxiliary == value) return;
            _showAuxiliary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AuxiliaryButtonLabel));
            RefreshVisibleDevices(SelectedDevice?.Device.Id);
        }
    }
    public string ConfigurationStatus
    {
        get => _configurationStatus;
        private set { _configurationStatus = value; OnPropertyChanged(); }
    }
    public ProfileFile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value)) return;
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProfile));
        }
    }

    public bool HasSelectedProfile => SelectedProfile is not null;
    public string ProfileStatus
    {
        get => _profileStatus;
        set { _profileStatus = value; OnPropertyChanged(); }
    }
    public string ScanStatus
    {
        get => _scanStatus;
        private set { _scanStatus = value; OnPropertyChanged(); }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanProbeLogitech));
        }
    }

    private bool _isProbingLogitech;
    public bool IsProbingLogitech
    {
        get => _isProbingLogitech;
        private set
        {
            _isProbingLogitech = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanProbeLogitech));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshProfiles()
    {
        var priorPath = SelectedProfile?.Path;
        Profiles.Clear();
        foreach (var profile in _profiles.List()) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Path == priorPath)
            ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedProfile));
    }

    public ProfileFile ImportProfile(string path) => _profiles.Import(path);
    public string ExportProfile(ProfileFile profile, string targetPath) => _profiles.Export(profile, targetPath);

    public async Task RefreshAsync()
    {
        if (IsScanning || IsProbingLogitech) return;
        IsScanning = true;
        var priorId = SelectedDevice?.Device.Id;
        try
        {
            var found = await Task.Run(_discovery.Scan);
            _allDevices = found.Select(device =>
            {
                var card = new DeviceCardViewModel(device, _adapters.Describe(device),
                    _deviceProfiles.Load(device.ModelKey.Length > 0 ? device.ModelKey : device.Id));
                if (_logitechProbeResults.TryGetValue(device.Id, out var result)) card.SetLogitechHidppStatus(result);
                return card;
            }).ToList();
            RefreshVisibleDevices(priorId);
            ScanStatus = _allDevices.Count == 0
                ? "No hay dispositivos HID o mandos Xbox conectados."
                : $"{Devices.Count} dispositivo(s) · {AuxiliaryCount} elemento(s) auxiliar(es) oculto(s) · {DateTime.Now:HH:mm:ss}";
            OnPropertyChanged(nameof(HasNoDevices));
            OnPropertyChanged(nameof(HasAuxiliaryDevices));
            OnPropertyChanged(nameof(AuxiliaryCount));
            OnPropertyChanged(nameof(AuxiliaryButtonLabel));
        }
        catch (Exception exception)
        {
            ScanStatus = $"No se pudo completar la búsqueda: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task ReadLogitechCapabilitiesAsync()
    {
        var card = SelectedDevice;
        if (card?.IsLogitechProWireless != true || !CanProbeLogitech) return;

        IsProbingLogitech = true;
        SetLogitechProbeResult(card.Device.Id, new("HID++ / OMM", "Consultando", "Lectura manual HID++; no escribe DPI ni perfiles."));
        try
        {
            var slots = await _logitechHidpp.ReadOmmGateAsync(card.Device);
            var detail = slots.Count == 0
                ? "No se completó la verificación en las ranuras 1–6; esto no demuestra incompatibilidad. OMM también filtra el tipo de dispositivo y no se ha confirmado compatibilidad completa."
                : string.Join("\n", slots.Select(slot =>
                    $"Ranura {slot.DeviceIndex} · HID++ {slot.ProtocolMajor}.{slot.ProtocolMinor} · 0x0003 {(slot.HasDeviceInformation ? "sí" : "no")} · 0x0005 {(slot.HasDeviceNameAndType ? "sí" : "no")} · tipo HID++ {(slot.HidppDeviceType is byte type ? $"0x{type:X2}" : "sin respuesta")} · DPI {(slot.HasAdjustableDpi ? "sí" : "no")} · perfiles 0x8100 {(slot.HasOnboardProfiles ? "sí" : "no")}")) +
                    "\nEl tipo es el byte HID++ sin traducir; no demuestra compatibilidad completa con OMM.";
            var result = new CapabilityInfo("HID++ / OMM", slots.Any(slot => slot.HasRequiredFeatures)
                ? "Features observadas" : slots.Count == 0 ? "Sin verificación" : "Features parciales", detail);
            SetLogitechProbeResult(card.Device.Id, result);
            ConfigurationStatus = $"{card.DisplayName}: consulta HID++ finalizada; solo se leyeron versión, features y tipo.";
        }
        catch (Exception exception)
        {
            var result = new CapabilityInfo("HID++ / OMM", "Error de lectura", exception.Message);
            SetLogitechProbeResult(card.Device.Id, result);
            ConfigurationStatus = $"No se pudo consultar HID++: {exception.Message}";
        }
        finally
        {
            IsProbingLogitech = false;
        }
    }

    private void SetLogitechProbeResult(string deviceId, CapabilityInfo result)
    {
        _logitechProbeResults[deviceId] = result;
        foreach (var device in _allDevices.Where(item => item.Device.Id == deviceId))
            device.SetLogitechHidppStatus(result);
    }

    public void ApplyAudioLevel(AudioEndpointViewModel endpoint)
    {
        try
        {
            endpoint.Level = _audioEndpoints.SetLevel(endpoint.Id, endpoint.Level);
            ConfigurationStatus = $"{endpoint.Name}: nivel de Windows aplicado y leído de vuelta ({endpoint.Level}%).";
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"No se pudo cambiar el nivel de Windows: {exception.Message}";
        }
    }

    public void SaveDeviceProfile()
    {
        if (SelectedDevice is null) return;
        try
        {
            var path = _deviceProfiles.Save(SelectedDevice.LocalProfileKey, SelectedDevice.LocalProfile);
            ConfigurationStatus = $"Perfil Nexo guardado en este PC: {path}";
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"No se pudo guardar el perfil local: {exception.Message}";
        }
    }

    public void SetAjazzKeyColor(string keyCode, string keyName)
    {
        var device = SelectedDevice;
        if (device is null || !device.IsAjazzKeyboard) return;

        var color = device.LocalProfile.LightingColor.Trim().TrimStart('#');
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^[0-9A-Fa-f]{6}$"))
        {
            ConfigurationStatus = "Escribe el color como seis dígitos hexadecimales, por ejemplo 53D6C2.";
            return;
        }

        device.LocalProfile.KeyboardKeyColors[keyCode] = color.ToUpperInvariant();
        ConfigurationStatus = $"Color {color.ToUpperInvariant()} asignado a {keyName} en el perfil local. Pulsa Guardar perfil Nexo para conservarlo.";
    }

    public void ClearAjazzKeyColor(string keyCode, string keyName)
    {
        var device = SelectedDevice;
        if (device is null || !device.IsAjazzKeyboard) return;

        device.LocalProfile.KeyboardKeyColors.Remove(keyCode);
        ConfigurationStatus = $"Se quitó el color personalizado de {keyName}. El cambio solo afecta al perfil local de Nexo.";
    }

    private void RefreshVisibleDevices(string? preferredId)
    {
        Devices.Clear();
        foreach (var device in _allDevices.Where(device => ShowAuxiliary || !device.Device.IsAuxiliary))
            Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(item => item.Device.Id == preferredId) ?? Devices.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(HasAuxiliaryDevices));
        OnPropertyChanged(nameof(AuxiliaryCount));
        OnPropertyChanged(nameof(AuxiliaryButtonLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
