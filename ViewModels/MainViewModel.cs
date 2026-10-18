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
    private readonly XboxEliteProfileWriter _xboxEliteProfileWriter = new();
    private readonly LogitechHidppCapabilityReader _logitechHidpp = new();
    private readonly Dictionary<string, CapabilityInfo> _logitechProbeResults = [];
    private readonly Dictionary<string, string> _logitechConnectionStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string ModelKey, string DeviceName)> _logitechVerifiedModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _logitechIdentityAttempts = new(StringComparer.OrdinalIgnoreCase);
    public const int MaxAutomaticLogitechIdentityAttempts = 2;
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
            OnPropertyChanged(nameof(CanSaveLogitechOnboardDpi));
            OnPropertyChanged(nameof(CanIdentifyLogitech));
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;
    public bool HasNoDevices => Devices.Count == 0;
    public bool CanProbeLogitech => SelectedDevice?.IsLogitechDpiWritable == true && !IsScanning && !IsProbingLogitech;
    public bool CanApplyLogitechDpi => SelectedDevice?.IsLogitechDpiWritable == true && !IsScanning && !IsProbingLogitech;
    public bool CanSaveLogitechOnboardDpi => SelectedDevice?.CanSaveLogitechOnboardDpi == true && !IsScanning && !IsProbingLogitech;
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
            RefreshVisibleDevices(SelectedDevice?.Device.Id, SelectedDevice?.Device);
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
            OnPropertyChanged(nameof(CanSaveLogitechOnboardDpi));
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
            OnPropertyChanged(nameof(CanSaveLogitechOnboardDpi));
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
        var priorDevice = SelectedDevice?.Device;
        var priorId = priorDevice?.Id;
        try
        {
            var existingProfiles = _allDevices.ToDictionary(card => card.Device.Id,
                card => (ModelKey: card.LocalProfileKey, Profiles: (IReadOnlyList<DeviceLocalProfile>)card.LocalProfiles.ToList(),
                    ActiveId: card.LocalProfile.Id, IsEditing: card.IsEditingXboxProfile,
                    SelectedXboxButton: card.SelectedXboxPhysicalButton, IsXboxBackView: card.IsXboxControllerBackView));
            var found = await Task.Run(_discovery.Scan);
            var connectedIds = found.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var disconnectedId in _logitechVerifiedModels.Keys.Where(id => !connectedIds.Contains(id)).ToArray())
                _logitechVerifiedModels.Remove(disconnectedId);
            foreach (var disconnectedId in _logitechIdentityAttempts.Keys.Where(id => !connectedIds.Contains(id)).ToArray())
                _logitechIdentityAttempts.Remove(disconnectedId);
            _allDevices = found.Select(device =>
            {
                if (_logitechVerifiedModels.TryGetValue(device.Id, out var identity))
                    device = ApplyLogitechIdentity(device, identity.ModelKey, identity.DeviceName);
                var modelKey = device.ModelKey.Length > 0 ? device.ModelKey : device.Id;
                IReadOnlyList<DeviceLocalProfile> profiles;
                string activeProfileId;
                var hasExisting = existingProfiles.TryGetValue(device.Id, out var existing);
                if (hasExisting && existing.ModelKey == modelKey)
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
                if (hasExisting && existing.ModelKey == modelKey)
                {
                    card.IsEditingXboxProfile = existing.IsEditing;
                    card.SelectedXboxPhysicalButton = existing.SelectedXboxButton;
                    card.IsXboxControllerBackView = existing.IsXboxBackView;
                }
                if (_logitechProbeResults.TryGetValue(device.Id, out var result)) card.SetLogitechHidppStatus(result);
                if (_logitechConnectionStatuses.TryGetValue(device.Id, out var connectionStatus))
                    card.SetLogitechConnectionStatus(connectionStatus);
                return card;
            }).ToList();
            var currentSelection = SelectedDevice?.Device ?? priorDevice;
            RefreshVisibleDevices(currentSelection?.Id ?? priorId, currentSelection);
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

        foreach (var candidate in _allDevices.Where(card => card.IsLogitechHidppCandidate && !card.IsLogitechDpiWritable &&
                     !_logitechVerifiedModels.ContainsKey(card.Device.Id) &&
                     IsAutomaticLogitechIdentityAttemptAllowed(_logitechIdentityAttempts.GetValueOrDefault(card.Device.Id))).ToArray())
        {
            _logitechIdentityAttempts[candidate.Device.Id] = _logitechIdentityAttempts.GetValueOrDefault(candidate.Device.Id) + 1;
            await IdentifyLogitechMouseAsync(candidate);
        }

        if (DateTime.UtcNow - _lastLogitechConnectionCheck >= TimeSpan.FromSeconds(30))
            await RefreshLogitechConnectionStatusesAsync();
    }

    public static bool IsAutomaticLogitechIdentityAttemptAllowed(int previousAttempts) =>
        previousAttempts >= 0 && previousAttempts < MaxAutomaticLogitechIdentityAttempts;

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
                        : string.Empty;
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
            ConfigurationStatus = $"{card.DisplayName}: {verifiedDpi:N0} DPI aplicado.";
        }
        catch (Exception)
        {
            ConfigurationStatus = "No se pudo aplicar el DPI. Comprueba que el ratón siga conectado e inténtalo otra vez.";
        }
        finally
        {
            IsProbingLogitech = false;
        }
    }

    public async Task SaveLogitechOnboardDpiAsync()
    {
        var card = SelectedDevice;
        if (card?.CanSaveLogitechOnboardDpi != true || !CanSaveLogitechOnboardDpi) return;

        var stageCount = card.LocalProfile.DpiStageCount;
        var stages = new[]
        {
            card.LocalProfile.Dpi1, card.LocalProfile.Dpi2, card.LocalProfile.Dpi3,
            card.LocalProfile.Dpi4, card.LocalProfile.Dpi5
        }.Take(stageCount).ToArray();
        var defaultIndex = (byte)(Math.Clamp(card.ActiveLogitechDpiStage, 1, stageCount) - 1);
        var activeDpi = stages[defaultIndex];

        IsProbingLogitech = true;
        ConfigurationStatus = "Guardando las etapas en la memoria del ratón…";
        try
        {
            var saved = await _logitechHidpp.SaveOnboardDpiStagesAsync(card.Device, stages, defaultIndex);
            try
            {
                var applied = await _logitechHidpp.SetCurrentDpiAsync(card.Device, activeDpi);
                ConfigurationStatus = saved.Changed
                    ? $"{card.DisplayName}: etapas guardadas en el ratón. {applied:N0} DPI activo."
                    : $"{card.DisplayName}: etapas ya guardadas en el ratón. {applied:N0} DPI activo.";
            }
            catch (Exception)
            {
                ConfigurationStatus = $"Las etapas quedaron guardadas; no se pudo aplicar {activeDpi:N0} DPI al ratón.";
            }
        }
        catch (Exception)
        {
            ConfigurationStatus = "No se pudieron guardar los DPI en el ratón. Los perfiles de Nexo siguen intactos.";
        }
        finally
        {
            IsProbingLogitech = false;
        }
    }

    public async Task IdentifyLogitechMouseAsync(DeviceCardViewModel? target = null)
    {
        var card = target ?? SelectedDevice;
        if (card?.IsLogitechHidppCandidate != true || IsScanning || IsProbingLogitech) return;

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
        var previousCard = _allDevices.FirstOrDefault(item => item.Device.Id.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
        if (!_deviceProfiles.HasSavedSet(modelKey) && previousCard is { IsUnidentifiedLogitechMouse: true })
        {
            profileSet = (previousCard.LocalProfiles.ToList(), previousCard.LocalProfile.Id);
            _deviceProfiles.SaveSet(modelKey, profileSet.Profiles, profileSet.ActiveProfileId);
        }
        var replacement = new DeviceCardViewModel(resolvedDevice, _adapters.Describe(resolvedDevice),
            localProfiles: profileSet.Profiles, activeProfileId: profileSet.ActiveProfileId);
        replacement.SetLogitechIdentityStatus(identityStatus);
        if (_logitechProbeResults.TryGetValue(resolvedDevice.Id, out var probeResult))
            replacement.SetLogitechHidppStatus(probeResult);
        if (_logitechConnectionStatuses.TryGetValue(resolvedDevice.Id, out var connectionStatus))
            replacement.SetLogitechConnectionStatus(connectionStatus);
        var index = _allDevices.FindIndex(item => item.Device.Id == resolvedDevice.Id);
        if (index >= 0) _allDevices[index] = replacement;
        var selectedDevice = SelectedDevice?.Device;
        RefreshVisibleDevices(selectedDevice?.Id ?? resolvedDevice.Id, selectedDevice);
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

    public void SaveDeviceProfile(bool applyXboxLeftBumperAssignment = false, string? expectedCurrentXboxLeftBumperAssignment = null)
    {
        if (SelectedDevice is null) return;
        try
        {
            var path = _deviceProfiles.SaveSet(SelectedDevice.LocalProfileKey, SelectedDevice.LocalProfiles,
                SelectedDevice.LocalProfile.Id);
            if (SelectedDevice.IsXboxElite)
            {
                if (!applyXboxLeftBumperAssignment)
                {
                    var selectedButton = SelectedDevice.SelectedXboxPhysicalButton;
                    if (selectedButton != "LB")
                    {
                        var selectedAssignment = SelectedDevice.LocalProfile.XboxButtonAssignments.GetValueOrDefault(selectedButton, selectedButton);
                        ConfigurationStatus = $"Perfil guardado en este PC. {selectedButton} → {selectedAssignment} sigue siendo local; la escritura al mando validada aquí solo cubre LB.";
                    }
                    else
                    {
                        ConfigurationStatus = $"Perfil guardado en este PC: {path}. El mando solo se actualiza al elegir un destino para LB.";
                    }
                    return;
                }

                if (SelectedDevice.SelectedXboxPhysicalButton != "LB" ||
                    !SelectedDevice.LocalProfile.XboxButtonAssignments.TryGetValue("LB", out var assignment))
                {
                    ConfigurationStatus = $"Perfil guardado en este PC: {path}. No se escribió al mando porque no se cambió una asignación explícita de LB.";
                    return;
                }
                if (SelectedDevice.LocalProfile.XboxAccessoriesSlot is not (1 or 2))
                {
                    ConfigurationStatus = $"Perfil guardado en este PC: {path}. Asigna el perfil a una ranura del mando antes de aplicar LB.";
                    return;
                }
                if (SelectedDevice.ActiveXboxProfileSlot != SelectedDevice.LocalProfile.XboxAccessoriesSlot)
                {
                    ConfigurationStatus = $"Perfil guardado en este PC: {path}. LB no se escribió porque el mando no identifica esta ranura como activa.";
                    return;
                }
                if (string.IsNullOrWhiteSpace(expectedCurrentXboxLeftBumperAssignment))
                {
                    ConfigurationStatus = $"Perfil guardado en este PC: {path}. LB no se escribió porque falta el valor anterior para confirmar la ranura activa.";
                    return;
                }
                int? liveXboxSlot;
                try
                {
                    var liveMapping = _xboxEliteProfileWriter.ReadCurrentLeftBumperMapping(SelectedDevice.Device);
                    liveXboxSlot = SelectedDevice.ResolveXboxProfileSlotFromLeftBumper(liveMapping);
                }
                catch (Exception exception)
                {
                    ConfigurationStatus = $"Perfil local guardado en este PC: {path}. No se escribió LB porque no se pudo confirmar la ranura activa: {exception.Message}";
                    return;
                }
                if (liveXboxSlot != SelectedDevice.LocalProfile.XboxAccessoriesSlot)
                {
                    ConfigurationStatus = $"Perfil local guardado en este PC: {path}. No se escribió LB porque la lectura actual no identifica de forma única la ranura de «{SelectedDevice.LocalProfile.Name}».";
                    return;
                }
                if (!XboxEliteProfileWriter.TryGetLeftBumperMapping(assignment, out _, out _))
                {
                    ConfigurationStatus = $"Perfil guardado en este PC: {path}. LB → {assignment} aún no tiene una codificación admitida por el escritor; no se escribió al mando.";
                    return;
                }

                string writerStatus;
                string accessoriesSync;
                try
                {
                    writerStatus = _xboxEliteProfileWriter.ApplyLeftBumperAssignment(
                        SelectedDevice.Device, assignment, expectedCurrentXboxLeftBumperAssignment);
                }
                catch (Exception exception)
                {
                    ConfigurationStatus = $"Perfil local guardado en este PC; el mando no confirmó LB → {assignment}: {exception.Message}";
                    return;
                }
                try
                {
                    accessoriesSync = _deviceProfiles.SyncXboxAccessoriesLeftBumperAssignment(SelectedDevice.LocalProfile.Id, assignment);
                }
                catch (Exception exception)
                {
                    accessoriesSync = $"El mando respondió, pero no se sincronizó la copia local de Xbox Accessories: {exception.Message}";
                }
                ConfigurationStatus = $"{writerStatus} {accessoriesSync}";
            }
            else
            {
                ConfigurationStatus = $"{SelectedDevice.LocalProfiles.Count} perfil(es) Nexo guardado(s) en este PC: {path}";
            }
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"No se pudo guardar el perfil local: {exception.Message}";
        }
    }

    public byte ReadXboxLeftBumperMapping(HardwareDevice device) =>
        _xboxEliteProfileWriter.ReadCurrentLeftBumperMapping(device);

    public void AddDeviceLocalProfile()
    {
        if (SelectedDevice?.CanManageDeviceProfiles != true) return;
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
        if (SelectedDevice?.CanManageDeviceProfiles != true || !SelectedDevice.CanDeleteLocalProfile) return;
        SelectedDevice.RemoveLocalProfile();
        ConfigurationStatus = "Perfil quitado de la lista local. Pulsa Guardar perfil Nexo para confirmar el cambio.";
    }

    public void LinkDeviceProfileToApplication(string executablePath)
    {
        if (SelectedDevice?.CanManageDeviceProfiles != true) return;
        if (SelectedDevice.LocalProfiles.Any(profile => !ReferenceEquals(profile, SelectedDevice.LocalProfile) &&
            profile.ApplicationExecutable.Equals(executablePath, StringComparison.OrdinalIgnoreCase)))
        {
            ConfigurationStatus = "Esa aplicación ya tiene otro perfil local asociado a este dispositivo.";
            return;
        }
        SelectedDevice.LocalProfile.ApplicationExecutable = executablePath;
        ConfigurationStatus = $"Perfil local asociado a {Path.GetFileName(executablePath)}; se seleccionará automáticamente mientras esa aplicación esté activa.";
    }

    public void UnlinkDeviceProfileFromApplication()
    {
        if (SelectedDevice?.CanManageDeviceProfiles != true) return;
        SelectedDevice.LocalProfile.ApplicationExecutable = "";
        ConfigurationStatus = "Se quitó la asociación con la aplicación. El perfil queda como opción local manual.";
    }

    public void SelectProfilesForApplication(string executablePath)
    {
        MacroManager.SetActiveApplication(executablePath);
        foreach (var device in _allDevices.Where(card => card.CanManageDeviceProfiles))
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

    private void RefreshVisibleDevices(string? preferredId, HardwareDevice? fallbackDevice = null)
    {
        Devices.Clear();
        foreach (var device in _allDevices.Where(device => ShowAuxiliary || !device.Device.IsAuxiliary))
            Devices.Add(device);
        var selected = Devices.FirstOrDefault(item => item.Device.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
        if (selected is null && fallbackDevice is not null)
        {
            selected = fallbackDevice.XInputSlot is int slot
                ? Devices.FirstOrDefault(item => item.Device.XInputSlot == slot)
                : null;
            selected ??= fallbackDevice.ContainerId.Length > 0
                ? Devices.FirstOrDefault(item => item.Device.ContainerId.Equals(fallbackDevice.ContainerId, StringComparison.OrdinalIgnoreCase))
                : null;
            selected ??= fallbackDevice.ModelKey.Length > 0
                ? Devices.FirstOrDefault(item => item.Device.ModelKey.Equals(fallbackDevice.ModelKey, StringComparison.OrdinalIgnoreCase))
                : null;
            selected ??= fallbackDevice.VendorId != 0 && fallbackDevice.ProductId != 0
                ? Devices.FirstOrDefault(item => item.Device.VendorId == fallbackDevice.VendorId &&
                    item.Device.ProductId == fallbackDevice.ProductId)
                : null;
        }
        SelectedDevice = selected ?? (fallbackDevice is null ? Devices.FirstOrDefault() : null);
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(HasAuxiliaryDevices));
        OnPropertyChanged(nameof(AuxiliaryCount));
        OnPropertyChanged(nameof(AuxiliaryButtonLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
