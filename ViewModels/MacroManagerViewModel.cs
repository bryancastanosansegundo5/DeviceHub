using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NexoHub.Models;
using NexoHub.Services;

namespace NexoHub.ViewModels;

public sealed class MacroManagerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MacroLibrary _library = new();
    private readonly MacroInputRecorder _recorder = new();
    private readonly MacroPlaybackService _playback = new();
    private readonly MouseMacroTrigger _mouseTrigger;
    private MacroDefinition? _selectedMacro;
    private string _recorderStatus = "Grabación apagada";
    private string _assignmentStatus = "Ratón y botones/direcciones XInput · requiere Nexo abierto.";
    private string _activeApplicationExecutable = "";
    private MacroTrigger _gShiftTrigger;
    private Key _selectedKey = Key.A;
    private MacroSystemCommandOption _selectedSystemCommand = MacroSystemCommandOption.All[0];
    private MacroSequenceSection _selectedSequenceSection;
    private ObservableCollection<MacroStep> _selectedMacroSteps = [];
    private bool _isRecording;
    private bool _isEnabled;

    public MacroManagerViewModel()
    {
        _mouseTrigger = new MouseMacroTrigger(FindMacro, () => GShiftTrigger, _playback);
        Bindings = new ObservableCollection<MacroBindingOption>(MacroBindingOption.All);
        RepeatModes = new ObservableCollection<MacroRepeatOption>(MacroRepeatOption.All);
        SequenceSections = new ObservableCollection<MacroSequenceSectionOption>(MacroSequenceSectionOption.All);
        KeyOptions = new ObservableCollection<Key>(Enum.GetValues<Key>()
            .Distinct().Where(key => KeyInterop.VirtualKeyFromKey(key) is > 0 and <= 255 && key != Key.System));
        SystemCommands = new ObservableCollection<MacroSystemCommandOption>(MacroSystemCommandOption.All);
        try
        {
            foreach (var macro in _library.Load()) AddMacro(macro);
            GShiftTrigger = _library.GShiftTrigger;
            SelectedMacro = Macros.FirstOrDefault();
        }
        catch (Exception exception)
        {
            AssignmentStatus = $"No se pudo leer la biblioteca: {exception.Message}";
        }
    }

    public ObservableCollection<MacroDefinition> Macros { get; } = [];
    public ObservableCollection<MacroBindingOption> Bindings { get; }
    public ObservableCollection<MacroRepeatOption> RepeatModes { get; }
    public ObservableCollection<MacroSequenceSectionOption> SequenceSections { get; }
    public ObservableCollection<Key> KeyOptions { get; }
    public ObservableCollection<MacroSystemCommandOption> SystemCommands { get; }
    public ObservableCollection<MacroStep> SelectedMacroSteps { get => _selectedMacroSteps; private set { _selectedMacroSteps = value; OnPropertyChanged(); } }
    public MacroSequenceSectionOption SelectedSequenceSectionOption
    {
        get => MacroSequenceSectionOption.All.Single(option => option.Section == _selectedSequenceSection);
        set
        {
            if (value is null || _selectedSequenceSection == value.Section) return;
            _selectedSequenceSection = value.Section;
            OnPropertyChanged();
            UpdateSelectedMacroSteps();
        }
    }
    public Key SelectedKey { get => _selectedKey; set { _selectedKey = value; OnPropertyChanged(); } }
    public MacroSystemCommandOption SelectedSystemCommandOption
    {
        get => _selectedSystemCommand;
        set { if (value is not null) { _selectedSystemCommand = value; OnPropertyChanged(); } }
    }
    public MacroBindingOption GShiftTriggerOption
    {
        get => Bindings.First(option => option.Trigger == GShiftTrigger);
        set { if (value is not null) GShiftTrigger = value.Trigger; }
    }
    public MacroTrigger GShiftTrigger
    {
        get => _gShiftTrigger;
        set
        {
            if (_gShiftTrigger == value || !Enum.IsDefined(value)) return;
            _gShiftTrigger = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GShiftTriggerOption));
        }
    }
    public MacroDefinition? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (ReferenceEquals(_selectedMacro, value)) return;
            _selectedMacro = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedMacro));
            UpdateSelectedMacroSteps();
        }
    }
    public bool HasSelectedMacro => SelectedMacro is not null;
    public string RecorderStatus { get => _recorderStatus; private set { _recorderStatus = value; OnPropertyChanged(); } }
    public string AssignmentStatus { get => _assignmentStatus; private set { _assignmentStatus = value; OnPropertyChanged(); } }
    public bool IsRecording { get => _isRecording; private set { _isRecording = value; OnPropertyChanged(); } }
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (value && !Macros.Any(macro =>
                (macro.Binding != MacroTrigger.None && macro.Binding != GShiftTrigger ||
                 GShiftTrigger != MacroTrigger.None && macro.ShiftBinding != MacroTrigger.None && macro.ShiftBinding != GShiftTrigger) &&
                macro.HasActions))
            {
                AssignmentStatus = "Asigna una macro a un botón de una capa y guárdala antes de activar.";
                OnPropertyChanged(nameof(IsEnabled));
                return;
            }
            try
            {
                if (value)
                {
                    _library.ValidateForPlayback(Macros, GShiftTrigger);
                    _mouseTrigger.Enable();
                }
                else _mouseTrigger.Disable();
                _isEnabled = value;
                AssignmentStatus = value
                    ? "Activo: el ratón puede consumirse; el mando conserva su entrada física. Desactiva para editar."
                    : "Ratón y botones/direcciones XInput · requiere Nexo abierto.";
                OnPropertyChanged();
            }
            catch (Exception exception)
            {
                _mouseTrigger.Disable();
                AssignmentStatus = exception.Message;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetActiveApplication(string? executablePath)
    {
        _activeApplicationExecutable = !string.IsNullOrWhiteSpace(executablePath) &&
            Path.IsPathFullyQualified(executablePath) &&
            Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? executablePath : "";
    }

    public void ProcessXboxMacroInput(MacroTrigger trigger, bool isDown)
    {
        if (IsEnabled) _mouseTrigger.ProcessExternalInput(trigger, isDown);
    }

    public void NewMacro()
    {
        if (Macros.Count >= 100)
        {
            AssignmentStatus = "La biblioteca admite hasta 100 macros.";
            return;
        }
        var macro = new MacroDefinition { Name = $"Nueva macro {Macros.Count + 1}" };
        AddMacro(macro);
        SelectedMacro = macro;
    }

    public void StartRecording()
    {
        if (SelectedMacro is null) { AssignmentStatus = "Crea o selecciona una macro primero."; return; }
        if (IsEnabled) IsEnabled = false;
        try
        {
            _recorder.Start();
            IsRecording = true;
            RecorderStatus = "Grabando teclas en todo Windows…";
        }
        catch (Exception exception)
        {
            RecorderStatus = exception.Message;
        }
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        var steps = _recorder.Stop();
        var selected = SelectedMacro!;
        var recorded = new ObservableCollection<MacroStep>(steps);
        switch (_selectedSequenceSection)
        {
            case MacroSequenceSection.Hold: selected.HoldSteps = recorded; break;
            case MacroSequenceSection.Release: selected.ReleaseSteps = recorded; break;
            default: selected.Steps = recorded; break;
        }
        UpdateSelectedMacroSteps();
        IsRecording = false;
        RecorderStatus = $"Grabación terminada · {steps.Count} acciones de teclado y ratón";
    }

    public void Save()
    {
        if (IsRecording) StopRecording();
        try
        {
            _library.Save(Macros, GShiftTrigger);
            AssignmentStatus = "Biblioteca guardada en este PC.";
        }
        catch (Exception exception)
        {
            AssignmentStatus = $"No se pudo guardar: {exception.Message}";
        }
    }

    public void AddKeyAction()
    {
        if (SelectedMacro is null) return;
        foreach (var step in CreateKeyAction(SelectedKey)) SelectedMacroSteps.Add(step);
        RefreshMacroIndexes(SelectedMacroSteps);
        RecorderStatus = $"Tecla {SelectedKey} añadida; guarda la macro para conservarla.";
    }

    public void AddSystemCommandAction()
    {
        if (SelectedMacro is null) return;
        SelectedMacroSteps.Add(new MacroStep
        {
            InputType = MacroInputType.SystemCommand,
            SystemCommand = SelectedSystemCommandOption.Command
        });
        RefreshMacroIndexes(SelectedMacroSteps);
        RecorderStatus = $"{SelectedSystemCommandOption.Label} añadido; guarda la macro para conservarlo.";
    }

    public void MoveStep(MacroStep step, int offset)
    {
        if (SelectedMacro is null) return;
        var index = SelectedMacroSteps.IndexOf(step);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SelectedMacroSteps.Count) return;
        SelectedMacroSteps.Move(index, target);
        RefreshMacroIndexes(SelectedMacroSteps);
    }

    public void RemoveStep(MacroStep step)
    {
        if (SelectedMacro is null || !SelectedMacroSteps.Remove(step)) return;
        RefreshMacroIndexes(SelectedMacroSteps);
    }

    public static IReadOnlyList<MacroStep> CreateKeyAction(Key key)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(key));
        return
        [
            new() { InputType = MacroInputType.Key, VirtualKey = virtualKey, IsDown = true },
            new() { InputType = MacroInputType.Key, VirtualKey = virtualKey, IsDown = false }
        ];
    }

    private static void RefreshMacroIndexes(IEnumerable<MacroStep> steps)
    {
        foreach (var item in steps.Select((step, index) => (step, index)))
            item.step.Index = $"{item.index + 1:000}";
    }

    private void UpdateSelectedMacroSteps()
    {
        if (SelectedMacro is null)
        {
            SelectedMacroSteps = [];
            return;
        }
        if (SelectedMacro.RepeatMode != MacroRepeatMode.Sequence && _selectedSequenceSection != MacroSequenceSection.Press)
        {
            _selectedSequenceSection = MacroSequenceSection.Press;
            OnPropertyChanged(nameof(SelectedSequenceSectionOption));
        }
        SelectedMacroSteps = _selectedSequenceSection switch
        {
            MacroSequenceSection.Hold when SelectedMacro.RepeatMode == MacroRepeatMode.Sequence => SelectedMacro.HoldSteps,
            MacroSequenceSection.Release when SelectedMacro.RepeatMode == MacroRepeatMode.Sequence => SelectedMacro.ReleaseSteps,
            _ => SelectedMacro.Steps
        };
        RefreshMacroIndexes(SelectedMacroSteps);
    }

    private MacroDefinition? FindMacro(MacroTrigger trigger, bool shifted) =>
        ResolveMacro(Macros, trigger, shifted, _activeApplicationExecutable);

    public static MacroDefinition? ResolveMacro(IEnumerable<MacroDefinition> macros, MacroTrigger trigger, bool shifted,
        string? applicationExecutable = null)
    {
        var snapshot = macros.ToArray();
        MacroDefinition[] scoped = string.IsNullOrWhiteSpace(applicationExecutable)
            ? []
            : snapshot.Where(macro => string.Equals(macro.ApplicationExecutable, applicationExecutable,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        var global = snapshot.Where(macro => string.IsNullOrWhiteSpace(macro.ApplicationExecutable)).ToArray();
        return (shifted ? FindExact(scoped, trigger, shifted: true) ?? FindExact(global, trigger, shifted: true) : null) ??
            FindExact(scoped, trigger, shifted: false) ?? FindExact(global, trigger, shifted: false);
    }

    private static MacroDefinition? FindExact(IEnumerable<MacroDefinition> macros, MacroTrigger trigger, bool shifted) =>
        macros.FirstOrDefault(macro => (shifted ? macro.ShiftBinding : macro.Binding) == trigger && macro.HasActions);

    private void AddMacro(MacroDefinition macro)
    {
        Macros.Add(macro);
        macro.PropertyChanged += MacroChanged;
    }

    private void MacroChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MacroDefinition.Binding) or nameof(MacroDefinition.ShiftBinding) or nameof(MacroDefinition.Steps))
            OnPropertyChanged(nameof(Macros));
        if (ReferenceEquals(sender, SelectedMacro) &&
            args.PropertyName is (nameof(MacroDefinition.RepeatMode) or nameof(MacroDefinition.Steps) or
                nameof(MacroDefinition.HoldSteps) or nameof(MacroDefinition.ReleaseSteps)))
            UpdateSelectedMacroSteps();
    }

    public void Dispose()
    {
        StopRecording();
        _mouseTrigger.Dispose();
        _playback.CancelCurrent();
        _recorder.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
