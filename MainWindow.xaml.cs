using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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
    private readonly DispatcherTimer _xboxSlotReadTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _xboxProfileSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Dictionary<int, XboxMacroInputTracker> _xboxMacroTrackers = [];
    private bool _deviceViewTransitioning;
    private bool _syncingXboxHotspots;
    private bool _xboxSlotReadInProgress;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        XboxProfileEditor.AddHandler(RangeBase.ValueChangedEvent,
            new RoutedPropertyChangedEventHandler<double>(XboxEditorValueChanged), true);
        XboxProfileEditor.AddHandler(Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(XboxEditorSelectionChanged), true);
        XboxProfileEditor.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(XboxEditorCheckChanged), true);
        XboxProfileEditor.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(XboxEditorCheckChanged), true);
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        Loaded += WindowLoaded;
        Closing += WindowClosing;
        _scanTimer.Tick += ScanTimerTick;
        _xboxInputTimer.Tick += XboxInputTimerTick;
        _profileSwitchTimer.Tick += ProfileSwitchTimerTick;
        _xboxSlotReadTimer.Tick += XboxSlotReadTimerTick;
        _xboxProfileSaveTimer.Tick += XboxProfileSaveTimerTick;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        SetDarkTitleBar();
        await _viewModel.RefreshAsync();
        _scanTimer.Start();
        _xboxInputTimer.Start();
        _profileSwitchTimer.Start();
        _xboxSlotReadTimer.Start();
    }

    private async void ScanTimerTick(object? sender, EventArgs e)
    {
        if (DevicesPage.Visibility != Visibility.Visible || DeviceCardsView.Visibility != Visibility.Visible) return;
        await _viewModel.RefreshAsync();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDevice) && _viewModel.SelectedDevice is { } selected &&
            DeviceSettingsView.Visibility == Visibility.Visible)
            DevicesPageSubtitle.Text = $"Ajustes para {selected.DisplayName}.";
    }

    private void XboxInputTimerTick(object? sender, EventArgs e)
    {
        var activeSlots = new HashSet<int>();
        foreach (var device in _viewModel.Devices.Where(device => device.Device.XInputSlot is not null))
        {
            var slot = device.Device.XInputSlot!.Value;
            activeSlots.Add(slot);
            if (!_xboxMacroTrackers.TryGetValue(slot, out var tracker))
                _xboxMacroTrackers[slot] = tracker = new XboxMacroInputTracker();
            ProcessXboxMacroTransitions(tracker.Update(device.XboxInput.Refresh(slot), device.LocalProfile));
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

    private async void XboxSlotReadTimerTick(object? sender, EventArgs e)
    {
        if (_xboxSlotReadInProgress || DeviceSettingsView.Visibility != Visibility.Visible ||
            _viewModel.SelectedDevice is not { IsXboxElite: true } device)
            return;

        _xboxSlotReadInProgress = true;
        try
        {
            var mapping = await Task.Run(() => _viewModel.ReadXboxLeftBumperMapping(device.Device));
            if (!ReferenceEquals(_viewModel.SelectedDevice, device)) return;
            var previousSlot = device.ActiveXboxProfileSlot;
            device.SetDetectedXboxProfileSlot(device.ResolveXboxProfileSlotFromLeftBumper(mapping));
            if (previousSlot != device.ActiveXboxProfileSlot)
                SyncXboxHotspots(device.SelectedXboxPhysicalButton);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var previousSlot = device.ActiveXboxProfileSlot;
            device.SetDetectedXboxProfileSlot(null);
            if (previousSlot is not null) SyncXboxHotspots(device.SelectedXboxPhysicalButton);
        }
        finally
        {
            _xboxSlotReadInProgress = false;
        }
    }
    private async void RefreshDevices(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void OpenDeviceSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceCardViewModel device }) return;
        _viewModel.SelectedDevice = device;
        if (device.IsXboxElite)
        {
            device.ShowXboxHome();
            SyncXboxHotspots(device.SelectedXboxPhysicalButton);
        }
        TransitionDeviceView(DeviceCardsView, DeviceSettingsView, showSettings: true);
        if (device.IsLogitechHidppCandidate && !device.IsLogitechDpiWritable &&
            !_viewModel.IsScanning && !_viewModel.IsProbingLogitech)
            _ = _viewModel.IdentifyLogitechMouseAsync(device);
    }

    private void ShowDeviceCards(object sender, RoutedEventArgs e)
    {
        DeviceCardsScrollViewer.ScrollToTop();
        TransitionDeviceView(DeviceSettingsView, DeviceCardsView, showSettings: false);
    }

    private void TransitionDeviceView(Grid from, Grid to, bool showSettings)
    {
        if (_deviceViewTransitioning || from.Visibility != Visibility.Visible) return;
        _deviceViewTransitioning = true;
        var exitDuration = TimeSpan.FromMilliseconds(150);
        var enterDuration = TimeSpan.FromMilliseconds(240);
        var outgoingOffset = new TranslateTransform();
        from.RenderTransform = outgoingOffset;
        var fadeOut = new DoubleAnimation(0, exitDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        outgoingOffset.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(showSettings ? -18 : 18, exitDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        fadeOut.Completed += (_, _) =>
        {
            from.Visibility = Visibility.Collapsed;
            from.Opacity = 1;
            from.RenderTransform = Transform.Identity;
            DevicesPageTitle.Text = showSettings ? "Configura tu dispositivo" : "Tus dispositivos";
            DevicesPageSubtitle.Text = showSettings && _viewModel.SelectedDevice is { } selected
                ? $"Ajustes para {selected.DisplayName}."
                : "Una vista para el equipo que usas.";
            BackToDevicesButton.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
            var immersiveXbox = showSettings && _viewModel.SelectedDevice is { IsXboxElite: true };
            SetXboxImmersiveLayout(immersiveXbox);
            DevicesPageHeader.Visibility = immersiveXbox ? Visibility.Collapsed : Visibility.Visible;
            DevicesPageHeaderGap.Height = new GridLength(immersiveXbox ? 0 : 18);
            to.Opacity = 0;
            var incomingOffset = new TranslateTransform(showSettings ? 18 : -18, 0);
            to.RenderTransform = incomingOffset;
            to.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, enterDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) =>
            {
                to.RenderTransform = Transform.Identity;
                _deviceViewTransitioning = false;
            };
            incomingOffset.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, enterDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            to.BeginAnimation(OpacityProperty, fadeIn);
        };
        from.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void WindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;

        if (DevicesPage.Visibility != Visibility.Visible)
        {
            DevicesNav.IsChecked = true;
            e.Handled = true;
            return;
        }

        if (DeviceSettingsView.Visibility != Visibility.Visible) return;
        if (_viewModel.SelectedDevice is { IsXboxElite: true, IsEditingXboxProfile: true })
            ReturnXboxToProfiles(this, new RoutedEventArgs());
        else if (_viewModel.SelectedDevice is { IsXboxElite: true, IsShowingXboxProfiles: true })
            ReturnXboxToHome(this, new RoutedEventArgs());
        else
            ShowDeviceCards(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private async void ReadLogitechCapabilities(object sender, RoutedEventArgs e) => await _viewModel.ReadLogitechCapabilitiesAsync();
    private async void ApplyLogitechDpi(object sender, RoutedEventArgs e) => await _viewModel.ApplyLogitechDpiAsync();
    private async void SaveLogitechOnboardDpi(object sender, RoutedEventArgs e) => await _viewModel.SaveLogitechOnboardDpiAsync();
    private async void ReadLogitechMouseIdentity(object sender, RoutedEventArgs e) => await _viewModel.IdentifyLogitechMouseAsync();
    private void ToggleAuxiliary(object sender, RoutedEventArgs e) => _viewModel.ShowAuxiliary = !_viewModel.ShowAuxiliary;
    private void AddLogitechDpiStage(object sender, RoutedEventArgs e) => _viewModel.SelectedDevice?.AddLogitechDpiStage();
    private void RemoveLogitechDpiStage(object sender, RoutedEventArgs e) => _viewModel.SelectedDevice?.RemoveLogitechDpiStage();
    private void AddDeviceLocalProfile(object sender, RoutedEventArgs e)
    {
        var saveXbox = _viewModel.SelectedDevice is { IsXboxElite: true, CanAddLocalProfile: true };
        _viewModel.AddDeviceLocalProfile();
        if (saveXbox) SaveXboxProfileImmediately();
    }
    private void RemoveDeviceLocalProfile(object sender, RoutedEventArgs e)
    {
        var saveXbox = _viewModel.SelectedDevice is { IsXboxElite: true, CanDeleteLocalProfile: true };
        _viewModel.RemoveDeviceLocalProfile();
        if (saveXbox) SaveXboxProfileImmediately();
    }
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

    private void XboxPhysicalButtonSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingXboxHotspots || sender is not ComboBox { SelectedItem: string button } || _viewModel.SelectedDevice is not { IsXboxElite: true } device)
            return;
        device.SelectedXboxPhysicalButton = button;
        SyncXboxHotspots(button);
    }

    private void XboxAssignmentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingXboxHotspots || sender is not ComboBox { SelectedItem: string assignment } ||
            _viewModel.SelectedDevice is not { IsXboxElite: true } device)
            return;

        var expectedCurrentAssignment = device.LocalProfile.XboxButtonAssignments.GetValueOrDefault(
            device.SelectedXboxPhysicalButton, device.SelectedXboxPhysicalButton);
        // Profile/source-button changes also update SelectedItem through binding.
        // Only a choice made from the open assignment list should write to hardware.
        var userSelectedAssignment = XboxAssignmentSelector.IsDropDownOpen;
        device.SelectedXboxAssignment = assignment;
        SyncXboxHotspots(device.SelectedXboxPhysicalButton);
        if (!userSelectedAssignment) return;

        _xboxProfileSaveTimer.Stop();
        _viewModel.SaveDeviceProfile(applyXboxLeftBumperAssignment:
                string.Equals(device.SelectedXboxPhysicalButton, "LB", StringComparison.Ordinal),
            expectedCurrentXboxLeftBumperAssignment: expectedCurrentAssignment);
    }

    private void SelectXboxButtonHotspot(object sender, RoutedEventArgs e)
    {
        if (_syncingXboxHotspots || sender is not RadioButton { Tag: string button } || _viewModel.SelectedDevice is not { IsXboxElite: true } device)
            return;
        device.SelectedXboxPhysicalButton = button;
        XboxPhysicalButtonSelector.SelectedItem = button;
        SyncXboxHotspots(button);
    }

    private void SyncXboxHotspots(string? selectedButton)
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true } device) return;
        _syncingXboxHotspots = true;
        try
        {
            foreach (var radio in XboxFrontHotspots.Children.OfType<RadioButton>().Concat(XboxRearHotspots.Children.OfType<RadioButton>()))
            {
                if (radio.Tag is string button)
                {
                    var assignment = device.LocalProfile.XboxButtonAssignments.GetValueOrDefault(button,
                        button.StartsWith("Paleta", StringComparison.Ordinal) ? "Sin asignar" : button);
                    radio.Content = DeviceCardViewModel.GetXboxAssignmentShortLabel(assignment);
                    radio.ToolTip = $"{button} → {assignment}";
                }
                radio.IsChecked = string.Equals(radio.Tag as string, selectedButton, StringComparison.Ordinal);
            }
            RebuildXboxProfilePreview(device);
            XboxPhysicalButtonSelector.SelectedItem = selectedButton;
        }
        finally { _syncingXboxHotspots = false; }
    }

    private void RebuildXboxProfilePreview(DeviceCardViewModel device)
    {
        RebuildXboxProfilePreview(XboxProfilePreview, device, device.LocalProfile);
        RebuildXboxProfilePreview(XboxHomePreview, device, device.XboxHomePreviewProfile);
    }

    private void RebuildXboxProfilePreview(Canvas canvas, DeviceCardViewModel device, DeviceLocalProfile? profile)
    {
        canvas.Children.Clear();
        if (profile is null) return;
        var configuredAssignments = device.GetConfiguredXboxButtonAssignments(profile);
        foreach (var (button, assignment) in configuredAssignments)
        {
            var geometry = GetXboxProfileCallout(button);
            if (geometry is not { } points) continue;
            AddXboxMappingCallout(canvas, points.Start, points.Bend, points.End,
                DeviceCardViewModel.GetXboxAssignmentShortLabel(assignment));
        }
        if (ReferenceEquals(canvas, XboxHomePreview))
        {
            foreach (var stick in new[] { "Palanca izquierda", "Palanca derecha" })
            {
                if (configuredAssignments.Any(item => item.Key == stick)) continue;
                if (GetXboxProfileCallout(stick) is { } points)
                    AddXboxMappingCallout(canvas, points.Start, points.Bend, points.End, "−");
            }
            AddXboxTriggerReadout(canvas, "LT", profile.XboxLeftTriggerDeadzoneStart,
                profile.XboxLeftTriggerDeadzoneEnd, left: true);
            AddXboxTriggerReadout(canvas, "RT", profile.XboxRightTriggerDeadzoneStart,
                profile.XboxRightTriggerDeadzoneEnd, left: false);
        }
    }

    private void AddXboxTriggerReadout(Canvas canvas, string trigger, int start, int end, bool left)
    {
        var route = GetXboxProfileCallout(trigger);
        if (route is not { } points) return;
        canvas.Children.Add(new Polyline
        {
            Points = new PointCollection([points.Start, points.Bend, points.End]),
            Stroke = (Brush)FindResource("XboxBlue"),
            StrokeThickness = 1.7
        });
        var text = new TextBlock
        {
            Text = $"{start} – {end}",
            Foreground = (Brush)FindResource("XboxBlue"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Width = 66,
            TextAlignment = left ? TextAlignment.Left : TextAlignment.Right
        };
        Canvas.SetLeft(text, left ? 10 : 624);
        Canvas.SetTop(text, 16);
        canvas.Children.Add(text);
        var badge = new Border
        {
            Width = 30,
            Height = 22,
            BorderBrush = (Brush)FindResource("XboxBlue"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(225, 31, 31, 31)),
            Child = new TextBlock
            {
                Text = trigger,
                Foreground = (Brush)FindResource("XboxBlue"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(badge, left ? 80 : 590);
        Canvas.SetTop(badge, 16);
        canvas.Children.Add(badge);
    }

    private static (Point Start, Point Bend, Point End)? GetXboxProfileCallout(string button) => button switch
    {
        "LB" => (new(127, 34), new(83, 17), new(43, 17)),
        "RB" => (new(516, 34), new(570, 17), new(650, 17)),
        "LT" => (new(155, 20), new(92, 35), new(35, 52)),
        "RT" => (new(488, 20), new(560, 35), new(665, 52)),
        "Botón A" => (new(536, 188), new(604, 188), new(658, 188)),
        "Botón B" => (new(581, 144), new(625, 116), new(658, 99)),
        "Botón X" => (new(485, 140), new(574, 128), new(657, 128)),
        "Botón Y" => (new(537, 87), new(600, 68), new(658, 55)),
        "Botón Ver" => (new(299, 140), new(220, 112), new(43, 112)),
        "Botón Menú" => (new(404, 140), new(484, 108), new(658, 108)),
        "Palanca izquierda" => (new(162, 138), new(104, 138), new(42, 138)),
        "Palanca derecha" => (new(443, 247), new(548, 247), new(658, 247)),
        "Cruceta arriba" => (new(258, 235), new(225, 88), new(190, 42)),
        "Cruceta abajo" => (new(258, 277), new(225, 410), new(190, 472)),
        "Cruceta izquierda" => (new(235, 256), new(132, 256), new(42, 256)),
        "Cruceta derecha" => (new(281, 256), new(480, 300), new(658, 300)),
        "Paleta P1" => (new(178, 378), new(238, 430), new(270, 455)),
        "Paleta P2" => (new(194, 390), new(276, 445), new(320, 472)),
        "Paleta P3" => (new(522, 378), new(462, 430), new(430, 455)),
        "Paleta P4" => (new(506, 390), new(428, 445), new(380, 472)),
        _ => null
    };

    private void AddXboxMappingCallout(Canvas canvas, Point start, Point bend, Point end, string label)
    {
        var connector = new Polyline
        {
            Points = new PointCollection([start, bend, end]),
            Stroke = (Brush)FindResource("XboxBlue"),
            StrokeThickness = 1.7
        };
        canvas.Children.Add(connector);

        const double markerSize = 28;
        var marker = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Stroke = (Brush)FindResource("XboxBlue"),
            StrokeThickness = 1.7,
            Fill = new SolidColorBrush(Color.FromArgb(225, 31, 31, 31))
        };
        Canvas.SetLeft(marker, end.X - markerSize / 2);
        Canvas.SetTop(marker, end.Y - markerSize / 2);
        canvas.Children.Add(marker);

        var text = new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("XboxBlue"),
            FontSize = label.Length > 2 ? 14 : 18,
            FontWeight = FontWeights.SemiBold,
            Width = markerSize,
            Height = markerSize,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(text, end.X - markerSize / 2);
        Canvas.SetTop(text, end.Y - markerSize / 2 + 2);
        canvas.Children.Add(text);
    }

    private void XboxProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true } device) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_viewModel.SelectedDevice is { IsXboxElite: true } selected)
                SyncXboxHotspots(selected.SelectedXboxPhysicalButton);
            UpdateXboxStickCurve(null, e);
            ScheduleXboxProfileSave();
        }), DispatcherPriority.DataBind);
    }

    private void OpenXboxProfileEditor(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is { IsXboxElite: true, CanEditXboxProfile: true } device)
            device.IsEditingXboxProfile = true;
    }

    private void OpenXboxProfiles(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true } device) return;
        device.ShowXboxProfiles();
        SyncXboxHotspots(device.SelectedXboxPhysicalButton);
    }

    private void ReturnXboxToHome(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true } device) return;
        device.ShowXboxHome();
        SyncXboxHotspots(device.SelectedXboxPhysicalButton);
    }

    private void ReturnXboxToProfiles(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is { IsXboxElite: true } device)
        {
            SaveXboxProfileImmediately();
            device.IsEditingXboxProfile = false;
            SyncXboxHotspots(device.SelectedXboxPhysicalButton);
        }
    }

    private void ResetXboxProfile(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true } device) return;
        device.ResetXboxSettings();
        SyncXboxHotspots(device.SelectedXboxPhysicalButton);
        UpdateXboxStickCurve(null, e);
        SaveXboxProfileImmediately();
    }

    private void XboxEditorValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider)
        {
            if (_viewModel.SelectedDevice is { IsXboxElite: true } device)
                RebuildXboxProfilePreview(device);
            ScheduleXboxProfileSave();
        }
    }

    private void XboxEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { Name: not nameof(XboxPhysicalButtonSelector) }) ScheduleXboxProfileSave();
    }

    private void XboxEditorCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox) ScheduleXboxProfileSave();
    }

    private void ScheduleXboxProfileSave()
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true }) return;
        _xboxProfileSaveTimer.Stop();
        _xboxProfileSaveTimer.Start();
    }

    private void XboxProfileSaveTimerTick(object? sender, EventArgs e) => SaveXboxProfileImmediately();

    private void SaveXboxProfileImmediately()
    {
        _xboxProfileSaveTimer.Stop();
        if (_viewModel.SelectedDevice is { IsXboxElite: true }) _viewModel.SaveDeviceProfile();
    }

    private void XboxProfileNameChanged(object sender, TextChangedEventArgs e) => ScheduleXboxProfileSave();

    private void FocusXboxProfileName(object sender, RoutedEventArgs e)
    {
        XboxProfileNameInput.Focus();
        XboxProfileNameInput.SelectAll();
    }

    private void SetDarkTitleBar()
    {
        var dark = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    private void UpdateXboxStickCurve(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is not { IsXboxElite: true } device) return;
        var profile = device.LocalProfile;
        if (sender is FrameworkElement { Tag: "Left" })
            UpdateXboxStickCurve(profile.XboxLeftStickCurve, profile.XboxLeftStickCurveAdjustment, XboxLeftStickCurveLine);
        else if (sender is FrameworkElement { Tag: "Right" })
            UpdateXboxStickCurve(profile.XboxRightStickCurve, profile.XboxRightStickCurveAdjustment, XboxRightStickCurveLine);
        else
        {
            UpdateXboxStickCurve(profile.XboxLeftStickCurve, profile.XboxLeftStickCurveAdjustment, XboxLeftStickCurveLine);
            UpdateXboxStickCurve(profile.XboxRightStickCurve, profile.XboxRightStickCurveAdjustment, XboxRightStickCurveLine);
        }
    }

    private static void UpdateXboxStickCurve(string curve, int adjustment, System.Windows.Shapes.Polyline line)
    {
        var kneeY = 158 - adjustment * 7;
        var jump = curve switch
        {
            "Instantáneo" => 18 + adjustment * 2,
            "Agresivo" => 38 + adjustment * 2,
            "Suave" => 5 + adjustment,
            "Retardado" => 0,
            _ => 22 + adjustment
        };
        line.Points = [new Point(0, 158), new Point(72, kneeY), new Point(72, Math.Max(0, kneeY - jump)), new Point(288, 0)];
    }

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
        if (page != "Devices") SetXboxImmersiveLayout(false);
        DevicesPage.Visibility = page == "Devices" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        MacrosPage.Visibility = page == "Macros" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetXboxImmersiveLayout(bool enabled)
    {
        NexoNavigation.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        NexoNavigationColumn.Width = new GridLength(enabled ? 0 : 226);
        NexoNavigationGap.Width = new GridLength(enabled ? 0 : 24);
        NexoRootGrid.Margin = enabled ? new Thickness(0) : new Thickness(16);
        Grid.SetColumn(NexoPages, enabled ? 0 : 2);
        Grid.SetColumnSpan(NexoPages, enabled ? 3 : 1);
    }

    private void OpenXboxMacros(object sender, RoutedEventArgs e) => MacrosNav.IsChecked = true;

    private void ShowXboxHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Configurar abre los perfiles del mando. Desde ahí puedes elegir un perfil y editar sus asignaciones, sticks, gatillos, vibración y brillo.",
        "Nexo Device Hub", MessageBoxButton.OK, MessageBoxImage.Information);

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
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoHub", "Profiles");
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
        SaveXboxProfileImmediately();
        _scanTimer.Stop();
        _xboxInputTimer.Stop();
        _profileSwitchTimer.Stop();
        _xboxSlotReadTimer.Stop();
        _viewModel.MacroManager.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
