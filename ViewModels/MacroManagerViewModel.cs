using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private string _assignmentStatus = "Botones 4 y 5 de cualquier ratón · requiere Nexo abierto.";
    private bool _isRecording;
    private bool _isEnabled;

    public MacroManagerViewModel()
    {
        _mouseTrigger = new MouseMacroTrigger(FindMacro, _playback);
        Bindings = new ObservableCollection<MacroBindingOption>(MacroBindingOption.All);
        try
        {
            foreach (var macro in _library.Load()) AddMacro(macro);
            SelectedMacro = Macros.FirstOrDefault();
        }
        catch (Exception exception)
        {
            AssignmentStatus = $"No se pudo leer la biblioteca: {exception.Message}";
        }
    }

    public ObservableCollection<MacroDefinition> Macros { get; } = [];
    public ObservableCollection<MacroBindingOption> Bindings { get; }
    public MacroDefinition? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (ReferenceEquals(_selectedMacro, value)) return;
            _selectedMacro = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedMacro));
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
            if (value && !Macros.Any(macro => macro.Binding != MacroTrigger.None && macro.Steps.Count > 0))
            {
                AssignmentStatus = "Asigna una macro al botón 4 o 5 y guárdala antes de activar.";
                OnPropertyChanged(nameof(IsEnabled));
                return;
            }
            try
            {
                if (value) _mouseTrigger.Enable();
                else _mouseTrigger.Disable();
                _isEnabled = value;
                AssignmentStatus = value
                    ? "Activo: el botón asignado se consume y la macro se ejecuta en la ventana activa."
                    : "Botones 4 y 5 de cualquier ratón · requiere Nexo abierto.";
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
        foreach (var step in steps.Select((step, index) => (step, index)))
            step.step.Index = $"{step.index + 1:000}";
        SelectedMacro!.Steps = new ObservableCollection<MacroStep>(steps);
        IsRecording = false;
        RecorderStatus = $"Grabación terminada · {steps.Count} acciones de teclado y ratón";
    }

    public void Save()
    {
        if (IsRecording) StopRecording();
        try
        {
            _library.Save(Macros);
            AssignmentStatus = "Biblioteca guardada en este PC.";
        }
        catch (Exception exception)
        {
            AssignmentStatus = $"No se pudo guardar: {exception.Message}";
        }
    }

    private MacroDefinition? FindMacro(MacroTrigger trigger) =>
        Macros.FirstOrDefault(macro => macro.Binding == trigger && macro.Steps.Count > 0);

    private void AddMacro(MacroDefinition macro)
    {
        Macros.Add(macro);
        macro.PropertyChanged += MacroChanged;
    }

    private void MacroChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MacroDefinition.Binding) or nameof(MacroDefinition.Steps))
            OnPropertyChanged(nameof(Macros));
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
