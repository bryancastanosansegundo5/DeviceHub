using System.Collections.ObjectModel;
using System.IO;
using NexoHub.Adapters;
using NexoHub.Models;
using NexoHub.Services;

var testRoot = Path.Combine(AppContext.BaseDirectory, "self-check-data");
if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
Directory.CreateDirectory(testRoot);
try
{
    CheckProfileRoundTrip(testRoot);
    CheckMacroRoundTrip(testRoot);
    CheckDeviceProfileRoundTrip(testRoot);
    CheckAdapterSelection();
    CheckMacroBindingLabels();
    CheckLiveInventory();
    Console.WriteLine("NexoHub self-check: 6 grupos correctos.");
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
    var macro = new MacroDefinition { Name = "Secuencia", Binding = MacroTrigger.XButton1 };
    macro.Steps = new ObservableCollection<MacroStep>
    {
        new() { InputType = MacroInputType.Key, VirtualKey = 0x41, IsDown = true },
        new() { InputType = MacroInputType.Key, VirtualKey = 0x41, IsDown = false, DelayMs = 18 },
        new() { InputType = MacroInputType.MouseButton, MouseButton = MacroMouseButton.Left, IsDown = true },
        new() { InputType = MacroInputType.MouseButton, MouseButton = MacroMouseButton.Left, IsDown = false },
        new() { InputType = MacroInputType.MouseWheel, WheelDelta = 120, IsDown = true }
    };
    library.Save([macro]);
    var loaded = library.Load().Single();
    Assert(loaded.Steps.Count == 5 && loaded.Binding == MacroTrigger.XButton1, "se conserva la secuencia y su asignación");

    var invalid = new MacroDefinition { Name = "Tecla sin soltar" };
    invalid.Steps.Add(new MacroStep { VirtualKey = 0x41, IsDown = true });
    var rejected = false;
    try { library.Save([invalid]); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "se rechazan macros que dejan una tecla pulsada");
}

static void CheckDeviceProfileRoundTrip(string root)
{
    var library = new DeviceLocalProfileLibrary(Path.Combine(root, "device-profiles"));
    var profile = new DeviceLocalProfile { Name = "Prueba", Dpi3 = 1450, ReportRate = 500, LightingColor = "FF8844" };
    profile.KeyboardKeyColors["0x04"] = "00AAFF";
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

static void CheckAdapterSelection()
{
    var registry = new DeviceAdapterRegistry();
    var astro = registry.Describe(new HardwareDevice("astro", "Astro Gaming", "Astro A50", 0x9886, 0x0015, "USB/HID", 2, ""));
    var logitech = registry.Describe(new HardwareDevice("logi", "Logitech", "G device", 0x046D, 0xC547, "USB/HID", 1, ""));
    var xbox = registry.Describe(new HardwareDevice("xbox", "Microsoft", "Xbox controller", 0x045E, 0, "XInput", 0, "", 0));
    var yeti = registry.Describe(new HardwareDevice("yeti", "Blue Microphones", "Yeti Classic", 0xB58E, 0x9E84,
        "USB Audio", 0, "", ModelKey: "blue-yeti-classic", Kind: DeviceKind.Microphone));
    var keyboard = registry.Describe(new HardwareDevice("ak820", "AJAZZ", "AK820", 0x0C45, 0x8006,
        "USB/HID", 1, "", ModelKey: "ajazz-ak820", Kind: DeviceKind.Keyboard));
    Assert(astro.Brand == "ASTRO" && logitech.Brand == "LOGITECH G" && xbox.Brand == "XBOX" &&
        yeti.Brand == "BLUE" && keyboard.Brand == "AJAZZ",
        "los modelos piloto seleccionan su adaptador correspondiente");
}

static void CheckLiveInventory()
{
    var endpoints = new WindowsAudioEndpointService().Enumerate();
    var devices = new DeviceDiscoveryService().Scan();
    Console.WriteLine($"Audio Windows: {endpoints.Count} endpoint(s) · Yeti={endpoints.Any(item => item.Name.Contains("Yeti Classic", StringComparison.OrdinalIgnoreCase))} · ASTRO={endpoints.Any(item => item.Name.Contains("Astro A50", StringComparison.OrdinalIgnoreCase))}");
    Console.WriteLine("Dispositivos físicos: " + string.Join(" | ", devices.Select(item => $"{item.Product} [{item.Kind}] aux={item.IsAuxiliary} audio={item.AudioEndpoints?.Count ?? 0}")));
}

static void CheckMacroBindingLabels()
{
    var buttonFour = MacroBindingOption.All.Single(option => option.Trigger == MacroTrigger.XButton1);
    Assert(buttonFour.ToString() == "Botón lateral 4", "el selector de asignación muestra el nombre legible del botón");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"Self-check failed: {message}");
}
