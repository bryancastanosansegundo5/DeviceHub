using System.IO;
using Microsoft.Win32.SafeHandles;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class LogitechHidppCapabilityReader
{
    private const byte LongReportId = 0x11;
    private const byte RootFeatureIndex = 0;
    private const byte SoftwareId = 0x0A;
    private const int LongReportLength = 20;
    private const int ResponseTimeoutMs = 350;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly ushort[] OmmGateFeatures = [0x0003, 0x0005, 0x2201, 0x2202, 0x8100];

    public async Task<IReadOnlyList<LogitechHidppSlotStatus>> ReadOmmGateAsync(
        HardwareDevice device, CancellationToken cancellationToken = default)
    {
        if (device.ModelKey != "logitech-pro-wireless" || device.VendorId != 0x046D || device.ProductId != 0xC539)
            throw new InvalidOperationException("La consulta está limitada al receptor C539 identificado como PRO WIRELESS.");

        var endpoint = (device.HidInterfaces ?? []).FirstOrDefault(item => item.UsagePage == 0xFF00 &&
            item.Usage == 0x0002 && item.InputReportBytes == LongReportLength &&
            item.OutputReportBytes == LongReportLength && item.Path.Length > 0);
        if (endpoint is null)
            throw new InvalidOperationException("No se encontró la colección HID de 20 bytes observada en este receptor.");

        using var handle = CreateFile(endpoint.Path, GenericRead | GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows no pudo abrir la colección HID de consulta.");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, LongReportLength, isAsync: true);

        var slots = new List<LogitechHidppSlotStatus>();
        for (byte index = 1; index <= 6; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameAndTypeIndex = await ReadFeatureIndexAsync(stream, index, 0x0005, cancellationToken);
            if (nameAndTypeIndex is null or 0) continue;

            var protocol = await ReadProtocolVersionAsync(stream, index, cancellationToken);
            if (protocol is null || protocol.Value.Major < 2) continue;

            var features = new Dictionary<ushort, bool>();
            foreach (var feature in OmmGateFeatures)
            {
                var featureIndex = feature == 0x0005
                    ? nameAndTypeIndex
                    : await ReadFeatureIndexAsync(stream, index, feature, cancellationToken);
                features[feature] = featureIndex is > 0;
            }

            slots.Add(new(index, protocol.Value.Major, protocol.Value.Minor,
                features[0x0003], features[0x0005],
                features[0x2201] || features[0x2202], features[0x8100]));
        }

        return slots;
    }

    public static byte[] BuildGetFeatureRequest(byte deviceIndex, ushort featureId, byte softwareId)
    {
        if (softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException(nameof(softwareId));
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = RootFeatureIndex;
        report[3] = softwareId;
        report[4] = (byte)(featureId >> 8);
        report[5] = (byte)featureId;
        return report;
    }

    public static bool TryReadFeatureIndex(ReadOnlySpan<byte> response, byte deviceIndex, byte softwareId, out byte featureIndex)
    {
        featureIndex = 0;
        if (response.Length < 7 || response[0] != LongReportId || response[1] != deviceIndex ||
            response[2] != RootFeatureIndex || response[3] != softwareId) return false;
        featureIndex = response[4];
        return true;
    }

    private static async Task<byte?> ReadFeatureIndexAsync(
        FileStream stream, byte deviceIndex, ushort featureId, CancellationToken cancellationToken)
    {
        var request = BuildGetFeatureRequest(deviceIndex, featureId, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, SoftwareId, RootFeatureIndex, cancellationToken);
        return response is not null && TryReadFeatureIndex(response, deviceIndex, SoftwareId, out var index)
            ? index : null;
    }

    private static async Task<(byte Major, byte Minor)?> ReadProtocolVersionAsync(
        FileStream stream, byte deviceIndex, CancellationToken cancellationToken)
    {
        const byte pingData = 0xA5;
        var request = new byte[LongReportLength];
        request[0] = LongReportId;
        request[1] = deviceIndex;
        request[2] = RootFeatureIndex;
        request[3] = (byte)(0x10 | SoftwareId);
        request[6] = pingData;
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, SoftwareId, request[3], cancellationToken);
        return response is { Length: LongReportLength } && response[6] == pingData
            ? (response[4], response[5]) : null;
    }

    private static async Task<byte[]?> ReadResponseAsync(
        FileStream stream, byte deviceIndex, byte softwareId, byte functionAndSoftwareId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeoutMs);
        var report = new byte[LongReportLength];
        while (true)
        {
            int count;
            try { count = await stream.ReadAsync(report, timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            if (count < 7 || report[0] != LongReportId || report[1] != deviceIndex) continue;
            if (report[2] == 0xFF && count > 5 && (report[4] & 0x0F) == softwareId) return null;
            if (report[2] == RootFeatureIndex && report[3] == functionAndSoftwareId) return report;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}

public sealed record LogitechHidppSlotStatus(
    byte DeviceIndex,
    byte ProtocolMajor,
    byte ProtocolMinor,
    bool HasDeviceInformation,
    bool HasDeviceNameAndType,
    bool HasAdjustableDpi,
    bool HasOnboardProfiles)
{
    public bool HasOmmFeaturePrerequisites => ProtocolMajor >= 2 && HasDeviceInformation &&
        HasDeviceNameAndType && HasAdjustableDpi && HasOnboardProfiles;
}
