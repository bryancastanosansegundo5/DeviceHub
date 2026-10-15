using System.IO;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class ProfileLibrary
{
    private const long MaximumProfileSize = 1024 * 1024;
    private readonly string _folder;

    public ProfileLibrary(string? folder = null) => _folder = folder ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoHub", "Profiles");

    public IReadOnlyList<ProfileFile> List()
    {
        Directory.CreateDirectory(_folder);
        return Directory.EnumerateFiles(_folder)
            .Where(IsSupported)
            .Select(Read)
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public ProfileFile Import(string sourcePath)
    {
        if (!IsSupported(sourcePath)) throw new InvalidDataException("Elige un archivo .agp o .astroeq.");
        var source = new FileInfo(sourcePath);
        if (source.Length > MaximumProfileSize) throw new InvalidDataException("El perfil supera el límite de 1 MB.");
        Directory.CreateDirectory(_folder);
        var destination = System.IO.Path.Combine(_folder,
            $"{System.IO.Path.GetFileNameWithoutExtension(source.Name)}-{Guid.NewGuid():N}{source.Extension.ToLowerInvariant()}");
        File.Copy(source.FullName, destination);
        return Read(destination);
    }

    public string Export(ProfileFile profile, string targetPath)
    {
        var destination = System.IO.Path.GetFullPath(targetPath);
        File.Copy(profile.Path, destination, overwrite: true);
        return destination;
    }

    private static bool IsSupported(string path) =>
        System.IO.Path.GetExtension(path).Equals(".agp", StringComparison.OrdinalIgnoreCase) ||
        System.IO.Path.GetExtension(path).Equals(".astroeq", StringComparison.OrdinalIgnoreCase);

    private static ProfileFile Read(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > MaximumProfileSize) throw new InvalidDataException($"El perfil {file.Name} supera el límite de 1 MB.");
        var values = new List<ProfileValue>();
        var section = "General";
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values.Add(new ProfileValue(section, line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        return new ProfileFile
        {
            Path = file.FullName,
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Format = System.IO.Path.GetExtension(path)[1..].ToUpperInvariant(),
            Values = values
        };
    }
}
