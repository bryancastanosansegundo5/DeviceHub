using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NexoHub.Models;

namespace NexoHub.Services;

public sealed class LogitechHidppCapabilityReader
{
    private const byte LongReportId = 0x11;
    private const byte ShortReportId = 0x10;
    private const byte RootFeatureIndex = 0;
    private const byte SoftwareId = 0x0A;
    public const byte HidppMouseDeviceType = 0x03;
    private const ushort DeviceInformationFeatureId = 0x0003;
    private const ushort AdjustableReportRateFeatureId = 0x8060;
    private const ushort BatteryLevelStatusFeatureId = 0x1000;
    private const int LongReportLength = 20;
    private const int ShortReportLength = 7;
    private const int ResponseTimeoutMs = 350;
    private const int WriteResponseTimeoutMs = 3000;
    private const int ReceiverRegisterTimeoutMs = 1800;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    public static IReadOnlyList<(ushort Id, string Name)> MouseFeatureCatalog { get; } =
    [
        (0x0005, "Nombre y tipo"), (0x0003, "Información del dispositivo"),
        (0x1000, "Nivel de batería"), (0x2201, "DPI ajustable"), (0x2202, "DPI extendido"),
        (0x8060, "Frecuencia de respuesta"), (0x8061, "Capacidades extendidas de respuesta (OMM)"),
        (0x8100, "Perfiles integrados"),
        (0x8070, "Efectos LED"), (0x8071, "Efectos RGB"), (0x1B00, "Botones reasignables 1"),
        (0x1B01, "Botones reasignables 2"), (0x1B02, "Botones reasignables 3"),
        (0x1B03, "Botones reasignables 4"), (0x1B04, "Botones reasignables 5"),
        (0x1B0C, "Funciones analógicas de botón (OMM)"),
        (0x1BC0, "Usos HID de botones"), (0x1C00, "Acción reasignable persistente"),
        (0x2001, "Intercambiar botones izquierdo/derecho"), (0x2005, "Cancelar intercambio de botones"),
        (0x2006, "Orientación de ejes"), (0x2100, "Desplazamiento vertical"),
        (0x2110, "Rueda SmartShift"), (0x2120, "Desplazamiento de alta resolución"),
        (0x2121, "Rueda HiRes"), (0x2130, "Rueda con trinquete"), (0x2150, "Rueda de pulgar"),
        (0x2200, "Puntero del ratón"), (0x2205, "Escalado de movimiento"),
        (0x2230, "Ajuste de ángulo del sensor"), (0x2240, "Ajuste de superficie"),
        (0x2400, "Seguimiento híbrido"), (0x1001, "Voltaje de batería"),
        (0x1010, "Control de carga"), (0x1814, "Cambiar dispositivo anfitrión"),
        (0x1981, "Retroiluminación 1"), (0x1982, "Retroiluminación 2"),
        (0x8110, "Filtro de botones del ratón")
    ];

    private static readonly byte[] DeviceIndexCandidates = [0xFF, 1, 2, 3, 4, 5, 6];

    public async Task<IReadOnlyList<LogitechHidppSlotStatus>> ReadOmmGateAsync(
        HardwareDevice device, CancellationToken cancellationToken = default)
    {
        if (device.VendorId != 0x046D ||
            device.ModelKey == "logitech-pro-wireless" && device.ProductId != 0xC539 ||
            device.ModelKey == "logitech-pro-x-superlight" && device.ProductId != 0xC547 ||
            device.ModelKey is not ("logitech-pro-wireless" or "logitech-pro-x-superlight"))
            throw new InvalidOperationException("La consulta requiere un PRO WIRELESS C539 o un PRO X SUPERLIGHT C547 identificado.");

        var endpoint = (device.HidInterfaces ?? []).FirstOrDefault(item => item.UsagePage == 0xFF00 &&
            item.Usage == 0x0002 && item.InputReportBytes == LongReportLength &&
            item.OutputReportBytes == LongReportLength && item.Path.Length > 0);
        if (endpoint is null)
            throw new InvalidOperationException("No se encontró la colección HID++ de 20 bytes de este ratón.");

        using var handle = CreateFile(endpoint.Path, GenericRead | GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows no pudo abrir la colección HID de consulta.");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, LongReportLength, isAsync: true);

        var slots = new List<LogitechHidppSlotStatus>();
        foreach (var index in DeviceIndexCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameAndTypeIndex = await ReadFeatureIndexAsync(stream, index, 0x0005, cancellationToken);
            if (nameAndTypeIndex is null or 0) continue;

            var protocol = await ReadProtocolVersionAsync(stream, index, cancellationToken);
            if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major)) continue;
            var hidppDeviceType = await ReadDeviceTypeAsync(stream, index, nameAndTypeIndex.Value, cancellationToken);
            if (hidppDeviceType != HidppMouseDeviceType) continue;
            var deviceName = await ReadDeviceNameAsync(stream, index, nameAndTypeIndex.Value, cancellationToken);
            if (ModelKeyFromDeviceName(deviceName ?? string.Empty) != device.ModelKey) continue;

            var features = new Dictionary<ushort, byte?>(MouseFeatureCatalog.Count);
            ushort? featureQueryStoppedAt = null;
            foreach (var (featureId, _) in MouseFeatureCatalog)
            {
                var featureIndex = featureId == 0x0005
                    ? nameAndTypeIndex
                    : await ReadFeatureIndexAsync(stream, index, featureId, cancellationToken);
                features[featureId] = featureIndex;
                if (featureIndex is null)
                {
                    featureQueryStoppedAt = featureId;
                    break;
                }
            }
            foreach (var (featureId, _) in MouseFeatureCatalog)
                features.TryAdd(featureId, null);

            var adjustableDpiFeatureIndex = features[0x2201];
            var deviceInformationFeatureIndex = features[DeviceInformationFeatureId];
            var reportRateFeatureIndex = features[AdjustableReportRateFeatureId];
            var reportRateMs = reportRateFeatureIndex is > 0
                ? await ReadCurrentReportRateAsync(stream, index, reportRateFeatureIndex.Value, cancellationToken)
                : null;
            var batteryFeatureIndex = features[BatteryLevelStatusFeatureId];
            var battery = batteryFeatureIndex is > 0
                ? await ReadBatteryLevelStatusAsync(stream, index, batteryFeatureIndex.Value, cancellationToken)
                : null;
            var dpi = hidppDeviceType == HidppMouseDeviceType && adjustableDpiFeatureIndex is > 0
                ? await ReadCurrentDpiAsync(stream, index, adjustableDpiFeatureIndex.Value, cancellationToken)
                : null;
            var firmware = deviceInformationFeatureIndex is > 0
                ? await ReadFirmwareInfoAsync(stream, index, deviceInformationFeatureIndex.Value, cancellationToken)
                : [];
            var onboardProfilesInfo = features[0x8100] is > 0
                ? await ReadOnboardProfilesInfoAsync(stream, index, features[0x8100]!.Value, cancellationToken)
                : null;
            slots.Add(new(index, protocol.Value.Major, protocol.Value.Minor,
                features[0x0003] is > 0, features[0x0005] is > 0,
                features[0x2201] is > 0, features[0x2202] is > 0,
                features[0x8100] is > 0, hidppDeviceType)
            {
                FeatureIndexes = features,
                FeatureQueryStoppedAt = featureQueryStoppedAt,
                AdjustableDpiFeatureIndex = adjustableDpiFeatureIndex,
                AdjustableReportRateFeatureIndex = reportRateFeatureIndex,
                CurrentReportRateMs = reportRateMs,
                CurrentDpi = dpi,
                BatteryFeatureIndex = batteryFeatureIndex,
                Battery = battery,
                OnboardProfilesInfo = onboardProfilesInfo,
                Firmware = firmware
            });
            break;
        }

