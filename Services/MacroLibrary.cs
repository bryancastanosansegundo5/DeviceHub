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
    public MacroTrigger GShiftTrigger { get; private set; }

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
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var data = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => new LibraryDocument
            {
                Macros = JsonSerializer.Deserialize<List<MacroDefinition>>(document.RootElement.GetRawText(), _jsonOptions) ?? []
            },
            JsonValueKind.Object => JsonSerializer.Deserialize<LibraryDocument>(document.RootElement.GetRawText(), _jsonOptions)
                ?? new LibraryDocument(),
            _ => throw new InvalidDataException("La biblioteca de macros tiene un formato no admitido.")
        };
        if (!Enum.IsDefined(data.GShiftTrigger)) throw new InvalidDataException("El botón de G-Shift no es válido.");
        GShiftTrigger = data.GShiftTrigger;
        var macros = data.Macros ?? [];
        if (macros.Count > MaximumMacros || macros.Any(macro => macro is null ||
            macro.Steps.Count + macro.HoldSteps.Count + macro.ReleaseSteps.Count > MaximumStepsPerMacro))
            throw new InvalidDataException("La biblioteca de macros supera los límites admitidos.");
        foreach (var macro in macros) Validate(macro);
        ValidateAssignments(macros, GShiftTrigger);
        return macros;
    }

    public void Save(IEnumerable<MacroDefinition> macros, MacroTrigger gShiftTrigger = MacroTrigger.None)
    {
        ArgumentNullException.ThrowIfNull(macros);
        var snapshot = macros.ToArray();
        ValidateSnapshot(snapshot, gShiftTrigger);
        Directory.CreateDirectory(_folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new LibraryDocument
        {
            GShiftTrigger = gShiftTrigger,
            Macros = snapshot.ToList()
        }, _jsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public void ValidateForPlayback(IEnumerable<MacroDefinition> macros, MacroTrigger gShiftTrigger)
    {
        ArgumentNullException.ThrowIfNull(macros);
        ValidateSnapshot(macros.ToArray(), gShiftTrigger);
    }

    private static void ValidateSnapshot(IReadOnlyList<MacroDefinition> snapshot, MacroTrigger gShiftTrigger)
    {
        if (snapshot.Count > MaximumMacros) throw new InvalidDataException("Se permiten hasta 100 macros.");
        if (!Enum.IsDefined(gShiftTrigger)) throw new InvalidDataException("El botón de G-Shift no es válido.");
        foreach (var macro in snapshot) Validate(macro);
        ValidateAssignments(snapshot, gShiftTrigger);
    }

    private static void Validate(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 80)
            throw new InvalidDataException("Cada macro necesita un nombre de hasta 80 caracteres.");
        if (macro.ApplicationExecutable is null || macro.ApplicationExecutable.Length > 1024 ||
            (macro.ApplicationExecutable.Length > 0 &&
             (!Path.IsPathFullyQualified(macro.ApplicationExecutable) ||
              !Path.GetExtension(macro.ApplicationExecutable).Equals(".exe", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("El perfil de macro debe usar la ruta completa de un archivo .exe.");
        var steps = macro.Steps.Concat(macro.HoldSteps).Concat(macro.ReleaseSteps).ToArray();
        if (!Enum.IsDefined(macro.Binding) || !Enum.IsDefined(macro.ShiftBinding) || !Enum.IsDefined(macro.RepeatMode) ||
            macro.RepeatDelayMs is < 1 or > 10000 || steps.Length > MaximumStepsPerMacro)
            throw new InvalidDataException($"La macro {macro.Name} contiene datos no admitidos.");
        if (steps.Any(step => step is null || !Enum.IsDefined(step.InputType) || step.DelayMs is < 0 or > 10000 ||
            (step.InputType == MacroInputType.Key && step.VirtualKey is < 1 or > 255) ||
            (step.InputType == MacroInputType.MouseButton && !Enum.IsDefined(step.MouseButton)) ||
            (step.InputType == MacroInputType.MouseWheel && (step.WheelDelta is 0 or < -1200 or > 1200)) ||
            (step.InputType == MacroInputType.SystemCommand && !Enum.IsDefined(step.SystemCommand))))
            throw new InvalidDataException($"La macro {macro.Name} contiene una tecla, botón o pausa fuera de rango.");
        if (steps.Sum(step => step.DelayMs) > 600000)
            throw new InvalidDataException($"La macro {macro.Name} dura más de 10 minutos.");

        if (macro.RepeatMode != MacroRepeatMode.Sequence && (macro.HoldSteps.Count != 0 || macro.ReleaseSteps.Count != 0))
            throw new InvalidDataException($"La macro {macro.Name} solo puede usar fases con el tipo Secuencia.");

        var held = ApplyHeldSteps(macro.Steps, [], macro.Name);
        if (macro.RepeatMode == MacroRepeatMode.Sequence)
        {
            var afterHold = ApplyHeldSteps(macro.HoldSteps, new(held, StringComparer.Ordinal), macro.Name);
            if (!afterHold.SetEquals(held))
                throw new InvalidDataException($"La fase de mantener de {macro.Name} debe liberar las pulsaciones que inicia.");
            held = ApplyHeldSteps(macro.ReleaseSteps, new(held, StringComparer.Ordinal), macro.Name);
        }
        if (held.Count > 0) throw new InvalidDataException($"La secuencia de {macro.Name} deja una tecla o botón pulsado.");
    }

    private static HashSet<string> ApplyHeldSteps(IEnumerable<MacroStep> steps, HashSet<string> held, string name)
    {
        foreach (var step in steps)
        {
            if (step.InputType is MacroInputType.MouseWheel or MacroInputType.SystemCommand) continue;
            var key = step.InputType == MacroInputType.Key ? $"K:{step.VirtualKey}" : $"M:{step.MouseButton}";
            var changed = step.IsDown ? held.Add(key) : held.Remove(key);
            if (!changed) throw new InvalidDataException($"La secuencia de {name} tiene pulsaciones duplicadas o sin pareja.");
        }
        return held;
    }

    private static void ValidateAssignments(IEnumerable<MacroDefinition> macros, MacroTrigger gShiftTrigger)
    {
        var snapshot = macros.ToArray();
        if (snapshot.GroupBy(macro => macro.ApplicationExecutable, StringComparer.OrdinalIgnoreCase).Any(scope =>
            scope.Where(macro => macro.Binding != MacroTrigger.None)
                    .GroupBy(macro => macro.Binding).Any(group => group.Count() > 1) ||
            scope.Where(macro => macro.ShiftBinding != MacroTrigger.None)
                    .GroupBy(macro => macro.ShiftBinding).Any(group => group.Count() > 1)))
            throw new InvalidDataException("Cada botón puede tener una sola macro asignada.");
        if (gShiftTrigger != MacroTrigger.None && snapshot.Any(macro =>
            macro.Binding == gShiftTrigger || macro.ShiftBinding == gShiftTrigger))
            throw new InvalidDataException("El botón que mantiene G-Shift no puede activar una macro.");
    }

    private sealed class LibraryDocument
    {
        public LibraryDocument() { }
        public MacroTrigger GShiftTrigger { get; set; }
        public List<MacroDefinition> Macros { get; set; } = [];
    }
}
