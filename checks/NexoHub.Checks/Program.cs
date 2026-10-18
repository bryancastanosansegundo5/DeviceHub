using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using NexoHub.Adapters;
using NexoHub.Models;
using NexoHub.Services;
using NexoHub.ViewModels;

if (args is ["--probe-logitech"])
{
    await ProbeLogitechAsync();
    return;
}
if (args is ["--probe-logitech-profile-memory"])
{
    await ProbeLogitechProfileMemoryAsync();
    return;
}
if (args is ["--probe-logitech-pairing"])
{
    await ProbeLogitechPairingAsync();
    return;
}
if (args is ["--test-logitech-dpi-roundtrip"])
{
    await TestLogitechDpiRoundTripAsync();
    return;
}
if (args is ["--save-logitech-onboard-dpi"])
{
    await SaveLogitechOnboardDpiAsync();
    return;
}
if (args is ["--apply-xbox-elite-lb-menu"])
{
    var elite = new HardwareDevice(
        Id: "xbox-one-elite",
        Manufacturer: "Microsoft",
        Product: "Xbox One Elite",
        VendorId: 0x045E,
        ProductId: 0x02E3,
        Transport: "USB",
        InterfaceCount: 1,
        Detail: "Acción explícita autorizada: LB → Menú",
        ModelKey: "xbox-one-elite",
        Kind: DeviceKind.Gamepad,
        InstanceId: "USB\\VID_045E&PID_02E3\\7EED8B24F47E");
    Console.WriteLine(new XboxEliteProfileWriter().ApplyLeftBumperAssignment(elite, "Botón Menú"));
    return;
}
if (args is ["--apply-xbox-elite-lb-profile1"])
{
    var library = new DeviceLocalProfileLibrary();
    var (profiles, activeProfileId) = library.LoadSet("xbox-one-elite");
    var profile = profiles.FirstOrDefault(item => item.Id == activeProfileId);
    if (profile is null || !profile.Name.Equals("Perfil 1", StringComparison.OrdinalIgnoreCase) ||
        !profile.StorageLabel.Contains("ranura 1", StringComparison.OrdinalIgnoreCase) ||
        !XboxEliteProfileWriter.TryGetLeftBumperMapping(profile.XboxButtonAssignments.GetValueOrDefault("LB", "LB"), out _, out _))
        throw new InvalidOperationException("Perfil 1 no está ligado a ranura 1 o no tiene un destino LB admitido; no se escribió nada.");

    var elite = new HardwareDevice(
        Id: "xbox-one-elite",
        Manufacturer: "Microsoft",
        Product: "Xbox One Elite",
        VendorId: 0x045E,
        ProductId: 0x02E3,
        Transport: "USB",
        InterfaceCount: 1,
        Detail: "Aplicación explícita del destino LB guardado en Perfil 1",
        ModelKey: "xbox-one-elite",
        Kind: DeviceKind.Gamepad,
        InstanceId: "USB\\VID_045E&PID_02E3\\7EED8B24F47E");
    var assignment = profile.XboxButtonAssignments.GetValueOrDefault("LB", "LB");
    var accessoriesSync = library.SyncXboxAccessoriesLeftBumperAssignment(profile.Id, assignment);
    Console.WriteLine($"{new XboxEliteProfileWriter().ApplyLeftBumperAssignment(elite, assignment)} {accessoriesSync}");
    return;
}
if (args is ["--sync-xbox-accessories-lb-menu"])
{
    var library = new DeviceLocalProfileLibrary();
    var (profiles, activeProfileId) = library.LoadSet("xbox-one-elite");
    var profile = profiles.FirstOrDefault(item => item.Id == activeProfileId);
    if (profile is null || !profile.Name.Equals("Perfil 1", StringComparison.OrdinalIgnoreCase) ||
        !profile.XboxButtonAssignments.GetValueOrDefault("LB", "LB").Equals("Botón Menú", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("El perfil local activo no es Perfil 1 con LB → Menú; no se cambió Xbox Accessories.");
    Console.WriteLine(library.SyncXboxAccessoriesLeftBumperAssignment(profile.Id, "Botón Menú"));
    return;
}
if (args.Length is 1 or 2 && args[0] == "--watch-xinput")
{
    var seconds = args.Length == 2 && int.TryParse(args[1], out var requestedSeconds)
        ? Math.Clamp(requestedSeconds, 1, 3600)
        : 600;
    var input = new XboxInputViewModel();
    ushort? previousButtons = null;
    var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
    Console.WriteLine($"Observación XInput de solo lectura durante {seconds}s; no se envían entradas.");
    while (DateTimeOffset.UtcNow < deadline)
    {
        var state = input.Refresh(0);
        if (state?.Buttons != previousButtons)
        {
            var buttons = state?.Buttons ?? 0;
            Console.WriteLine($"{DateTimeOffset.Now:O} connected={input.IsConnected} buttons=0x{buttons:X4} {input.ButtonsLabel}");
            previousButtons = state?.Buttons;
        }
        Thread.Sleep(20);
    }
    return;
}

var testRoot = Path.Combine(AppContext.BaseDirectory, "self-check-data");
if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
Directory.CreateDirectory(testRoot);
try
{
    CheckProfileRoundTrip(testRoot);
    CheckMacroRoundTrip(testRoot);
    CheckDeviceProfileRoundTrip(testRoot);
    CheckLogitechHidppMessages();
    CheckAdapterSelection();
    CheckMacroBindingLabels();
    CheckXboxMacroInputEdges();
    CheckXboxEliteRemapEncoding();
    CheckXboxAccessoriesProfileSync(testRoot);
    CheckLiveInventory();
    Console.WriteLine("NexoHub self-check: 10 grupos correctos.");
}
finally
{
    var fullRoot = Path.GetFullPath(testRoot);
    var allowedRoot = Path.GetFullPath(AppContext.BaseDirectory);
    if (fullRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
        Directory.Delete(fullRoot, recursive: true);
}

static void CheckProfileRoundTrip(string root)
{
    var source = Path.Combine(root, "sample.agp");
    File.WriteAllText(source, "[MainDevice]\nDevice_Id=9886.18\n[Microphone]\nSidetone_Value=70\nNoisegate_Value=HOME\n");
    var library = new ProfileLibrary(Path.Combine(root, "profiles"));
    var imported = library.Import(source);
    var listed = library.List().Single();
    Assert(imported.DeviceId == "9886.18", "el ID del dispositivo se lee del .agp");
    Assert(listed.Values.Count == 3, "se recuperan los campos INI del perfil");
    var exported = Path.Combine(root, "exported.agp");
    library.Export(listed, exported);
    Assert(File.ReadAllText(exported) == File.ReadAllText(source), "la copia exportada conserva el archivo original");
}

static void CheckMacroRoundTrip(string root)
{
    var library = new MacroLibrary(Path.Combine(root, "macros"));
    var macro = new MacroDefinition
    {
        Name = "Secuencia", Binding = MacroTrigger.XButton1, ShiftBinding = MacroTrigger.XButton2,
        RepeatMode = MacroRepeatMode.WhileHeld, RepeatDelayMs = 50
    };
    macro.Steps = new ObservableCollection<MacroStep>
    {
        new() { InputType = MacroInputType.Key, VirtualKey = 0x41, IsDown = true },
        new() { InputType = MacroInputType.Key, VirtualKey = 0x41, IsDown = false, DelayMs = 18 },
        new() { InputType = MacroInputType.MouseButton, MouseButton = MacroMouseButton.Left, IsDown = true },
        new() { InputType = MacroInputType.MouseButton, MouseButton = MacroMouseButton.Left, IsDown = false },
        new() { InputType = MacroInputType.MouseWheel, WheelDelta = 120, IsDown = true },
        new() { InputType = MacroInputType.SystemCommand, SystemCommand = MacroSystemCommand.VolumeMute }
    };
    var sequence = new MacroDefinition
    {
        Name = "Pulsar y mantener", Binding = MacroTrigger.RightButton, RepeatMode = MacroRepeatMode.Sequence,
        Steps = new ObservableCollection<MacroStep> { new() { VirtualKey = 0x41, IsDown = true } },
        HoldSteps = new ObservableCollection<MacroStep>
        {
            new() { InputType = MacroInputType.MouseButton, MouseButton = MacroMouseButton.Left, IsDown = true },
            new() { InputType = MacroInputType.MouseButton, MouseButton = MacroMouseButton.Left, IsDown = false }
        },
        ReleaseSteps = new ObservableCollection<MacroStep> { new() { VirtualKey = 0x41, IsDown = false } }
    };
    var appPath = Path.Combine(root, "game.exe");
    var appMacro = new MacroDefinition
    {
        Name = "Macro de juego", ApplicationExecutable = appPath, Binding = MacroTrigger.XButton1,
        Steps = new ObservableCollection<MacroStep>
        {
            new() { VirtualKey = 0x42, IsDown = true }, new() { VirtualKey = 0x42, IsDown = false }
        }
    };
    var xboxMacro = new MacroDefinition
    {
        Name = "Skill FC", ApplicationExecutable = appPath, Binding = MacroTrigger.XboxLeftStickUpRight,
        Steps = new ObservableCollection<MacroStep>
        {
            new() { VirtualKey = 0x43, IsDown = true }, new() { VirtualKey = 0x43, IsDown = false }
        }
    };
    library.Save([macro, sequence, appMacro, xboxMacro], MacroTrigger.MiddleButton);
    var loadedMacros = library.Load();
    var loaded = loadedMacros.Single(item => item.Name == "Secuencia");
    var loadedSequence = loadedMacros.Single(item => item.Name == "Pulsar y mantener");
    Assert(loaded.Steps.Count == 6 && loaded.Steps[5].SystemCommand == MacroSystemCommand.VolumeMute &&
        loaded.Binding == MacroTrigger.XButton1 &&
        loaded.ShiftBinding == MacroTrigger.XButton2 && loaded.RepeatMode == MacroRepeatMode.WhileHeld &&
        loaded.RepeatDelayMs == 50 && library.GShiftTrigger == MacroTrigger.MiddleButton,
        "se conservan las asignaciones normal/G-Shift, repetición y acción de sistema");
    Assert(loadedSequence.RepeatMode == MacroRepeatMode.Sequence && loadedSequence.Steps.Count == 1 &&
        loadedSequence.HoldSteps.Count == 2 && loadedSequence.ReleaseSteps.Count == 1 &&
        MacroManagerViewModel.ResolveMacro([loadedSequence], MacroTrigger.RightButton, shifted: false) == loadedSequence,
        "se serializan las tres fases de una secuencia y se asigna aunque la tecla se libere al final");
    var loadedAppMacro = loadedMacros.Single(item => item.Name == "Macro de juego");
    Assert(loadedAppMacro.ApplicationExecutable == appPath &&
        MacroManagerViewModel.ResolveMacro([loaded, loadedAppMacro], MacroTrigger.XButton1, false, appPath) == loadedAppMacro &&
        MacroManagerViewModel.ResolveMacro([loaded, loadedAppMacro], MacroTrigger.XButton1, false, appPath.ToUpperInvariant()) == loadedAppMacro &&
        MacroManagerViewModel.ResolveMacro([loaded, loadedAppMacro], MacroTrigger.XButton1, false, Path.Combine(root, "otro.exe")) == loaded,
        "las asignaciones por ejecutable sobreviven al guardado y se impone la macro global como respaldo");
    var loadedXboxMacro = loadedMacros.Single(item => item.Name == "Skill FC");
    Assert(loadedXboxMacro.Binding == MacroTrigger.XboxLeftStickUpRight &&
        MacroManagerViewModel.ResolveMacro(loadedMacros, MacroTrigger.XboxLeftStickUpRight, false, appPath) == loadedXboxMacro,
        "las macros Xbox se guardan y se resuelven con el perfil de su aplicación");
    var keyAction = MacroManagerViewModel.CreateKeyAction(System.Windows.Input.Key.A);
    Assert(keyAction.Count == 2 && keyAction[0].VirtualKey == 0x41 && keyAction[0].IsDown && !keyAction[1].IsDown,
        "una tecla directa se representa como pulsación y liberación emparejadas");
    Assert(MacroPlaybackService.GetSystemCommandVirtualKey(MacroSystemCommand.VolumeMute) == 0xAD &&
        MacroPlaybackService.GetSystemCommandVirtualKey(MacroSystemCommand.NextTrack) == 0xB0 &&
        MacroPlaybackService.GetSystemCommandVirtualKey(MacroSystemCommand.BrowserBack) == 0xA6,
        "las acciones de sistema se traducen a teclas multimedia y de navegación estándar");
    var shiftedMacro = new MacroDefinition { Name = "Desplazada", ShiftBinding = MacroTrigger.XButton1, Steps = macro.Steps };
    Assert(MacroManagerViewModel.ResolveMacro([macro, shiftedMacro], MacroTrigger.XButton1, shifted: false) == macro &&
        MacroManagerViewModel.ResolveMacro([macro, shiftedMacro], MacroTrigger.XButton1, shifted: true) == shiftedMacro &&
        MacroManagerViewModel.ResolveMacro([macro], MacroTrigger.XButton1, shifted: true) == macro,
        "la capa G-Shift prioriza su propia macro y vuelve a la asignación normal si no existe otra");

    var legacyFolder = Path.Combine(root, "legacy-macros");
    Directory.CreateDirectory(legacyFolder);
    File.WriteAllText(Path.Combine(legacyFolder, "macros.json"),
        "[{\"Name\":\"Anterior\",\"Binding\":\"XButton1\",\"Steps\":[]}]");
    var legacyLibrary = new MacroLibrary(legacyFolder);
    var legacyMacro = legacyLibrary.Load().Single();
    Assert(legacyMacro.Name == "Anterior" && legacyMacro.ShiftBinding == MacroTrigger.None &&
        legacyLibrary.GShiftTrigger == MacroTrigger.None, "se migran bibliotecas antiguas de macros sin cambiar sus asignaciones");

    var invalid = new MacroDefinition { Name = "Tecla sin soltar" };
    invalid.Steps.Add(new MacroStep { VirtualKey = 0x41, IsDown = true });
    var rejected = false;
    try { library.Save([invalid]); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "se rechazan macros que dejan una tecla pulsada");

    var invalidSequence = new MacroDefinition
    {
        Name = "Secuencia sin soltar", RepeatMode = MacroRepeatMode.Sequence,
        Steps = new ObservableCollection<MacroStep> { new() { VirtualKey = 0x41, IsDown = true } }
    };
    rejected = false;
    try { library.Save([invalidSequence]); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "una macro Secuencia debe soltar en su fase final toda tecla pulsada al inicio");

    var duplicateShift = false;
    try
    {
        library.Save([
            new MacroDefinition { Name = "Shift 1", ShiftBinding = MacroTrigger.XButton1 },
            new MacroDefinition { Name = "Shift 2", ShiftBinding = MacroTrigger.XButton1 }
        ]);
    }
    catch (InvalidDataException) { duplicateShift = true; }
    Assert(duplicateShift, "se rechaza asignar dos macros al mismo botón G-Shift");

    var reservedShift = false;
    try { library.Save([new MacroDefinition { Name = "Reservada", Binding = MacroTrigger.MiddleButton }], MacroTrigger.MiddleButton); }
    catch (InvalidDataException) { reservedShift = true; }
    Assert(reservedShift, "el botón que mantiene G-Shift no puede tener otra macro asignada");

    var invalidRepeatDelay = false;
    try { library.Save([new MacroDefinition { Name = "Ritmo no válido", RepeatDelayMs = 0 }]); }
    catch (InvalidDataException) { invalidRepeatDelay = true; }
    Assert(invalidRepeatDelay, "la biblioteca rechaza intervalos de repetición fuera de rango");

    var invalidMacroPath = false;
    try { library.Save([new MacroDefinition { Name = "Perfil no válido", ApplicationExecutable = "game.exe" }]); }
    catch (InvalidDataException) { invalidMacroPath = true; }
    Assert(invalidMacroPath, "los perfiles de macro requieren una ruta absoluta de ejecutable");

    var duplicateAppAssignment = false;
    try
    {
        library.Save([
            new MacroDefinition { Name = "Juego A", ApplicationExecutable = appPath, Binding = MacroTrigger.XButton2 },
            new MacroDefinition { Name = "Juego B", ApplicationExecutable = appPath.ToUpperInvariant(), Binding = MacroTrigger.XButton2 }
        ]);
    }
    catch (InvalidDataException) { duplicateAppAssignment = true; }
    Assert(duplicateAppAssignment, "se rechazan asignaciones duplicadas al mismo botón y ejecutable sin distinguir mayúsculas");
}

static void CheckDeviceProfileRoundTrip(string root)
{
    var xboxAccessoriesProfile = Path.Combine(root, "xbox-accessories-profile.json");
    File.WriteAllText(xboxAccessoriesProfile,
        "{\"ConfigurationType\":\"Elite\",\"ConfigurationGuid\":\"0bfe3cf7-9208-4f98-99e6-706dc42247d0\",\"Name\":\"Perfil 1\",\"HardwareLeftBumper\":\"Menu\"}");
    var parser = typeof(DeviceLocalProfileLibrary).GetMethod("ReadXboxAccessoriesProfile", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("No se encontró el lector de perfiles Xbox Accessories.");
    var importedXboxProfile = parser.Invoke(null, [xboxAccessoriesProfile]) as DeviceLocalProfile;
    Assert(importedXboxProfile is not null && importedXboxProfile.XboxButtonAssignments.TryGetValue("LB", out var leftBumper) &&
        leftBumper == "Botón Menú", "el perfil Xbox Accessories conserva el remapeo real de LB a Menú");

    var profileDirectory = Path.Combine(root, "device-profiles");
    Directory.CreateDirectory(profileDirectory);
    File.WriteAllText(Path.Combine(profileDirectory, "logitech-pro-wireless.json"), "{\"Name\":\"Legacy\"}");
    var library = new DeviceLocalProfileLibrary(profileDirectory, Path.Combine(root, "empty-xbox-accessories-wgs"));
    var legacy = library.Load("logitech-pro-wireless");
    Assert(legacy.Name == "Legacy" && legacy.ShiftDpi == 400,
        "los perfiles Logitech antiguos reciben el valor G-Shift inicial propio del modelo");
    var profile = new DeviceLocalProfile { Name = "Prueba", Dpi3 = 1450, Dpi5 = 25600, ShiftDpi = 1450, DpiStageCount = 5, ActiveDpiStage = 5, ReportRate = 500, LightingColor = "FF8844" };
    profile.Id = legacy.Id;
    profile.KeyboardKeyColors["0x04"] = "00AAFF";
    library.Save("logitech-pro-wireless", profile);
    var proWireless = library.Load("logitech-pro-wireless");
    Assert(proWireless.Dpi3 == 1450 && proWireless.Dpi5 == 25600 && proWireless.DpiStageCount == 5 && proWireless.ActiveDpiStage == 5, "el G PRO Wireless conserva sus cinco etapas DPI y la activa");
    Assert(proWireless.ShiftDpi == 1450, "el perfil local conserva el DPI de G-Shift");
    var invalidLogitechShiftDpiRejected = false;
    try { library.Save("logitech-pro-wireless", new DeviceLocalProfile { Name = profile.Name, ShiftDpi = 25601 }); }
    catch (InvalidDataException) { invalidLogitechShiftDpiRejected = true; }
    Assert(invalidLogitechShiftDpiRejected, "el perfil local rechaza un DPI de G-Shift sobre el máximo del sensor");
    var alternate = new DeviceLocalProfile { Name = "Apuntar", ApplicationExecutable = @"C:\Games\Aim.exe", Dpi1 = 1300, Dpi2 = 1650, DpiStageCount = 2, ShiftDpi = 1300 };
    library.SaveSet("logitech-pro-wireless", [profile, alternate], alternate.Id);
    var profileSet = library.LoadSet("logitech-pro-wireless");
    Assert(profileSet.Profiles.Count == 2 && profileSet.ActiveProfileId == alternate.Id &&
        profileSet.Profiles.Single(item => item.Id == alternate.Id).ApplicationExecutable == alternate.ApplicationExecutable &&
        library.Load("logitech-pro-wireless").Name == "Apuntar",
        "se conservan perfiles locales alternos, su aplicación asociada y el perfil activo");
    var xboxDesktop = new DeviceLocalProfile { Name = "Escritorio", LeftStickMacroThresholdPercent = 70 };
    var xboxGame = new DeviceLocalProfile
    {
        Name = "FC27", ApplicationExecutable = @"C:\Games\FC27.exe", LeftStickMacroThresholdPercent = 20
    };
    library.SaveSet("xbox-one-elite", [xboxDesktop, xboxGame], xboxDesktop.Id);
    var savedXboxProfiles = library.LoadSet("xbox-one-elite");
    var xboxCard = new DeviceCardViewModel(
        new HardwareDevice("elite", "Microsoft", "Xbox One Elite", 0x045E, 0x02E3, "XInput", 0, "",
            ModelKey: "xbox-one-elite", Kind: DeviceKind.Gamepad),
        new AdapterInfo("XBOX", "Xbox Elite", "Disponible", "", []),
        localProfiles: savedXboxProfiles.Profiles, activeProfileId: savedXboxProfiles.ActiveProfileId);
    var xboxTracker = new XboxMacroInputTracker();
    var shortStickMove = new XboxMacroInputState(0, 0, 0, 8_000, 0, 0, 0);
    var desktopMove = xboxTracker.Update(shortStickMove, xboxCard.LocalProfile);
    var switchedToFc27 = xboxCard.SelectProfileForApplication(@"c:\games\fc27.exe");
    var gameMove = xboxTracker.Update(shortStickMove, xboxCard.LocalProfile);
    Assert(savedXboxProfiles.Profiles.Count == 2 && switchedToFc27 && desktopMove.Count == 0 &&
        xboxCard.LocalProfile.LeftStickMacroThresholdPercent == 20 &&
        gameMove.Single() == new XboxMacroInputTransition(MacroTrigger.XboxLeftStickRight, true),
        "los perfiles de mando sobreviven en disco y el asociado a FC27 activa su umbral de macro");
    var invalidLogitechDpiRejected = false;
    try { library.Save("logitech-pro-wireless", new DeviceLocalProfile { Name = profile.Name, Dpi4 = 25601 }); }
    catch (InvalidDataException) { invalidLogitechDpiRejected = true; }
    Assert(invalidLogitechDpiRejected, "el G PRO Wireless rechaza DPI sobre el máximo actualizado de 25600");
    var invalidLogitechPollingRejected = false;
    try { library.Save("logitech-pro-wireless", new DeviceLocalProfile { Name = profile.Name, ReportRate = 333 }); }
    catch (InvalidDataException) { invalidLogitechPollingRejected = true; }
    Assert(invalidLogitechPollingRejected, "el G PRO Wireless rechaza tasas de respuesta que no están en la lista permitida");
    var invalidLogitechStageRejected = false;
    try { library.Save("logitech-pro-wireless", new DeviceLocalProfile { Name = profile.Name, DpiStageCount = 5, ActiveDpiStage = 6 }); }
    catch (InvalidDataException) { invalidLogitechStageRejected = true; }
    Assert(invalidLogitechStageRejected, "el G PRO Wireless rechaza una etapa DPI activa fuera de las cinco etapas configuradas");

    library.Save("logitech-pro-x-superlight", profile);
    var loaded = library.Load("logitech-pro-x-superlight");
    Assert(loaded.Name == "Prueba" && loaded.Dpi3 == 1450 && loaded.ReportRate == 500 && loaded.LightingColor == "FF8844" &&
        loaded.KeyboardKeyColors.TryGetValue("0x04", out var keyColor) && keyColor == "00AAFF",
        "los perfiles locales de periféricos sobreviven una carga desde JSON");

    var invalidRejected = false;
    try { library.Save("ajazz-ak820", new DeviceLocalProfile { LightingColor = "rojo" }); }
    catch (InvalidDataException) { invalidRejected = true; }
    Assert(invalidRejected, "el perfil local rechaza colores que no son RGB hexadecimal");

    var invalidKeyRejected = false;
    try
    {
        var invalidKey = new DeviceLocalProfile();
        invalidKey.KeyboardKeyColors["tecla inventada"] = "FF8844";
        library.Save("ajazz-ak820", invalidKey);
    }
    catch (InvalidDataException) { invalidKeyRejected = true; }
    Assert(invalidKeyRejected, "el perfil del AK820 rechaza códigos de tecla que no parecen códigos HID");
}

static void CheckXboxEliteRemapEncoding()
{
    var configuration = Enumerable.Range(0, 55).Select(value => (byte)value).ToArray();
    configuration[6] = 0xD3; // Captura de Xbox Accessories: LB → Ver.
    var mapped = XboxEliteProfileWriter.BuildLeftBumperConfiguration(configuration, "Botón A");
    Assert(mapped.Length == 55 && mapped[6] == 0xD4 &&
        Enumerable.Range(0, 55).Where(index => index != 6).All(index => mapped[index] == configuration[index]),
        "LB → A usa el valor derivado del enum GIP y conserva intactos los otros 54 bytes");

    configuration[6] = 0xDC; // Valor escrito por versiones previas de Nexo en el campo de LB.
    mapped = XboxEliteProfileWriter.BuildLeftBumperConfiguration(configuration, "Botón Menú");
    Assert(mapped[6] == 0xD2, "LB → Menú repara el valor DC al byte D2 observado");

    configuration[6] = 0xD2;
    mapped = XboxEliteProfileWriter.BuildLeftBumperConfiguration(configuration, "Botón Menú");
    Assert(mapped.SequenceEqual(configuration), "LB → Menú es idempotente cuando el valor ya es D2");

    var mappings = new (string Assignment, byte Value)[]
    {
        ("Botón Menú", 0xD2), ("Botón Ver", 0xD3), ("Botón A", 0xD4), ("Botón B", 0xD5),
        ("Botón X", 0xD6), ("Botón Y", 0xD7), ("Cruceta arriba", 0xD8), ("Cruceta abajo", 0xD9),
        ("Cruceta izquierda", 0xDA), ("Cruceta derecha", 0xDB), ("LB", 0xDC), ("RB", 0xDD),
        ("Palanca izquierda", 0xDE), ("Palanca derecha", 0xDF)
    };
    Assert(mappings.All(mapping => XboxEliteProfileWriter.TryGetLeftBumperMapping(mapping.Assignment, out var value, out _) &&
        value == mapping.Value), "los destinos estándar siguen la tabla derivada de GipButtonMapVal, anclada por las capturas D2/D3");

    configuration[6] = 0xA5;
    var rejectedUnknownMapping = false;
    try { _ = XboxEliteProfileWriter.BuildLeftBumperConfiguration(configuration, "Botón Menú"); }
    catch (InvalidOperationException) { rejectedUnknownMapping = true; }
    Assert(rejectedUnknownMapping, "se rechaza escribir sobre un código LB no observado");

    var unsupportedTargetRejected = false;
    try { _ = XboxEliteProfileWriter.BuildLeftBumperConfiguration(configuration, "LT"); }
    catch (InvalidOperationException) { unsupportedTargetRejected = true; }
    Assert(unsupportedTargetRejected, "los gatillos y las paletas quedan fuera del mapa GIP verificado");
}

static void CheckXboxAccessoriesProfileSync(string root)
{
    var missingSlotRoot = Path.Combine(root, "xbox-accessories-missing-slot");
    var missingSlotConfig = Path.Combine(missingSlotRoot, "account", "configs2");
    Directory.CreateDirectory(missingSlotConfig);
    var slotOneGuid = Guid.NewGuid();
    var missingSlotGuid = Guid.NewGuid();
    var unassignedProfileGuid = Guid.NewGuid();
    File.WriteAllText(Path.Combine(missingSlotConfig, "configDisambig"),
        $"{{\"GIP:controller\":{{\"slot1\":\"{slotOneGuid:D}\",\"slot2\":\"{missingSlotGuid:D}\"}}}}");
    File.WriteAllText(Path.Combine(missingSlotConfig, slotOneGuid.ToString("D")),
        $"{{\"ConfigurationType\":\"Elite\",\"ConfigurationGuid\":\"{slotOneGuid:D}\",\"Name\":\"Perfil 1\"}}");
    File.WriteAllText(Path.Combine(missingSlotConfig, unassignedProfileGuid.ToString("D")),
        $"{{\"ConfigurationType\":\"Elite\",\"ConfigurationGuid\":\"{unassignedProfileGuid:D}\",\"Name\":\"FIFA sin joys\"}}");
    var missingSlotImport = new DeviceLocalProfileLibrary(
        Path.Combine(root, "nexo-profiles-missing-slot"), missingSlotRoot).LoadSet("xbox-one-elite");
    var missingSlotCard = new DeviceCardViewModel(
        new HardwareDevice("elite-missing-slot", "Microsoft", "Xbox One Elite", 0x045E, 0x02E3, "USB/HID", 1, "",
            ModelKey: "xbox-one-elite", Kind: DeviceKind.Gamepad),
        new AdapterInfo("XBOX", "Xbox Elite", "Disponible", "", []),
        localProfiles: missingSlotImport.Profiles, activeProfileId: missingSlotImport.ActiveProfileId);
    Assert(missingSlotImport.Profiles.Single(profile => profile.XboxAccessoriesSlot == 2).IsXboxAccessoriesSlotPlaceholder &&
        missingSlotCard.XboxSlot2ProfileName == "Perfil no encontrado · ranura 2" &&
        missingSlotCard.ResolveXboxProfileSlotFromLeftBumper(0xDC) is null &&
        missingSlotCard.LocalProfiles.Any(profile => profile.Name == "FIFA sin joys" && profile.XboxAccessoriesSlot is null),
        "una ranura sin archivo local queda identificada y no se asigna por conjetura a otro perfil con LB predeterminado");
    var missingSlotLibrary = new DeviceLocalProfileLibrary(
        Path.Combine(root, "nexo-profiles-missing-slot"), missingSlotRoot);
    missingSlotLibrary.SaveSet("xbox-one-elite", missingSlotImport.Profiles, missingSlotImport.ActiveProfileId);
    File.WriteAllText(Path.Combine(missingSlotConfig, missingSlotGuid.ToString("D")),
        $"{{\"ConfigurationType\":\"Elite\",\"ConfigurationGuid\":\"{missingSlotGuid:D}\",\"Name\":\"Perfil recuperado\",\"HardwareUpperLeftPaddle\":\"Unmapped\"}}");
    var recoveredMissingSlot = missingSlotLibrary.LoadSet("xbox-one-elite").Profiles.Single(profile => profile.XboxAccessoriesSlot == 2);
    Assert(!recoveredMissingSlot.IsXboxAccessoriesSlotPlaceholder && recoveredMissingSlot.Name == "Perfil recuperado",
        "si Xbox Accessories vuelve a materializar el archivo de una ranura, se recupera su perfil editable");

    var wgsRoot = Path.Combine(root, "xbox-accessories-wgs");
    var configDirectory = Path.Combine(wgsRoot, "account", "configs2");
    Directory.CreateDirectory(configDirectory);
    var profileId = Guid.NewGuid();
    var slotTwoId = Guid.NewGuid();
    File.WriteAllText(Path.Combine(configDirectory, "configDisambig"),
        $"{{\"GIP:controller\":{{\"slot1\":\"{profileId:D}\",\"slot2\":\"{slotTwoId:D}\"}}}}");
    var profilePath = Path.Combine(configDirectory, profileId.ToString("D"));
    File.WriteAllText(profilePath,
        $"{{\"ConfigurationType\":\"Elite\",\"ConfigurationGuid\":\"{profileId:D}\",\"Name\":\"Perfil 1\",\"HardwareLeftBumper\":\"LeftBumper\",\"HardwareUpperLeftPaddle\":\"LeftBumper\"}}");
    var slotTwoPath = Path.Combine(configDirectory, slotTwoId.ToString("D"));
    File.WriteAllText(slotTwoPath,
        $"{{\"ConfigurationType\":\"Elite\",\"ConfigurationGuid\":\"{slotTwoId:D}\",\"Name\":\"Perfil 2\",\"HardwareLeftBumper\":\"Menu\"}}");

    var library = new DeviceLocalProfileLibrary(Path.Combine(root, "nexo-profiles"), wgsRoot);
    var imported = library.LoadSet("xbox-one-elite");
    var importedCard = new DeviceCardViewModel(
        new HardwareDevice("elite-slots", "Microsoft", "Xbox One Elite", 0x045E, 0x02E3, "USB/HID", 1, "",
            ModelKey: "xbox-one-elite", Kind: DeviceKind.Gamepad),
        new AdapterInfo("XBOX", "Xbox Elite", "Disponible", "", []),
        localProfiles: imported.Profiles, activeProfileId: imported.ActiveProfileId);
    var profileOneMappings = importedCard.GetConfiguredXboxButtonAssignments();
    Assert(imported.Profiles.Single(item => item.XboxAccessoriesSlot == 1).Name == "Perfil 1" &&
        imported.Profiles.Single(item => item.XboxAccessoriesSlot == 2).Name == "Perfil 2" &&
        imported.ActiveProfileId == profileId.ToString("N") &&
        profileOneMappings.SequenceEqual([new KeyValuePair<string, string>("Paleta P1", "LB")]) &&
        importedCard.ResolveXboxProfileSlotFromLeftBumper(0xDC) == 1 &&
        importedCard.ResolveXboxProfileSlotFromLeftBumper(0xD2) == 2,
        "la copia de Xbox Accessories asigna los dos perfiles, solo pinta la paleta remapeada y distingue LB por perfil");
    importedCard.SetDetectedXboxProfileSlot(2);
    importedCard.LocalProfile.XboxButtonAssignments["Botón A"] = "Botón B";
    Assert(importedCard.LocalProfile.Name == "Perfil 2" &&
        importedCard.GetConfiguredXboxButtonAssignments().Contains(new KeyValuePair<string, string>("LB", "Botón Menú")) &&
        importedCard.GetConfiguredXboxButtonAssignments().Contains(new KeyValuePair<string, string>("Botón A", "Botón B")),
        "al detectar la segunda ranura se cambia de perfil y la vista previa incluye también remapeos de botones frontales");

    var result = library.SyncXboxAccessoriesLeftBumperAssignment(profileId.ToString("N"), "Botón A");
    using var profile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(profilePath));
    using var slotTwo = System.Text.Json.JsonDocument.Parse(File.ReadAllText(slotTwoPath));
    Assert(profile.RootElement.GetProperty("HardwareLeftBumper").GetString() == "A" &&
        profile.RootElement.GetProperty("HardwareUpperLeftPaddle").GetString() == "LeftBumper",
        "la copia local sincroniza LB → A sin tocar las otras asignaciones del perfil Xbox de la ranura 1");
    Assert(slotTwo.RootElement.GetProperty("HardwareLeftBumper").GetString() == "Menu" &&
        Directory.GetFiles(Path.Combine(root, "nexo-profiles", "XboxAccessoriesBackups")).Length == 1 &&
        result.Contains("respaldo guardado", StringComparison.OrdinalIgnoreCase),
        "la sincronización conserva ranura 2 y guarda respaldo de Perfil 1");
}

static void CheckAdapterSelection()
{
    Assert(MainViewModel.IsAutomaticLogitechIdentityAttemptAllowed(0) &&
        MainViewModel.IsAutomaticLogitechIdentityAttemptAllowed(1) &&
        !MainViewModel.IsAutomaticLogitechIdentityAttemptAllowed(2),
        "la identidad del Superlight recibe como máximo dos lecturas automáticas por conexión");

    var registry = new DeviceAdapterRegistry();
    var astro = registry.Describe(new HardwareDevice("astro", "Astro Gaming", "Astro A50", 0x9886, 0x0015, "USB/HID", 2, ""));
    var logitech = registry.Describe(new HardwareDevice("logi", "Logitech", "G device", 0x046D, 0xC547, "USB/HID", 1, ""));
    var proWirelessDevice = new HardwareDevice("pro-wireless", "Logitech G", "PRO WIRELESS", 0x046D, 0xC539,
        "USB/HID", 1, "", ModelKey: "logitech-pro-wireless", Kind: DeviceKind.Mouse,
        HidInterfaces: [new(0xFF00, 0x0002, 20, 20, 0)]);
    var proWireless = registry.Describe(proWirelessDevice);
    var unconfirmedSuperlightDevice = new HardwareDevice("superlight-receiver", "Logitech G", "Ratón Logitech G LIGHTSPEED",
        0x046D, 0xC547, "USB/HID", 1, "", ModelKey: "logitech-lightspeed-mouse", Kind: DeviceKind.Mouse,
        HidInterfaces: [new(0xFF00, 0x0002, 20, 20, 0)]);
    var unconfirmedSuperlight = new DeviceCardViewModel(unconfirmedSuperlightDevice,
        registry.Describe(unconfirmedSuperlightDevice));
    var confirmedSuperlightDevice = unconfirmedSuperlightDevice with
    {
        Product = "PRO X SUPERLIGHT",
        ModelKey = "logitech-pro-x-superlight",
        IdentificationNote = "HID++ 0x0005 confirmó PRO X SUPERLIGHT"
    };
    var confirmedSuperlight = new DeviceCardViewModel(confirmedSuperlightDevice,
        registry.Describe(confirmedSuperlightDevice));
    var xbox = registry.Describe(new HardwareDevice("xbox", "Microsoft", "Xbox controller", 0x045E, 0, "XInput", 0, "", 0));
    var xboxEliteDevice = new HardwareDevice("xbox-elite", "Microsoft", "Xbox One Elite", 0x045E, 0x02E3,
        "USB/HID", 1, "", ModelKey: "xbox-one-elite", Kind: DeviceKind.Gamepad);
    var xboxElite = new DeviceCardViewModel(xboxEliteDevice, registry.Describe(xboxEliteDevice));
    xboxElite.AddLocalProfile();
    Assert(xboxElite.CanManageDeviceProfiles && xboxElite.LocalProfiles.Count == 2 && xboxElite.CanDeleteLocalProfile,
        "la ficha Elite permite crear perfiles locales para preparar configuraciones distintas");
    var xboxDesktopProfile = new DeviceLocalProfile { Name = "Escritorio", LeftStickMacroThresholdPercent = 70 };
    var xboxGameProfile = new DeviceLocalProfile
    {
        Name = "FC27", ApplicationExecutable = @"C:\Games\FC27.exe", LeftStickMacroThresholdPercent = 20
    };
    var xboxProfilesCard = new DeviceCardViewModel(xboxEliteDevice, registry.Describe(xboxEliteDevice),
        localProfiles: [xboxDesktopProfile, xboxGameProfile], activeProfileId: xboxDesktopProfile.Id);
    var xboxProfileTracker = new XboxMacroInputTracker();
    var smallStickGesture = new XboxMacroInputState(0, 0, 0, 8_000, 0, 0, 0);
    var desktopProfileTransitions = xboxProfileTracker.Update(smallStickGesture, xboxProfilesCard.LocalProfile);
    var switchedToGame = xboxProfilesCard.SelectProfileForApplication(@"c:\games\fc27.exe");
    var gameProfileTransitions = xboxProfileTracker.Update(smallStickGesture, xboxProfilesCard.LocalProfile);
    Assert(desktopProfileTransitions.Count == 0 && switchedToGame && xboxProfilesCard.LocalProfile == xboxGameProfile &&
        gameProfileTransitions.Single() == new XboxMacroInputTransition(MacroTrigger.XboxLeftStickRight, true),
        "el perfil local del Elite cambia con FC27 y ajusta el gesto que activa una macro");
    var yeti = registry.Describe(new HardwareDevice("yeti", "Blue Microphones", "Yeti Classic", 0xB58E, 0x9E84,
        "USB Audio", 0, "", ModelKey: "blue-yeti-classic", Kind: DeviceKind.Microphone));
    var keyboard = registry.Describe(new HardwareDevice("ak820", "AJAZZ", "AK820", 0x0C45, 0x8009,
        "USB/HID", 1, "", ModelKey: "ajazz-ak820", Kind: DeviceKind.Keyboard));
    var unrelatedAjazz = registry.Describe(new HardwareDevice("other-ajazz", "AJAZZ / Microdia", "Dispositivo USB", 0x0C45, 0x8006,
        "USB/HID", 1, "", ModelKey: "hid-0C45-8006", Kind: DeviceKind.Keyboard));
    Assert(astro.Brand == "ASTRO" && logitech.Brand == "LOGITECH G" &&
        !logitech.DisplayName.Contains("SUPERLIGHT", StringComparison.OrdinalIgnoreCase) &&
        proWireless.DisplayName == "PRO WIRELESS" && xbox.Brand == "XBOX" &&
        yeti.Brand == "BLUE" && keyboard.Brand == "AJAZZ" && unrelatedAjazz.DisplayName == "Dispositivo USB" &&
        unrelatedAjazz.Capabilities.Single().Name == "Inventario",
        "los modelos piloto seleccionan su adaptador y un VID compartido no identifica otro HID como AK820");
    var descriptorDetail = proWireless.Capabilities.Single(item => item.Name == "Descriptor HID").Detail;
    Assert(descriptorDetail.Contains("FF00/0002", StringComparison.Ordinal) &&
        descriptorDetail.Contains("no confirman funciones HID++", StringComparison.Ordinal),
        "la ficha Logitech incluye datos del descriptor sin afirmar compatibilidad HID++");
    Assert(!new DeviceCardViewModel(proWirelessDevice, proWireless).ShowNoAudioEndpointsMessage &&
        new DeviceCardViewModel(new HardwareDevice("yeti", "Blue Microphones", "Yeti Classic", 0xB58E, 0x9E84,
            "USB Audio", 0, "", ModelKey: "blue-yeti-classic", Kind: DeviceKind.Microphone), yeti).ShowNoAudioEndpointsMessage,
        "el mensaje de endpoint de audio no aparece en la ficha del ratón");
    Assert(unconfirmedSuperlight.IsUnidentifiedLogitechMouse && unconfirmedSuperlight.HasLogitechMouseSettings &&
        unconfirmedSuperlight.HasLocalEditor && unconfirmedSuperlight.CanManageDeviceProfiles &&
        !unconfirmedSuperlight.IsLogitechDpiWritable && !unconfirmedSuperlight.CanSaveLogitechOnboardDpi &&
        unconfirmedSuperlight.CanAddLogitechDpiStage,
        "el C547 sin identificar muestra el editor común sin habilitar escrituras al ratón");
    Assert(confirmedSuperlight.DisplayName == "PRO X SUPERLIGHT" &&
        new DeviceCardViewModel(proWirelessDevice, proWireless).DisplayName == "PRO WIRELESS",
        "las fichas identificadas muestran el nombre correcto de cada ratón Logitech");
    Assert(confirmedSuperlight.IsLogitechDpiWritable && confirmedSuperlight.CanSaveLogitechOnboardDpi &&
        !new DeviceCardViewModel(proWirelessDevice, proWireless).CanSaveLogitechOnboardDpi,
        "el guardado integrado solo se muestra para el modelo cuyo perfil onboard está validado");
    unconfirmedSuperlight.SelectedLogitechDpiValue = 1800;
    var genericProfiles = Path.Combine(Path.GetTempPath(), "nexo-superlight-profile-check", Guid.NewGuid().ToString("N"));
    try
    {
        var library = new DeviceLocalProfileLibrary(genericProfiles);
        library.SaveSet(unconfirmedSuperlight.LocalProfileKey, unconfirmedSuperlight.LocalProfiles,
            unconfirmedSuperlight.LocalProfile.Id);
        Assert(library.HasSavedSet(unconfirmedSuperlight.LocalProfileKey) &&
            library.Load(unconfirmedSuperlight.LocalProfileKey).Dpi1 == 1800,
            "el perfil DPI del C547 sin identificar se guarda en Nexo con los mismos límites que los modelos confirmados");
    }
    finally
    {
        if (Directory.Exists(genericProfiles)) Directory.Delete(genericProfiles, recursive: true);
    }
    var dpiCard = new DeviceCardViewModel(proWirelessDevice, proWireless);
    dpiCard.IsLogitechDpiStage3Selected = true;
    dpiCard.SelectedLogitechDpiValue = 1450;
    Assert(dpiCard.ActiveLogitechDpiStage == 3 && dpiCard.LocalProfile.ActiveDpiStage == 3 &&
        dpiCard.LocalProfile.Dpi1 == 400 && dpiCard.LocalProfile.Dpi3 == 1450 && dpiCard.LogitechDpiStage3Value == 1450,
        "el selector DPI cambia solo la etapa activa del perfil local");
    dpiCard.SelectedLogitechDpiValue = 30000;
    Assert(dpiCard.LocalProfile.Dpi3 == 25600, "el deslizador local mantiene el DPI dentro del rango del G PRO Wireless");
    var desktopProfile = new DeviceLocalProfile { Name = "Escritorio" };
    var gameProfile = new DeviceLocalProfile { Name = "Juego", ApplicationExecutable = @"C:\Games\Aim.exe" };
    var appProfileCard = new DeviceCardViewModel(proWirelessDevice, proWireless,
        localProfiles: [desktopProfile, gameProfile], activeProfileId: desktopProfile.Id);
    Assert(appProfileCard.SelectProfileForApplication(@"c:\games\aim.exe") && appProfileCard.LocalProfile == gameProfile &&
        appProfileCard.SelectProfileForApplication(@"C:\Other\game.exe") && appProfileCard.LocalProfile == desktopProfile,
        "el perfil local cambia con la aplicación coincidente y vuelve al escritorio como perfil predeterminado");
    var manualCard = new DeviceCardViewModel(proWirelessDevice, proWireless,
        localProfiles: [desktopProfile, new DeviceLocalProfile { Name = "Manual" }], activeProfileId: desktopProfile.Id);
    manualCard.LocalProfile = manualCard.LocalProfiles[1];
    Assert(!manualCard.SelectProfileForApplication(@"C:\Other\game.exe") && manualCard.LocalProfile.Name == "Manual",
        "la selección manual se conserva si no hay perfiles asociados a aplicaciones");
    var ambiguousCard = new DeviceCardViewModel(proWirelessDevice, proWireless,
        localProfiles: [desktopProfile, new DeviceLocalProfile { Name = "Manual 2" }, gameProfile], activeProfileId: gameProfile.Id);
    Assert(ambiguousCard.SelectProfileForApplication(@"C:\Other\game.exe") && ambiguousCard.LocalProfile == desktopProfile,
        "si una aplicación no coincide, se selecciona el primer perfil local sin ejecutable asociado");
}

static void CheckLogitechHidppMessages()
{
    const byte deviceIndex = 1;
    const byte softwareId = 0x0A;
    const byte pingData = 0xA5;
    var pairingRequest = LogitechHidppCapabilityReader.BuildGetReceiverPairingInfoRequest(1);
    var slotSixPairingRequest = LogitechHidppCapabilityReader.BuildGetReceiverPairingInfoRequest(6);
    var slotTwoNameRequest = LogitechHidppCapabilityReader.BuildGetReceiverDeviceNameRequest(2);
    var pairingResponse = new byte[20];
    pairingResponse[0] = 0x11;
    pairingResponse[1] = 0xFF;
    pairingResponse[2] = 0x83;
    pairingResponse[3] = 0xB5;
    pairingResponse[4] = 0x20;
    pairingResponse[7] = 0x40;
    pairingResponse[8] = 0x79;
    pairingResponse[11] = 0x02;
    var storedNameResponse = new byte[20];
    storedNameResponse[0] = 0x11;
    storedNameResponse[1] = 0xFF;
    storedNameResponse[2] = 0x83;
    storedNameResponse[3] = 0xB5;
    storedNameResponse[4] = 0x41;
    var storedMouseName = System.Text.Encoding.ASCII.GetBytes("G502 X LS");
    storedNameResponse[5] = (byte)storedMouseName.Length;
    storedMouseName.CopyTo(storedNameResponse, 6);
    Assert(pairingRequest.SequenceEqual(new byte[] { 0x10, 0xFF, 0x83, 0xB5, 0x20, 0, 0 }) &&
        slotSixPairingRequest.SequenceEqual(new byte[] { 0x10, 0xFF, 0x83, 0xB5, 0x25, 0, 0 }) &&
        slotTwoNameRequest.SequenceEqual(new byte[] { 0x10, 0xFF, 0x83, 0xB5, 0x41, 0, 0 }) &&
        LogitechHidppCapabilityReader.TryReadReceiverPairingInfoResponse(pairingResponse, 1, out var wpid, out var kind) &&
        wpid == 0x4079 && kind == 2 &&
        LogitechHidppCapabilityReader.ModelFromWirelessProductId(wpid)?.ModelKey == "logitech-pro-wireless" &&
        LogitechHidppCapabilityReader.ModelFromWirelessProductId(0x4093)?.ModelKey == "logitech-pro-x-superlight",
        "el registro del receptor identifica el modelo emparejado sin afirmar actividad en vivo");
    Assert(LogitechHidppCapabilityReader.TryReadReceiverPairingInfoResponse(pairingResponse, 1, out _, out _) &&
        LogitechHidppCapabilityReader.TryReadReceiverDeviceNameResponse(storedNameResponse, 2, out var storedName) &&
        storedName == "G502 X LS" &&
        !LogitechHidppCapabilityReader.TryReadReceiverDeviceNameResponse(storedNameResponse, 1, out _) &&
        !new LogitechReceiverPairingInfo(2, 0x409F, 2, storedName).LiveActivityConfirmed,
        "se distingue el nombre emparejado guardado de la actividad en vivo, que queda sin confirmar");
    pairingResponse[4] = 0x21;
    Assert(!LogitechHidppCapabilityReader.TryReadReceiverPairingInfoResponse(pairingResponse, 1, out _, out _),
        "se rechaza una respuesta de otro slot del registro de emparejamiento");
    var request = LogitechHidppCapabilityReader.BuildGetFeatureRequest(deviceIndex, 0x2201, softwareId);
    var reportRateRequest = LogitechHidppCapabilityReader.BuildGetFeatureRequest(deviceIndex, 0x8060, softwareId);
    Assert(request.Length == 20 && request.Take(6).SequenceEqual(new byte[] { 0x11, 1, 0, 0x0A, 0x22, 0x01 }) &&
        request.Skip(6).All(value => value == 0) && reportRateRequest.Take(6).SequenceEqual(new byte[] { 0x11, 1, 0, 0x0A, 0x80, 0x60 }),
        "la petición HID++ GetFeature usa el framing largo y el ID en big-endian, incluido polling 0x8060");

    var featureResponse = new byte[20];
    featureResponse[0] = 0x11;
    featureResponse[1] = deviceIndex;
    featureResponse[3] = softwareId;
    featureResponse[4] = 0x07;
    Assert(LogitechHidppCapabilityReader.TryReadFeatureIndex(featureResponse, deviceIndex, softwareId, out var featureIndex) &&
        featureIndex == 0x07, "se acepta el índice devuelto por IRoot.GetFeature");
    featureResponse[4] = 0;
    Assert(LogitechHidppCapabilityReader.TryReadFeatureIndex(featureResponse, deviceIndex, softwareId, out var absentIndex) &&
        absentIndex == 0, "un índice cero válido representa una feature no anunciada");
    featureResponse[4] = 0x07;
    Assert(!LogitechHidppCapabilityReader.TryReadFeatureIndex(featureResponse.AsSpan(0, 7), deviceIndex, softwareId, out _) &&
        !LogitechHidppCapabilityReader.TryReadFeatureIndex(featureResponse, 2, softwareId, out _),
        "se rechazan respuestas cortas o de otra ranura");
    featureResponse[3] = 0x1A;
    Assert(!LogitechHidppCapabilityReader.TryReadFeatureIndex(featureResponse, deviceIndex, softwareId, out _),
        "se rechaza una respuesta de función distinta");

    var onboardInfoRequest = LogitechHidppCapabilityReader.BuildGetOnboardProfilesInfoRequest(
        deviceIndex, 7, softwareId);
    var onboardInfoResponse = new byte[20];
    onboardInfoResponse[0] = 0x11;
    onboardInfoResponse[1] = deviceIndex;
    onboardInfoResponse[2] = 7;
    onboardInfoResponse[3] = softwareId;
    onboardInfoResponse[4] = 1;
    onboardInfoResponse[5] = 4;
    onboardInfoResponse[7] = 5;
    onboardInfoResponse[9] = 6;
    onboardInfoResponse[10] = 16;
    onboardInfoResponse[12] = 0xFF;
    Array.Fill(onboardInfoResponse, (byte)0xFF, 15, 5);
    Assert(onboardInfoRequest.Take(5).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, softwareId, 0 }) &&
        onboardInfoRequest.Skip(5).All(value => value == 0) &&
        LogitechHidppCapabilityReader.TryReadOnboardProfilesInfoResponse(onboardInfoResponse, deviceIndex, 7,
            softwareId, out var onboardInfo) && onboardInfo is
            { MemoryModelId: 1, ProfileFormatId: 4, ProfileCount: 5, ButtonCount: 6, SectorCount: 16, SectorSize: 255 } &&
        onboardInfo.Reserved.Count == 5 && onboardInfo.Reserved.All(value => value == 0xFF),
        "se consulta y decodifica la información de perfiles integrados 0x8100 en una lectura de solo consulta");
    onboardInfoResponse[1]++;
    Assert(!LogitechHidppCapabilityReader.TryReadOnboardProfilesInfoResponse(onboardInfoResponse, deviceIndex, 7,
        softwareId, out _), "se rechaza una descripción de memoria integrada perteneciente a otra ranura");

    var memoryReadRequest = LogitechHidppCapabilityReader.BuildReadOnboardProfileMemoryRequest(
        deviceIndex, 7, 2, 0x00EF, softwareId);
    var memoryReadResponse = new byte[20];
    memoryReadResponse[0] = 0x11;
    memoryReadResponse[1] = deviceIndex;
    memoryReadResponse[2] = 7;
    memoryReadResponse[3] = 0x5A;
    for (var index = 4; index < memoryReadResponse.Length; index++) memoryReadResponse[index] = (byte)index;
    Assert(memoryReadRequest.Take(8).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x5A, 0, 2, 0, 0xEF }) &&
        LogitechHidppCapabilityReader.TryReadOnboardProfileMemoryResponse(memoryReadResponse, deviceIndex, 7,
            softwareId, out var chunk) && chunk.Length == 16 && chunk[0] == 4 && chunk[^1] == 19,
        "la lectura de perfil 0x8100 usa el sector y offset big-endian y devuelve 16 bytes");
    var currentProfileRequest = LogitechHidppCapabilityReader.BuildGetCurrentOnboardProfileRequest(
        deviceIndex, 7, softwareId);
    memoryReadResponse[3] = 0x4A;
    memoryReadResponse[4] = 0;
    memoryReadResponse[5] = 3;
    Assert(currentProfileRequest.Take(4).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x4A }) &&
        LogitechHidppCapabilityReader.TryReadCurrentOnboardProfileResponse(memoryReadResponse, deviceIndex, 7,
            softwareId, out var activeProfile) && activeProfile == 3,
        "se identifica el perfil activo mediante la función 0x40 de 0x8100");

    var profileModeRequest = LogitechHidppCapabilityReader.BuildGetOnboardProfileModeRequest(
        deviceIndex, 7, softwareId);
    var profileModeResponse = memoryReadResponse.ToArray();
    profileModeResponse[3] = 0x2A;
    profileModeResponse[4] = 0x01;
    Assert(profileModeRequest.Take(4).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x2A }) &&
        LogitechHidppCapabilityReader.TryReadOnboardProfileModeResponse(profileModeResponse, deviceIndex, 7,
            softwareId, out var profileMode) && profileMode == 0x01 &&
        !LogitechHidppCapabilityReader.TryReadOnboardProfileModeResponse(
            profileModeResponse, deviceIndex, 7, (byte)(softwareId + 1), out _),
        "se comprueba el modo integrado antes de consultar el perfil activo");

    var directorySector = Enumerable.Repeat((byte)0xFF, 255).ToArray();
    for (var index = 0; index < 5; index++)
    {
        directorySector[index * 4] = 0;
        directorySector[index * 4 + 1] = (byte)(index + 1);
        directorySector[index * 4 + 2] = 0;
    }
    directorySector[2] = 1;
    var directoryChecksum = LogitechHidppCapabilityReader.ComputeOnboardProfileChecksum(directorySector);
    directorySector[^2] = (byte)(directoryChecksum >> 8);
    directorySector[^1] = (byte)directoryChecksum;
    var profileSector = Enumerable.Repeat((byte)0xFF, 255).ToArray();
    profileSector[1] = 1;
    var stagesExpected = new int?[] { 400, 800, 1600, 3200, 6400 };
    for (var index = 0; index < stagesExpected.Length; index++)
    {
        profileSector[3 + index * 2] = (byte)stagesExpected[index]!.Value;
        profileSector[4 + index * 2] = (byte)(stagesExpected[index]!.Value >> 8);
    }
    var profileChecksum = LogitechHidppCapabilityReader.ComputeOnboardProfileChecksum(profileSector);
    profileSector[^2] = (byte)(profileChecksum >> 8);
    profileSector[^1] = (byte)profileChecksum;
    var crcVector = Encoding.ASCII.GetBytes("123456789\0\0");
    Assert(LogitechHidppCapabilityReader.ComputeOnboardProfileChecksum(crcVector) == 0x29B1 &&
        LogitechHidppCapabilityReader.TryReadOnboardProfileDirectory(directorySector, 16, 5, out var directoryEntries) &&
        directoryEntries.Count == 5 && directoryEntries.Single(entry => entry.IsEnabled).Sector == 1 &&
        LogitechHidppCapabilityReader.TryReadBaseV1DpiTable(profileSector, out var defaultDpiIndex,
            out var storedStages) && defaultDpiIndex == 1 && storedStages.SequenceEqual(stagesExpected),
        "se comprueba el checksum, el directorio y las cinco etapas DPI base-v1");

    profileSector[30] = 0x42;
    var checksumAfterSentinel = LogitechHidppCapabilityReader.ComputeOnboardProfileChecksum(profileSector);
    profileSector[^2] = (byte)(checksumAfterSentinel >> 8);
    profileSector[^1] = (byte)checksumAfterSentinel;
    var updatedProfile = LogitechHidppCapabilityReader.BuildBaseV1DpiProfile(
        profileSector, [400, 800, 1300, 2000], 2);
    var startWrite = LogitechHidppCapabilityReader.BuildStartOnboardProfileWriteRequest(
        deviceIndex, 7, 1, 255, softwareId);
    var writeChunk = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
    var writeData = LogitechHidppCapabilityReader.BuildOnboardProfileWriteDataRequest(
        deviceIndex, 7, writeChunk, softwareId);
    var endWrite = LogitechHidppCapabilityReader.BuildEndOnboardProfileWriteRequest(deviceIndex, 7, softwareId);
    Assert(LogitechHidppCapabilityReader.TryReadBaseV1DpiTable(updatedProfile, out var updatedDefault,
            out var updatedStages) && updatedDefault == 2 &&
        updatedStages.Select(value => value ?? -1).SequenceEqual(new[] { 400, 800, 1300, 2000, -1 }) &&
        updatedProfile[30] == 0x42 &&
        startWrite.Take(10).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x6A, 0, 1, 0, 0, 0, 255 }) &&
        writeData.Take(20).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x7A }.Concat(writeChunk)) &&
        endWrite.Take(4).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x8A }) &&
        LogitechHidppCapabilityReader.HasValidOnboardProfileChecksum(updatedProfile),
        "se construye la escritura HID++ 0x8100 en bloques y se actualizan DPI sin alterar bytes ajenos ni checksum");

    var versionRequest = LogitechHidppCapabilityReader.BuildGetProtocolVersionRequest(deviceIndex, softwareId, pingData);
    var versionResponse = new byte[20];
    versionResponse[0] = 0x11;
    versionResponse[1] = deviceIndex;
    versionResponse[3] = 0x1A;
    versionResponse[4] = 0x04;
    versionResponse[5] = 0x05;
    versionResponse[6] = pingData;
    Assert(versionRequest[3] == 0x1A && versionRequest[6] == pingData &&
        LogitechHidppCapabilityReader.TryReadProtocolVersionResponse(versionResponse, deviceIndex, softwareId,
            pingData, out var major, out var minor) && major == 0x04 && minor == 0x05,
        "IRoot.GetProtocolVersion devuelve versión y eco del ping");
    versionResponse[6]++;
    Assert(!LogitechHidppCapabilityReader.TryReadProtocolVersionResponse(versionResponse, deviceIndex, softwareId,
        pingData, out _, out _), "se rechaza una respuesta de protocolo con eco incorrecto");
    Assert(LogitechHidppCapabilityReader.IsHidpp20ProtocolMajor(0x02) &&
        LogitechHidppCapabilityReader.IsHidpp20ProtocolMajor(0x04) &&
        !LogitechHidppCapabilityReader.IsHidpp20ProtocolMajor(0x8F),
        "el gate acepta HID++ 2.0 y rechaza HID++ 1.0");
    var typeRequest = LogitechHidppCapabilityReader.BuildGetDeviceTypeRequest(deviceIndex, 3, softwareId);
    var typeResponse = new byte[20];
    typeResponse[0] = 0x11;
    typeResponse[1] = deviceIndex;
    typeResponse[2] = 3;
    typeResponse[3] = 0x2A;
    typeResponse[4] = 3;
    Assert(typeRequest[2] == 3 && typeRequest[3] == 0x2A &&
        LogitechHidppCapabilityReader.TryReadDeviceTypeResponse(typeResponse, deviceIndex, 3, softwareId, out var type) &&
        type == 3 && !LogitechHidppCapabilityReader.TryReadDeviceTypeResponse(typeResponse, deviceIndex, 4,
            softwareId, out _), "se consulta y valida GetDeviceType en la feature 0x0005");
    var nameLengthRequest = LogitechHidppCapabilityReader.BuildGetDeviceNameLengthRequest(deviceIndex, 3, softwareId);
    var nameLengthResponse = new byte[20];
    nameLengthResponse[0] = 0x11;
    nameLengthResponse[1] = deviceIndex;
    nameLengthResponse[2] = 3;
    nameLengthResponse[3] = softwareId;
    nameLengthResponse[4] = 17;
    var nameChunkRequest = LogitechHidppCapabilityReader.BuildGetDeviceNameChunkRequest(deviceIndex, 3, 16, softwareId);
    var nameChunkResponse = new byte[20];
    nameChunkResponse[0] = 0x11;
    nameChunkResponse[1] = deviceIndex;
    nameChunkResponse[2] = 3;
    nameChunkResponse[3] = 0x1A;
    nameChunkResponse[4] = (byte)'X';
    var nameChunk = new byte[1];
    Assert(nameLengthRequest.Take(5).SequenceEqual(new byte[] { 0x11, deviceIndex, 3, softwareId, 0 }) &&
        LogitechHidppCapabilityReader.TryReadDeviceNameLengthResponse(nameLengthResponse, deviceIndex, 3,
            softwareId, out var nameLength) && nameLength == 17 &&
        nameChunkRequest.Take(5).SequenceEqual(new byte[] { 0x11, deviceIndex, 3, 0x1A, 16 }) &&
        LogitechHidppCapabilityReader.TryReadDeviceNameChunkResponse(nameChunkResponse, deviceIndex, 3, 16,
            softwareId, nameChunk) && nameChunk[0] == (byte)'X' &&
        LogitechHidppCapabilityReader.ModelKeyFromDeviceName("G PRO X SUPERLIGHT") == "logitech-pro-x-superlight" &&
        LogitechHidppCapabilityReader.ModelKeyFromDeviceName("PRO X Wireless") == "logitech-pro-x-superlight",
        "se leen los fragmentos HID++ 0x0005 y se reconoce el nombre del SUPERLIGHT");
    var reportRateGet = LogitechHidppCapabilityReader.BuildGetReportRateRequest(deviceIndex, 15, softwareId);
    var reportRateResponse = new byte[20];
    reportRateResponse[0] = 0x11;
    reportRateResponse[1] = deviceIndex;
    reportRateResponse[2] = 15;
    reportRateResponse[3] = 0x1A;
    reportRateResponse[4] = 1;
    Assert(reportRateGet.Take(5).SequenceEqual(new byte[] { 0x11, deviceIndex, 15, 0x1A, 0 }) &&
        reportRateGet.Skip(5).All(value => value == 0) &&
        LogitechHidppCapabilityReader.TryReadReportRateResponse(reportRateResponse, deviceIndex, 15, softwareId,
            out var reportRateMs) && reportRateMs == 1,
        "se construye y valida el getter de polling HID++ 0x8060 sin incluir setter");
    reportRateResponse[1]++;
    Assert(!LogitechHidppCapabilityReader.TryReadReportRateResponse(reportRateResponse, deviceIndex, 15, softwareId,
        out _), "se rechaza una tasa HID++ de otra ranura");
    reportRateResponse[1] = deviceIndex;
    reportRateResponse[3] = 0x2A;
    Assert(!LogitechHidppCapabilityReader.TryReadReportRateResponse(reportRateResponse, deviceIndex, 15, softwareId,
        out _), "se rechaza una respuesta HID++ de polling con función/SWID distinto");
    reportRateResponse[3] = 0x1A;
    reportRateResponse[4] = 0;
    Assert(!LogitechHidppCapabilityReader.TryReadReportRateResponse(reportRateResponse, deviceIndex, 15, softwareId,
        out _), "se rechaza un intervalo de polling cero");
    var sensorCountRequest = LogitechHidppCapabilityReader.BuildGetSensorCountRequest(deviceIndex, 7, softwareId);
    var sensorCountResponse = new byte[20];
    sensorCountResponse[0] = 0x11;
    sensorCountResponse[1] = deviceIndex;
    sensorCountResponse[2] = 7;
    sensorCountResponse[3] = softwareId;
    sensorCountResponse[4] = 1;
    var sensorDpiRequest = LogitechHidppCapabilityReader.BuildGetSensorDpiRequest(deviceIndex, 7, 0, softwareId);
    var sensorDpiResponse = new byte[20];
    sensorDpiResponse[0] = 0x11;
    sensorDpiResponse[1] = deviceIndex;
    sensorDpiResponse[2] = 7;
    sensorDpiResponse[3] = 0x2A;
    sensorDpiResponse[4] = 0;
    sensorDpiResponse[5] = 0x05;
    sensorDpiResponse[6] = 0x78;
    sensorDpiResponse[7] = 0x03;
    sensorDpiResponse[8] = 0xE8;
    Assert(sensorCountRequest[2] == 7 && sensorCountRequest[3] == softwareId &&
        LogitechHidppCapabilityReader.TryReadSensorCountResponse(sensorCountResponse, deviceIndex, 7, softwareId,
            out var sensorCount) && sensorCount == 1 &&
        sensorDpiRequest[2] == 7 && sensorDpiRequest[3] == 0x2A && sensorDpiRequest[4] == 0 &&
        LogitechHidppCapabilityReader.TryReadSensorDpiResponse(sensorDpiResponse, deviceIndex, 7, 0, softwareId,
            out var currentDpi, out var defaultDpi) && currentDpi == 1400 && defaultDpi == 1000 &&
        !LogitechHidppCapabilityReader.TryReadSensorDpiResponse(sensorDpiResponse, deviceIndex, 7, 1, softwareId,
            out _, out _), "se valida la lectura HID++ del conteo de sensores y DPI actual/default");
    sensorDpiResponse[5] = sensorDpiResponse[6] = 0;
    Assert(!LogitechHidppCapabilityReader.TryReadSensorDpiResponse(sensorDpiResponse, deviceIndex, 7, 0,
        softwareId, out _, out _), "se rechaza un DPI actual fuera del rango de la feature");
    var dpiListRequest = LogitechHidppCapabilityReader.BuildGetSensorDpiListRequest(deviceIndex, 7, 0, softwareId);
    var dpiListResponse = new byte[20];
    dpiListResponse[0] = 0x11;
    dpiListResponse[1] = deviceIndex;
    dpiListResponse[2] = 7;
    dpiListResponse[3] = 0x1A;
    dpiListResponse[4] = 0;
    dpiListResponse[5] = 0x01;
    dpiListResponse[6] = 0x90;
    dpiListResponse[7] = 0xE0;
    dpiListResponse[8] = 0x64;
    dpiListResponse[9] = 0x05;
    dpiListResponse[10] = 0x14;
    Assert(dpiListRequest.Take(5).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x1A, 0 }) &&
        LogitechHidppCapabilityReader.TryReadSensorDpiListResponse(dpiListResponse, deviceIndex, 7, 0,
            softwareId, out var dpiValues) && dpiValues.Count == 10 && dpiValues[0] == 400 &&
        dpiValues[5] == 900 && dpiValues[^1] == 1300,
        "se valida la lista DPI y se expande una etapa codificada como rango con paso");
    var setDpiRequest = LogitechHidppCapabilityReader.BuildSetSensorDpiRequest(deviceIndex, 7, 0, 1600, softwareId);
    var setDpiResponse = new byte[20];
    setDpiResponse[0] = 0x11;
    setDpiResponse[1] = deviceIndex;
    setDpiResponse[2] = 7;
    setDpiResponse[3] = 0x3A;
    Assert(setDpiRequest.Take(7).SequenceEqual(new byte[] { 0x11, deviceIndex, 7, 0x3A, 0, 0x06, 0x40 }) &&
        LogitechHidppCapabilityReader.TryReadSetSensorDpiResponse(setDpiResponse, deviceIndex, 7, softwareId),
        "el setter DPI construye su solicitud y acepta una confirmación HID++ sin eco del valor");
    setDpiResponse[3]++;
    Assert(!LogitechHidppCapabilityReader.TryReadSetSensorDpiResponse(setDpiResponse, deviceIndex, 7, softwareId),
        "se rechaza una confirmación del setter con función distinta");
    var batteryRequest = LogitechHidppCapabilityReader.BuildGetBatteryLevelStatusRequest(deviceIndex, 9, softwareId);
    var batteryResponse = new byte[20];
    batteryResponse[0] = 0x11;
    batteryResponse[1] = deviceIndex;
    batteryResponse[2] = 9;
    batteryResponse[3] = softwareId;
    batteryResponse[4] = 63;
    batteryResponse[5] = 50;
    Assert(batteryRequest.Take(4).SequenceEqual(new byte[] { 0x11, deviceIndex, 9, softwareId }) &&
        LogitechHidppCapabilityReader.TryReadBatteryLevelStatusResponse(batteryResponse, deviceIndex, 9,
            softwareId, out var battery) && battery is { Level: 63, NextLevel: 50, Status: 0 } &&
        !LogitechHidppCapabilityReader.TryReadBatteryLevelStatusResponse(batteryResponse, 2, 9,
            softwareId, out _) &&
        !LogitechHidppCapabilityReader.TryReadBatteryLevelStatusResponse(batteryResponse, deviceIndex, 9,
            0, out _),
        "se valida la lectura de batería HID++ con porcentaje, umbral siguiente y estado de carga");
    batteryResponse[4] = 101;
    Assert(!LogitechHidppCapabilityReader.TryReadBatteryLevelStatusResponse(batteryResponse, deviceIndex, 9,
        softwareId, out _), "se rechaza un porcentaje de batería fuera de 0–100");
    var firmwareCountRequest = LogitechHidppCapabilityReader.BuildGetFirmwareEntityCountRequest(0xFF, 12, softwareId);
    var firmwareCountResponse = new byte[20];
    firmwareCountResponse[0] = 0x11;
    firmwareCountResponse[1] = 0xFF;
    firmwareCountResponse[2] = 12;
    firmwareCountResponse[3] = softwareId;
    firmwareCountResponse[4] = 2;
    var firmwareInfoRequest = LogitechHidppCapabilityReader.BuildGetFirmwareInfoRequest(0xFF, 12, 1, softwareId);
    var firmwareInfoResponse = new byte[20];
    firmwareInfoResponse[0] = 0x11;
    firmwareInfoResponse[1] = 0xFF;
    firmwareInfoResponse[2] = 12;
    firmwareInfoResponse[3] = 0x1A;
    firmwareInfoResponse[4] = 0;
    firmwareInfoResponse[5] = (byte)'M';
    firmwareInfoResponse[6] = (byte)'P';
    firmwareInfoResponse[7] = (byte)'O';
    firmwareInfoResponse[8] = 0x15;
    firmwareInfoResponse[9] = 0x02;
    firmwareInfoResponse[10] = 0;
    firmwareInfoResponse[11] = 0x1A;
    Assert(firmwareCountRequest.Take(4).SequenceEqual(new byte[] { 0x11, 0xFF, 12, softwareId }) &&
        LogitechHidppCapabilityReader.TryReadFirmwareEntityCountResponse(firmwareCountResponse, 0xFF, 12,
            softwareId, out var firmwareCount) && firmwareCount == 2 &&
        firmwareInfoRequest.Take(5).SequenceEqual(new byte[] { 0x11, 0xFF, 12, 0x1A, 1 }) &&
        LogitechHidppCapabilityReader.TryReadFirmwareInfoResponse(firmwareInfoResponse, 0xFF, 12,
            softwareId, 1, out var firmware) && firmware is { Prefix: "MPO", Version: 0x1502, Build: 0x001A },
        "se valida el conteo y la lectura bruta de firmware HID++ en el índice del receptor");
    firmwareCountResponse[4] = 33;
    Assert(!LogitechHidppCapabilityReader.TryReadFirmwareEntityCountResponse(firmwareCountResponse, 0xFF, 12,
        softwareId, out _), "se rechaza un conteo de firmware fuera del límite admitido");
    firmwareInfoResponse[4] = 2;
    firmwareInfoResponse[5] = 0xA2;
    Assert(LogitechHidppCapabilityReader.TryReadFirmwareInfoResponse(firmwareInfoResponse, 0xFF, 12,
        softwareId, 1, out var hardwareFirmware) && hardwareFirmware is { FirmwareType: 2, HardwareVersion: 0xA2 },
        "se valida la variante de firmware de tipo hardware sin interpretarla como versión de software");
    var completeFeatures = new LogitechHidppSlotStatus(deviceIndex, 0x04, 0x05, true, true, true, false, true, 3);
    Assert(completeFeatures.MatchesStaticOmmFilter &&
        (completeFeatures with { AdjustableReportRateFeatureIndex = 0 }).MatchesStaticOmmFilter &&
        (completeFeatures with { HasFeature2201 = false, HasFeature2202 = true }).MatchesStaticOmmFilter &&
        !(completeFeatures with { HasFeature2201 = false, HasFeature2202 = false }).MatchesStaticOmmFilter &&
        !(completeFeatures with { ProtocolMajor = 0x8F }).MatchesStaticOmmFilter &&
        !(completeFeatures with { HidppDeviceType = null }).MatchesStaticOmmFilter,
        "el filtro requiere HID++ 2.0, las cuatro capacidades y una respuesta de tipo");
}

