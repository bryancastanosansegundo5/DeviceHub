using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class DeviceLocalProfileLibrary
{
    private const int MaximumProfilesPerDevice = 50;
    private readonly string _directory;
    private readonly string _xboxAccessoriesWgsRoot;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public DeviceLocalProfileLibrary(string? directory = null, string? xboxAccessoriesWgsRoot = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _directory = directory ?? Path.Combine(localAppData, "NexoHub", "DeviceProfiles");
        _xboxAccessoriesWgsRoot = xboxAccessoriesWgsRoot ??
            Path.Combine(localAppData, "Packages", "Microsoft.XboxDevices_8wekyb3d8bbwe", "LocalState", "wgs");
    }

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
                if (set.SchemaVersion == 0 && modelKey.StartsWith("xbox-", StringComparison.OrdinalIgnoreCase))
                    foreach (var profile in set.Profiles) ScaleLegacyXboxTriggerValues(profile);
                set.SchemaVersion = 1;
                if (modelKey == "xbox-one-elite") RefreshXboxAccessoriesSlotAssignments(set);
                ValidateSet(modelKey, set);
                return (set.Profiles, set.ActiveProfileId);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
            }
        }

        var legacyPath = PathFor(modelKey);
        if (!File.Exists(legacyPath) && modelKey == "xbox-one-elite" && LoadXboxAccessoriesProfiles() is { } accessories)
            return accessories;

        var legacy = LoadLegacy(modelKey);
        return ([legacy], legacy.Id);
    }

    private (IReadOnlyList<DeviceLocalProfile> Profiles, string ActiveProfileId)? LoadXboxAccessoriesProfiles()
    {
        var wgsRoot = _xboxAccessoriesWgsRoot;
        if (!Directory.Exists(wgsRoot)) return null;

        try
        {
            var configDirectory = FindNewestXboxAccessoriesConfigDirectory(wgsRoot);
            if (configDirectory is null) return null;

            var slotAssignments = ReadXboxAccessoriesSlots(configDirectory);
            var profiles = new List<DeviceLocalProfile>();
            foreach (var path in Directory.EnumerateFiles(configDirectory)
                         .Where(path => !Path.GetFileName(path).Equals("configDisambig", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var profile = ReadXboxAccessoriesProfile(path);
                    if (profile is null || profiles.Count >= MaximumProfilesPerDevice ||
                        profiles.Any(item => item.Id == profile.Id || item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    profile.XboxAccessoriesSlot = slotAssignments.TryGetValue(profile.Id, out var slot)
                        ? slot
                        : null;
                    profile.StorageLabel = profile.XboxAccessoriesSlot is int assignedSlot
                        ? $"Copia local · ranura {assignedSlot}"
                        : "Copia local";
                    profiles.Add(profile);
                }
                catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
                {
                }
            }

            if (profiles.Count == 0) return null;
            profiles = profiles
                .OrderBy(profile => slotAssignments.TryGetValue(profile.Id, out var slot) ? slot : int.MaxValue)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddMissingXboxAccessoriesSlotProfiles(profiles, slotAssignments);
            var activeProfileId = slotAssignments.FirstOrDefault(item => item.Value == 1).Key;
            if (string.IsNullOrEmpty(activeProfileId) || profiles.All(profile => profile.Id != activeProfileId))
                activeProfileId = profiles.FirstOrDefault(profile => profile.Name.Equals("Perfil 1", StringComparison.OrdinalIgnoreCase))?.Id
                    ?? profiles[0].Id;

            var set = new ProfileSetData { SchemaVersion = 1, Profiles = profiles, ActiveProfileId = activeProfileId };
            ValidateSet("xbox-one-elite", set);
            return (set.Profiles, set.ActiveProfileId);
        }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    public string SyncXboxAccessoriesLeftBumperAssignment(string profileId, string assignment)
    {
        if (!XboxEliteProfileWriter.TryGetLeftBumperMapping(assignment, out _, out var accessoriesValue))
            return $"No se sincronizó Xbox Accessories: «{assignment}» no es un destino verificado.";
        if (!Guid.TryParse(profileId, out var profileGuid))
            return "No se sincronizó Xbox Accessories: el perfil no tiene un identificador válido.";

        var configDirectory = FindNewestXboxAccessoriesConfigDirectory(_xboxAccessoriesWgsRoot);
        if (configDirectory is null)
            return "No se encontró la copia local de Xbox Accessories; el cambio sigue guardado en Nexo.";

        var profileKey = profileGuid.ToString("N");
        if (!ReadXboxAccessoriesSlots(configDirectory).TryGetValue(profileKey, out var slot) || slot is not (1 or 2))
            return "El perfil no está asignado a una ranura de Xbox Accessories; el cambio sigue guardado en Nexo.";

        var profilePath = Directory.EnumerateFiles(configDirectory)
            .FirstOrDefault(path => Guid.TryParse(Path.GetFileName(path), out var fileGuid) && fileGuid == profileGuid);
        if (profilePath is null)
            return "No se encontró el archivo de Perfil 1 en Xbox Accessories; el cambio sigue guardado en Nexo.";

        var json = JsonNode.Parse(File.ReadAllText(profilePath)) as JsonObject
            ?? throw new InvalidDataException($"La copia local de la ranura {slot} de Xbox Accessories no es un objeto JSON.");
        if (!string.Equals(json["ConfigurationType"]?.GetValue<string>(), "Elite", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(json["ConfigurationGuid"]?.GetValue<string>(), out var storedGuid) || storedGuid != profileGuid)
            throw new InvalidDataException($"El archivo encontrado no coincide con el perfil Elite de la ranura {slot}.");

        if (string.Equals(json["HardwareLeftBumper"]?.GetValue<string>(), accessoriesValue, StringComparison.OrdinalIgnoreCase))
            return $"La copia local de Xbox Accessories ya tiene LB → {assignment}.";

        json["HardwareLeftBumper"] = accessoriesValue;
        var backupDirectory = Path.Combine(_directory, "XboxAccessoriesBackups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory,
            $"{profileGuid:N}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
        File.Copy(profilePath, backupPath);

        var temporaryPath = Path.Combine(_directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json.ToJsonString(_options));
            File.Replace(temporaryPath, profilePath, null);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        using var readback = JsonDocument.Parse(File.ReadAllText(profilePath));
        if (!string.Equals(ReadString(readback.RootElement, "HardwareLeftBumper"), accessoriesValue, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"La copia local de Xbox Accessories no confirmó LB → {assignment} al volver a leerla.");

        return $"Copia local de la ranura {slot} sincronizada con LB → {assignment}; respaldo guardado en {backupPath}.";
    }

    private void RefreshXboxAccessoriesSlotAssignments(ProfileSetData set)
    {
        var configDirectory = FindNewestXboxAccessoriesConfigDirectory(_xboxAccessoriesWgsRoot);
        if (configDirectory is null) return;

        var accessoriesProfiles = ReadXboxAccessoriesProfilesById(configDirectory);
        var slots = ReadXboxAccessoriesSlots(configDirectory);
        foreach (var profile in set.Profiles)
        {
            if (accessoriesProfiles.TryGetValue(NormalizeProfileId(profile.Id), out var accessoriesProfile))
                CopyXboxAccessoriesHardwareSettings(profile, accessoriesProfile);

            if (slots.TryGetValue(NormalizeProfileId(profile.Id), out var slot))
            {
                profile.XboxAccessoriesSlot = slot;
                if (!profile.IsXboxAccessoriesSlotPlaceholder)
                    profile.StorageLabel = $"Copia local · ranura {slot}";
            }
            else
            {
                profile.XboxAccessoriesSlot = null;
                if (profile.IsXboxAccessoriesSlotPlaceholder)
                    profile.StorageLabel = "Perfil predeterminado del mando";
                if (!profile.IsXboxAccessoriesSlotPlaceholder &&
                    profile.StorageLabel.Contains("ranura", StringComparison.OrdinalIgnoreCase))
                    profile.StorageLabel = "Copia local";
            }
        }

        set.Profiles.RemoveAll(profile => profile.IsXboxAccessoriesSlotPlaceholder && profile.XboxAccessoriesSlot is null);
        AddMissingXboxAccessoriesSlotProfiles(set.Profiles, slots);
        if (set.Profiles.All(profile => profile.Id != set.ActiveProfileId))
            set.ActiveProfileId = set.Profiles.FirstOrDefault(profile => profile.XboxAccessoriesSlot == 1)?.Id
                ?? set.Profiles.FirstOrDefault()?.Id
                ?? string.Empty;
    }

    private static Dictionary<string, DeviceLocalProfile> ReadXboxAccessoriesProfilesById(string configDirectory)
    {
        var profiles = new Dictionary<string, DeviceLocalProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(configDirectory)
                     .Where(path => !Path.GetFileName(path).Equals("configDisambig", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (ReadXboxAccessoriesProfile(path) is { } profile)
                    profiles[NormalizeProfileId(profile.Id)] = profile;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
            }
        }
        return profiles;
    }

    private static void CopyXboxAccessoriesHardwareSettings(DeviceLocalProfile target, DeviceLocalProfile source)
    {
        if (string.IsNullOrWhiteSpace(target.XboxAccessoriesSourceName) ||
            target.Name.Equals(target.XboxAccessoriesSourceName, StringComparison.OrdinalIgnoreCase))
            target.Name = source.Name;
        target.XboxAccessoriesSourceName = source.Name;
        target.IsXboxAccessoriesSlotPlaceholder = false;
        target.XboxButtonAssignments = new Dictionary<string, string>(source.XboxButtonAssignments, StringComparer.OrdinalIgnoreCase);
        target.XboxSwapSticks = source.XboxSwapSticks;
        target.XboxInvertLeftStickY = source.XboxInvertLeftStickY;
        target.XboxInvertRightStickY = source.XboxInvertRightStickY;
        target.XboxSwapTriggers = source.XboxSwapTriggers;
        target.XboxLeftStickRadialCalculation = source.XboxLeftStickRadialCalculation;
        target.XboxRightStickRadialCalculation = source.XboxRightStickRadialCalculation;
        target.XboxLeftTriggerDeadzoneStart = source.XboxLeftTriggerDeadzoneStart;
        target.XboxLeftTriggerDeadzoneEnd = source.XboxLeftTriggerDeadzoneEnd;
        target.XboxRightTriggerDeadzoneStart = source.XboxRightTriggerDeadzoneStart;
        target.XboxRightTriggerDeadzoneEnd = source.XboxRightTriggerDeadzoneEnd;
        target.XboxLeftTriggerVibration = source.XboxLeftTriggerVibration;
        target.XboxLeftMainVibration = source.XboxLeftMainVibration;
        target.XboxRightTriggerVibration = source.XboxRightTriggerVibration;
        target.XboxRightMainVibration = source.XboxRightMainVibration;
        target.XboxButtonBrightness = source.XboxButtonBrightness;
    }

    private static void AddMissingXboxAccessoriesSlotProfiles(
        List<DeviceLocalProfile> profiles,
        IReadOnlyDictionary<string, int> slots)
    {
        foreach (var (profileId, slot) in slots.OrderBy(item => item.Value))
        {
            if (profiles.Any(profile => NormalizeProfileId(profile.Id).Equals(profileId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (profiles.Count >= MaximumProfilesPerDevice) break;

            profiles.Add(new DeviceLocalProfile
            {
                Id = profileId,
                Name = "Predeterminado",
                StorageLabel = $"Ranura {slot} asignada en Xbox Accessories; falta su archivo local",
                XboxAccessoriesSlot = slot,
                IsXboxAccessoriesSlotPlaceholder = true
            });
        }
    }

    private static string NormalizeProfileId(string profileId) =>
        Guid.TryParse(profileId, out var id) ? id.ToString("N") : profileId;

    private static string? FindNewestXboxAccessoriesConfigDirectory(string wgsRoot)
    {
        if (!Directory.Exists(wgsRoot)) return null;
        // shortcut: select the most recently updated local Xbox Accessories account when several are present.
        return Directory.EnumerateDirectories(wgsRoot, "configs2", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Updated = File.Exists(Path.Combine(path, "configDisambig"))
                    ? File.GetLastWriteTimeUtc(Path.Combine(path, "configDisambig"))
                    : Directory.GetLastWriteTimeUtc(path)
            })
            .OrderByDescending(item => item.Updated)
            .FirstOrDefault()?.Path;
    }

    private static DeviceLocalProfile? ReadXboxAccessoriesProfile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !string.Equals(ReadString(root, "ConfigurationType"), "Elite", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(ReadString(root, "ConfigurationGuid"), out var configurationId))
            return null;

        var name = ReadString(root, "Name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var profile = new DeviceLocalProfile { Id = configurationId.ToString("N"), Name = name.Trim(), XboxAccessoriesSourceName = name.Trim() };
        MapXboxAccessoriesButton(root, profile, "HardwareLeftBumper", "LB");
        MapXboxAccessoriesButton(root, profile, "HardwareUpperLeftPaddle", "Paleta P1");
        MapXboxAccessoriesButton(root, profile, "HardwareLowerLeftPaddle", "Paleta P2");
        MapXboxAccessoriesButton(root, profile, "HardwareUpperRightPaddle", "Paleta P3");
        MapXboxAccessoriesButton(root, profile, "HardwareLowerRightPaddle", "Paleta P4");
        profile.XboxSwapSticks = ReadBoolean(root, "SwapThumbsticks");
        profile.XboxInvertLeftStickY = ReadBoolean(root, "InvertLeftStickYAxis");
        profile.XboxInvertRightStickY = ReadBoolean(root, "InvertRightStickYAxis");
        profile.XboxSwapTriggers = ReadBoolean(root, "SwapTriggers");
        profile.XboxLeftStickRadialCalculation = ReadBoolean(root, "LeftThumbstickRadial");
        profile.XboxRightStickRadialCalculation = ReadBoolean(root, "RightThumbstickRadial");
        profile.XboxLeftTriggerDeadzoneStart = ScaleXboxTrigger(ReadInt(root, "LeftTriggerHardwareParams", "TriggerMin", 0));
        profile.XboxLeftTriggerDeadzoneEnd = ScaleXboxTrigger(ReadInt(root, "LeftTriggerHardwareParams", "TriggerMax", 1023));
        profile.XboxRightTriggerDeadzoneStart = ScaleXboxTrigger(ReadInt(root, "RightTriggerHardwareParams", "TriggerMin", 0));
        profile.XboxRightTriggerDeadzoneEnd = ScaleXboxTrigger(ReadInt(root, "RightTriggerHardwareParams", "TriggerMax", 1023));
        profile.XboxLeftTriggerVibration = ScaleXboxMotor(ReadInt(root, "LeftRumbleMotor", "Impulse", 255));
        profile.XboxLeftMainVibration = ScaleXboxMotor(ReadInt(root, "LeftRumbleMotor", "Rumble", 255));
        profile.XboxRightTriggerVibration = ScaleXboxMotor(ReadInt(root, "RightRumbleMotor", "Impulse", 255));
        profile.XboxRightMainVibration = ScaleXboxMotor(ReadInt(root, "RightRumbleMotor", "Rumble", 255));
        profile.XboxButtonBrightness = Math.Clamp(ReadInt(root, "", "NexusBrightnessKey", 100), 0, 100);
        return profile;
    }

    private static Dictionary<string, int> ReadXboxAccessoriesSlots(string configDirectory)
    {
        var path = Path.Combine(configDirectory, "configDisambig");
        if (!File.Exists(path)) return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return slots;
            foreach (var device in document.RootElement.EnumerateObject())
            {
                if (device.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var slot in new[] { (Name: "slot1", Number: 1), (Name: "slot2", Number: 2) })
                    if (device.Value.TryGetProperty(slot.Name, out var value) && value.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(value.GetString(), out var configurationId))
                        slots[configurationId.ToString("N")] = slot.Number;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return slots;
    }

    private static void MapXboxAccessoriesButton(JsonElement root, DeviceLocalProfile profile, string source, string button)
    {
        var assignment = ReadString(root, source)?.ToLowerInvariant() switch
        {
            "a" => "Botón A",
            "b" => "Botón B",
            "x" => "Botón X",
            "y" => "Botón Y",
            "leftbumper" => "LB",
            "rightbumper" => "RB",
            "lefttrigger" => "LT",
            "righttrigger" => "RT",
            "leftthumbstickclick" => "Palanca izquierda",
            "rightthumbstickclick" => "Palanca derecha",
            "menu" => "Botón Menú",
            "view" => "Botón Ver",
            "dpadup" => "Cruceta arriba",
            "dpaddown" => "Cruceta abajo",
            "dpadleft" => "Cruceta izquierda",
            "dpadright" => "Cruceta derecha",
            "unmapped" => "Sin asignar",
            _ => null
        };
        if (assignment is null || assignment == "Sin asignar") return;
        var defaultAssignment = button switch
        {
            "LB" => "LB",
            "RB" => "RB",
            "LT" => "LT",
            "RT" => "RT",
            "Botón A" => "Botón A",
            "Botón B" => "Botón B",
            "Botón X" => "Botón X",
            "Botón Y" => "Botón Y",
            "Palanca izquierda" => "Palanca izquierda",
            "Palanca derecha" => "Palanca derecha",
            "Botón Menú" => "Botón Menú",
            "Botón Ver" => "Botón Ver",
            _ => null
        };
        if (!string.Equals(assignment, defaultAssignment, StringComparison.OrdinalIgnoreCase))
            profile.XboxButtonAssignments[button] = assignment;
    }

    private static string? ReadString(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBoolean(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement json, string section, string property, int fallback)
    {
        var value = json;
        if (section.Length > 0 && (!json.TryGetProperty(section, out value) || value.ValueKind != JsonValueKind.Object)) return fallback;
        return value.TryGetProperty(property, out var number) && number.TryGetInt32(out var result) ? result : fallback;
    }

    private static int ScaleXboxTrigger(int value) => Math.Clamp((int)Math.Round(Math.Clamp(value, 0, 1023) * 100d / 1023d), 0, 100);

    private static void ScaleLegacyXboxTriggerValues(DeviceLocalProfile profile)
    {
        profile.XboxLeftTriggerDeadzoneStart = Math.Clamp(profile.XboxLeftTriggerDeadzoneStart * 10, 0, 100);
        profile.XboxLeftTriggerDeadzoneEnd = Math.Clamp(profile.XboxLeftTriggerDeadzoneEnd * 10, 0, 100);
        profile.XboxRightTriggerDeadzoneStart = Math.Clamp(profile.XboxRightTriggerDeadzoneStart * 10, 0, 100);
        profile.XboxRightTriggerDeadzoneEnd = Math.Clamp(profile.XboxRightTriggerDeadzoneEnd * 10, 0, 100);
    }

    private static int ScaleXboxMotor(int value) => Math.Clamp((int)Math.Round(Math.Clamp(value, 0, 255) * 100d / 255d), 0, 100);

    public bool HasSavedSet(string modelKey) => File.Exists(ProfileSetPathFor(modelKey)) || File.Exists(PathFor(modelKey));

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
            if (modelKey.StartsWith("xbox-", StringComparison.OrdinalIgnoreCase))
                ScaleLegacyXboxTriggerValues(profile);
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
        var set = new ProfileSetData { SchemaVersion = 1, Profiles = profiles.ToList(), ActiveProfileId = activeProfileId };
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
            "logitech-lightspeed-mouse" => 25600,
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
                    if (new[] { profile.XboxLeftTriggerDeadzoneStart, profile.XboxLeftTriggerDeadzoneEnd,
                                profile.XboxRightTriggerDeadzoneStart, profile.XboxRightTriggerDeadzoneEnd }
                        .Any(value => value is < 0 or > 100) ||
                        profile.XboxLeftTriggerDeadzoneStart > profile.XboxLeftTriggerDeadzoneEnd ||
                        profile.XboxRightTriggerDeadzoneStart > profile.XboxRightTriggerDeadzoneEnd)
                        throw new InvalidDataException("Las zonas muertas deben ser porcentajes de 0 a 100 y el inicio no puede superar el final.");
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
        public int SchemaVersion { get; set; }
        public List<DeviceLocalProfile> Profiles { get; set; } = [];
        public string ActiveProfileId { get; set; } = "";
    }
}
