namespace NexoHub.Models;

public sealed record ProfileValue(string Section, string Key, string Value);

public sealed class ProfileFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Format { get; init; }
    public required IReadOnlyList<ProfileValue> Values { get; init; }
    public string DeviceId => Values.FirstOrDefault(value => value.Key.Equals("Device_Id", StringComparison.OrdinalIgnoreCase))?.Value ?? "Perfil EQ";
    public string Summary => $"{Format} · {Values.Count} ajustes · {DeviceId}";
}