static void CheckLiveInventory()
{
    var endpoints = new WindowsAudioEndpointService().Enumerate();
    var devices = new DeviceDiscoveryService().Scan();
    var proWireless = devices.FirstOrDefault(device => device.VendorId == 0x046D && device.ProductId == 0xC539);
    if (proWireless is not null)
    {
        Assert(proWireless.ModelKey == "logitech-pro-wireless" && proWireless.Product == "PRO WIRELESS",
            "Windows identifica el receptor C539 por su nombre PnP PRO WIRELESS");
        if (proWireless.HidInterfaces is { Count: > 0 } hidInterfaces)
        {
            Assert(hidInterfaces.Any(item => item.UsagePage == 0xFF00 && item.Usage == 0x0001 &&
                item.InputReportBytes == 7 && item.OutputReportBytes == 7),
                "Windows lee el descriptor de la colección Logitech de 7 bytes");
            Assert(hidInterfaces.Any(item => item.UsagePage == 0xFF00 && item.Usage == 0x0002 &&
                item.InputReportBytes == 20 && item.OutputReportBytes == 20),
                "Windows lee el descriptor de la colección Logitech de 20 bytes");
        }
    }
    var c547Mouse = devices.FirstOrDefault(device => device.VendorId == 0x046D && device.ProductId == 0xC547 &&
        (device.HidInterfaces ?? []).Any(item => item.UsagePage == 0x0001 && item.Usage == 0x0002));
    if (c547Mouse is not null)
    {
        Assert(c547Mouse.ModelKey == "logitech-lightspeed-mouse" && c547Mouse.Kind == DeviceKind.Mouse && !c547Mouse.IsAuxiliary,
            "el ratón Logitech C547 aparece en el inventario principal aunque no informe el modelo");
        Assert(c547Mouse.ModelKey != "logitech-pro-x-superlight",
            "el C547 no se atribuye al SUPERLIGHT sin confirmar su identidad por HID++");
    }
    Console.WriteLine($"Audio Windows: {endpoints.Count} endpoint(s) · Yeti={endpoints.Any(item => item.Name.Contains("Yeti Classic", StringComparison.OrdinalIgnoreCase))} · ASTRO={endpoints.Any(item => item.Name.Contains("Astro A50", StringComparison.OrdinalIgnoreCase))}");
    Console.WriteLine("Dispositivos físicos: " + string.Join(" | ", devices.Select(item => $"{item.Product} [{item.Kind}] aux={item.IsAuxiliary} audio={item.AudioEndpoints?.Count ?? 0}")));
    if (proWireless is not null)
        Console.WriteLine("Interfaces PRO WIRELESS: " + string.Join(" | ", (proWireless.HidInterfaces ?? [])
            .Select(item => $"{item.UsagePage:X4}/{item.Usage:X4} IN={item.InputReportBytes} OUT={item.OutputReportBytes} FEATURE={item.FeatureReportBytes}")));
}

