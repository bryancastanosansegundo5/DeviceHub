using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NexoHub.Models;
using NexoHub.Services;

namespace NexoHub.ViewModels;

public sealed class DeviceCardViewModel : INotifyPropertyChanged
{
    private static readonly string[] XboxButtonOptions =
    [
        "Botón A", "Botón B", "Botón X", "Botón Y", "LB", "RB", "LT", "RT",
        "Botón Ver", "Botón Menú", "Palanca izquierda", "Palanca derecha",
        "Cruceta arriba", "Cruceta abajo", "Cruceta izquierda", "Cruceta derecha",
        "Paleta P1", "Paleta P2", "Paleta P3", "Paleta P4", "Sin asignar"
    ];
    private static readonly string[] XboxStickCurveOptions =
        ["Predeterminado", "Instantáneo", "Suave", "Agresivo", "Retardado"];
    private int _activeLogitechDpiStage;
    private DeviceLocalProfile _localProfile;
    private string _logitechConnectionStatus = "Sin comprobar";
    private string _selectedXboxPhysicalButton = "Botón A";
    private string _selectedXboxAssignment = "Botón A";
    private bool _isXboxControllerBackView;
    private bool _isEditingXboxProfile;
    private bool _isShowingXboxHome = true;
    private int? _activeXboxProfileSlot;

    public DeviceCardViewModel(HardwareDevice device, AdapterInfo adapter, DeviceLocalProfile? localProfile = null,
        IReadOnlyList<DeviceLocalProfile>? localProfiles = null, string? activeProfileId = null)
    {
        Device = device;
        ProductImageSource = ResolveProductImage(device.ModelKey);
        Adapter = adapter;
        Capabilities = new ObservableCollection<CapabilityInfo>(adapter.Capabilities);
        var profiles = localProfiles?.ToList() ?? [];
        if (profiles.Count == 0) profiles.Add(localProfile ?? new DeviceLocalProfile());
        LocalProfiles = new ObservableCollection<DeviceLocalProfile>(profiles);
        LocalProfiles.CollectionChanged += LocalProfilesChanged;
        foreach (var profile in LocalProfiles) profile.PropertyChanged += LocalProfileChanged;
        _localProfile = profiles.FirstOrDefault(profile => profile.Id == activeProfileId) ?? profiles[0];
        _selectedXboxAssignment = GetXboxAssignment(_selectedXboxPhysicalButton);
        LocalProfile.DpiStageCount = Math.Clamp(LocalProfile.DpiStageCount, 1, 5);
        _activeLogitechDpiStage = Math.Clamp(LocalProfile.ActiveDpiStage, 1, LocalProfile.DpiStageCount);
        LocalProfile.ActiveDpiStage = _activeLogitechDpiStage;
        XboxInput = new XboxInputViewModel();
        AudioEndpoints = new ObservableCollection<AudioEndpointViewModel>((device.AudioEndpoints ?? [])
            .Select(endpoint => new AudioEndpointViewModel(endpoint)));
    }

