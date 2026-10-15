using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class MacroLibrary
{
    private const int MaximumMacros = 100;
    private const int MaximumStepsPerMacro = 5000;
    private const long MaximumLibrarySize = 8 * 1024 * 1024;
    private readonly string _folder;
    private string FilePath => Path.Combine(_folder, "macros.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public MacroLibrary(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoHub");
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public IReadOnlyList<MacroDefinition> Load()
    {
        Directory.CreateDirectory(_folder);
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > MaximumLibrarySize)
            throw new InvalidDataException("La biblioteca de macros supera el límite de 8 MB.");
        var macros = JsonSerializer.Deserialize<List<MacroDefinition>>(File.ReadAllText(FilePath), _jsonOptions) ?? [];
        if (macros.Count > MaximumMacros || macros.Any(macro => macro.Steps.Count > MaximumStepsPerMacro))
            throw new InvalidDataException("La biblioteca de macros supera los límites admitidos.");
        foreach (var macro in macros) Validate(macro);
        ValidateAssignments(macros);
        return macros;
    }

    public void Save(IEnumerable<MacroDefinition> macros)
    {
        var snapshot = macros.ToArray();
        if (snapshot.Length > MaximumMacros) throw new InvalidDataException("Se permiten hasta 100 macros.");
        foreach (var macro in snapshot) Validate(macro);
        ValidateAssignments(snapshot);
        Directory.CreateDirectory(_folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, _jsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private static void Validate(MacroDefinition macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 80)
            throw new InvalidDataException("Cada macro necesita un nombre de hasta 80 caracteres.");
        if (!Enum.IsDefined(macro.Binding) || macro.Steps.Count > MaximumStepsPerMacro)
            throw new InvalidDataException($"La macro {macro.Name} contiene datos no admitidos.");
        if (macro.Steps.Any(step => step is null || !Enum.IsDefined(step.InputType) || step.DelayMs is < 0 or > 10000 ||
            (step.InputType == MacroInputType.Key && step.VirtualKey is < 1 or > 255) ||
            (step.InputType == MacroInputType.MouseButton && !Enum.IsDefined(step.MouseButton)) ||
            (step.InputType == MacroInputType.MouseWheel && (step.WheelDelta is 0 or < -1200 or > 1200))))
            throw new InvalidDataException($"La macro {macro.Name} contiene una tecla, botón o pausa fuera de rango.");
        if (macro.Steps.Sum(step => step.DelayMs) > 600000)
            throw new InvalidDataException($"La macro {macro.Name} dura más de 10 minutos.");

        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in macro.Steps)
        {
            if (step.InputType == MacroInputType.MouseWheel) continue;
            var key = step.InputType == MacroInputType.Key ? $"K:{step.VirtualKey}" : $"M:{step.MouseButton}";
            var changed = step.IsDown ? held.Add(key) : held.Remove(key);
            if (!changed) throw new InvalidDataException($"La secuencia de {macro.Name} tiene pulsaciones duplicadas o sin pareja.");
        }
        if (held.Count > 0) throw new InvalidDataException($"La secuencia de {macro.Name} deja una tecla o botón pulsado.");
    }

    private static void ValidateAssignments(IEnumerable<MacroDefinition> macros)
    {
        if (macros.Where(macro => macro.Binding != MacroTrigger.None)
            .GroupBy(macro => macro.Binding).Any(group => group.Count() > 1))
            throw new InvalidDataException("Cada botón lateral puede tener una sola macro asignada.");
    }
}
