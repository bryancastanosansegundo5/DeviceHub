using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    private readonly Dictionary<string, string> _logitechConnectionStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string ModelKey, string DeviceName)> _logitechVerifiedModels = new(StringComparer.OrdinalIgnoreCase);
    private List<DeviceCardViewModel> _allDevices = [];
    private DeviceCardViewModel? _selectedDevice;
    private string _scanStatus = "Buscando dispositivos…";
    private string _profileStatus = "Los perfiles importados se guardan en este PC.";
    private bool _isScanning;
    private ProfileFile? _selectedProfile;
    private bool _showAuxiliary;
    private string _configurationStatus = "";
    private DateTime _lastLogitechConnectionCheck;

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
            OnPropertyChanged(nameof(CanApplyLogitechDpi));
            OnPropertyChanged(nameof(CanIdentifyLogitech));
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;
    public bool HasNoDevices => Devices.Count == 0;
    public bool CanProbeLogitech => SelectedDevice?.IsLogitechDpiWritable == true && !IsScanning && !IsProbingLogitech;
    public bool CanApplyLogitechDpi => SelectedDevice?.IsLogitechDpiWritable == true && !IsScanning && !IsProbingLogitech;
    public bool CanIdentifyLogitech => SelectedDevice?.IsLogitechHidppCandidate == true && !IsScanning && !IsProbingLogitech;
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
            OnPropertyChanged(nameof(CanApplyLogitechDpi));
            OnPropertyChanged(nameof(CanIdentifyLogitech));
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
            OnPropertyChanged(nameof(CanApplyLogitechDpi));
            OnPropertyChanged(nameof(CanIdentifyLogitech));
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
            var existingProfiles = _allDevices.ToDictionary(card => card.Device.Id,
                card => (ModelKey: card.LocalProfileKey, Profiles: (IReadOnlyList<DeviceLocalProfile>)card.LocalProfiles.ToList(), ActiveId: card.LocalProfile.Id));
            var found = await Task.Run(_discovery.Scan);
            var connectedIds = found.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var disconnectedId in _logitechVerifiedModels.Keys.Where(id => !connectedIds.Contains(id)).ToArray())
                _logitechVerifiedModels.Remove(disconnectedId);
            _allDevices = found.Select(device =>
            {
                if (_logitechVerifiedModels.TryGetValue(device.Id, out var identity))
                    device = ApplyLogitechIdentity(device, identity.ModelKey, identity.DeviceName);
                var modelKey = device.ModelKey.Length > 0 ? device.ModelKey : device.Id;
                IReadOnlyList<DeviceLocalProfile> profiles;
                string activeProfileId;
                if (existingProfiles.TryGetValue(device.Id, out var existing) && existing.ModelKey == modelKey)
                {
                    profiles = existing.Profiles;
                    activeProfileId = existing.ActiveId;
                }
                else
                {
                    var profileSet = _deviceProfiles.LoadSet(modelKey);
                    profiles = profileSet.Profiles;
                    activeProfileId = profileSet.ActiveProfileId;
                }
                var card = new DeviceCardViewModel(device, _adapters.Describe(device),
                    localProfiles: profiles, activeProfileId: activeProfileId);
                if (_logitechProbeResults.TryGetValue(device.Id, out var result)) card.SetLogitechHidppStatus(result);
                if (_logitechConnectionStatuses.TryGetValue(device.Id, out var connectionStatus))
                    card.SetLogitechConnectionStatus(connectionStatus);
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
            if (DateTime.UtcNow - _lastLogitechConnectionCheck >= TimeSpan.FromSeconds(30))
                await RefreshLogitechConnectionStatusesAsync();
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
        if (card?.IsLogitechDpiWritable != true || !CanProbeLogitech) return;

        IsProbingLogitech = true;
        SetLogitechProbeResult(card.Device.Id, new("HID++ / OMM", "Consultando", "Lectura manual HID++; no escribe DPI ni perfiles."));
        try
        {
            var slots = await _logitechHidpp.ReadOmmGateAsync(card.Device);
            var detail = slots.Count == 0
                ? "El ratón no respondió a la comprobación. No se modificó ningún ajuste."
                : string.Join("\n", slots.Select(slot =>
                {
                    var reportRate = slot.HidppDeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType
                        ? $"0x8060 {slot.AdjustableReportRateFeatureIndex switch { null => "sin respuesta/error", 0 => "no anunciada", byte featureIndex => $"índice 0x{featureIndex:X2}" }}{(slot.CurrentReportRateMs is byte rateMs ? $" · polling actual: {1000d / rateMs:N0} Hz ({rateMs} ms)" : slot.AdjustableReportRateFeatureIndex is > 0 ? " · polling actual: sin respuesta/error" : string.Empty)}"
                        : string.Empty;
                    var dpi = slot.CurrentDpi is { } current
                        ? $" · DPI actual sensor {current.SensorIndex}: {current.CurrentDpi:N0}{(current.DefaultDpi is int defaultDpi ? $" (predeterminado {defaultDpi:N0})" : string.Empty)} · sensores {current.SensorCount}"
                        : slot.HasFeature2201 && slot.HidppDeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType
                            ? " · DPI actual: sin respuesta/error"
                            : string.Empty;
                    var battery = slot.Battery is { } status
                        ? $" · batería {(status.Level is byte level ? $"{level}%" : "desconocida")} · {status.StatusLabel}"
                        : slot.BatteryFeatureIndex is > 0 ? " · batería: sin respuesta/error" : string.Empty;
                    var firmware = slot.Firmware.Count > 0
                        ? $" · firmware ratón: {string.Join(", ", slot.Firmware.Select(item => item.Summary))}"
                        : slot.HasDeviceInformation ? " · firmware ratón: sin datos" : string.Empty;
                    var receiverFirmware = slot.ReceiverFirmware.Count > 0
                        ? $" · firmware receptor: {string.Join(", ", slot.ReceiverFirmware.Select(item => item.Summary))}"
                        : " · firmware receptor: sin respuesta/feature no anunciada";
                    var featureInventory = string.Join(" · ", LogitechHidppCapabilityReader.MouseFeatureCatalog.Select(feature =>
                    {
                        slot.FeatureIndexes.TryGetValue(feature.Id, out var featureIndex);
                        var status = featureIndex switch
                        {
                            null => slot.FeatureQueryStoppedAt == feature.Id
                                ? "sin respuesta/error"
                                : "sin consultar tras el error anterior",
                            0 => "no anunciada",
                            byte index => $"índice 0x{index:X2}"
                        };
                        return $"{feature.Name} 0x{feature.Id:X4}: {status}";
                    }));
                    return $"Ranura {slot.DeviceIndex} · HID++ {slot.ProtocolMajor}.{slot.ProtocolMinor} · 0x0003 {(slot.HasDeviceInformation ? "sí" : "no")} · 0x0005 {(slot.HasDeviceNameAndType ? "sí" : "no")} · tipo HID++ {(slot.HidppDeviceType is byte type ? $"0x{type:X2}" : "sin respuesta")} · DPI 0x2201 {(slot.HasFeature2201 ? "sí" : "no")} · DPI 0x2202 {(slot.HasFeature2202 ? "sí" : "no")} · perfiles 0x8100 {(slot.HasOnboardProfiles ? "sí" : "no")}{(string.IsNullOrEmpty(reportRate) ? string.Empty : $" · {reportRate}")}{dpi}{battery}{firmware}{receiverFirmware}\nFunciones de ratón: {featureInventory}";
                })) +
                    "\nLa consulta es de solo lectura. Las versiones de firmware se muestran en formato HID++ bruto; el tipo no demuestra compatibilidad completa con OMM.";
            var result = new CapabilityInfo("HID++ / OMM", slots.Any(slot => slot.MatchesStaticOmmFilter)
                ? "Filtro estático coincidente" : slots.Count == 0 ? "Sin verificación" : "Features parciales", detail);
            SetLogitechProbeResult(card.Device.Id, result);
            ConfigurationStatus = $"{card.DisplayName}: consulta HID++ finalizada; solo se leyeron firmware, features, tipo, DPI, polling y batería.";
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

    public async Task ApplyLogitechDpiAsync()
    {
        var card = SelectedDevice;
        if (card?.IsLogitechDpiWritable != true || !CanApplyLogitechDpi) return;

        IsProbingLogitech = true;
        var dpi = (int)Math.Round(card.SelectedLogitechDpiValue, MidpointRounding.AwayFromZero);
        ConfigurationStatus = $"Aplicando {dpi:N0} DPI al sensor…";
        try
        {
            var verifiedDpi = await _logitechHidpp.SetCurrentDpiAsync(card.Device, dpi);
            ConfigurationStatus = $"{card.DisplayName}: {verifiedDpi:N0} DPI aplicado al sensor y leído de vuelta. No modifica las etapas ni la memoria integrada.";
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"No se pudo confirmar el cambio de DPI: {exception.Message}";
        }
        finally
        {
            IsProbingLogitech = false;
        }
    }

    public async Task IdentifyLogitechMouseAsync()
    {
        var card = SelectedDevice;
        if (card?.IsLogitechHidppCandidate != true || !CanIdentifyLogitech) return;

        IsProbingLogitech = true;
        card.SetLogitechIdentityStatus(new("Identidad del ratón", "Consultando", "Leyendo el receptor; no modifica ajustes."));
        try
        {
            LogitechReceiverPairingInfo? pairing = null;
            try { pairing = await _logitechHidpp.ReadReceiverPairingInfoAsync(card.Device); }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException) { }
            var pairedModel = pairing is { DeviceKind: 2 }
                ? LogitechHidppCapabilityReader.ModelFromWirelessProductId(pairing.WirelessProductId)
                : null;
            var expectedPairedProductId = pairedModel?.ModelKey switch
            {
                "logitech-pro-wireless" => (ushort)0xC539,
                "logitech-pro-x-superlight" => (ushort)0xC547,
                _ => (ushort)0
            };
            if (pairedModel is not null && expectedPairedProductId == card.Device.ProductId)
            {
                var pairedStatus = new CapabilityInfo("Identidad del ratón", "Modelo identificado",
                    $"Identidad guardada en el receptor (WPID {pairing!.WirelessProductId:X4}); no hace falta despertar el ratón.");
                ReplaceWithVerifiedLogitechMouse(card.Device, pairedModel.Value.ModelKey,
                    pairedModel.Value.DeviceName, pairedStatus);
                ConfigurationStatus = $"{pairedModel.Value.DeviceName}: modelo confirmado desde el receptor.";
                return;
            }

            var identities = await _logitechHidpp.ReadDeviceIdentitiesAsync(card.Device);
            var mice = identities.Where(item => item.DeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType).ToArray();
            SetLogitechConnectionStatus(card.Device.Id, mice.Length > 0 ? "Activo" :
                "Inactivo");
            var summary = identities.Count == 0
                ? "El ratón no respondió directamente. Se consultó su identidad guardada en el receptor."
                : string.Join(" · ", identities.Select(item =>
                    $"Ranura {item.DeviceIndex}: {item.DeviceName} · tipo {(item.DeviceType is byte type ? $"0x{type:X2}" : "sin respuesta")} · HID++ {item.ProtocolMajor}.{item.ProtocolMinor}" +
                    (item.Firmware.Count > 0 ? $" · firmware: {string.Join(", ", item.Firmware.Select(firmware => firmware.Summary))}" : string.Empty)));
            var modelKey = mice.Length == 1 ? LogitechHidppCapabilityReader.ModelKeyFromDeviceName(mice[0].DeviceName) : null;
            var deviceName = mice.Length == 1 ? mice[0].DeviceName : string.Empty;
            if (modelKey is null && pairedModel is not null)
            {
                modelKey = pairedModel.Value.ModelKey;
                deviceName = pairedModel.Value.DeviceName;
            }
            var expectedProductId = modelKey switch
            {
                "logitech-pro-wireless" => (ushort)0xC539,
                "logitech-pro-x-superlight" => (ushort)0xC547,
                _ => (ushort)0
            };
            if (expectedProductId != card.Device.ProductId) modelKey = null;

            var status = new CapabilityInfo("Identidad del ratón", modelKey is null ? "Sin confirmar" : "Modelo identificado",
                pairing is not null ? $"WPID {pairing.WirelessProductId:X4}. {summary}" : summary);
            if (modelKey is null)
            {
                card.SetLogitechIdentityStatus(status);
                ConfigurationStatus = $"No pude confirmar el modelo de este ratón. {summary}";
                return;
            }

            ReplaceWithVerifiedLogitechMouse(card.Device, modelKey, deviceName, status);
            ConfigurationStatus = $"{deviceName}: modelo confirmado desde el receptor. La ficha específica ya está disponible.";
        }
        catch (Exception exception)
        {
            card.SetLogitechIdentityStatus(new("Identidad HID++", "Error de lectura", exception.Message));
            ConfigurationStatus = $"No se pudo leer la identidad HID++: {exception.Message}";
        }
        finally
        {
            IsProbingLogitech = false;
        }
    }

    private DeviceCardViewModel ReplaceWithVerifiedLogitechMouse(
        HardwareDevice device, string modelKey, string deviceName, CapabilityInfo identityStatus)
    {
        _logitechVerifiedModels[device.Id] = (modelKey, deviceName);
        var resolvedDevice = ApplyLogitechIdentity(device, modelKey, deviceName);
        var profileSet = _deviceProfiles.LoadSet(modelKey);
        var replacement = new DeviceCardViewModel(resolvedDevice, _adapters.Describe(resolvedDevice),
            localProfiles: profileSet.Profiles, activeProfileId: profileSet.ActiveProfileId);
        replacement.SetLogitechIdentityStatus(identityStatus);
        if (_logitechProbeResults.TryGetValue(resolvedDevice.Id, out var probeResult))
            replacement.SetLogitechHidppStatus(probeResult);
        if (_logitechConnectionStatuses.TryGetValue(resolvedDevice.Id, out var connectionStatus))
            replacement.SetLogitechConnectionStatus(connectionStatus);
        var index = _allDevices.FindIndex(item => item.Device.Id == resolvedDevice.Id);
        if (index >= 0) _allDevices[index] = replacement;
        RefreshVisibleDevices(resolvedDevice.Id);
        return replacement;
    }

    private static HardwareDevice ApplyLogitechIdentity(HardwareDevice device, string modelKey, string deviceName) => device with
    {
        Manufacturer = "Logitech G",
        Product = deviceName,
        ModelKey = modelKey,
        Kind = DeviceKind.Mouse,
        IsAuxiliary = false,
        IdentificationNote = $"Nombre «{deviceName}» y tipo ratón confirmados por la feature HID++ 0x0005; el receptor anuncia PID 0x{device.ProductId:X4}."
    };

    private void SetLogitechProbeResult(string deviceId, CapabilityInfo result)
    {
        _logitechProbeResults[deviceId] = result;
        foreach (var device in _allDevices.Where(item => item.Device.Id == deviceId))
            device.SetLogitechHidppStatus(result);
    }

    private async Task RefreshLogitechConnectionStatusesAsync()
    {
        var candidates = _allDevices.Where(card => card.IsLogitechHidppCandidate).ToArray();
        if (candidates.Length == 0 || IsProbingLogitech) return;
        _lastLogitechConnectionCheck = DateTime.UtcNow;
        IsProbingLogitech = true;
        try
        {
            foreach (var card in candidates)
            {
            var status = "Sin respuesta";
                try
                {
                    var identities = await _logitechHidpp.ReadDeviceIdentitiesAsync(card.Device);
                    status = identities.Any(item => item.DeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType)
                        ? "Activo"
                    : "Inactivo";
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    status = "Sin respuesta";
                }

                SetLogitechConnectionStatus(card.Device.Id, status);
            }
        }
        finally
        {
            IsProbingLogitech = false;
        }
    }

    private void SetLogitechConnectionStatus(string deviceId, string status)
    {
        _logitechConnectionStatuses[deviceId] = status;
        _allDevices.FirstOrDefault(card => card.Device.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase))?
            .SetLogitechConnectionStatus(status);
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
            var path = _deviceProfiles.SaveSet(SelectedDevice.LocalProfileKey, SelectedDevice.LocalProfiles,
                SelectedDevice.LocalProfile.Id);
            ConfigurationStatus = $"{SelectedDevice.LocalProfiles.Count} perfil(es) Nexo guardado(s) en este PC: {path}";
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"No se pudo guardar el perfil local: {exception.Message}";
        }
    }

    public void AddDeviceLocalProfile()
    {
        if (SelectedDevice is null || !SelectedDevice.IsLogitechMouse) return;
        if (!SelectedDevice.CanAddLocalProfile)
        {
            ConfigurationStatus = "El ratón ya tiene 50 perfiles locales.";
            return;
        }
        SelectedDevice.AddLocalProfile();
        ConfigurationStatus = "Perfil Nexo creado a partir del actual. Ajusta los valores y pulsa Guardar perfil Nexo.";
    }

    public void RemoveDeviceLocalProfile()
    {
        if (SelectedDevice is null || !SelectedDevice.IsLogitechMouse || !SelectedDevice.CanDeleteLocalProfile) return;
        SelectedDevice.RemoveLocalProfile();
        ConfigurationStatus = "Perfil quitado de la lista local. Pulsa Guardar perfil Nexo para confirmar el cambio.";
    }

    public void LinkDeviceProfileToApplication(string executablePath)
    {
        if (SelectedDevice is null || !SelectedDevice.IsLogitechMouse) return;
        if (SelectedDevice.LocalProfiles.Any(profile => !ReferenceEquals(profile, SelectedDevice.LocalProfile) &&
            profile.ApplicationExecutable.Equals(executablePath, StringComparison.OrdinalIgnoreCase)))
        {
            ConfigurationStatus = "Esa aplicación ya tiene otro perfil local asociado a este ratón.";
            return;
        }
        SelectedDevice.LocalProfile.ApplicationExecutable = executablePath;
        ConfigurationStatus = $"Perfil local asociado a {Path.GetFileName(executablePath)}; se seleccionará automáticamente mientras esa aplicación esté activa.";
    }

    public void UnlinkDeviceProfileFromApplication()
    {
        if (SelectedDevice is null || !SelectedDevice.IsLogitechMouse) return;
        SelectedDevice.LocalProfile.ApplicationExecutable = "";
        ConfigurationStatus = "Se quitó la asociación con la aplicación. El perfil queda como opción local manual.";
    }

    public void SelectProfilesForApplication(string executablePath)
    {
        MacroManager.SetActiveApplication(executablePath);
        foreach (var device in _allDevices.Where(card => card.IsLogitechMouse))
            device.SelectProfileForApplication(executablePath);
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
