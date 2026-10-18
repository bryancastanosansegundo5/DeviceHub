using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class XboxEliteProfileWriter
{
    private const uint IoctlGipMessage = 0x40001CCC;
    private const int ErrorIoPending = 997;
    private static readonly object ControllerIoLock = new();
    // GipButtonMapVal enumerates Menu=2, View=3, A=4 ... RightThumbstick=15.
    // Captures from Xbox Accessories confirm LB→Menu=D2 and LB→View=D3, so the
    // mapping byte is represented as 0xD0 + enum value. Values beyond those two
    // captures are derived from the package enum and must not be described as
    // semantically verified until XInput/game output is observed.
    private static readonly IReadOnlyDictionary<string, (byte Value, string AccessoriesValue)> LeftBumperMappings =
        new Dictionary<string, (byte, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Botón Menú"] = (0xD2, "Menu"),
            ["Botón Ver"] = (0xD3, "View"),
            ["Botón A"] = (0xD4, "A"),
            ["Botón B"] = (0xD5, "B"),
            ["Botón X"] = (0xD6, "X"),
            ["Botón Y"] = (0xD7, "Y"),
            ["Cruceta arriba"] = (0xD8, "DpadUp"),
            ["Cruceta abajo"] = (0xD9, "DpadDown"),
            ["Cruceta izquierda"] = (0xDA, "DpadLeft"),
            ["Cruceta derecha"] = (0xDB, "DpadRight"),
            ["LB"] = (0xDC, "LeftBumper"),
            ["RB"] = (0xDD, "RightBumper"),
            ["Palanca izquierda"] = (0xDE, "LeftThumbstickClick"),
            ["Palanca derecha"] = (0xDF, "RightThumbstickClick")
        };

    public static bool TryGetLeftBumperMapping(string assignment, out byte value, out string accessoriesValue)
    {
        if (LeftBumperMappings.TryGetValue(assignment, out var mapping))
        {
            value = mapping.Value;
            accessoriesValue = mapping.AccessoriesValue;
            return true;
        }

        value = 0;
        accessoriesValue = string.Empty;
        return false;
    }

    public static bool TryGetLeftBumperAssignment(byte value, out string assignment)
    {
        var mapping = LeftBumperMappings.FirstOrDefault(item => item.Value.Value == value);
        assignment = mapping.Key ?? string.Empty;
        return assignment.Length > 0;
    }

    public byte ReadCurrentLeftBumperMapping(HardwareDevice device)
    {
        lock (ControllerIoLock) return ReadCurrentLeftBumperMappingCore(device);
    }

    private byte ReadCurrentLeftBumperMappingCore(HardwareDevice device)
    {
        ValidateDevice(device);
        var token = TryGetControllerToken(device.InstanceId)
            ?? throw new InvalidOperationException("No se pudo derivar el token GIP de la identidad PnP del Elite conectado.");

        using var handle = CreateFile("\\\\.\\XboxGIP", 0, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo abrir XboxGIP para lectura.");

        var response = Send(handle, MakeRequest(token, [0x02, 0x1D, 0x37]), 59);
        ValidateReadback(response);
        return response[10];
    }

    public string ApplyLeftBumperAssignment(HardwareDevice device, string assignment, string? expectedCurrentAssignment = null)
    {
        lock (ControllerIoLock) return ApplyLeftBumperAssignmentCore(device, assignment, expectedCurrentAssignment);
    }

    private string ApplyLeftBumperAssignmentCore(HardwareDevice device, string assignment, string? expectedCurrentAssignment)
    {
        ValidateDevice(device);
        if (!TryGetLeftBumperMapping(assignment, out var targetMapping, out _))
            throw new InvalidOperationException($"El destino «{assignment}» no tiene una codificación admitida por este escritor.");

        var token = TryGetControllerToken(device.InstanceId)
            ?? throw new InvalidOperationException("No se pudo derivar el token GIP de la identidad PnP del Elite conectado.");

        using var handle = CreateFile("\\\\.\\XboxGIP", 0, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo abrir XboxGIP.");

        var query = new byte[] { 0x02, 0x1D, 0x37 };
        var before = Send(handle, MakeRequest(token, query), 59);
        ValidateReadback(before);

        var originalConfig = before[4..];
        var previousLeftBumperMapping = originalConfig[6];
        if (expectedCurrentAssignment is not null &&
            (!TryGetLeftBumperMapping(expectedCurrentAssignment, out var expectedMapping, out _) ||
             previousLeftBumperMapping != expectedMapping))
            throw new InvalidOperationException("La configuración leída ya no coincide con el perfil editado; se canceló la escritura para no cambiar la otra ranura.");
        var expectedConfig = BuildLeftBumperConfiguration(originalConfig, assignment);
        if (originalConfig.AsSpan().SequenceEqual(expectedConfig))
            return $"No se escribió nada: XboxGIP ya expone LB → {assignment} ({targetMapping:X2}). Esta lectura no confirma la salida XInput.";

        var write = new byte[3 + expectedConfig.Length];
        new byte[] { 0x01, 0x1D, 0x37 }.CopyTo(write, 0);
        expectedConfig.CopyTo(write, 3);
        var ack = Send(handle, MakeRequest(token, write), 3);
        if (!ack.AsSpan().SequenceEqual(new byte[] { 0x01, 0x80, 0x1D }))
            throw new InvalidOperationException("El mando no confirmó el mensaje de configuración.");

        var after = Send(handle, MakeRequest(token, query), 59);
        ValidateReadback(after);
        if (!after.AsSpan(4).SequenceEqual(expectedConfig))
            throw new InvalidOperationException("El readback no coincide con el cambio solicitado; se desconoce el estado final del mando.");
        return $"Se cambió solo LB: {previousLeftBumperMapping:X2} → {targetMapping:X2} ({assignment}). ACK y lectura inmediata confirmados; falta verificar la salida XInput.";
    }

    private static void ValidateDevice(HardwareDevice device)
    {
        if (device.ModelKey != "xbox-one-elite" || device.VendorId != 0x045E || device.ProductId != 0x02E3)
            throw new InvalidOperationException("El adaptador solo admite el Xbox One Elite identificado por VID/PID.");
    }

    public static byte[] BuildLeftBumperConfiguration(ReadOnlySpan<byte> originalConfig, string assignment)
    {
        const int configLength = 55;
        const int leftBumperMappingOffset = 6;

        if (originalConfig.Length != configLength)
            throw new InvalidOperationException("El bloque GIP del Elite no tiene los 55 bytes esperados.");
        if (!TryGetLeftBumperMapping(assignment, out var targetMapping, out _))
            throw new InvalidOperationException($"El destino «{assignment}» no tiene una codificación admitida por este escritor.");

        var currentMapping = originalConfig[leftBumperMappingOffset];
        if (!LeftBumperMappings.Values.Any(mapping => mapping.Value == currentMapping))
            throw new InvalidOperationException("El valor actual de LB no coincide con los valores observados; no se escribió nada.");

        var expectedConfig = originalConfig.ToArray();
        expectedConfig[leftBumperMappingOffset] = targetMapping;
        return expectedConfig;
    }

    private static ulong? TryGetControllerToken(string instanceId)
    {
        const string expectedPrefix = "USB\\VID_045E&PID_02E3\\";
        if (!instanceId.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var serial = instanceId[(instanceId.LastIndexOf('\\') + 1)..];
        if (serial.Length != 12 || !serial.All(Uri.IsHexDigit)) return null;
        // shortcut: this maps the observed Elite USB serial to its GIP token; replace with IGipControllerApi.GetDeviceId when available.
        Span<byte> tokenBytes = stackalloc byte[8];
        for (var i = 0; i < 6; i++)
            tokenBytes[i] = byte.Parse(serial.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        return BinaryPrimitives.ReadUInt64LittleEndian(tokenBytes);
    }

    private static void ValidateReadback(byte[] data)
    {
        if (data.Length != 59 || !data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x02, 0x80, 0x1D, 0x37 }))
            throw new InvalidOperationException("La respuesta de lectura GIP no tiene el formato esperado.");
    }

    private static byte[] MakeRequest(ulong token, byte[] payload)
    {
        var request = new byte[32 + payload.Length];
        BitConverter.TryWriteBytes(request.AsSpan(0, 8), token);
        BitConverter.TryWriteBytes(request.AsSpan(8, 4), 0x4D);
        BitConverter.TryWriteBytes(request.AsSpan(12, 4), payload.Length);
        BitConverter.TryWriteBytes(request.AsSpan(20, 8), token);
        BitConverter.TryWriteBytes(request.AsSpan(28, 4), payload.Length);
        payload.CopyTo(request, 32);
        return request;
    }

    private static byte[] Send(SafeFileHandle handle, byte[] input, int outputLength)
    {
        var output = new byte[outputLength];
        var inputPin = GCHandle.Alloc(input, GCHandleType.Pinned);
        var outputPin = GCHandle.Alloc(output, GCHandleType.Pinned);
        var overlapped = Marshal.AllocHGlobal(IntPtr.Size == 8 ? 32 : 20);
        var bytesReturned = Marshal.AllocHGlobal(sizeof(int));
        var eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        if (eventHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (var i = 0; i < (IntPtr.Size == 8 ? 32 : 20); i += sizeof(int)) Marshal.WriteInt32(overlapped, i, 0);
            Marshal.WriteIntPtr(overlapped, IntPtr.Size == 8 ? 24 : 16, eventHandle);
            Marshal.WriteInt32(bytesReturned, 0);
            var succeeded = DeviceIoControl(handle, IoctlGipMessage, inputPin.AddrOfPinnedObject(), input.Length,
                outputPin.AddrOfPinnedObject(), output.Length, bytesReturned, overlapped);
            if (!succeeded && Marshal.GetLastWin32Error() != ErrorIoPending)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "XboxGIP rechazó el mensaje GIP.");
            if (!GetOverlappedResult(handle, overlapped, out var count, true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se completó el mensaje GIP.");
            if (count != output.Length) throw new InvalidOperationException($"Readback GIP incompleto: {count} de {output.Length} bytes.");
            return output;
        }
        finally
        {
            CloseHandle(eventHandle);
            Marshal.FreeHGlobal(bytesReturned);
            Marshal.FreeHGlobal(overlapped);
            inputPin.Free();
            outputPin.Free();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputLength,
        IntPtr output, int outputLength, IntPtr bytesReturned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle device, IntPtr overlapped, out int bytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