static async Task ProbeLogitechAsync()
{
    var candidates = new DeviceDiscoveryService().Scan()
        .Where(device => device.VendorId == 0x046D && (device.HidInterfaces ?? []).Any(item =>
            item.UsagePage == 0xFF00 && item.Usage == 0x0002 &&
            item.InputReportBytes == 20 && item.OutputReportBytes == 20))
        .ToArray();
    if (candidates.Length == 0)
    {
        Console.WriteLine("No se encontraron receptores Logitech con colección HID++ larga de 20 bytes.");
        return;
    }

    var reader = new LogitechHidppCapabilityReader();
    foreach (var device in candidates)
    {
        Console.WriteLine($"{device.Product} PID={device.ProductId:X4}: consulta HID++ de solo lectura");
        try
        {
            var identities = await reader.ReadDeviceIdentitiesAsync(device);
            foreach (var identity in identities)
            {
                var model = LogitechHidppCapabilityReader.ModelKeyFromDeviceName(identity.DeviceName) ?? "no mapeado";
                Console.WriteLine($"  ranura {identity.DeviceIndex}: nombre «{identity.DeviceName}» · tipo {(identity.DeviceType is byte type ? $"0x{type:X2}" : "sin respuesta")} · HID++ {identity.ProtocolMajor}.{identity.ProtocolMinor} · modelo {model}" +
                    (identity.Firmware.Count > 0 ? $" · firmware: {string.Join(", ", identity.Firmware.Select(firmware => firmware.Summary))}" : string.Empty));
            }
            if (identities.Count == 0) Console.WriteLine("  ninguna ranura respondió con protocolo y tipo de dispositivo");
            var supportedMice = identities.Where(identity => identity.DeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType)
                .Select(identity => (Identity: identity, ModelKey: LogitechHidppCapabilityReader.ModelKeyFromDeviceName(identity.DeviceName)))
                .Where(item => item.ModelKey is not null).ToArray();
            if (supportedMice.Length == 1)
            {
                var (identity, modelKey) = supportedMice[0];
                var expectedProductId = modelKey == "logitech-pro-wireless" ? 0xC539 : 0xC547;
                if (device.ProductId == expectedProductId)
                {
                    var identifiedDevice = device with
                    {
                        Product = identity.DeviceName,
                        ModelKey = modelKey!,
                        Kind = DeviceKind.Mouse,
                        IsAuxiliary = false
                    };
                    var slots = await reader.ReadOmmGateAsync(identifiedDevice);
                    foreach (var slot in slots)
                    {
                        var memory = slot.OnboardProfilesInfo is { } info
                            ? $"formato {info.ProfileFormatId} · {info.ProfileCount} perfiles · {info.SectorCount} sectores de {info.SectorSize} bytes"
                            : "sin respuesta";
                        Console.WriteLine($"  ranura {slot.DeviceIndex}: filtro OMM estático={slot.MatchesStaticOmmFilter} · DPI={slot.CurrentDpi?.CurrentDpi.ToString() ?? "?"} · polling={slot.CurrentReportRateMs?.ToString() ?? "?"} ms · memoria integrada={memory} · batería={slot.Battery?.Level?.ToString() ?? "?"}% · firmware={string.Join(", ", slot.Firmware.Select(firmware => firmware.Summary))} · firmware receptor={string.Join(", ", slot.ReceiverFirmware.Select(firmware => firmware.Summary))}");
                    }
                    if (slots.Count == 0) Console.WriteLine("  la identidad coincidió, pero las consultas de funciones no respondieron");
                }
                else Console.WriteLine("  el nombre identificado no coincide con el PID del receptor; no se consultan funciones de modelo");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  error de lectura: {exception.Message}");
        }
    }
}

static async Task ProbeLogitechProfileMemoryAsync()
{
    var candidates = new DeviceDiscoveryService().Scan()
        .Where(device => device.VendorId == 0x046D && device.ProductId is 0xC539 or 0xC547 &&
            (device.HidInterfaces ?? []).Any(item => item.UsagePage == 0xFF00 && item.Usage == 0x0002 &&
                item.InputReportBytes == 20 && item.OutputReportBytes == 20))
        .ToArray();
    if (candidates.Length == 0)
    {
        Console.WriteLine("No se encontró ningún ratón Logitech compatible conectado.");
        return;
    }

    var reader = new LogitechHidppCapabilityReader();
    foreach (var device in candidates)
    {
        try
        {
            var identity = (await reader.ReadDeviceIdentitiesAsync(device)).FirstOrDefault(item =>
                item.DeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType &&
                LogitechHidppCapabilityReader.ModelKeyFromDeviceName(item.DeviceName) is not null);
            if (identity is null)
            {
                Console.WriteLine($"{device.Product}: el ratón no respondió con un modelo confirmado.");
                continue;
            }

            var modelKey = LogitechHidppCapabilityReader.ModelKeyFromDeviceName(identity.DeviceName)!;
            var identifiedDevice = device with
            {
                Product = identity.DeviceName,
                ModelKey = modelKey,
                Kind = DeviceKind.Mouse,
                IsAuxiliary = false
            };
            var profile = await reader.ReadOnboardProfilesAsync(identifiedDevice);
            var profiles = string.Join(" | ", profile.Profiles.Select(stored =>
            {
                var stages = string.Join(", ", stored.DpiStages.Select((dpi, index) => dpi is int value
                    ? $"{value:N0}{(index == stored.DefaultDpiIndex ? "*" : string.Empty)}"
                    : "vacía"));
                return $"sector {stored.Sector}{(profile.ActiveSector == stored.Sector ? " activo" : stored.IsEnabled ? " habilitado" : string.Empty)}: {stages} DPI";
            }));
            Console.WriteLine($"{identity.DeviceName} · modo {(profile.Mode == 1 ? "integrado" : "G HUB")} · formato {profile.Info.ProfileFormatId} · " +
                $"perfiles: {profiles} · DPI en vivo: {profile.LiveDpi?.ToString("N0") ?? "sin respuesta"} · " +
                "checksums verificados · solo lectura (* = predeterminado)");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"No se pudo leer la memoria integrada: {exception.Message}");
        }
    }
}

