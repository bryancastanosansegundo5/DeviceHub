using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NexoHub.Models;
using NexoHub.Services;
using NexoHub.ViewModels;

namespace NexoHub;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly DispatcherTimer _xboxInputTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _profileSwitchTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<int, XboxMacroInputTracker> _xboxMacroTrackers = [];
    private bool _deviceViewTransitioning;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
        _scanTimer.Tick += ScanTimerTick;
        _xboxInputTimer.Tick += XboxInputTimerTick;
        _profileSwitchTimer.Tick += ProfileSwitchTimerTick;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
        _scanTimer.Start();
        _xboxInputTimer.Start();
        _profileSwitchTimer.Start();
    }

    private async void ScanTimerTick(object? sender, EventArgs e) => await _viewModel.RefreshAsync();
    private void XboxInputTimerTick(object? sender, EventArgs e)
    {
        var activeSlots = new HashSet<int>();
        foreach (var device in _viewModel.Devices.Where(device => device.Device.XInputSlot is not null))
        {
            var slot = device.Device.XInputSlot!.Value;
            activeSlots.Add(slot);
            if (!_xboxMacroTrackers.TryGetValue(slot, out var tracker))
                _xboxMacroTrackers[slot] = tracker = new XboxMacroInputTracker();
            ProcessXboxMacroTransitions(tracker.Update(device.XboxInput.Refresh(slot)));
        }

        foreach (var slot in _xboxMacroTrackers.Keys.Where(slot => !activeSlots.Contains(slot)).ToArray())
        {
            ProcessXboxMacroTransitions(_xboxMacroTrackers[slot].Update(null));
            _xboxMacroTrackers.Remove(slot);
        }
    }

    private void ProcessXboxMacroTransitions(IEnumerable<XboxMacroInputTransition> transitions)
    {
        foreach (var transition in transitions)
            _viewModel.MacroManager.ProcessXboxMacroInput(transition.Trigger, transition.IsDown);
    }
    private void ProfileSwitchTimerTick(object? sender, EventArgs e)
    {
        var window = GetForegroundWindow();
        if (GetWindowThreadProcessId(window, out var processId) == 0)
        {
            _viewModel.MacroManager.SetActiveApplication(null);
            return;
        }
        if (processId == (uint)Environment.ProcessId)
        {
            _viewModel.MacroManager.SetActiveApplication(null);
            return;
        }
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (process.MainModule?.FileName is string path) _viewModel.SelectProfilesForApplication(path);
            else _viewModel.MacroManager.SetActiveApplication(null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            _viewModel.MacroManager.SetActiveApplication(null);
        }
    }
    private async void RefreshDevices(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void OpenDeviceSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceCardViewModel device }) return;
        _viewModel.SelectedDevice = device;
        TransitionDeviceView(DeviceCardsView, DeviceSettingsView, showSettings: true);
    }

    private void ShowDeviceCards(object sender, RoutedEventArgs e) =>
        TransitionDeviceView(DeviceSettingsView, DeviceCardsView, showSettings: false);

    private void TransitionDeviceView(Grid from, Grid to, bool showSettings)
    {
        if (_deviceViewTransitioning || from.Visibility != Visibility.Visible) return;
        _deviceViewTransitioning = true;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) =>
        {
            from.Visibility = Visibility.Collapsed;
            from.Opacity = 1;
            DevicesPageTitle.Text = showSettings ? "Configura tu dispositivo" : "Tus dispositivos";
            DevicesPageSubtitle.Text = showSettings && _viewModel.SelectedDevice is { } selected
                ? $"Ajustes para {selected.DisplayName}."
                : "Una vista para el equipo que usas.";
            BackToDevicesButton.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            to.Opacity = 0;
            to.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) => _deviceViewTransitioning = false;
            to.BeginAnimation(OpacityProperty, fadeIn);
        };
        from.BeginAnimation(OpacityProperty, fadeOut);
    }

    private async void ReadLogitechCapabilities(object sender, RoutedEventArgs e) => await _viewModel.ReadLogitechCapabilitiesAsync();
    private async void ApplyLogitechDpi(object sender, RoutedEventArgs e) => await _viewModel.ApplyLogitechDpiAsync();
    private async void SaveLogitechOnboardDpi(object sender, RoutedEventArgs e) => await _viewModel.SaveLogitechOnboardDpiAsync();
    private async void ReadLogitechMouseIdentity(object sender, RoutedEventArgs e) => await _viewModel.IdentifyLogitechMouseAsync();
    private void ToggleAuxiliary(object sender, RoutedEventArgs e) => _viewModel.ShowAuxiliary = !_viewModel.ShowAuxiliary;
    private void AddLogitechDpiStage(object sender, RoutedEventArgs e) => _viewModel.SelectedDevice?.AddLogitechDpiStage();
    private void RemoveLogitechDpiStage(object sender, RoutedEventArgs e) => _viewModel.SelectedDevice?.RemoveLogitechDpiStage();
    private void AddDeviceLocalProfile(object sender, RoutedEventArgs e) => _viewModel.AddDeviceLocalProfile();
    private void RemoveDeviceLocalProfile(object sender, RoutedEventArgs e) => _viewModel.RemoveDeviceLocalProfile();
    private void LinkDeviceProfileApplication(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Asociar perfil local a una aplicación", Filter = "Aplicación (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _viewModel.LinkDeviceProfileToApplication(dialog.FileName);
    }
    private void UnlinkDeviceProfileApplication(object sender, RoutedEventArgs e) => _viewModel.UnlinkDeviceProfileFromApplication();

    private void ApplyAudioLevel(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AudioEndpointViewModel endpoint }) _viewModel.ApplyAudioLevel(endpoint);
    }

    private void SaveDeviceProfile(object sender, RoutedEventArgs e) => _viewModel.SaveDeviceProfile();

    private void AssignAjazzKeyColor(object sender, RoutedEventArgs e)
    {
        _viewModel.SetAjazzKeyColor(AjazzKeyboardMap.SelectedKeyCode, AjazzKeyboardMap.SelectedKeyName);
        AjazzKeyboardMap.RefreshColors();
    }

    private void ClearAjazzKeyColor(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearAjazzKeyColor(AjazzKeyboardMap.SelectedKeyCode, AjazzKeyboardMap.SelectedKeyName);
        AjazzKeyboardMap.RefreshColors();
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string page, IsChecked: true }) return;
        if (page != "Macros" && _viewModel.MacroManager.IsRecording)
            _viewModel.MacroManager.StopRecording();
        DevicesPage.Visibility = page == "Devices" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        MacrosPage.Visibility = page == "Macros" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar perfil de dispositivo",
            Filter = "Perfiles ASTRO (*.agp;*.astroeq)|*.agp;*.astroeq|Todos los archivos (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var imported = 0;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                _viewModel.ImportProfile(path);
                imported++;
            }
            catch (Exception exception)
            {
                _viewModel.ProfileStatus = exception.Message;
                break;
            }
        }
        _viewModel.RefreshProfiles();
        if (imported > 0)
            _viewModel.ProfileStatus = $"{imported} perfil(es) importado(s) a la biblioteca local. Aún no se han escrito en ningún dispositivo.";
    }

    private void ExportProfile(object sender, RoutedEventArgs e)
    {
        var profile = _viewModel.SelectedProfile;
        if (profile is null) return;
        var extension = profile.Format.Equals("AGP", StringComparison.OrdinalIgnoreCase) ? ".agp" : ".astroeq";
        var dialog = new SaveFileDialog
        {
            Title = "Exportar copia del perfil",
            FileName = profile.Name + extension,
            DefaultExt = extension,
            AddExtension = true,
            Filter = extension == ".agp" ? "Perfil ASTRO (*.agp)|*.agp" : "Ecualizador ASTRO (*.astroeq)|*.astroeq",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _viewModel.ExportProfile(profile, dialog.FileName);
            _viewModel.ProfileStatus = $"Copia exportada: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            _viewModel.ProfileStatus = $"No se pudo exportar el perfil: {exception.Message}";
        }
    }

    private void OpenProfileFolder(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoHub", "Profiles");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void NewMacro(object sender, RoutedEventArgs e) => _viewModel.MacroManager.NewMacro();
    private void StartMacroRecording(object sender, RoutedEventArgs e) => _viewModel.MacroManager.StartRecording();
    private void StopMacroRecording(object sender, RoutedEventArgs e) => _viewModel.MacroManager.StopRecording();
    private void AddMacroKeyAction(object sender, RoutedEventArgs e) => _viewModel.MacroManager.AddKeyAction();
    private void AddMacroSystemAction(object sender, RoutedEventArgs e) => _viewModel.MacroManager.AddSystemCommandAction();
    private void MoveMacroStepUp(object sender, RoutedEventArgs e) => MoveMacroStep(sender, -1);
    private void MoveMacroStepDown(object sender, RoutedEventArgs e) => MoveMacroStep(sender, 1);
    private void DeleteMacroStep(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MacroStep step }) _viewModel.MacroManager.RemoveStep(step);
    }
    private void SaveMacro(object sender, RoutedEventArgs e) => _viewModel.MacroManager.Save();
    private void LinkMacroToApplication(object sender, RoutedEventArgs e)
    {
        if (_viewModel.MacroManager.SelectedMacro is not { } macro) return;
        var dialog = new OpenFileDialog { Title = "Asociar macro a una aplicación", Filter = "Aplicación (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) macro.ApplicationExecutable = dialog.FileName;
    }

    private void MoveMacroStep(object sender, int offset)
    {
        if (sender is Button { Tag: MacroStep step }) _viewModel.MacroManager.MoveStep(step, offset);
    }

    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _scanTimer.Stop();
        _xboxInputTimer.Stop();
        _profileSwitchTimer.Stop();
        _viewModel.MacroManager.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
