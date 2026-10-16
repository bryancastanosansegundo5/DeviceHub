using System.IO;
using System.Text.Json;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class DeviceLocalProfileLibrary
{
    private const int MaximumProfilesPerDevice = 50;
    private readonly string _directory;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public DeviceLocalProfileLibrary(string? directory = null) => _directory = directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoHub", "DeviceProfiles");

    public DeviceLocalProfile Load(string modelKey)
    {
        var (profiles, activeProfileId) = LoadSet(modelKey);
        return profiles.First(profile => profile.Id == activeProfileId);
    }

    public (IReadOnlyList<DeviceLocalProfile> Profiles, string ActiveProfileId) LoadSet(string modelKey)
    {
        var setPath = ProfileSetPathFor(modelKey);
        if (File.Exists(setPath))
        {
            try
            {
                var set = JsonSerializer.Deserialize<ProfileSetData>(File.ReadAllText(setPath)) ?? new ProfileSetData();
                ValidateSet(modelKey, set);
                return (set.Profiles, set.ActiveProfileId);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
            }
        }

        var legacy = LoadLegacy(modelKey);
        return ([legacy], legacy.Id);
    }

    private DeviceLocalProfile LoadLegacy(string modelKey)
    {
        var path = PathFor(modelKey);
        if (!File.Exists(path)) return DefaultFor(modelKey);
        try
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<DeviceLocalProfile>(json) ?? new DeviceLocalProfile();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                !document.RootElement.TryGetProperty(nameof(DeviceLocalProfile.ShiftDpi), out _))
                profile.ShiftDpi = DefaultFor(modelKey).ShiftDpi;
            Validate(modelKey, profile);
            return profile;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new DeviceLocalProfile();
        }
    }

    public string Save(string modelKey, DeviceLocalProfile profile)
    {
        var (currentProfiles, _) = LoadSet(modelKey);
        var profiles = currentProfiles.ToList();
        var index = profiles.FindIndex(item => item.Id == profile.Id);
        if (index < 0) profiles.Add(profile);
        else profiles[index] = profile;
        return SaveSet(modelKey, profiles, profile.Id);
    }

    public string SaveSet(string modelKey, IEnumerable<DeviceLocalProfile> profiles, string activeProfileId)
    {
        var set = new ProfileSetData { Profiles = profiles.ToList(), ActiveProfileId = activeProfileId };
        ValidateSet(modelKey, set);
        Directory.CreateDirectory(_directory);
        var path = ProfileSetPathFor(modelKey);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(set, _options));
        File.Move(tempPath, path, overwrite: true);
        return path;
    }

    private static void ValidateSet(string modelKey, ProfileSetData set)
    {
        if (set.Profiles is null || set.Profiles.Count is < 1 or > MaximumProfilesPerDevice)
            throw new InvalidDataException($"El ratón debe tener entre 1 y {MaximumProfilesPerDevice} perfiles locales.");
        if (set.Profiles.Any(profile => profile is null))
            throw new InvalidDataException("La lista contiene un perfil local vacío.");
        foreach (var profile in set.Profiles) Validate(modelKey, profile);
        if (set.Profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != set.Profiles.Count)
            throw new InvalidDataException("Cada perfil local debe tener un identificador único.");
        if (set.Profiles.Select(profile => profile.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != set.Profiles.Count)
            throw new InvalidDataException("Cada perfil local debe tener un nombre único.");
        if (!set.Profiles.Any(profile => profile.Id == set.ActiveProfileId))
            throw new InvalidDataException("El perfil local activo debe pertenecer a la lista guardada.");
        var applications = set.Profiles.Select(profile => profile.ApplicationExecutable)
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (applications.Distinct(StringComparer.OrdinalIgnoreCase).Count() != applications.Length)
            throw new InvalidDataException("Cada aplicación puede tener un solo perfil local por ratón.");
    }

    private static void Validate(string modelKey, DeviceLocalProfile profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 64)
            throw new InvalidDataException("Cada perfil necesita un identificador válido.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80)
            throw new InvalidDataException("Escribe un nombre para el perfil de hasta 80 caracteres.");
        if (profile.ApplicationExecutable is null || profile.ApplicationExecutable.Length > 1024 ||
            (profile.ApplicationExecutable.Length > 0 &&
             (!Path.IsPathFullyQualified(profile.ApplicationExecutable) ||
              !Path.GetExtension(profile.ApplicationExecutable).Equals(".exe", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("La aplicación del perfil debe ser la ruta completa de un archivo .exe.");

        var dpiMaximum = modelKey switch
        {
            "logitech-pro-wireless" => 25600,
            "logitech-pro-x-superlight" => 25600,
            _ => 0
        };
        if (dpiMaximum > 0)
        {
            if (profile.DpiStageCount is < 1 or > 5)
                throw new InvalidDataException("El ratón debe tener entre 1 y 5 etapas DPI.");
            if (profile.ActiveDpiStage < 1 || profile.ActiveDpiStage > profile.DpiStageCount)
                throw new InvalidDataException("La etapa DPI activa debe estar dentro de las etapas configuradas.");
            foreach (var dpi in new[] { profile.Dpi1, profile.Dpi2, profile.Dpi3, profile.Dpi4, profile.Dpi5 })
                if (dpi is < 100 || dpi > dpiMaximum)
                    throw new InvalidDataException($"El DPI debe estar entre 100 y {dpiMaximum}.");
            if (profile.ShiftDpi is < 100 || profile.ShiftDpi > dpiMaximum)
                throw new InvalidDataException($"El DPI de G-Shift debe estar entre 100 y {dpiMaximum}.");
            if (profile.ReportRate is not (125 or 250 or 500 or 1000))
                throw new InvalidDataException("La tasa de respuesta debe ser 125, 250, 500 o 1000 Hz.");
            if (modelKey == "logitech-pro-wireless")
            {
                if (profile.LightingBrightness is < 0 or > 100)
                    throw new InvalidDataException("El brillo LIGHTSYNC debe estar entre 0 y 100.");
                if (profile.LightingCycleDurationMs is < 100 or > 60000)
                    throw new InvalidDataException("La duración del ciclo local debe estar entre 100 y 60000 ms.");
            }
            return;
        }

        switch (modelKey)
        {
            case "ajazz-ak820":
                if (profile.LightingBrightness is < 0 or > 100)
                    throw new InvalidDataException("El brillo debe estar entre 0 y 100.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(profile.LightingColor, "^[0-9A-Fa-f]{6}$"))
                    throw new InvalidDataException("El color debe usar seis dígitos hexadecimales, por ejemplo 53D6C2.");
                if (profile.KeyboardKeyColors is null || profile.KeyboardKeyColors.Count > 81 ||
                    profile.KeyboardKeyColors.Any(item =>
                        !System.Text.RegularExpressions.Regex.IsMatch(item.Key, "^0x[0-9A-Fa-f]{1,2}$") ||
                        !System.Text.RegularExpressions.Regex.IsMatch(item.Value, "^[0-9A-Fa-f]{6}$")))
                    throw new InvalidDataException("Cada tecla debe tener un código HID y un color RGB hexadecimal válido.");
                break;
            default:
                if (modelKey.StartsWith("xbox-", StringComparison.OrdinalIgnoreCase))
                {
                    if (new[] { profile.LeftTriggerStart, profile.LeftTriggerEnd, profile.RightTriggerStart, profile.RightTriggerEnd }
                        .Any(value => value is < 0 or > 10))
                        throw new InvalidDataException("Los límites de los gatillos deben estar entre 0 y 10.");
                    if (profile.LeftTriggerStart > profile.LeftTriggerEnd || profile.RightTriggerStart > profile.RightTriggerEnd)
                        throw new InvalidDataException("El punto inicial de cada gatillo debe ser menor o igual que el punto final.");
                }
                else if (modelKey == "astro-a50" &&
                    new[] { profile.EqBass, profile.EqLowMid, profile.EqMid, profile.EqHighMid, profile.EqTreble }
                        .Any(value => value is < -12 or > 12))
                {
                    throw new InvalidDataException("Cada banda de EQ debe estar entre -12 y +12 dB.");
                }
                break;
        }
    }

    private static DeviceLocalProfile DefaultFor(string modelKey) => modelKey switch
    {
        "logitech-pro-wireless" => new DeviceLocalProfile
        {
            Dpi1 = 400, Dpi2 = 800, Dpi3 = 1600, Dpi4 = 3200, Dpi5 = 6400,
            DpiStageCount = 5, ActiveDpiStage = 2, ShiftDpi = 400, MouseLightingEnabled = true,
            LightingBrightness = 100, LightingCycleDurationMs = 8000
        },
        "logitech-pro-x-superlight" => new DeviceLocalProfile
        {
            Dpi1 = 1300, Dpi2 = 2000, DpiStageCount = 2, ShiftDpi = 1300
        },
        _ => new DeviceLocalProfile()
    };

    private string ProfileSetPathFor(string modelKey) => Path.Combine(_directory, SafeModelKey(modelKey) + ".profiles.json");

    private string PathFor(string modelKey) => Path.Combine(_directory, SafeModelKey(modelKey) + ".json");

    private static string SafeModelKey(string modelKey)
    {
        var safe = System.Text.RegularExpressions.Regex.Replace(modelKey, @"[^A-Za-z0-9_-]", "_");
        if (safe.Length == 0) safe = "generic-device";
        return safe;
    }

    private sealed class ProfileSetData
    {
        public ProfileSetData() { }
        public List<DeviceLocalProfile> Profiles { get; set; } = [];
        public string ActiveProfileId { get; set; } = "";
    }
}
