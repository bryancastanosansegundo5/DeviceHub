using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NexoHub.ViewModels;

namespace NexoHub;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly DispatcherTimer _xboxInputTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
        _scanTimer.Tick += ScanTimerTick;
        _xboxInputTimer.Tick += XboxInputTimerTick;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
        _scanTimer.Start();
        _xboxInputTimer.Start();
    }

    private async void ScanTimerTick(object? sender, EventArgs e) => await _viewModel.RefreshAsync();
    private void XboxInputTimerTick(object? sender, EventArgs e)
    {
        var selected = _viewModel.SelectedDevice;
        if (selected?.Device.XInputSlot is int slot) selected.XboxInput.Refresh(slot);
    }
    private async void RefreshDevices(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void ToggleAuxiliary(object sender, RoutedEventArgs e) => _viewModel.ShowAuxiliary = !_viewModel.ShowAuxiliary;

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
    private void SaveMacro(object sender, RoutedEventArgs e) => _viewModel.MacroManager.Save();

    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _scanTimer.Stop();
        _xboxInputTimer.Stop();
        _viewModel.MacroManager.Dispose();
    }
}
