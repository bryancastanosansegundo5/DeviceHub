using System.IO;
using Microsoft.Win32.SafeHandles;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class LogitechHidppCapabilityReader
{
    private const byte LongReportId = 0x11;
    private const byte RootFeatureIndex = 0;
    private const byte SoftwareId = 0x0A;
    public const byte HidppMouseDeviceType = 0x03;
    private const ushort AdjustableReportRateFeatureId = 0x8060;
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
            if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major)) continue;

            var features = new Dictionary<ushort, bool>();
            byte? adjustableDpiFeatureIndex = null;
            var queryFailed = false;
            foreach (var feature in OmmGateFeatures)
            {
                var featureIndex = feature == 0x0005
                    ? nameAndTypeIndex
                    : await ReadFeatureIndexAsync(stream, index, feature, cancellationToken);
                if (featureIndex is null)
                {
                    queryFailed = true;
                    break;
                }
                features[feature] = featureIndex is > 0;
                if (feature == 0x2201) adjustableDpiFeatureIndex = featureIndex;
            }
            if (queryFailed) continue;

            var hidppDeviceType = features[0x0005]
                ? await ReadDeviceTypeAsync(stream, index, nameAndTypeIndex.Value, cancellationToken)
                : null;
            var reportRateFeatureIndex = hidppDeviceType == HidppMouseDeviceType
                ? await ReadFeatureIndexAsync(stream, index, AdjustableReportRateFeatureId, cancellationToken)
                : null;
            var reportRateMs = reportRateFeatureIndex is > 0
                ? await ReadCurrentReportRateAsync(stream, index, reportRateFeatureIndex.Value, cancellationToken)
                : null;
            var dpi = hidppDeviceType == HidppMouseDeviceType && adjustableDpiFeatureIndex is > 0
                ? await ReadCurrentDpiAsync(stream, index, adjustableDpiFeatureIndex.Value, cancellationToken)
                : null;
            slots.Add(new(index, protocol.Value.Major, protocol.Value.Minor,
                features[0x0003], features[0x0005],
                features[0x2201], features[0x2202], features[0x8100], hidppDeviceType)
            {
                AdjustableDpiFeatureIndex = adjustableDpiFeatureIndex,
                AdjustableReportRateFeatureIndex = reportRateFeatureIndex,
                CurrentReportRateMs = reportRateMs,
                CurrentDpi = dpi
            });
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
        if (response.Length != LongReportLength || response[0] != LongReportId || response[1] != deviceIndex ||
            response[2] != RootFeatureIndex || response[3] != softwareId) return false;
        featureIndex = response[4];
        return true;
    }

    public static byte[] BuildGetProtocolVersionRequest(byte deviceIndex, byte softwareId, byte pingData)
    {
        if (softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException(nameof(softwareId));
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = RootFeatureIndex;
        report[3] = (byte)(0x10 | softwareId);
        report[6] = pingData;
        return report;
    }

    public static bool TryReadProtocolVersionResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte softwareId, byte pingData,
        out byte major, out byte minor)
    {
        major = minor = 0;
        if (softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex ||
            response[2] != RootFeatureIndex || response[3] != (byte)(0x10 | softwareId) ||
            response[6] != pingData) return false;
        major = response[4];
        minor = response[5];
        return true;
    }

    public static bool IsHidpp20ProtocolMajor(byte major) => major is 0x02 or 0x04;

    public static byte[] BuildGetDeviceTypeRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x20 | softwareId);
        return report;
    }

    public static bool TryReadDeviceTypeResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out byte deviceType)
    {
        deviceType = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x20 | softwareId)) return false;
        deviceType = response[4];
        return true;
    }

    public static byte[] BuildGetReportRateRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x10 | softwareId);
        return report;
    }

    public static bool TryReadReportRateResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out byte rateMs)
    {
        rateMs = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x10 | softwareId) || response[4] == 0) return false;
        rateMs = response[4];
        return true;
    }

    public static byte[] BuildGetSensorCountRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = softwareId;
        return report;
    }

    public static bool TryReadSensorCountResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out byte sensorCount)
    {
        sensorCount = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != softwareId) return false;
        sensorCount = response[4];
        return true;
    }

    public static byte[] BuildGetSensorDpiRequest(byte deviceIndex, byte featureIndex, byte sensorIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x20 | softwareId);
        report[4] = sensorIndex;
        return report;
    }

    public static bool TryReadSensorDpiResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte sensorIndex, byte softwareId,
        out int currentDpi, out int? defaultDpi)
    {
        currentDpi = 0;
        defaultDpi = null;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x20 | softwareId) || response[4] != sensorIndex) return false;
        currentDpi = (response[5] << 8) | response[6];
        var defaultValue = (response[7] << 8) | response[8];
        if (currentDpi is < 1 or > 57343) return false;
        defaultDpi = defaultValue is >= 1 and <= 57343 ? defaultValue : null;
        return true;
    }

    private static async Task<byte?> ReadFeatureIndexAsync(
        FileStream stream, byte deviceIndex, ushort featureId, CancellationToken cancellationToken)
    {
        var request = BuildGetFeatureRequest(deviceIndex, featureId, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, RootFeatureIndex, request[3], cancellationToken);
        return response is not null && TryReadFeatureIndex(response, deviceIndex, SoftwareId, out var index)
            ? index : null;
    }

    private static async Task<(byte Major, byte Minor)?> ReadProtocolVersionAsync(
        FileStream stream, byte deviceIndex, CancellationToken cancellationToken)
    {
        const byte pingData = 0xA5;
        var request = BuildGetProtocolVersionRequest(deviceIndex, SoftwareId, pingData);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, RootFeatureIndex, request[3], cancellationToken);
        return response is not null && TryReadProtocolVersionResponse(response, deviceIndex, SoftwareId, pingData,
            out var major, out var minor) ? (major, minor) : null;
    }

    private static async Task<byte?> ReadDeviceTypeAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var request = BuildGetDeviceTypeRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
        return response is not null && TryReadDeviceTypeResponse(response, deviceIndex, featureIndex, SoftwareId,
            out var deviceType) ? deviceType : null;
    }

    private static async Task<LogitechDpiReading?> ReadCurrentDpiAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var countRequest = BuildGetSensorCountRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(countRequest, cancellationToken);
        var countResponse = await ReadResponseAsync(stream, deviceIndex, featureIndex, countRequest[3], cancellationToken);
        if (countResponse is null || !TryReadSensorCountResponse(countResponse, deviceIndex, featureIndex,
            SoftwareId, out var sensorCount) || sensorCount == 0) return null;

        const byte sensorIndex = 0;
        var dpiRequest = BuildGetSensorDpiRequest(deviceIndex, featureIndex, sensorIndex, SoftwareId);
        await stream.WriteAsync(dpiRequest, cancellationToken);
        var dpiResponse = await ReadResponseAsync(stream, deviceIndex, featureIndex, dpiRequest[3], cancellationToken);
        return dpiResponse is not null && TryReadSensorDpiResponse(dpiResponse, deviceIndex, featureIndex,
            sensorIndex, SoftwareId, out var currentDpi, out var defaultDpi)
            ? new(sensorIndex, sensorCount, currentDpi, defaultDpi)
            : null;
    }

    private static async Task<byte?> ReadCurrentReportRateAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var request = BuildGetReportRateRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
        return response is not null && TryReadReportRateResponse(response, deviceIndex, featureIndex, SoftwareId,
            out var rateMs) ? rateMs : null;
    }

    private static async Task<byte[]?> ReadResponseAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, byte functionAndSoftwareId,
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
            if (count != LongReportLength || report[0] != LongReportId || report[1] != deviceIndex) continue;
            if (report[2] == 0xFF && report[3] == featureIndex && report[4] == functionAndSoftwareId) return null;
            if (report[2] == featureIndex && report[3] == functionAndSoftwareId) return report;
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
    bool HasFeature2201,
    bool HasFeature2202,
    bool HasOnboardProfiles,
    byte? HidppDeviceType)
{
    public byte? AdjustableDpiFeatureIndex { get; init; }
    public byte? AdjustableReportRateFeatureIndex { get; init; }
    public byte? CurrentReportRateMs { get; init; }
    public LogitechDpiReading? CurrentDpi { get; init; }

    public bool HasAdjustableDpi => HasFeature2201 || HasFeature2202;

    public bool HasRequiredFeatures => LogitechHidppCapabilityReader.IsHidpp20ProtocolMajor(ProtocolMajor) && HidppDeviceType is not null && HasDeviceInformation &&
        HasDeviceNameAndType && HasAdjustableDpi && HasOnboardProfiles;
}

public sealed record LogitechDpiReading(byte SensorIndex, byte SensorCount, int CurrentDpi, int? DefaultDpi);
