using System.IO;
using System.Text.Json;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class DeviceLocalProfileLibrary
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public DeviceLocalProfileLibrary(string? directory = null) => _directory = directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoHub", "DeviceProfiles");

    public DeviceLocalProfile Load(string modelKey)
    {
        var path = PathFor(modelKey);
        if (!File.Exists(path)) return new DeviceLocalProfile();
        try
        {
            var profile = JsonSerializer.Deserialize<DeviceLocalProfile>(File.ReadAllText(path)) ?? new DeviceLocalProfile();
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
        Validate(modelKey, profile);
        Directory.CreateDirectory(_directory);
        var path = PathFor(modelKey);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(profile, _options));
        File.Move(tempPath, path, overwrite: true);
        return path;
    }

    private static void Validate(string modelKey, DeviceLocalProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("Escribe un nombre para el perfil.");

        var dpiMaximum = modelKey switch
        {
            "logitech-pro-wireless" => 25600,
            "logitech-pro-x-superlight" => 25600,
            _ => 0
        };
        if (dpiMaximum > 0)
        {
            if (profile.ActiveDpiStage is < 1 or > 4)
                throw new InvalidDataException("La etapa DPI activa debe estar entre 1 y 4.");
            foreach (var dpi in new[] { profile.Dpi1, profile.Dpi2, profile.Dpi3, profile.Dpi4 })
                if (dpi is < 100 || dpi > dpiMaximum)
                    throw new InvalidDataException($"El DPI debe estar entre 100 y {dpiMaximum}.");
            if (profile.ReportRate is not (125 or 250 or 500 or 1000))
                throw new InvalidDataException("La tasa de respuesta debe ser 125, 250, 500 o 1000 Hz.");
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

    private string PathFor(string modelKey)
    {
        var safe = System.Text.RegularExpressions.Regex.Replace(modelKey, @"[^A-Za-z0-9_-]", "_");
        if (safe.Length == 0) safe = "generic-device";
        return Path.Combine(_directory, safe + ".json");
    }
}
