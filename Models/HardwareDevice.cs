namespace NexoHub.Models;

public sealed record HardwareDevice(
    string Id,
    string Manufacturer,
    string Product,
    ushort VendorId,
    ushort ProductId,
    string Transport,
    int InterfaceCount,
    string Detail,
    int? XInputSlot = null,
    string ModelKey = "",
    DeviceKind Kind = DeviceKind.Generic,
    string InstanceId = "",
    string ContainerId = "",
    bool IsAuxiliary = false,
    string IdentificationNote = "",
    IReadOnlyList<AudioEndpointInfo>? AudioEndpoints = null);

public enum DeviceKind
{
    Generic,
    Mouse,
    Microphone,
    Headset,
    Gamepad,
    Keyboard,
    Receiver,
    Lighting
}

public enum AudioFlow
{
    Render,
    Capture
}

public sealed record AudioEndpointInfo(string Id, string Name, AudioFlow Flow, int Level);

public sealed record CapabilityInfo(string Name, string Status, string Detail);

public sealed record AdapterInfo(
    string Brand,
    string DisplayName,
    string Status,
    string Description,
    IReadOnlyList<CapabilityInfo> Capabilities);
