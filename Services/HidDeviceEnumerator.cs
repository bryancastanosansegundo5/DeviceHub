using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NexoHub.Models;

namespace NexoHub.Services;

/// <summary>Lists physical HID devices, grouping every collection beneath its USB/Bluetooth parent.</summary>
public sealed class HidDeviceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint OpenExisting = 3;
    private const uint FileShareReadWrite = 3;
    private static Guid HidInterfaceGuid;
    private static readonly IntPtr InvalidHandle = new(-1);

    static HidDeviceEnumerator() => HidD_GetHidGuid(out HidInterfaceGuid);

    public IReadOnlyList<HardwareDevice> Enumerate()
    {
        var groups = new Dictionary<string, List<Endpoint>>(StringComparer.OrdinalIgnoreCase);
        var set = SetupDiGetClassDevs(ref HidInterfaceGuid, IntPtr.Zero, IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandle) return [];

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new DeviceInterfaceData { Size = (uint)Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref HidInterfaceGuid, index, ref interfaceData))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    continue;
                }

                var endpoint = ReadEndpoint(set, ref interfaceData);
                if (endpoint is null) continue;
                var key = endpoint.ContainerId.Length > 0 ? endpoint.ContainerId : endpoint.RootInstanceId;
                if (key.Length == 0) key = $"{endpoint.VendorId:X4}:{endpoint.ProductId:X4}:{endpoint.Product}";
                if (!groups.TryGetValue(key, out var endpoints)) groups[key] = endpoints = [];
                endpoints.Add(endpoint);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }

        return groups.Values.Select(BuildDevice).ToArray();
    }

    private static HardwareDevice BuildDevice(List<Endpoint> endpoints)
    {
        var root = endpoints.FirstOrDefault(endpoint => endpoint.RootInstanceId.Length > 0) ?? endpoints[0];
        var identity = ResolveIdentity(root, endpoints);
        var hidPaths = endpoints.Select(endpoint => endpoint.Path).Distinct().ToArray();
        return new HardwareDevice(
            $"PNP-{Sanitize(identity.ModelKey.Length > 0 ? identity.ModelKey : root.RootInstanceId)}",
            identity.Manufacturer, identity.Name, identity.VendorId, identity.ProductId,
            root.RootInstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? "Bluetooth / HID" : "USB / HID",
            hidPaths.Length, string.Join(Environment.NewLine, hidPaths),
            ModelKey: identity.ModelKey, Kind: identity.Kind, InstanceId: root.RootInstanceId,
            ContainerId: root.ContainerId, IsAuxiliary: identity.IsAuxiliary, IdentificationNote: identity.Note,
            HidInterfaces: endpoints.Select(endpoint => endpoint.HidInterface).OfType<HidInterfaceInfo>().Distinct().ToArray());
    }

    private static Identity ResolveIdentity(Endpoint root, IReadOnlyList<Endpoint> endpoints)
    {
        var vendor = root.VendorId;
        var product = root.ProductId;
        var text = string.Join(" ", endpoints.SelectMany(endpoint => new[]
            { endpoint.Product, endpoint.RootFriendlyName, endpoint.RootDescription, endpoint.Manufacturer }));

        if (vendor == 0x045E && product == 0x02E3)
            return new("xbox-one-elite", "Microsoft", "Xbox One Elite", vendor, product, DeviceKind.Gamepad, false, "Windows identifica el dispositivo como Mando Xbox One Elite.");
        if (vendor == 0x045E && product == 0x02FF)
            return new("xbox-elite-family", "Microsoft", "Mando Xbox Elite", vendor, product, DeviceKind.Gamepad, false, "Colección XInput del mando; el ID del padre físico no quedó enlazado.");
        if (vendor == 0x9886 && product == 0x0015)
            return new("astro-a50", "ASTRO Gaming", "ASTRO A50", vendor, product, DeviceKind.Headset, false, "VID/PID del Astro Command Center; audio USB disponible en Windows.");
        if ((vendor == 0xB58E && product == 0x9E84) || text.Contains("Yeti Classic", StringComparison.OrdinalIgnoreCase))
            return new("blue-yeti-classic", "Blue Microphones", "Yeti Classic", vendor, product, DeviceKind.Microphone, false, "Nombre de Windows y firma yeti_classic en G HUB.");
        if (vendor == 0x046D && product == 0xC539 && text.Contains("PRO WIRELESS", StringComparison.OrdinalIgnoreCase))
            return new("logitech-pro-wireless", "Logitech G", "PRO WIRELESS", vendor, product, DeviceKind.Mouse, false, "Windows identifica el receptor como PRO WIRELESS; la captura de G HUB confirma el nombre del modelo.");
        if (vendor == 0x046D && product == 0xC52B)
            return new("logitech-usb-receiver", "Logitech", "Receptor Logitech USB", vendor, product, DeviceKind.Receiver, true, "Windows registra Logitech USB Input Device; no se identificó el periférico emparejado.");
        if ((vendor == 0x0C45 && product == 0x8006) || text.Contains("AK820", StringComparison.OrdinalIgnoreCase))
            return new("ajazz-ak820", "AJAZZ", "AK820 · variante Pro probable", vendor, product, DeviceKind.Keyboard, false, "Windows anuncia AK820 por Bluetooth; el paquete local AK820 Pro declara PID 8009, el USB conectado informa 8006.");
        if (vendor == 0x0C45 && product == 0xFDFD)
            return new("ajazz-24g-receiver", "AJAZZ", "Receptor AJAZZ 2.4 GHz", vendor, product, DeviceKind.Receiver, true, "Interfaz del receptor; no es el teclado configurable.");
        if (vendor == 0x0B05 && product == 0x19AF)
            return new("internal-lighting", "ASUS", "Controlador interno de iluminación", vendor, product, DeviceKind.Lighting, true, "Componente auxiliar de iluminación del PC.");

        var rootName = FirstUseful(root.RootFriendlyName, root.RootDescription, root.Product);
        var utility = !IsPhysicalRoot(root.RootInstanceId) ||
            ContainsAny(rootName, "USB Input Device", "USB Receiver", "Receptor", "Dispositivo de entrada USB", "Dispositivo compuesto USB", "HID Device", "HID Keyboard Device", "Keyboard Device", "@keyboard.inf", "G HUB Virtual Keyboard");
        var kind = InferKind(endpoints);
        var manufacturer = FirstUseful(root.RootManufacturer, root.Manufacturer, ManufacturerFor(vendor));
        return new($"hid-{vendor:X4}-{product:X4}", manufacturer,
            utility ? rootName : FirstUseful(rootName, "Dispositivo HID"), vendor, product,
            kind, utility || kind == DeviceKind.Lighting,
            utility ? "Interfaz o receptor auxiliar; disponible en Otros." : "Identificación HID sin adaptador específico.");
    }

    private static DeviceKind InferKind(IEnumerable<Endpoint> endpoints)
    {
        var paths = string.Join(" ", endpoints.Select(endpoint => endpoint.Path));
        if (paths.Contains("UP:0001_U:0002", StringComparison.OrdinalIgnoreCase)) return DeviceKind.Mouse;
        if (paths.Contains("UP:0001_U:0006", StringComparison.OrdinalIgnoreCase)) return DeviceKind.Keyboard;
        if (paths.Contains("UP:0001_U:0005", StringComparison.OrdinalIgnoreCase)) return DeviceKind.Gamepad;
        return DeviceKind.Generic;
    }

    private static Endpoint? ReadEndpoint(IntPtr set, ref DeviceInterfaceData interfaceData)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required == 0) return null;
        var detail = Marshal.AllocHGlobal((int)required);
        var deviceInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceInfoData>());
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            Marshal.StructureToPtr(new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() }, deviceInfoPointer, false);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detail, required, out _, deviceInfoPointer)) return null;
            var info = Marshal.PtrToStructure<DeviceInfoData>(deviceInfoPointer);
            var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? string.Empty;
            var ids = ParseIds(path);
            var root = FindPhysicalParent(info.DevInst, GetDeviceId(info.DevInst));
            var rootIds = ParseIds(root.InstanceId);
            if (rootIds.Vendor != 0) ids = rootIds;

            using var handle = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            var manufacturer = string.Empty;
            var product = string.Empty;
            if (!handle.IsInvalid)
            {
                var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                if (HidD_GetAttributes(handle, ref attributes) && rootIds.Vendor == 0)
                    ids = (attributes.VendorId, attributes.ProductId);
                manufacturer = ReadString(handle, HidD_GetManufacturerString);
                product = ReadString(handle, HidD_GetProductString);
            }
            return new(ids.Vendor, ids.Product, manufacturer, product, path, root.InstanceId,
                root.ContainerId, root.FriendlyName, root.Description, root.Manufacturer,
                !handle.IsInvalid ? ReadInterfaceInfo(handle, path) : null);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
            Marshal.FreeHGlobal(deviceInfoPointer);
        }
    }

    private static HidInterfaceInfo? ReadInterfaceInfo(SafeFileHandle handle, string path)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData) || preparsedData == IntPtr.Zero) return null;
        try
        {
            var caps = new HidpCaps { Reserved = new ushort[17] };
            return HidP_GetCaps(preparsedData, ref caps) == HidpStatusSuccess
                ? new(caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                    caps.OutputReportByteLength, caps.FeatureReportByteLength) { Path = path }
                : null;
        }
        finally { HidD_FreePreparsedData(preparsedData); }
    }

    private static PnpParent FindPhysicalParent(uint devInst, string instanceId)
    {
        var current = devInst;
        var id = instanceId;
        PnpParent best = new("", "", "", "", "");
        for (var depth = 0; depth < 8 && id.Length > 0; depth++)
        {
            var values = ReadRegistryDevice(id);
            if (IsPhysicalRoot(id))
                return new(id, values.ContainerId, values.FriendlyName, values.Description, values.Manufacturer);
            if (best.InstanceId.Length == 0 && HasUsefulName(values.FriendlyName, values.Description))
                best = new(id, values.ContainerId, values.FriendlyName, values.Description, values.Manufacturer);
            if (CM_Get_Parent(out var parent, current, 0) != 0) break;
            current = parent;
            id = GetDeviceId(current);
        }
        return best.InstanceId.Length > 0 ? best : new(instanceId, "", "", "", "");
    }

    private static bool IsPhysicalRoot(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^BTHLE\\DEV_[0-9A-F]+\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^BTHENUM\\DEV_[0-9A-F]+\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool HasUsefulName(string friendly, string description) =>
        (!string.IsNullOrWhiteSpace(friendly) && !ContainsAny(friendly, "Dispositivo HID", "Mouse compatible", "Dispositivo de teclado", "Dispositivo de entrada USB")) ||
        (!string.IsNullOrWhiteSpace(description) && !ContainsAny(description, "Dispositivo HID", "Mouse compatible", "Dispositivo de teclado", "Dispositivo de entrada USB"));

    private static RegistryValues ReadRegistryDevice(string id)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{id}", writable: false);
            return key is null ? new("", "", "", "") : new(
                key.GetValue("ContainerID")?.ToString() ?? "",
                key.GetValue("FriendlyName")?.ToString() ?? "",
                key.GetValue("DeviceDesc")?.ToString() ?? "",
                key.GetValue("Mfg")?.ToString() ?? "");
        }
        catch (System.Security.SecurityException) { return new("", "", "", ""); }
    }

    private static string GetDeviceId(uint devInst)
    {
        var buffer = new char[512];
        return CM_Get_Device_ID(devInst, buffer, (uint)buffer.Length, 0) == 0
            ? new string(buffer).TrimEnd('\0') : string.Empty;
    }

    private static string FirstUseful(params string[] values) => values.FirstOrDefault(value =>
        !string.IsNullOrWhiteSpace(value) && !ContainsAny(value, "Dispositivo HID", "Mouse compatible", "Dispositivo de teclado HID", "Dispositivo de entrada USB", "HID Keyboard Device", "@keyboard.inf"))?.Trim() ?? "";
    private static bool ContainsAny(string value, params string[] tokens) => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    private static string ManufacturerFor(ushort vendor) => vendor switch
    {
        0x045E => "Microsoft", 0x046D => "Logitech", 0x9886 => "ASTRO Gaming",
        0x0C45 => "AJAZZ / Microdia", _ => ""
    };
    private static string Sanitize(string value) => System.Text.RegularExpressions.Regex.Replace(value, @"[^A-Za-z0-9_-]+", "-").Trim('-');

    private static (ushort Vendor, ushort Product) ParseIds(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path,
            "vid_([0-9a-f]{4}).*?pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return (0, 0);
        return (Convert.ToUInt16(match.Groups[1].Value, 16), Convert.ToUInt16(match.Groups[2].Value, 16));
    }

    private static string ReadString(SafeFileHandle handle, HidStringReader reader)
    {
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            return reader(handle, buffer, 512) ? (Marshal.PtrToStringUni(buffer) ?? string.Empty).TrimEnd('\0') : string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed record Endpoint(ushort VendorId, ushort ProductId, string Manufacturer, string Product, string Path,
        string RootInstanceId, string ContainerId, string RootFriendlyName, string RootDescription, string RootManufacturer,
        HidInterfaceInfo? HidInterface);
    private sealed record Identity(string ModelKey, string Manufacturer, string Name, ushort VendorId, ushort ProductId,
        DeviceKind Kind, bool IsAuxiliary, string Note);
    private readonly record struct PnpParent(string InstanceId, string ContainerId, string FriendlyName, string Description, string Manufacturer);
    private readonly record struct RegistryValues(string ContainerId, string FriendlyName, string Description, string Manufacturer);
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public uint Size; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct HidAttributes { public int Size; public ushort VendorId; public ushort ProductId; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)] private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
    private delegate bool HidStringReader(SafeFileHandle handle, IntPtr buffer, int bufferLength);
    private const uint HidpStatusSuccess = 0x00110000;

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll", EntryPoint = "HidP_GetCaps")] private static extern uint HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, IntPtr buffer, int bufferLength);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(SafeFileHandle handle, IntPtr buffer, int bufferLength);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData interfaceData);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData interfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Parent")] private static extern uint CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint devInst, [Out] char[] deviceId, uint bufferLength, uint flags);
}