static async Task SaveLogitechOnboardDpiAsync()
{
    var candidates = new DeviceDiscoveryService().Scan()
        .Where(device => device.VendorId == 0x046D && device.ProductId is 0xC539 or 0xC547 &&
            (device.HidInterfaces ?? []).Any(item => item.UsagePage == 0xFF00 && item.Usage == 0x0002 &&
                item.InputReportBytes == 20 && item.OutputReportBytes == 20))
        .ToArray();
    var reader = new LogitechHidppCapabilityReader();
    foreach (var device in candidates)
    {
        try
        {
            var identity = (await reader.ReadDeviceIdentitiesAsync(device)).FirstOrDefault(item =>
                item.DeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType &&
                LogitechHidppCapabilityReader.ModelKeyFromDeviceName(item.DeviceName) is not null);
            if (identity is null) continue;
            var modelKey = LogitechHidppCapabilityReader.ModelKeyFromDeviceName(identity.DeviceName)!;
            var identified = device with
            {
                Product = identity.DeviceName, ModelKey = modelKey,
                Kind = DeviceKind.Mouse, IsAuxiliary = false
            };
            var profile = new DeviceLocalProfileLibrary().Load(modelKey);
            var stages = new[] { profile.Dpi1, profile.Dpi2, profile.Dpi3, profile.Dpi4, profile.Dpi5 }
                .Take(Math.Clamp(profile.DpiStageCount, 1, 5)).ToArray();
            var defaultIndex = (byte)(Math.Clamp(profile.ActiveDpiStage, 1, stages.Length) - 1);
            var before = (await reader.ReadOnboardProfilesAsync(identified)).LiveDpi;
            if (before is int currentDpi && Array.IndexOf(stages, currentDpi) is var matchingStage && matchingStage >= 0)
                defaultIndex = (byte)matchingStage;
            Console.WriteLine($"{identity.DeviceName}: guardando {string.Join(", ", stages)} DPI; se conserva {stages[defaultIndex]} DPI…");
            var result = await reader.SaveOnboardDpiStagesAsync(identified, stages, defaultIndex);
            var live = await reader.SetCurrentDpiAsync(identified, before ?? stages[defaultIndex]);
            Console.WriteLine($"Confirmado por lectura: sector {result.Sector}, {string.Join(", ", result.DpiStages)} DPI, predeterminado {stages[result.DefaultDpiIndex]} DPI; sensor actual {live} DPI.");
            return;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"No se pudo completar el guardado: {exception.Message}");
            return;
        }
    }
    Console.WriteLine("No se encontró un ratón Logitech compatible conectado y confirmado; no se escribió nada.");
}