    public HardwareDevice Device { get; }
    public Uri? ProductImageSource { get; }
    public Uri? XboxBackImageSource => ResolveXboxImage("Troy_back.png");
    public Uri? XboxLeftStickImageSource => ResolveXboxImage("Troy_left_stick.png");
    public Uri? XboxRightStickImageSource => ResolveXboxImage("Troy_right_stick.png");
    public Uri? XboxLeftTriggerImageSource => ResolveXboxImage("Troy_left_trigger.png");
    public Uri? XboxRightTriggerImageSource => ResolveXboxImage("Troy_right_trigger.png");
    public Uri? XboxBrightnessImageSource => ResolveXboxImage("Xbox_button_Brightness.png");
    public IReadOnlyList<string> XboxPhysicalButtons => XboxButtonOptions[..^1];
    public IReadOnlyList<string> XboxAssignmentOptions => XboxButtonOptions;
    public IReadOnlyList<string> XboxStickCurves => XboxStickCurveOptions;
    public string XboxPaddleAssignmentsSummary => string.Join("   ·   ",
        GetConfiguredXboxButtonAssignments()
            .Where(item => item.Key.StartsWith("Paleta", StringComparison.Ordinal))
            .Select(item => $"{GetXboxAssignmentShortLabel(item.Key)} → {GetXboxAssignmentShortLabel(item.Value)}"));
    public bool HasXboxPaddleAssignments => XboxPaddleAssignmentsSummary.Length > 0;
    public string XboxSlot1ProfileName => LocalProfiles.FirstOrDefault(profile => profile.XboxAccessoriesSlot == 1)?.DisplayName ?? "Predeterminado";
    public string XboxSlot2ProfileName => LocalProfiles.FirstOrDefault(profile => profile.XboxAccessoriesSlot == 2)?.DisplayName ?? "Predeterminado";
    public int? ActiveXboxProfileSlot => _activeXboxProfileSlot;
    public DeviceLocalProfile? ActiveXboxProfile => _activeXboxProfileSlot is int slot
        ? LocalProfiles.FirstOrDefault(profile => profile.XboxAccessoriesSlot == slot)
        : null;
    public DeviceLocalProfile? XboxHomePreviewProfile
    {
        get
        {
            var profile = ActiveXboxProfile ?? LocalProfile;
            return profile.IsXboxAccessoriesSlotPlaceholder ? null : profile;
        }
    }
    public int? XboxHomePreviewSlot => ActiveXboxProfileSlot ??
        (LocalProfile.XboxAccessoriesSlot is 1 or 2 ? LocalProfile.XboxAccessoriesSlot : null);
    public bool IsShowingXboxHome
    {
        get => _isShowingXboxHome;
        private set
        {
            if (_isShowingXboxHome == value) return;
            _isShowingXboxHome = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsShowingXboxProfiles));
        }
    }
    public bool IsShowingXboxProfiles => !_isShowingXboxHome && !_isEditingXboxProfile;
    public bool IsXboxSlotPlaceholder => LocalProfile.IsXboxAccessoriesSlotPlaceholder;
    public bool CanEditXboxProfile => IsXboxElite && !IsXboxSlotPlaceholder;
    public string XboxProfileSlotLabel => LocalProfile.XboxAccessoriesSlot is int slot ? $"Ranura {slot}" : "Sin asignar";
    public bool IsXboxControllerBackView
    {
        get => _isXboxControllerBackView;
        set
        {
            if (_isXboxControllerBackView == value) return;
            _isXboxControllerBackView = value;
            OnPropertyChanged();
        }
    }
    public bool IsEditingXboxProfile
    {
        get => _isEditingXboxProfile;
        set
        {
            if (_isEditingXboxProfile == value) return;
            _isEditingXboxProfile = value;
            if (value) IsShowingXboxHome = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsShowingXboxProfiles));
        }
    }

    public void ShowXboxHome()
    {
        IsEditingXboxProfile = false;
        IsShowingXboxHome = true;
    }

    public void ShowXboxProfiles()
    {
        IsShowingXboxHome = false;
        IsEditingXboxProfile = false;
    }
    public string SelectedXboxPhysicalButton
    {
        get => _selectedXboxPhysicalButton;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !XboxPhysicalButtons.Contains(value) || _selectedXboxPhysicalButton == value) return;
            _selectedXboxPhysicalButton = value;
            _selectedXboxAssignment = GetXboxAssignment(value);
            IsXboxControllerBackView = value.StartsWith("Paleta", StringComparison.Ordinal);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedXboxAssignment));
        }
    }
    public string SelectedXboxAssignment
    {
        get => _selectedXboxAssignment;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !XboxAssignmentOptions.Contains(value) || _selectedXboxAssignment == value) return;
            _selectedXboxAssignment = value;
            LocalProfile.XboxButtonAssignments[_selectedXboxPhysicalButton] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(XboxPaddleAssignmentsSummary));
            OnPropertyChanged(nameof(HasXboxPaddleAssignments));
        }
    }
    public bool HasProductImage => ProductImageSource is not null;
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
            _selectedXboxAssignment = GetXboxAssignment(_selectedXboxPhysicalButton);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedXboxAssignment));
            OnPropertyChanged(nameof(XboxPaddleAssignmentsSummary));
            OnPropertyChanged(nameof(HasXboxPaddleAssignments));
            OnPropertyChanged(nameof(XboxHomePreviewProfile));
            OnPropertyChanged(nameof(XboxHomePreviewSlot));
            OnPropertyChanged(nameof(XboxSlot1ProfileName));
            OnPropertyChanged(nameof(XboxSlot2ProfileName));
            OnPropertyChanged(nameof(IsXboxSlotPlaceholder));
            OnPropertyChanged(nameof(CanEditXboxProfile));
            OnPropertyChanged(nameof(XboxProfileSlotLabel));
            OnPropertyChanged(nameof(CanDeleteLocalProfile));
            OnPropertyChanged(nameof(CanAddLocalProfile));
            OnPropertyChanged(nameof(LogitechDpiStageCount));
            OnPropertyChanged(nameof(HasLogitechDpiStage2));
            OnPropertyChanged(nameof(HasLogitechDpiStage3));
            OnPropertyChanged(nameof(HasLogitechDpiStage4));
            OnPropertyChanged(nameof(HasLogitechDpiStage5));
            OnPropertyChanged(nameof(CanAddLogitechDpiStage));
            OnPropertyChanged(nameof(CanRemoveLogitechDpiStage));
            OnPropertyChanged(nameof(ActiveLogitechDpiStage));
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
    public bool CanDeleteLocalProfile => LocalProfiles.Count > 1 && !LocalProfile.IsXboxAccessoriesSlotPlaceholder;
    public bool CanAddLocalProfile => LocalProfiles.Count < 50;
    public XboxInputViewModel XboxInput { get; }
    public static string GetXboxAssignmentShortLabel(string? assignment) => assignment switch
    {
        "Botón A" => "A",
        "Botón B" => "B",
        "Botón X" => "X",
        "Botón Y" => "Y",
        "Botón Ver" => "Ver",
        "Botón Menú" => "≡",
        "Palanca izquierda" => "LS",
        "Palanca derecha" => "RS",
        "Cruceta arriba" => "↑",
        "Cruceta abajo" => "↓",
        "Cruceta izquierda" => "←",
        "Cruceta derecha" => "→",
        "Paleta P1" => "P1",
        "Paleta P2" => "P2",
        "Paleta P3" => "P3",
        "Paleta P4" => "P4",
        "Sin asignar" => "—",
        _ => assignment ?? "—"
    };

    private string GetXboxAssignment(string physicalButton) =>
        LocalProfile.XboxButtonAssignments.GetValueOrDefault(physicalButton,
            physicalButton.StartsWith("Paleta", StringComparison.Ordinal) ? "Sin asignar" : physicalButton);

    public IReadOnlyList<KeyValuePair<string, string>> GetConfiguredXboxButtonAssignments(DeviceLocalProfile? profile = null)
    {
        profile ??= LocalProfile;
        return XboxPhysicalButtons
            .Where(profile.XboxButtonAssignments.ContainsKey)
            .Select(button => new KeyValuePair<string, string>(button,
                profile.XboxButtonAssignments.GetValueOrDefault(button, button.StartsWith("Paleta", StringComparison.Ordinal) ? "Sin asignar" : button)))
            .Where(item => IsXboxButtonAssignmentConfigured(item.Key, item.Value))
            .ToArray();
    }

    public static bool IsXboxButtonAssignmentConfigured(string physicalButton, string? assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment) ||
            assignment.Equals("Sin asignar", StringComparison.OrdinalIgnoreCase)) return false;
        if (physicalButton.StartsWith("Paleta", StringComparison.OrdinalIgnoreCase)) return true;
        return !physicalButton.Equals(assignment, StringComparison.OrdinalIgnoreCase);
    }

    public int? ResolveXboxProfileSlotFromLeftBumper(byte liveMapping)
    {
        var matchingSlots = LocalProfiles
            .Where(profile => profile.XboxAccessoriesSlot is 1 or 2)
            .Where(profile =>
            {
                var assignment = profile.XboxButtonAssignments.GetValueOrDefault("LB", "LB");
                return XboxEliteProfileWriter.TryGetLeftBumperMapping(assignment, out var value, out _) && value == liveMapping;
            })
            .Select(profile => profile.XboxAccessoriesSlot!.Value)
            .Distinct()
            .ToArray();
        return matchingSlots.Length == 1 ? matchingSlots[0] : null;
    }

    public void SetDetectedXboxProfileSlot(int? slot)
    {
        if (slot is not (1 or 2)) slot = null;
        if (_activeXboxProfileSlot != slot)
        {
            _activeXboxProfileSlot = slot;
            OnPropertyChanged(nameof(ActiveXboxProfileSlot));
            OnPropertyChanged(nameof(ActiveXboxProfile));
            OnPropertyChanged(nameof(XboxHomePreviewProfile));
            OnPropertyChanged(nameof(XboxHomePreviewSlot));
        }
        if (slot is int activeSlot && LocalProfiles.FirstOrDefault(profile => profile.XboxAccessoriesSlot == activeSlot) is { } profile &&
            !ReferenceEquals(LocalProfile, profile))
            LocalProfile = profile;
    }
    public string LocalProfileKey => Device.ModelKey.Length > 0 ? Device.ModelKey : Device.Id;
    public ObservableCollection<AudioEndpointViewModel> AudioEndpoints { get; }
    public string DisplayName => Device.ModelKey switch
    {
        "logitech-pro-wireless" => "PRO WIRELESS",
        "logitech-pro-x-superlight" => "PRO X SUPERLIGHT",
        _ => Adapter.DisplayName
    };
    public string Brand => Adapter.Brand;
    public string Status => Adapter.Status;
    public string LogitechConnectionStatus => _logitechConnectionStatus;
    public string Description => HasLogitechMouseSettings ? "Ratón Logitech G" : Adapter.Description;
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
        DeviceKind.Mouse => "Conecta el ratón para configurar sus niveles de DPI.",
        DeviceKind.Keyboard => "La iluminación se guarda como perfil Nexo. El envío al teclado espera la identificación exacta de esta variante.",
        DeviceKind.Gamepad => "La lectura XInput está disponible. El perfil Nexo aún no se escribe en el mando.",
        DeviceKind.Headset => "Windows no ha publicado endpoints de audio activos para este auricular.",
        DeviceKind.Microphone => "Windows no ha publicado endpoints de audio activos para este micrófono.",
        _ => "Este dispositivo no tiene controles del sistema disponibles; consulta el estado del adaptador antes de guardar cambios."
    };
    public string VisualKind => Device.Kind.ToString();
    public bool IsMouse => Device.Kind == DeviceKind.Mouse;
    public bool IsLogitechMouse => Device.ModelKey is "logitech-pro-wireless" or "logitech-pro-x-superlight";
    public bool IsUnidentifiedLogitechMouse => IsMouse && Device.ModelKey == "logitech-lightspeed-mouse" &&
        Device.VendorId == 0x046D && Device.ProductId == 0xC547 && IsLogitechHidppCandidate;
    public bool HasLogitechMouseSettings => IsLogitechMouse || IsUnidentifiedLogitechMouse;
    public bool ShowGenericSettingsHeader => !IsXboxElite && !HasLogitechMouseSettings;
    public bool CanManageDeviceProfiles => HasLogitechMouseSettings || IsXboxElite;
    public bool IsLogitechProWireless => Device.ModelKey == "logitech-pro-wireless" && Device.VendorId == 0x046D && Device.ProductId == 0xC539;
    public bool IsLogitechProXSuperlight => Device.ModelKey == "logitech-pro-x-superlight" && Device.VendorId == 0x046D &&
        Device.ProductId == 0xC547 && Device.IdentificationNote.Contains("HID++ 0x0005", StringComparison.OrdinalIgnoreCase);
    public bool IsLogitechDpiWritable => IsLogitechProWireless || IsLogitechProXSuperlight;
    public bool CanSaveLogitechOnboardDpi => IsLogitechProXSuperlight;
    public string LogitechProfileStorageStatus => IsUnidentifiedLogitechMouse
        ? "No se puede aplicar hasta identificar el ratón."
        : IsLogitechProXSuperlight
            ? "Los niveles se pueden guardar en el ratón."
            : "El DPI activo va al ratón; el perfil se guarda en este PC.";
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
    public bool CanAddLogitechDpiStage => HasLogitechMouseSettings && LogitechDpiStageCount < 5;
    public bool CanRemoveLogitechDpiStage => HasLogitechMouseSettings && LogitechDpiStageCount > 1;
    public int ActiveLogitechDpiStage => _activeLogitechDpiStage;
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
    public bool ShowNoAudioEndpointsMessage => !HasAudioEndpoints && (IsMicrophone || IsHeadset);
    public bool HasLocalEditor => HasLogitechMouseSettings || IsAjazzKeyboard || IsXboxElite || IsAstroA50;
    public bool HasAudioEndpoints => AudioEndpoints.Count > 0;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void LocalProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DeviceLocalProfile profile in e.OldItems) profile.PropertyChanged -= LocalProfileChanged;
        if (e.NewItems is not null)
            foreach (DeviceLocalProfile profile in e.NewItems) profile.PropertyChanged += LocalProfileChanged;
        OnPropertyChanged(nameof(XboxSlot1ProfileName));
        OnPropertyChanged(nameof(XboxSlot2ProfileName));
        OnPropertyChanged(nameof(ActiveXboxProfile));
        OnPropertyChanged(nameof(XboxHomePreviewProfile));
        OnPropertyChanged(nameof(XboxHomePreviewSlot));
        OnPropertyChanged(nameof(CanDeleteLocalProfile));
        OnPropertyChanged(nameof(CanAddLocalProfile));
    }

    private void LocalProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeviceLocalProfile.Name) || sender is not DeviceLocalProfile profile) return;
        if (profile.XboxAccessoriesSlot == 1) OnPropertyChanged(nameof(XboxSlot1ProfileName));
        if (profile.XboxAccessoriesSlot == 2) OnPropertyChanged(nameof(XboxSlot2ProfileName));
    }

    private static Uri? ResolveProductImage(string modelKey)
    {
        if (modelKey == "ajazz-ak820")
            return new Uri("pack://application:,,,/Assets/Devices/ajazz-ak820pro.png", UriKind.Absolute);
        if (modelKey == "astro-a50")
            return new Uri("pack://application:,,,/Assets/Devices/astro-a50-gen3.png", UriKind.Absolute);

        var path = modelKey switch
        {
            "logitech-pro-wireless" => FindLogitechImage(Path.Combine("pro_wireless_mouse_depot", "front_black.png")),
            "logitech-pro-x-superlight" => FindLogitechImage(Path.Combine("pro_x_wireless_mouse", "images", "device", "front.png")),
            "blue-yeti-classic" => FindLogitechImage(Path.Combine("yeti_classic", "Front.png")),
            // shortcut: Xbox Accessories package path includes its current version, update when the installed package changes.
            "xbox-one-elite" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps",
                "Microsoft.XboxDevices_2506.2505.28001.0_x64__8wekyb3d8bbwe", "Assets", "Images", "Troy.png"),
            _ => null
        };

        return path is not null && File.Exists(path) ? new Uri(path, UriKind.Absolute) : null;
    }

    private Uri? ResolveXboxImage(string fileName)
    {
        if (!IsXboxElite || ProductImageSource is null) return null;
        var path = Path.Combine(Path.GetDirectoryName(ProductImageSource.LocalPath)!, fileName);
        return File.Exists(path) ? new Uri(path, UriKind.Absolute) : null;
    }

    private static string? FindLogitechImage(string relativePath)
    {
        var depots = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LGHUB", "depots");
        try
        {
            return Directory.EnumerateDirectories(depots)
                .Select(depot => Path.Combine(depot, relativePath))
                .FirstOrDefault(File.Exists);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

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
        profile.StorageLabel = "Perfil Nexo local";
        profile.XboxAccessoriesSlot = null;
        profile.IsXboxAccessoriesSlotPlaceholder = false;
        profile.XboxAccessoriesSourceName = null;
        profile.ApplicationExecutable = "";
        var index = LocalProfiles.Count + 1;
        var profileName = IsXboxElite ? $"Perfil {index}" : $"Perfil Nexo {index}";
        while (LocalProfiles.Any(item => item.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            profileName = IsXboxElite ? $"Perfil {index}" : $"Perfil Nexo {index}";
        }
        profile.Name = profileName;
        LocalProfiles.Add(profile);
        LocalProfile = profile;
    }

    public void RemoveLocalProfile()
    {
        if (!CanDeleteLocalProfile) return;
        LocalProfiles.Remove(LocalProfile);
        LocalProfile = LocalProfiles[0];
    }

    public void ResetXboxSettings()
    {
        var profile = LocalProfile;
        profile.XboxButtonAssignments.Clear();
        profile.XboxSwapSticks = false;
        profile.XboxInvertLeftStickY = false;
        profile.XboxInvertRightStickY = false;
        profile.XboxSwapTriggers = false;
        profile.XboxLeftStickCurve = profile.XboxRightStickCurve = "Instantáneo";
        profile.XboxLeftStickCurveAdjustment = profile.XboxRightStickCurveAdjustment = 10;
        profile.XboxLeftStickRadialCalculation = profile.XboxRightStickRadialCalculation = false;
        profile.XboxLeftTriggerDeadzoneStart = profile.XboxRightTriggerDeadzoneStart = 0;
        profile.XboxLeftTriggerDeadzoneEnd = profile.XboxRightTriggerDeadzoneEnd = 10;
        profile.XboxMirrorTriggers = false;
        profile.XboxLeftTriggerVibration = profile.XboxLeftMainVibration = 100;
        profile.XboxRightTriggerVibration = profile.XboxRightMainVibration = 100;
        profile.XboxButtonBrightness = 100;
        _selectedXboxPhysicalButton = _selectedXboxAssignment = "Botón A";
        IsXboxControllerBackView = false;
        OnPropertyChanged(nameof(LocalProfile));
        OnPropertyChanged(nameof(SelectedXboxPhysicalButton));
        OnPropertyChanged(nameof(SelectedXboxAssignment));
        OnPropertyChanged(nameof(XboxPaddleAssignmentsSummary));
        OnPropertyChanged(nameof(HasXboxPaddleAssignments));
        OnPropertyChanged(nameof(HasXboxPaddleAssignments));
        OnPropertyChanged(nameof(XboxSlot1ProfileName));
        OnPropertyChanged(nameof(XboxSlot2ProfileName));
        OnPropertyChanged(nameof(IsXboxSlotPlaceholder));
        OnPropertyChanged(nameof(CanEditXboxProfile));
        OnPropertyChanged(nameof(XboxProfileSlotLabel));
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
        DeviceKind.Mouse => "DPI y perfiles",
        DeviceKind.Microphone => "Entrada de sonido",
        DeviceKind.Headset => "Audio del ASTRO A50",
        DeviceKind.Gamepad => "Prueba del mando y perfiles",
        DeviceKind.Keyboard => "Teclas, iluminación y macros",
        DeviceKind.Receiver => "Receptor USB",
        _ => "Ajustes del dispositivo"
    };
    public string SettingsSummary => Device.Kind switch
    {
        DeviceKind.Mouse when IsUnidentifiedLogitechMouse => "Prepara tus niveles mientras Nexo reconoce el ratón.",
        DeviceKind.Mouse when IsLogitechMouse => "Ajusta los niveles y aplícalos al ratón.",
        DeviceKind.Mouse => "Modelo no identificado; los controles específicos del ratón están desactivados.",
        DeviceKind.Microphone => "Cambia el nivel de entrada de Windows y comprueba que el micrófono Yeti Classic esté activo.",
        DeviceKind.Headset => "Ajusta los endpoints Game/Voice en Windows. EQ, sidetone y perfiles de memoria requieren el protocolo ASTRO validado.",
        DeviceKind.Gamepad => "Modelo Xbox One Elite leído de Windows. XInput permite probar botones y ejes; los perfiles locales no se escriben al mando.",
        DeviceKind.Keyboard => "El paquete AJAZZ declara AK820 por USB (8009/800A) o receptor 2.4 GHz (FDFD); Bluetooth no admite ajustes. Los cambios disponibles en Nexo son locales.",
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
