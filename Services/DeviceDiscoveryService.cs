using System.Runtime.InteropServices;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class DeviceDiscoveryService
{
    private readonly HidDeviceEnumerator _hid = new();
    private readonly WindowsAudioEndpointService _audio = new();

    public IReadOnlyList<HardwareDevice> Scan()
    {
        var devices = _hid.Enumerate().ToList();
        devices.RemoveAll(device => device.IsAuxiliary && string.IsNullOrWhiteSpace(device.Product));
        AddKnownAudioDevices(devices, _audio.Enumerate());

        foreach (var slot in FindXboxControllers())
        {
            var index = devices.FindIndex(device => device.VendorId == 0x045E && device.Kind == DeviceKind.Gamepad);
            if (index >= 0)
            {
                devices[index] = devices[index] with
                {
                    Transport = devices[index].Transport.Contains("XInput", StringComparison.OrdinalIgnoreCase)
                        ? devices[index].Transport : $"{devices[index].Transport} + XInput",
                    Detail = $"{devices[index].Detail}{Environment.NewLine}XInput slot {slot + 1}",
                    XInputSlot = slot
                };
            }
            else
            {
                devices.Add(new HardwareDevice($"XINPUT-{slot}", "Microsoft", "Mando Xbox",
                    0x045E, 0, "XInput", 0, $"Mando activo en el slot {slot + 1}.", slot,
                    "xbox-controller", DeviceKind.Gamepad, IdentificationNote: "XInput lo detecta; modelo USB no disponible."));
            }
        }

        devices = MergeAjazzTransports(devices);
        return devices.OrderBy(device => device.IsAuxiliary).ThenBy(device => device.Manufacturer)
            .ThenBy(device => device.Product).ToArray();
    }

    private static List<HardwareDevice> MergeAjazzTransports(List<HardwareDevice> devices)
    {
        var matches = devices.Where(device => device.ModelKey == "ajazz-ak820").ToArray();
        if (matches.Length < 2) return devices;
        var merged = matches[0] with
        {
            Id = "DEVICE-ajazz-ak820",
            Transport = string.Join(" + ", matches.Select(device => device.Transport).Distinct()),
            InterfaceCount = matches.Sum(device => device.InterfaceCount),
            Detail = string.Join(Environment.NewLine, matches.Select(device => device.Detail).Where(value => value.Length > 0)),
            IdentificationNote = string.Join(" ", matches.Select(device => device.IdentificationNote).Distinct())
        };
        devices.RemoveAll(device => device.ModelKey == "ajazz-ak820");
        devices.Add(merged);
        return devices;
    }

    private static void AddKnownAudioDevices(List<HardwareDevice> devices, IReadOnlyList<AudioEndpointInfo> endpoints)
    {
        var known = endpoints.Select(endpoint => (Endpoint: endpoint, ModelKey: AudioModel(endpoint.Name)))
            .Where(item => item.ModelKey.Length > 0 && !item.Endpoint.Name.StartsWith("Línea ", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.ModelKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in known)
        {
            var audio = group.Select(item => item.Endpoint).ToArray();
            var existing = devices.FindIndex(device => device.ModelKey.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                devices[existing] = devices[existing] with { AudioEndpoints = audio };
                continue;
            }

            var yeti = group.Key == "blue-yeti-classic";
            devices.Add(new HardwareDevice(
                $"AUDIO-{group.Key}",
                yeti ? "Blue Microphones" : "ASTRO Gaming",
                yeti ? "Yeti Classic" : "ASTRO A50",
                yeti ? (ushort)0xB58E : (ushort)0x9886,
                yeti ? (ushort)0x9E84 : (ushort)0x0015,
                "USB Audio",
                0,
                string.Join(Environment.NewLine, audio.Select(endpoint => endpoint.Name)),
                ModelKey: group.Key,
                Kind: yeti ? DeviceKind.Microphone : DeviceKind.Headset,
                IdentificationNote: "Nombre de los endpoints de audio activos de Windows.",
                AudioEndpoints: audio));
        }
    }

    private static string AudioModel(string name)
    {
        if (name.Contains("Yeti Classic", StringComparison.OrdinalIgnoreCase)) return "blue-yeti-classic";
        if (name.Contains("Astro A50", StringComparison.OrdinalIgnoreCase)) return "astro-a50";
        return string.Empty;
    }

    private static IReadOnlyList<int> FindXboxControllers()
    {
        var connected = new List<int>();
        for (var slot = 0; slot < 4; slot++)
        {
            try
            {
                if (XInputGetState((uint)slot, out _) == 0) connected.Add(slot);
            }
            catch (DllNotFoundException) { break; }
            catch (EntryPointNotFoundException) { break; }
        }
        return connected;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }
}