static async Task ProbeLogitechPairingAsync()
{
    var candidates = new DeviceDiscoveryService().Scan()
        .Where(device => device.VendorId == 0x046D && device.ProductId is 0xC539 or 0xC547)
        .ToArray();
    var reader = new LogitechHidppCapabilityReader();
    foreach (var device in candidates)
    {
        try
        {
            var pairing = await reader.ReadReceiverPairingInfoAsync(device);
            var model = pairing is { DeviceKind: LogitechHidppCapabilityReader.HidppMouseDeviceType }
                ? LogitechHidppCapabilityReader.ModelFromWirelessProductId(pairing.WirelessProductId)
                : null;
            Console.WriteLine(model is null
                ? $"{device.Product} PID={device.ProductId:X4}: el receptor no confirmó un ratón compatible emparejado."
                : $"{device.Product} PID={device.ProductId:X4}: {model.Value.DeviceName} confirmado por WPID {pairing!.WirelessProductId:X4}.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{device.Product} PID={device.ProductId:X4}: lectura del receptor fallida · {exception.Message}");
        }
    }
}

static async Task TestLogitechDpiRoundTripAsync()
{
    var device = new DeviceDiscoveryService().Scan().FirstOrDefault(item =>
        item.VendorId == 0x046D && item.ProductId == 0xC539);
    if (device is null)
    {
        Console.WriteLine("No se encontró el receptor PRO WIRELESS C539.");
        return;
    }

    var reader = new LogitechHidppCapabilityReader();
    var identities = await reader.ReadDeviceIdentitiesAsync(device);
    var identity = identities.SingleOrDefault(item =>
        item.DeviceType == LogitechHidppCapabilityReader.HidppMouseDeviceType &&
        LogitechHidppCapabilityReader.ModelKeyFromDeviceName(item.DeviceName) == "logitech-pro-wireless");
    if (identity is null)
    {
        Console.WriteLine("El PRO WIRELESS no respondió con identidad confirmada; no se cambió el DPI.");
        return;
    }

    var identifiedDevice = device with
    {
        Product = identity.DeviceName,
        ModelKey = "logitech-pro-wireless",
        Kind = DeviceKind.Mouse,
        IsAuxiliary = false
    };
    var before = (await reader.ReadOmmGateAsync(identifiedDevice))
        .SingleOrDefault(slot => slot.DeviceIndex == identity.DeviceIndex)?.CurrentDpi?.CurrentDpi;
    if (before is null)
    {
        Console.WriteLine("El PRO WIRELESS no devolvió un DPI actual; no se cambió el DPI.");
        return;
    }

    var trial = before == 800 ? 1600 : 800;
    try
    {
        var applied = await reader.SetCurrentDpiAsync(identifiedDevice, trial);
        Console.WriteLine($"DPI de prueba aplicado y leído de vuelta: {applied}.");
        var restored = await reader.SetCurrentDpiAsync(identifiedDevice, before.Value);
        Console.WriteLine($"DPI original restaurado y leído de vuelta: {restored}.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Prueba DPI detenida: {exception.Message}");
    }
}