        return slots;
    }

    public async Task<LogitechOnboardProfileSnapshot> ReadOnboardProfilesAsync(
        HardwareDevice device, CancellationToken cancellationToken = default)
    {
        if (device.VendorId != 0x046D ||
            device.ProductId == 0xC539 && device.ModelKey != "logitech-pro-wireless" ||
            device.ProductId == 0xC547 && device.ModelKey != "logitech-pro-x-superlight" ||
            device.ProductId is not (0xC539 or 0xC547))
            throw new InvalidOperationException("La lectura de memoria requiere un PRO WIRELESS C539 o PRO X SUPERLIGHT C547 identificado.");

        var endpoint = (device.HidInterfaces ?? []).FirstOrDefault(item => item.UsagePage == 0xFF00 &&
            item.Usage == 0x0002 && item.InputReportBytes == LongReportLength &&
            item.OutputReportBytes == LongReportLength && item.Path.Length > 0)
            ?? throw new InvalidOperationException("No se encontró la conexión HID++ de 20 bytes del ratón.");
        using var handle = CreateFile(endpoint.Path, GenericRead | GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows no pudo abrir la conexión HID++ para leer la memoria.");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, LongReportLength, isAsync: true);

        foreach (var deviceIndex in DeviceIndexCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameIndex = await ReadFeatureIndexAsync(stream, deviceIndex, 0x0005, cancellationToken);
            if (nameIndex is null or 0) continue;
            var protocol = await ReadProtocolVersionAsync(stream, deviceIndex, cancellationToken);
            if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major) ||
                await ReadDeviceTypeAsync(stream, deviceIndex, nameIndex.Value, cancellationToken) != HidppMouseDeviceType)
                continue;
            var name = await ReadDeviceNameAsync(stream, deviceIndex, nameIndex.Value, cancellationToken);
            if (ModelKeyFromDeviceName(name ?? string.Empty) != device.ModelKey) continue;

            var profileFeature = await ReadFeatureIndexAsync(stream, deviceIndex, 0x8100, cancellationToken);
            if (profileFeature is null or 0) throw new IOException("El ratón no respondió a perfiles integrados.");
            var info = await ReadOnboardProfilesInfoAsync(stream, deviceIndex, profileFeature.Value, cancellationToken)
                ?? throw new IOException("El ratón no respondió a la información de perfiles.");
            if (info.ProfileFormatId is not (3 or 4) || info.SectorSize != 255 || info.SectorCount < 2)
                throw new InvalidOperationException($"Formato de memoria no validado para esta lectura ({info.ProfileFormatId}, {info.SectorSize} bytes).");

            var modeRequest = BuildGetOnboardProfileModeRequest(deviceIndex, profileFeature.Value, SoftwareId);
            await stream.WriteAsync(modeRequest, cancellationToken);
            var modeResponse = await ReadResponseAsync(stream, deviceIndex, profileFeature.Value,
                modeRequest[3], cancellationToken);
            if (modeResponse is null || !TryReadOnboardProfileModeResponse(modeResponse, deviceIndex,
                profileFeature.Value, SoftwareId, out var mode))
                throw new IOException("El ratón no confirmó si usa el perfil integrado o el de G HUB.");
            ushort? activeSector = null;
            if (mode == 0x01)
            {
                var currentRequest = BuildGetCurrentOnboardProfileRequest(deviceIndex, profileFeature.Value, SoftwareId);
                await stream.WriteAsync(currentRequest, cancellationToken);
                var currentResponse = await ReadResponseAsync(stream, deviceIndex, profileFeature.Value,
                    currentRequest[3], cancellationToken);
                if (currentResponse is null || !TryReadCurrentOnboardProfileResponse(currentResponse, deviceIndex,
                    profileFeature.Value, SoftwareId, out var current))
                    throw new IOException("El ratón no confirmó qué perfil está activo.");
                activeSector = current;
            }

            var directory = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                0, info.SectorSize, cancellationToken);
            if (!TryReadOnboardProfileDirectory(directory, info.SectorCount, info.ProfileCount, out var entries))
                throw new IOException("La lista de perfiles no superó la validación de integridad.");
            if (activeSector is { } currentSector && !entries.Any(entry => entry.Sector == currentSector))
                throw new IOException("El perfil activo no aparece en la lista almacenada por el ratón.");

            var profiles = new List<LogitechStoredProfile>(entries.Count);
            foreach (var entry in entries)
            {
                var profile = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                    entry.Sector, info.SectorSize, cancellationToken);
                if (!TryReadBaseV1DpiTable(profile, out var defaultDpiIndex, out var stages))
                    throw new IOException($"El perfil {entry.Sector} tiene un formato DPI o checksum no válido.");
                profiles.Add(new(entry.Sector, entry.IsEnabled, defaultDpiIndex, stages, profile));
            }

            var dpiFeature = await ReadFeatureIndexAsync(stream, deviceIndex, 0x2201, cancellationToken);
            var liveDpi = dpiFeature is > 0
                ? await ReadCurrentDpiAsync(stream, deviceIndex, dpiFeature.Value, cancellationToken)
                : null;
            return new(info, mode, activeSector, entries, profiles, liveDpi?.CurrentDpi, directory);
        }

        throw new IOException("No se encontró el ratón identificado en los canales HID++ probados.");
    }

    public async Task<LogitechOnboardDpiWriteResult> SaveOnboardDpiStagesAsync(
        HardwareDevice device, IReadOnlyList<int> dpiStages, byte defaultDpiIndex,
        CancellationToken cancellationToken = default)
    {
        if (device.VendorId != 0x046D ||
            device.ProductId == 0xC539 && device.ModelKey != "logitech-pro-wireless" ||
            device.ProductId == 0xC547 && device.ModelKey != "logitech-pro-x-superlight" ||
            device.ProductId is not (0xC539 or 0xC547))
            throw new InvalidOperationException("Guardar DPI requiere un PRO WIRELESS C539 o PRO X SUPERLIGHT C547 identificado.");
        if (dpiStages.Count is < 1 or > 5 || defaultDpiIndex >= dpiStages.Count)
            throw new ArgumentOutOfRangeException(nameof(dpiStages), "El perfil debe tener entre una y cinco etapas y una etapa predeterminada válida.");

        var endpoint = (device.HidInterfaces ?? []).FirstOrDefault(item => item.UsagePage == 0xFF00 &&
            item.Usage == 0x0002 && item.InputReportBytes == LongReportLength &&
            item.OutputReportBytes == LongReportLength && item.Path.Length > 0)
            ?? throw new InvalidOperationException("No se encontró la conexión HID++ de 20 bytes del receptor.");
        using var handle = CreateFile(endpoint.Path, GenericRead | GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows no pudo abrir la conexión HID++ para guardar DPI.");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, LongReportLength, isAsync: true);

        foreach (var deviceIndex in DeviceIndexCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameIndex = await ReadFeatureIndexAsync(stream, deviceIndex, 0x0005, cancellationToken);
            if (nameIndex is null or 0) continue;
            var protocol = await ReadProtocolVersionAsync(stream, deviceIndex, cancellationToken);
            if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major) ||
                await ReadDeviceTypeAsync(stream, deviceIndex, nameIndex.Value, cancellationToken) != HidppMouseDeviceType ||
                ModelKeyFromDeviceName(await ReadDeviceNameAsync(stream, deviceIndex, nameIndex.Value, cancellationToken) ?? string.Empty)
                    != device.ModelKey) continue;

            var profileFeature = await ReadFeatureIndexAsync(stream, deviceIndex, 0x8100, cancellationToken);
            if (profileFeature is null or 0) throw new IOException("El ratón no respondió a perfiles integrados.");
            var info = await ReadOnboardProfilesInfoAsync(stream, deviceIndex, profileFeature.Value, cancellationToken)
                ?? throw new IOException("El ratón no respondió a la información de perfiles.");
            if (info.ProfileFormatId is not (3 or 4) || info.SectorSize != 255 || info.SectorCount < 3)
                throw new InvalidOperationException("La escritura solo está habilitada para los formatos 3 y 4 de 255 bytes verificados.");

            var modeRequest = BuildGetOnboardProfileModeRequest(deviceIndex, profileFeature.Value, SoftwareId);
            await stream.WriteAsync(modeRequest, cancellationToken);
            var modeResponse = await ReadResponseAsync(stream, deviceIndex, profileFeature.Value, modeRequest[3], cancellationToken);
            if (modeResponse is null || !TryReadOnboardProfileModeResponse(modeResponse, deviceIndex,
                profileFeature.Value, SoftwareId, out var mode))
                throw new IOException("El ratón no confirmó el modo de sus perfiles.");

            ushort? activeSector = null;
            if (mode == 0x01)
            {
                var currentRequest = BuildGetCurrentOnboardProfileRequest(deviceIndex, profileFeature.Value, SoftwareId);
                await stream.WriteAsync(currentRequest, cancellationToken);
                var currentResponse = await ReadResponseAsync(stream, deviceIndex, profileFeature.Value, currentRequest[3], cancellationToken);
                if (currentResponse is null || !TryReadCurrentOnboardProfileResponse(currentResponse, deviceIndex,
                    profileFeature.Value, SoftwareId, out var current))
                    throw new IOException("El ratón no confirmó el perfil integrado activo.");
                activeSector = current;
            }

            var directory = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                0, info.SectorSize, cancellationToken);
            if (!TryReadOnboardProfileDirectory(directory, info.SectorCount, info.ProfileCount, out var entries))
                throw new IOException("La lista de perfiles no superó la validación de integridad.");
            var eligible = mode == 0x01
                ? entries.Where(entry => entry.Sector == activeSector && entry.IsEnabled).ToArray()
                : entries.Where(entry => entry.IsEnabled).ToArray();
            if (eligible.Length != 1)
                throw new InvalidOperationException("No se puede elegir un perfil único y habilitado para guardar los DPI.");
            var target = eligible[0];

            var dpiFeature = await ReadFeatureIndexAsync(stream, deviceIndex, 0x2201, cancellationToken);
            if (dpiFeature is null or 0) throw new InvalidOperationException("El sensor no ofrece la lista de DPI compatibles.");
            var currentDpi = await ReadCurrentDpiAsync(stream, deviceIndex, dpiFeature.Value, cancellationToken)
                ?? throw new IOException("No se pudo leer el DPI actual del sensor.");
            var supported = await ReadSensorDpiListAsync(stream, deviceIndex, dpiFeature.Value,
                currentDpi.SensorIndex, cancellationToken)
                ?? throw new IOException("No se pudo leer la lista de DPI admitidos.");
            if (dpiStages.Any(dpi => dpi is < 100 or > 25600 || !supported.Contains(dpi)))
                throw new InvalidOperationException("Una etapa no coincide con los DPI admitidos por este ratón; no se escribió nada.");

            var original = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                target.Sector, info.SectorSize, cancellationToken);
            if (!TryReadBaseV1DpiTable(original, out _, out _))
                throw new IOException("El perfil que se va a modificar no tiene un formato DPI y checksum válidos.");
            var updated = BuildBaseV1DpiProfile(original, dpiStages, defaultDpiIndex);
            var changed = !original.AsSpan().SequenceEqual(updated);
            if (!changed) return new(target.Sector, defaultDpiIndex, dpiStages.ToArray(), false, mode == 0x01);

            var probeEntry = entries.FirstOrDefault(entry => !entry.IsEnabled && entry.Sector != target.Sector && entry.Sector != activeSector);
            if (probeEntry is null) throw new InvalidOperationException("No hay una ranura deshabilitada para validar la escritura de forma reversible.");
            var probeOriginal = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                probeEntry.Sector, info.SectorSize, cancellationToken);
            if (!TryReadBaseV1DpiTable(probeOriginal, out _, out _))
                throw new IOException("La ranura libre no superó el checksum; no se escribirá en el perfil activo.");
            await ProbeProfileWriteSequenceAsync(stream, deviceIndex, profileFeature.Value,
                probeEntry.Sector, probeOriginal, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await WriteOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                    target.Sector, updated, cancellationToken);
                var readBack = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                    target.Sector, info.SectorSize, cancellationToken);
                if (!readBack.AsSpan().SequenceEqual(updated) ||
                    !TryReadBaseV1DpiTable(readBack, out var readDefault, out var readStages) ||
                    readDefault != defaultDpiIndex || !readStages.Take(dpiStages.Count)
                        .Select(value => value ?? -1).SequenceEqual(dpiStages) ||
                    readStages.Skip(dpiStages.Count).Any(stage => stage is not null))
                    throw new IOException("El ratón no devolvió las mismas etapas DPI después de guardar.");
            }
            catch (Exception writeError)
            {
                try
                {
                    await WriteOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                        target.Sector, original, CancellationToken.None);
                    var restored = await ReadOnboardProfileSectorAsync(stream, deviceIndex, profileFeature.Value,
                        target.Sector, info.SectorSize, CancellationToken.None);
                    if (!restored.AsSpan().SequenceEqual(original))
                        throw new IOException("La lectura no coincide con la copia previa.");
                }
                catch (Exception restoreError)
                {
                    throw new IOException("Falló la escritura y tampoco se pudo confirmar la restauración del perfil. No desconectes el ratón y revisa su perfil.",
                        new AggregateException(writeError, restoreError));
                }
                throw new IOException("Falló la escritura; el perfil original se restauró y se leyó de vuelta.", writeError);
            }

            return new(target.Sector, defaultDpiIndex, dpiStages.ToArray(), true, mode == 0x01);
        }

        throw new IOException("El ratón no respondió en los canales HID++ probados.");
    }

    public async Task<int> SetCurrentDpiAsync(
        HardwareDevice device, int dpi, CancellationToken cancellationToken = default)
    {
        if (device.VendorId != 0x046D || device.ModelKey is not ("logitech-pro-wireless" or "logitech-pro-x-superlight") ||
            device.ModelKey == "logitech-pro-wireless" && device.ProductId != 0xC539 ||
            device.ModelKey == "logitech-pro-x-superlight" && device.ProductId != 0xC547)
            throw new InvalidOperationException("La escritura de DPI está limitada a un modelo Logitech confirmado por HID++.");
        if (dpi is < 100 or > 25600) throw new ArgumentOutOfRangeException(nameof(dpi));

        var endpoint = (device.HidInterfaces ?? []).FirstOrDefault(item => item.UsagePage == 0xFF00 &&
            item.Usage == 0x0002 && item.InputReportBytes == LongReportLength &&
            item.OutputReportBytes == LongReportLength && item.Path.Length > 0);
        if (endpoint is null)
            throw new InvalidOperationException("No se encontró la colección HID++ de 20 bytes del ratón.");

        using var handle = CreateFile(endpoint.Path, GenericRead | GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows no pudo abrir la colección HID++ del ratón.");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, LongReportLength, isAsync: true);

        var anySlotResponded = false;
        foreach (var deviceIndex in DeviceIndexCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameAndTypeIndex = await ReadFeatureIndexAsync(stream, deviceIndex, 0x0005, cancellationToken);
            if (nameAndTypeIndex is null) continue;
            anySlotResponded = true;
            if (nameAndTypeIndex == 0) continue;
            var protocol = await ReadProtocolVersionAsync(stream, deviceIndex, cancellationToken);
            if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major)) continue;
            if (await ReadDeviceTypeAsync(stream, deviceIndex, nameAndTypeIndex.Value, cancellationToken) != HidppMouseDeviceType)
                continue;
            var deviceName = await ReadDeviceNameAsync(stream, deviceIndex, nameAndTypeIndex.Value, cancellationToken);
            if (deviceName is null || ModelKeyFromDeviceName(deviceName) != device.ModelKey) continue;

            var dpiFeatureIndex = await ReadFeatureIndexAsync(stream, deviceIndex, 0x2201, cancellationToken);
            if (dpiFeatureIndex is null or 0) continue;
            var current = await ReadCurrentDpiAsync(stream, deviceIndex, dpiFeatureIndex.Value, cancellationToken)
                ?? throw new IOException("El ratón no respondió al leer el DPI actual. No se cambió el DPI.");
            var supportedDpi = await ReadSensorDpiListAsync(stream, deviceIndex, dpiFeatureIndex.Value,
                current.SensorIndex, cancellationToken);
            if (supportedDpi is null || !supportedDpi.Contains(dpi))
                throw new InvalidOperationException("El ratón no confirmó ese valor DPI. No se cambió el DPI.");
            if (current.CurrentDpi == dpi) return dpi;

            await SetSensorDpiAsync(stream, deviceIndex, dpiFeatureIndex.Value,
                current.SensorIndex, dpi, cancellationToken);
            var readback = await ReadCurrentDpiAsync(stream, deviceIndex, dpiFeatureIndex.Value, cancellationToken);
            if (readback?.CurrentDpi == dpi) return dpi;

            var restored = readback?.CurrentDpi == current.CurrentDpi;
            if (!restored)
            {
                await SetSensorDpiAsync(stream, deviceIndex, dpiFeatureIndex.Value,
                    current.SensorIndex, current.CurrentDpi, cancellationToken);
                var restoreReadback = await ReadCurrentDpiAsync(stream, deviceIndex, dpiFeatureIndex.Value, cancellationToken);
                restored = restoreReadback?.CurrentDpi == current.CurrentDpi;
            }

            throw new IOException(restored
                ? $"El ratón no confirmó {dpi:N0} DPI; se verificó la restauración de {current.CurrentDpi:N0} DPI."
                : $"No se confirmó {dpi:N0} DPI y tampoco se pudo verificar la restauración de {current.CurrentDpi:N0} DPI.");
        }

        throw anySlotResponded
            ? new InvalidOperationException("El ratón respondió, pero no confirmó un sensor DPI compatible. No se cambió el DPI.")
            : new IOException("El ratón no respondió a la comprobación. No se cambió el DPI.");
    }

    public async Task<IReadOnlyList<LogitechDeviceIdentity>> ReadDeviceIdentitiesAsync(
        HardwareDevice device, CancellationToken cancellationToken = default)
    {
        if (device.VendorId != 0x046D) throw new InvalidOperationException("El dispositivo no es Logitech.");
        var endpoint = (device.HidInterfaces ?? []).FirstOrDefault(item => item.UsagePage == 0xFF00 &&
            item.Usage == 0x0002 && item.InputReportBytes == LongReportLength &&
            item.OutputReportBytes == LongReportLength && item.Path.Length > 0);
        if (endpoint is null) throw new InvalidOperationException("No se encontró una colección HID++ de 20 bytes.");

        using var handle = CreateFile(endpoint.Path, GenericRead | GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows no pudo abrir la colección HID++.");
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, LongReportLength, isAsync: true);

        var devices = new List<LogitechDeviceIdentity>();
        foreach (var deviceIndex in DeviceIndexCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nameAndTypeIndex = await ReadFeatureIndexAsync(stream, deviceIndex, 0x0005, cancellationToken);
            if (nameAndTypeIndex is null or 0) continue;
            var protocol = await ReadProtocolVersionAsync(stream, deviceIndex, cancellationToken);
            if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major)) continue;
            var type = await ReadDeviceTypeAsync(stream, deviceIndex, nameAndTypeIndex.Value, cancellationToken);
            if (type is null) continue;
            var name = await ReadDeviceNameAsync(stream, deviceIndex, nameAndTypeIndex.Value, cancellationToken);
            var firmwareFeatureIndex = await ReadFeatureIndexAsync(stream, deviceIndex, DeviceInformationFeatureId, cancellationToken);
            var firmware = firmwareFeatureIndex is > 0
                ? await ReadFirmwareInfoAsync(stream, deviceIndex, firmwareFeatureIndex.Value, cancellationToken)
                : [];
            devices.Add(new(deviceIndex, name ?? string.Empty, type, protocol.Value.Major, protocol.Value.Minor)
            {
                Firmware = firmware
            });
            if (deviceIndex == 0xFF && type == HidppMouseDeviceType) return devices;
        }

        return devices;
    }

    public async Task<LogitechReceiverPairingInfo?> ReadReceiverPairingInfoAsync(
        HardwareDevice device, CancellationToken cancellationToken = default)
    {
        var pairings = await ReadReceiverPairingInfosAsync(device, cancellationToken, stopWhenMatchingModel: true);
        return pairings.FirstOrDefault(pairing => pairing.DeviceKind == 2) ?? pairings.FirstOrDefault();
    }

    public async Task<IReadOnlyList<LogitechReceiverPairingInfo>> ReadReceiverPairingInfosAsync(
        HardwareDevice device, CancellationToken cancellationToken = default, bool stopWhenMatchingModel = false)
    {
        if (device.VendorId != 0x046D || device.ProductId is not (0xC539 or 0xC547))
            throw new InvalidOperationException("La lectura de emparejamiento requiere un receptor Logitech C539 o C547.");

        var interfaces = device.HidInterfaces ?? [];
        var shortEndpoint = interfaces.FirstOrDefault(item => item.UsagePage == 0xFF00 && item.Usage == 0x0001 &&
            item.InputReportBytes == ShortReportLength && item.OutputReportBytes == ShortReportLength && item.Path.Length > 0);
        var longEndpoint = interfaces.FirstOrDefault(item => item.UsagePage == 0xFF00 && item.Usage == 0x0002 &&
            item.InputReportBytes == LongReportLength && item.OutputReportBytes == LongReportLength && item.Path.Length > 0);
        if (shortEndpoint is null || longEndpoint is null)
            throw new InvalidOperationException("Faltan las colecciones HID++ corta o larga del receptor.");

        using var writeHandle = CreateFile(shortEndpoint.Path, GenericWrite, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (writeHandle.IsInvalid) throw new IOException("Windows no pudo abrir el canal corto del receptor Logitech.");
        using var readHandle = CreateFile(longEndpoint.Path, GenericRead, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (readHandle.IsInvalid) throw new IOException("Windows no pudo abrir el canal largo del receptor Logitech.");

        await using var writeStream = new FileStream(writeHandle, FileAccess.Write, ShortReportLength, isAsync: true);
        await using var readStream = new FileStream(readHandle, FileAccess.Read, LongReportLength, isAsync: true);
        var pairings = new List<LogitechReceiverPairingInfo>();
        var expectedModelKey = device.ProductId == 0xC539 ? "logitech-pro-wireless" : "logitech-pro-x-superlight";
        for (byte slot = 1; slot <= 6; slot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildGetReceiverPairingInfoRequest(slot);
            var responseTask = ReadReceiverRegisterResponseAsync(readStream, request[4], cancellationToken);
            await writeStream.WriteAsync(request, cancellationToken);
            var response = await responseTask;
            if (response is null || !TryReadReceiverPairingInfoResponse(response, slot, out var wpid, out var kind))
                continue;

            string? name = null;
            if (kind == 2 && !stopWhenMatchingModel)
            {
                var nameRequest = BuildGetReceiverDeviceNameRequest(slot);
                var nameResponseTask = ReadReceiverRegisterResponseAsync(readStream, nameRequest[4], cancellationToken);
                await writeStream.WriteAsync(nameRequest, cancellationToken);
                var nameResponse = await nameResponseTask;
                if (nameResponse is not null && TryReadReceiverDeviceNameResponse(nameResponse, slot, out var storedName))
                    name = storedName;
            }
            pairings.Add(new(slot, wpid, kind, name));
            if (stopWhenMatchingModel && ModelFromWirelessProductId(wpid)?.ModelKey == expectedModelKey)
                return pairings;
        }

        return pairings;
    }

    public static byte[] BuildGetReceiverPairingInfoRequest(byte slot)
    {
        if (slot is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(slot));
        return [ShortReportId, 0xFF, 0x83, 0xB5, (byte)(0x20 + slot - 1), 0, 0];
    }

    public static byte[] BuildGetReceiverDeviceNameRequest(byte slot)
    {
        if (slot is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(slot));
        return [ShortReportId, 0xFF, 0x83, 0xB5, (byte)(0x40 + slot - 1), 0, 0];
    }

    public static bool TryReadReceiverPairingInfoResponse(
        ReadOnlySpan<byte> response, byte slot, out ushort wirelessProductId, out byte deviceKind)
    {
        wirelessProductId = 0;
        deviceKind = 0;
        if (slot is < 1 or > 6 || response.Length != LongReportLength || response[0] != LongReportId ||
            response[1] != 0xFF || response[2] != 0x83 || response[3] != 0xB5 || response[4] != 0x20 + slot - 1)
            return false;

        wirelessProductId = (ushort)((response[7] << 8) | response[8]);
        deviceKind = (byte)(response[11] & 0x0F);
        return wirelessProductId != 0 && deviceKind != 0;
    }

    public static bool TryReadReceiverDeviceNameResponse(ReadOnlySpan<byte> response, byte slot, out string deviceName)
    {
        deviceName = string.Empty;
        if (slot is < 1 or > 6 || response.Length != LongReportLength || response[0] != LongReportId ||
            response[1] != 0xFF || response[2] != 0x83 || response[3] != 0xB5 ||
            response[4] != 0x40 + slot - 1 || response[5] is 0 or > 14)
            return false;

        deviceName = Encoding.ASCII.GetString(response.Slice(6, response[5])).TrimEnd('\0');
        return deviceName.Length > 0;
    }

    public static (string ModelKey, string DeviceName)? ModelFromWirelessProductId(ushort wirelessProductId) => wirelessProductId switch
    {
        0x4079 => ("logitech-pro-wireless", "PRO WIRELESS"),
        0x4093 => ("logitech-pro-x-superlight", "PRO X SUPERLIGHT"),
        _ => null
    };

    public static string? ModelKeyFromDeviceName(string deviceName)
    {
        var normalized = new string(deviceName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized switch
        {
            "prowireless" or "gprowireless" or "gprowirelessgamingmouse" => "logitech-pro-wireless",
            "proxsuperlight" or "gproxsuperlight" or "proxwireless" => "logitech-pro-x-superlight",
            _ => null
        };
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

    public static byte[] BuildGetOnboardProfilesInfoRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = softwareId;
        return report;
    }

    public static bool TryReadOnboardProfilesInfoResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId,
        out LogitechOnboardProfilesInfo? info)
    {
        info = null;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != softwareId) return false;

        info = new(response[4], response[5], response[6], response[7], response[8], response[9], response[10],
            (ushort)((response[11] << 8) | response[12]), response[13], response[14], response[15..20].ToArray());
        return true;
    }

    public static byte[] BuildReadOnboardProfileMemoryRequest(
        byte deviceIndex, byte featureIndex, ushort sector, ushort offset, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || offset > 0x00EF)
            throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x50 | softwareId);
        report[4] = (byte)(sector >> 8);
        report[5] = (byte)sector;
        report[6] = (byte)(offset >> 8);
        report[7] = (byte)offset;
        return report;
    }

    public static bool TryReadOnboardProfileMemoryResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId,
        out byte[] data)
    {
        data = [];
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x50 | softwareId)) return false;
        data = response[4..20].ToArray();
        return true;
    }

    public static byte[] BuildGetCurrentOnboardProfileRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x40 | softwareId);
        return report;
    }

    public static byte[] BuildGetOnboardProfileModeRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x20 | softwareId);
        return report;
    }

    public static bool TryReadOnboardProfileModeResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out byte mode)
    {
        mode = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x20 | softwareId)) return false;
        mode = response[4];
        return mode is 0x01 or 0x02;
    }

    public static bool TryReadCurrentOnboardProfileResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out ushort sector)
    {
        sector = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x40 | softwareId)) return false;
        sector = (ushort)((response[4] << 8) | response[5]);
        return sector != 0 && sector != ushort.MaxValue;
    }

    public static bool TryReadOnboardProfileDirectory(
        ReadOnlySpan<byte> sector, byte sectorCount, byte expectedProfileCount,
        out IReadOnlyList<LogitechOnboardProfileDirectoryEntry> entries)
    {
        entries = [];
        if (sector.Length < 16 || !HasValidOnboardProfileChecksum(sector)) return false;
        var parsed = new List<LogitechOnboardProfileDirectoryEntry>();
        for (var offset = 0; offset + 3 < sector.Length - 2; offset += 4)
        {
            var profileSector = (ushort)((sector[offset] << 8) | sector[offset + 1]);
            if (profileSector is 0 or ushort.MaxValue) break;
            if (profileSector >= sectorCount || sector[offset + 2] > 1 ||
                parsed.Any(entry => entry.Sector == profileSector)) return false;
            parsed.Add(new(profileSector, sector[offset + 2] == 1));
        }

        if (parsed.Count != expectedProfileCount || parsed.Count == 0)
            return false;
        entries = parsed;
        return true;
    }

    public static bool TryReadBaseV1DpiTable(
        ReadOnlySpan<byte> profile, out byte defaultDpiIndex, out IReadOnlyList<int?> stages)
    {
        defaultDpiIndex = 0;
        stages = [];
        if (profile.Length != 255 || !HasValidOnboardProfileChecksum(profile)) return false;
        defaultDpiIndex = profile[1];
        if (defaultDpiIndex >= 5) return false;

        var parsed = new int?[5];
        for (var index = 0; index < parsed.Length; index++)
        {
            var offset = 3 + index * 2;
            var dpi = profile[offset] | profile[offset + 1] << 8;
            if (dpi is 0 or ushort.MaxValue) continue;
            if (dpi > 57343) return false;
            parsed[index] = dpi;
        }
        if (parsed[defaultDpiIndex] is null) return false;
        stages = parsed;
        return true;
    }

    public static byte[] BuildBaseV1DpiProfile(
        ReadOnlySpan<byte> original, IReadOnlyList<int> dpiStages, byte defaultDpiIndex)
    {
        if (original.Length != 255 || !TryReadBaseV1DpiTable(original, out _, out _) ||
            dpiStages.Count is < 1 or > 5 || defaultDpiIndex >= dpiStages.Count ||
            dpiStages.Any(dpi => dpi is < 100 or > 25600))
            throw new InvalidDataException("El perfil o la selección DPI base-v1 no son válidos.");

        var updated = original.ToArray();
        updated[1] = defaultDpiIndex;
        for (var index = 0; index < 5; index++)
        {
            var dpi = index < dpiStages.Count ? (ushort)dpiStages[index] : ushort.MaxValue;
            updated[3 + index * 2] = (byte)dpi;
            updated[4 + index * 2] = (byte)(dpi >> 8);
        }
        var checksum = ComputeOnboardProfileChecksum(updated);
        updated[^2] = (byte)(checksum >> 8);
        updated[^1] = (byte)checksum;
        return updated;
    }

    public static byte[] BuildStartOnboardProfileWriteRequest(
        byte deviceIndex, byte featureIndex, ushort sector, ushort length, byte softwareId)
    {
        if (featureIndex == 0 || sector == 0 || length is 0 or > 1024 || softwareId is 0 or > 0x0F)
            throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x60 | softwareId);
        report[4] = (byte)(sector >> 8);
        report[5] = (byte)sector;
        report[8] = (byte)(length >> 8);
        report[9] = (byte)length;
        return report;
    }

    public static byte[] BuildOnboardProfileWriteDataRequest(
        byte deviceIndex, byte featureIndex, ReadOnlySpan<byte> data, byte softwareId)
    {
        if (featureIndex == 0 || data.Length != 16 || softwareId is 0 or > 0x0F)
            throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x70 | softwareId);
        data.CopyTo(report.AsSpan(4));
        return report;
    }

    public static byte[] BuildEndOnboardProfileWriteRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x80 | softwareId);
        return report;
    }

    public static ushort ComputeOnboardProfileChecksum(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < 3) throw new ArgumentException("Un sector debe incluir datos y dos bytes de checksum.", nameof(sector));
        ushort crc = 0xFFFF;
        foreach (var value in sector[..^2])
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static bool HasValidOnboardProfileChecksum(ReadOnlySpan<byte> sector) =>
        sector.Length >= 3 && ComputeOnboardProfileChecksum(sector) ==
        (ushort)((sector[^2] << 8) | sector[^1]);

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

    public static byte[] BuildGetDeviceNameLengthRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = softwareId;
        return report;
    }

    public static bool TryReadDeviceNameLengthResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out byte nameLength)
    {
        nameLength = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != softwareId) return false;
        nameLength = response[4];
        return nameLength is > 0 and <= 128;
    }

    public static byte[] BuildGetDeviceNameChunkRequest(
        byte deviceIndex, byte featureIndex, byte characterIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x10 | softwareId);
        report[4] = characterIndex;
        return report;
    }

    public static byte[] BuildGetBatteryLevelStatusRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = softwareId;
        return report;
    }

    public static byte[] BuildGetFirmwareEntityCountRequest(byte deviceIndex, byte featureIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        return [LongReportId, deviceIndex, featureIndex, softwareId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    }

    public static bool TryReadFirmwareEntityCountResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, out byte entityCount)
    {
        entityCount = 0;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != softwareId || response[4] > 32) return false;
        entityCount = response[4];
        return true;
    }

    public static byte[] BuildGetFirmwareInfoRequest(byte deviceIndex, byte featureIndex, byte entityIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        return [LongReportId, deviceIndex, featureIndex, (byte)(0x10 | softwareId), entityIndex,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    }

    public static bool TryReadFirmwareInfoResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId, byte entityIndex,
        out LogitechFirmwareReading? firmware)
    {
        firmware = null;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x10 | softwareId) || response[4] > 0x0F) return false;

        var firmwareType = response[4];
        if (firmwareType == 2)
        {
            firmware = new(entityIndex, firmwareType, string.Empty, null, null, response[5]);
            return true;
        }

        var prefixBytes = response.Slice(5, 3);
        foreach (var value in prefixBytes)
            if (value is not (0 or >= 0x20 and <= 0x7E)) return false;
        var prefix = Encoding.ASCII.GetString(prefixBytes).TrimEnd('\0', ' ');
        var version = (ushort)((response[8] << 8) | response[9]);
        var build = (ushort)((response[10] << 8) | response[11]);
        firmware = new(entityIndex, firmwareType, prefix, version, build, null);
        return true;
    }

    public static bool TryReadBatteryLevelStatusResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId,
        out LogitechBatteryReading? battery)
    {
        battery = null;
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != softwareId || response[4] > 100 || response[5] > 100 || response[6] > 7 ||
            response[5] > 0 && response[5] > response[4]) return false;
        battery = new(response[4] == 0 ? null : response[4], response[5], response[6]);
        return true;
    }

    public static bool TryReadDeviceNameChunkResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte characterIndex, byte softwareId,
        Span<byte> destination)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x10 | softwareId) || destination.Length > 16) return false;
        response.Slice(4, destination.Length).CopyTo(destination);
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

    public static byte[] BuildGetSensorDpiListRequest(byte deviceIndex, byte featureIndex, byte sensorIndex, byte softwareId)
    {
        if (featureIndex == 0 || softwareId is 0 or > 0x0F) throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x10 | softwareId);
        report[4] = sensorIndex;
        return report;
    }

    public static bool TryReadSensorDpiListResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte sensorIndex, byte softwareId,
        out IReadOnlyList<int> dpiValues)
    {
        dpiValues = [];
        if (featureIndex == 0 || softwareId is 0 or > 0x0F || response.Length != LongReportLength ||
            response[0] != LongReportId || response[1] != deviceIndex || response[2] != featureIndex ||
            response[3] != (byte)(0x10 | softwareId) || response[4] != sensorIndex) return false;

        var values = new List<int>();
        for (var offset = 5; offset + 1 < response.Length; offset += 2)
        {
            var value = (response[offset] << 8) | response[offset + 1];
            if (value == 0) break;
            if (value >= 0xE000)
            {
                var step = value & 0x1FFF;
                if (values.Count == 0 || step == 0 || offset + 3 >= response.Length) return false;
                var end = (response[offset + 2] << 8) | response[offset + 3];
                if (end is 0 or > 57343 || end <= values[^1]) return false;
                for (var dpi = values[^1] + step; dpi < end; dpi += step) values.Add(dpi);
                if (values[^1] != end) values.Add(end);
                offset += 2;
            }
            else
            {
                if (value > 57343 || values.Count > 0 && value <= values[^1]) return false;
                values.Add(value);
            }
        }

        if (values.Count == 0) return false;
        dpiValues = values;
        return true;
    }

    public static byte[] BuildSetSensorDpiRequest(
        byte deviceIndex, byte featureIndex, byte sensorIndex, int dpi, byte softwareId)
    {
        if (featureIndex == 0 || dpi is < 1 or > 57343 || softwareId is 0 or > 0x0F)
            throw new ArgumentOutOfRangeException();
        var report = new byte[LongReportLength];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)(0x30 | softwareId);
        report[4] = sensorIndex;
        report[5] = (byte)(dpi >> 8);
        report[6] = (byte)dpi;
        return report;
    }

    public static bool TryReadSetSensorDpiResponse(
        ReadOnlySpan<byte> response, byte deviceIndex, byte featureIndex, byte softwareId) =>
        featureIndex > 0 && softwareId is > 0 and <= 0x0F && response.Length == LongReportLength &&
        response[0] == LongReportId && response[1] == deviceIndex && response[2] == featureIndex &&
        response[3] == (byte)(0x30 | softwareId);

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

    private static async Task<LogitechOnboardProfilesInfo?> ReadOnboardProfilesInfoAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var request = BuildGetOnboardProfilesInfoRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
        return response is not null && TryReadOnboardProfilesInfoResponse(response, deviceIndex, featureIndex,
            SoftwareId, out var info) ? info : null;
    }

    private static async Task<byte[]> ReadOnboardProfileSectorAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, ushort sector, ushort sectorSize,
        CancellationToken cancellationToken)
    {
        if (sectorSize is < 16 or > 1024) throw new InvalidOperationException("El tamaño del sector no es válido.");
        var bytes = new byte[sectorSize];
        async Task ReadChunkAsync(ushort offset)
        {
            var request = BuildReadOnboardProfileMemoryRequest(deviceIndex, featureIndex, sector, offset, SoftwareId);
            await stream.WriteAsync(request, cancellationToken);
            var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
            if (response is null || !TryReadOnboardProfileMemoryResponse(response, deviceIndex, featureIndex,
                SoftwareId, out var chunk))
                throw new IOException($"El ratón no devolvió el sector {sector} en el offset {offset}.");
            chunk.AsSpan(0, Math.Min(chunk.Length, bytes.Length - offset)).CopyTo(bytes.AsSpan(offset));
        }

        ushort offset = 0;
        for (; offset + 16 <= sectorSize; offset += 16)
            await ReadChunkAsync(offset);
        if (offset < sectorSize) await ReadChunkAsync((ushort)(sectorSize - 16));
        return bytes;
    }

    private static async Task ProbeProfileWriteSequenceAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, ushort sector, byte[] original,
        CancellationToken cancellationToken)
    {
        var offset = Array.FindLastIndex(original, original.Length - 3, original.Length - 2, value => value == 0xFF);
        if (offset < 0) offset = original.Length - 3;
        var probe = original.ToArray();
        probe[offset] ^= 0x01;
        var checksum = ComputeOnboardProfileChecksum(probe);
        probe[^2] = (byte)(checksum >> 8);
        probe[^1] = (byte)checksum;
        Exception? probeError = null;
        try
        {
            await WriteOnboardProfileSectorAsync(stream, deviceIndex, featureIndex, sector, probe, cancellationToken);
            var readBack = await ReadOnboardProfileSectorAsync(stream, deviceIndex, featureIndex,
                sector, (ushort)original.Length, cancellationToken);
            if (!readBack.AsSpan().SequenceEqual(probe) || !HasValidOnboardProfileChecksum(readBack))
                throw new IOException("La prueba reversible de escritura no coincidió con la lectura posterior.");
        }
        catch (Exception exception) { probeError = exception; }

        try
        {
            await WriteOnboardProfileSectorAsync(stream, deviceIndex, featureIndex, sector, original, CancellationToken.None);
            var restored = await ReadOnboardProfileSectorAsync(stream, deviceIndex, featureIndex,
                sector, (ushort)original.Length, CancellationToken.None);
            if (!restored.AsSpan().SequenceEqual(original))
                throw new IOException("La lectura no coincide con la copia original de la ranura libre.");
        }
        catch (Exception restoreError)
        {
            throw new IOException("Falló la prueba y no se pudo confirmar la restauración de la ranura libre.",
                probeError is null ? restoreError : new AggregateException(probeError, restoreError));
        }

        if (probeError is not null)
            throw new IOException("El ratón no confirmó la prueba de escritura; la ranura libre se restauró y verificó.", probeError);
    }

    private static async Task WriteOnboardProfileSectorAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, ushort sector, byte[] bytes,
        CancellationToken cancellationToken)
    {
        var start = BuildStartOnboardProfileWriteRequest(deviceIndex, featureIndex, sector, (ushort)bytes.Length, SoftwareId);
        await stream.WriteAsync(start, cancellationToken);
        if (await ReadResponseAsync(stream, deviceIndex, featureIndex, start[3], cancellationToken,
            WriteResponseTimeoutMs) is null)
            throw new IOException($"El ratón no confirmó el inicio de escritura del sector {sector}.");

        var chunk = new byte[16];
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Fill(chunk, (byte)0xFF);
            bytes.AsSpan(offset, Math.Min(16, bytes.Length - offset)).CopyTo(chunk);
            var request = BuildOnboardProfileWriteDataRequest(deviceIndex, featureIndex, chunk, SoftwareId);
            await stream.WriteAsync(request, cancellationToken);
            if (await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken,
                WriteResponseTimeoutMs) is null)
                throw new IOException($"El ratón no confirmó el bloque de escritura del sector {sector}.");
        }

        var end = BuildEndOnboardProfileWriteRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(end, cancellationToken);
        if (await ReadResponseAsync(stream, deviceIndex, featureIndex, end[3], cancellationToken,
            WriteResponseTimeoutMs) is null)
            throw new IOException($"El ratón no confirmó el cierre de escritura del sector {sector}.");
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

    private static async Task<string?> ReadDeviceNameAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var lengthRequest = BuildGetDeviceNameLengthRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(lengthRequest, cancellationToken);
        var lengthResponse = await ReadResponseAsync(stream, deviceIndex, featureIndex, lengthRequest[3], cancellationToken);
        if (lengthResponse is null || !TryReadDeviceNameLengthResponse(lengthResponse, deviceIndex, featureIndex,
            SoftwareId, out var nameLength)) return null;

        var name = new byte[nameLength];
        for (byte offset = 0; offset < nameLength; offset += 16)
        {
            var chunkLength = Math.Min(16, nameLength - offset);
            var request = BuildGetDeviceNameChunkRequest(deviceIndex, featureIndex, offset, SoftwareId);
            await stream.WriteAsync(request, cancellationToken);
            var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
            if (response is null || !TryReadDeviceNameChunkResponse(response, deviceIndex, featureIndex,
                offset, SoftwareId, name.AsSpan(offset, chunkLength))) return null;
        }

        return Encoding.UTF8.GetString(name).TrimEnd('\0');
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

    private static async Task<IReadOnlyList<int>?> ReadSensorDpiListAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, byte sensorIndex, CancellationToken cancellationToken)
    {
        var request = BuildGetSensorDpiListRequest(deviceIndex, featureIndex, sensorIndex, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
        return response is not null && TryReadSensorDpiListResponse(response, deviceIndex, featureIndex,
            sensorIndex, SoftwareId, out var dpiValues) ? dpiValues : null;
    }

    private static async Task<bool> SetSensorDpiAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, byte sensorIndex, int dpi,
        CancellationToken cancellationToken)
    {
        var request = BuildSetSensorDpiRequest(deviceIndex, featureIndex, sensorIndex, dpi, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
        return response is not null && TryReadSetSensorDpiResponse(response, deviceIndex, featureIndex, SoftwareId);
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

    private static async Task<LogitechBatteryReading?> ReadBatteryLevelStatusAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var request = BuildGetBatteryLevelStatusRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(request, cancellationToken);
        var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
        return response is not null && TryReadBatteryLevelStatusResponse(response, deviceIndex, featureIndex,
            SoftwareId, out var battery) ? battery : null;
    }

    private static async Task<IReadOnlyList<LogitechFirmwareReading>> ReadFirmwareInfoAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, CancellationToken cancellationToken)
    {
        var countRequest = BuildGetFirmwareEntityCountRequest(deviceIndex, featureIndex, SoftwareId);
        await stream.WriteAsync(countRequest, cancellationToken);
        var countResponse = await ReadResponseAsync(stream, deviceIndex, featureIndex, countRequest[3], cancellationToken);
        if (countResponse is null || !TryReadFirmwareEntityCountResponse(countResponse, deviceIndex, featureIndex,
            SoftwareId, out var entityCount)) return [];

        var firmware = new List<LogitechFirmwareReading>(entityCount);
        for (byte entityIndex = 0; entityIndex < entityCount; entityIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildGetFirmwareInfoRequest(deviceIndex, featureIndex, entityIndex, SoftwareId);
            await stream.WriteAsync(request, cancellationToken);
            var response = await ReadResponseAsync(stream, deviceIndex, featureIndex, request[3], cancellationToken);
            if (response is null || !TryReadFirmwareInfoResponse(response, deviceIndex, featureIndex,
                SoftwareId, entityIndex, out var info) || info is null) break;
            firmware.Add(info);
        }

        return firmware;
    }

    private static async Task<IReadOnlyList<LogitechFirmwareReading>> ReadReceiverFirmwareInfoAsync(
        FileStream stream, CancellationToken cancellationToken)
    {
        const byte receiverIndex = 0xFF;
        var protocol = await ReadProtocolVersionAsync(stream, receiverIndex, cancellationToken);
        if (protocol is null || !IsHidpp20ProtocolMajor(protocol.Value.Major)) return [];
        var featureIndex = await ReadFeatureIndexAsync(stream, receiverIndex, DeviceInformationFeatureId, cancellationToken);
        return featureIndex is > 0
            ? await ReadFirmwareInfoAsync(stream, receiverIndex, featureIndex.Value, cancellationToken)
            : [];
    }

    private static async Task<byte[]?> ReadResponseAsync(
        FileStream stream, byte deviceIndex, byte featureIndex, byte functionAndSoftwareId,
        CancellationToken cancellationToken, int timeoutMs = ResponseTimeoutMs)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
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

    private static async Task<byte[]?> ReadReceiverRegisterResponseAsync(
        FileStream stream, byte subRegister, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReceiverRegisterTimeoutMs);
        var report = new byte[LongReportLength];
        while (true)
        {
            int count;
            try { count = await stream.ReadAsync(report, timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            if (count == LongReportLength && report[0] == LongReportId && report[1] == 0xFF &&
                report[2] == 0x83 && report[3] == 0xB5 && report[4] == subRegister)
                return report;
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
    public IReadOnlyDictionary<ushort, byte?> FeatureIndexes { get; init; } = new Dictionary<ushort, byte?>();
    public ushort? FeatureQueryStoppedAt { get; init; }
    public byte? AdjustableDpiFeatureIndex { get; init; }
    public byte? AdjustableReportRateFeatureIndex { get; init; }
    public byte? CurrentReportRateMs { get; init; }
    public LogitechDpiReading? CurrentDpi { get; init; }
    public byte? BatteryFeatureIndex { get; init; }
    public LogitechBatteryReading? Battery { get; init; }
    public IReadOnlyList<LogitechFirmwareReading> Firmware { get; init; } = [];
    public IReadOnlyList<LogitechFirmwareReading> ReceiverFirmware { get; init; } = [];
    public LogitechOnboardProfilesInfo? OnboardProfilesInfo { get; init; }

    public bool HasAdjustableDpi => HasFeature2201 || HasFeature2202;

    public bool MatchesStaticOmmFilter => LogitechHidppCapabilityReader.IsHidpp20ProtocolMajor(ProtocolMajor) && HidppDeviceType is not null && HasDeviceInformation &&
        HasDeviceNameAndType && HasAdjustableDpi && HasOnboardProfiles;
}

public sealed record LogitechDpiReading(byte SensorIndex, byte SensorCount, int CurrentDpi, int? DefaultDpi);
public sealed record LogitechOnboardProfilesInfo(
    byte MemoryModelId, byte ProfileFormatId, byte MacroFormatId, byte ProfileCount, byte ProfileCountOutOfBand,
    byte ButtonCount, byte SectorCount, ushort SectorSize, byte MechanicalLayout, byte VariousInfo,
    IReadOnlyList<byte> Reserved);
public sealed record LogitechOnboardProfileDirectoryEntry(ushort Sector, bool IsEnabled);
public sealed record LogitechStoredProfile(
    ushort Sector, bool IsEnabled, byte DefaultDpiIndex, IReadOnlyList<int?> DpiStages, byte[] RawProfile);
public sealed record LogitechOnboardDpiWriteResult(
    ushort Sector, byte DefaultDpiIndex, IReadOnlyList<int> DpiStages, bool Changed, bool OnboardMode);
public sealed record LogitechOnboardProfileSnapshot(
    LogitechOnboardProfilesInfo Info, byte Mode, ushort? ActiveSector,
    IReadOnlyList<LogitechOnboardProfileDirectoryEntry> Directory,
    IReadOnlyList<LogitechStoredProfile> Profiles, int? LiveDpi, byte[] RawDirectory);
public sealed record LogitechBatteryReading(byte? Level, byte NextLevel, byte Status)
{
    public string StatusLabel => Status switch
    {
        0 => "descargando",
        1 => "recargando",
        2 => "carga en fase final",
        3 => "carga completa",
        4 => "recarga lenta",
        5 => "tipo de batería inválido",
        6 => "error térmico",
        7 => "otro error de carga",
        _ => "desconocido"
    };
}
public sealed record LogitechFirmwareReading(
    byte EntityIndex, byte FirmwareType, string Prefix, ushort? Version, ushort? Build, byte? HardwareVersion)
{
    public string Summary => HardwareVersion is byte hardwareVersion
        ? $"hardware 0x{hardwareVersion:X2}"
        : $"{Prefix} versión 0x{Version:X4}, build 0x{Build:X4} (valores HID++ brutos)";
}
public sealed record LogitechDeviceIdentity(byte DeviceIndex, string DeviceName, byte? DeviceType, byte ProtocolMajor, byte ProtocolMinor)
{
    public IReadOnlyList<LogitechFirmwareReading> Firmware { get; init; } = [];
}
public sealed record LogitechReceiverPairingInfo(byte Slot, ushort WirelessProductId, byte DeviceKind, string? DeviceName)
{
    public bool LiveActivityConfirmed => false;
}