static void CheckMacroBindingLabels()
{
    var buttonFour = MacroBindingOption.All.Single(option => option.Trigger == MacroTrigger.XButton1);
    Assert(buttonFour.ToString() == "Botón lateral 4" && MacroBindingOption.All.Count == 38 &&
        MacroBindingOption.All.Where(option => option.Trigger != MacroTrigger.None).Select(option => option.Trigger).Distinct().Count() == 37 &&
        MacroBindingOption.All.Single(option => option.Trigger == MacroTrigger.XboxLeftStickUpRight).Label == "Mando · Stick izq. ↗",
        "el selector conserva el ratón y ofrece una opción única para botones y direcciones XInput");
    Assert(MouseMacroTrigger.TryMapButtonMessage(0x0201, 0, out var left) && left == MacroTrigger.LeftButton &&
        MouseMacroTrigger.TryMapButtonMessage(0x0205, 0, out var right) && right == MacroTrigger.RightButton &&
        MouseMacroTrigger.TryMapButtonMessage(0x0208, 0, out var middle) && middle == MacroTrigger.MiddleButton &&
        MouseMacroTrigger.TryMapButtonMessage(0x020C, 1u << 16, out var back) && back == MacroTrigger.XButton1 &&
        MouseMacroTrigger.TryMapButtonMessage(0x020B, 2u << 16, out var forward) && forward == MacroTrigger.XButton2 &&
        !MouseMacroTrigger.TryMapButtonMessage(0x020B, 3u << 16, out _),
        "se asocian mensajes Windows de clic con el botón y rechazan códigos XButton desconocidos");
}

static void CheckXboxMacroInputEdges()
{
    var centeredPreview = XboxInputViewModel.GetStickPreviewOffset(0, 0);
    var upPreview = XboxInputViewModel.GetStickPreviewOffset(0, short.MaxValue);
    var diagonalPreview = XboxInputViewModel.GetStickPreviewOffset(short.MaxValue, short.MaxValue);
    Assert(centeredPreview == (0, 0) && upPreview == (0, -350) &&
        Math.Abs(Math.Sqrt(diagonalPreview.X * diagonalPreview.X + diagonalPreview.Y * diagonalPreview.Y) - 350) < 0.1,
        "la previsualización mueve el stick desde el centro y limita las diagonales al radio visible");

    var directions = new (short X, short Y, MacroTrigger Left, MacroTrigger Right)[]
    {
        (0, 32767, MacroTrigger.XboxLeftStickUp, MacroTrigger.XboxRightStickUp),
        (25000, 25000, MacroTrigger.XboxLeftStickUpRight, MacroTrigger.XboxRightStickUpRight),
        (32767, 0, MacroTrigger.XboxLeftStickRight, MacroTrigger.XboxRightStickRight),
        (25000, -25000, MacroTrigger.XboxLeftStickDownRight, MacroTrigger.XboxRightStickDownRight),
        (0, -32768, MacroTrigger.XboxLeftStickDown, MacroTrigger.XboxRightStickDown),
        (-25000, -25000, MacroTrigger.XboxLeftStickDownLeft, MacroTrigger.XboxRightStickDownLeft),
        (-32768, 0, MacroTrigger.XboxLeftStickLeft, MacroTrigger.XboxRightStickLeft),
        (-25000, 25000, MacroTrigger.XboxLeftStickUpLeft, MacroTrigger.XboxRightStickUpLeft)
    };
    foreach (var direction in directions)
    {
        var left = XboxMacroInputTracker.GetPressedTriggers(new(0, 0, 0, direction.X, direction.Y, 0, 0));
        var right = XboxMacroInputTracker.GetPressedTriggers(new(0, 0, 0, 0, 0, direction.X, direction.Y));
        Assert(left.SetEquals([direction.Left]) && right.SetEquals([direction.Right]),
            $"se detecta la dirección {direction.Left} y {direction.Right}");
    }

    var controls = XboxMacroInputTracker.GetPressedTriggers(new(0x1000, 128, 127, 0, 0, 0, 0));
    Assert(controls.SetEquals([MacroTrigger.XboxA, MacroTrigger.XboxLeftTrigger]),
        "el perfil predeterminado conserva el umbral original de los gatillos XInput");
    var triggerRange = XboxMacroInputTracker.GetPressedTriggers(new(0, 153, 127, 0, 0, 0, 0),
        leftTriggerStart: 2, leftTriggerEnd: 10, rightTriggerStart: 0, rightTriggerEnd: 10);
    Assert(triggerRange.SetEquals([MacroTrigger.XboxLeftTrigger]),
        "el perfil del mando ajusta el umbral LT de sus macros y mantiene el de RT");
    var narrowTriggerRange = XboxMacroInputTracker.GetPressedTriggers(new(0, 102, 101, 0, 0, 0, 0),
        leftTriggerStart: 0, leftTriggerEnd: 8, rightTriggerStart: 0, rightTriggerEnd: 10);
    Assert(narrowTriggerRange.Contains(MacroTrigger.XboxLeftTrigger) && !narrowTriggerRange.Contains(MacroTrigger.XboxRightTrigger),
        "el margen final del gatillo también modifica el disparo local de macros");

    var belowStickThreshold = XboxMacroInputTracker.GetPressedTriggers(new(0, 0, 0, 15_999, 0, 0, 0));
    var atStickThreshold = XboxMacroInputTracker.GetPressedTriggers(new(0, 0, 0, 16_000, 0, 0, 0));
    var relaxedStickThreshold = XboxMacroInputTracker.GetPressedTriggers(new(0, 0, 0, 8_000, 0, 0, 0),
        leftStickMacroThresholdPercent: 20);
    var strictStickThreshold = XboxMacroInputTracker.GetPressedTriggers(new(0, 0, 0, 20_000, 0, 0, 0),
        leftStickMacroThresholdPercent: 70);
    Assert(!belowStickThreshold.Contains(MacroTrigger.XboxLeftStickRight) &&
        atStickThreshold.Contains(MacroTrigger.XboxLeftStickRight) &&
        relaxedStickThreshold.Contains(MacroTrigger.XboxLeftStickRight) &&
        !strictStickThreshold.Contains(MacroTrigger.XboxLeftStickRight),
        "el umbral del stick ajusta cuándo se dispara una macro y conserva el valor predeterminado");
    var localProfile = new DeviceLocalProfile { LeftStickMacroThresholdPercent = 5, RightStickMacroThresholdPercent = 95 };
    Assert(localProfile.LeftStickMacroThresholdPercent == 10 && localProfile.RightStickMacroThresholdPercent == 90,
        "los límites de sensibilidad de macros se recortan al rango permitido");

    var tracker = new XboxMacroInputTracker();
    var upState = new XboxMacroInputState(0, 0, 0, 0, 32767, 0, 0);
    var down = tracker.Update(upState);
    var repeated = tracker.Update(upState);
    var turned = tracker.Update(upState with { LeftStickY = 0, LeftStickX = 32767 });
    var released = tracker.Update(null);
    var pressedAgain = tracker.Update(upState);
    Assert(down.Count == 1 && down[0] == new XboxMacroInputTransition(MacroTrigger.XboxLeftStickUp, true) &&
        repeated.Count == 0 && turned.SequenceEqual([
            new XboxMacroInputTransition(MacroTrigger.XboxLeftStickUp, false),
            new XboxMacroInputTransition(MacroTrigger.XboxLeftStickRight, true)]) &&
        released.Count == 1 && !released[0].IsDown && pressedAgain.Count == 1 && pressedAgain[0].IsDown,
        "la macro solo se activa en el flanco de entrada; mantener no repite y soltar rearma el disparo");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"Self-check failed: {message}");
}
