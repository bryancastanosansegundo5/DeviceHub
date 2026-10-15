using System.Runtime.InteropServices;
using NexoHub.Models;

namespace NexoHub.Services;

/// <summary>Reads and changes the Windows endpoint volume. It never writes vendor HID reports.</summary>
public sealed class WindowsAudioEndpointService
{
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public IReadOnlyList<AudioEndpointInfo> Enumerate()
    {
        var result = new List<AudioEndpointInfo>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            AddFlow(enumerator, AudioFlow.Capture, result);
            AddFlow(enumerator, AudioFlow.Render, result);
            return result;
        }
        finally { Release(enumerator); }
    }

    public int SetLevel(string endpointId, int level)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) throw new ArgumentException("Falta el identificador del dispositivo de audio.", nameof(endpointId));
        level = Math.Clamp(level, 0, 100);
        var device = GetEndpoint(endpointId);
        IAudioEndpointVolume? endpoint = null;
        try
        {
            var endpointVolumeId = EndpointVolumeId;
            device.Activate(ref endpointVolumeId, 23, IntPtr.Zero, out var volumeObject);
            endpoint = (IAudioEndpointVolume)volumeObject;
            var eventContext = Guid.Empty;
            endpoint.SetMasterVolumeLevelScalar(level / 100f, ref eventContext);
            endpoint.GetMasterVolumeLevelScalar(out var actual);
            return (int)Math.Round(actual * 100);
        }
        finally { Release(endpoint); Release(device); }
    }

    private static IMMDevice GetEndpoint(string endpointId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            enumerator.GetDevice(endpointId, out var endpoint);
            return endpoint;
        }
        finally { Release(enumerator); }
    }

    private static void AddFlow(IMMDeviceEnumerator enumerator, AudioFlow flow, List<AudioEndpointInfo> target)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator.EnumAudioEndpoints(flow == AudioFlow.Capture ? 1 : 0, 1, out collection);
            collection.GetCount(out var count);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                IAudioEndpointVolume? volume = null;
                try
                {
                    collection.Item(index, out device);
                    device.GetId(out var idPointer);
                    var id = Marshal.PtrToStringUni(idPointer) ?? string.Empty;
                    Marshal.FreeCoTaskMem(idPointer);
                    var name = ReadFriendlyName(device);
                    var endpointVolumeId = EndpointVolumeId;
                    device.Activate(ref endpointVolumeId, 23, IntPtr.Zero, out var volumeObject);
                    volume = (IAudioEndpointVolume)volumeObject;
                    volume.GetMasterVolumeLevelScalar(out var scalar);
                    target.Add(new AudioEndpointInfo(id, name, flow, (int)Math.Round(scalar * 100)));
                }
                catch (COMException) { }
                finally
                {
                    Release(volume);
                    Release(device);
                }
            }
        }
        catch (COMException) { }
        finally { Release(collection); }
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            device.OpenPropertyStore(0, out store);
            var friendlyNameKey = FriendlyNameKey;
            store.GetValue(ref friendlyNameKey, out var value);
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "Audio endpoint" : "Audio endpoint"; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice endpoint);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid interfaceId, uint classContext, IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        void OpenPropertyStore(uint access, out IPropertyStore properties);
        void GetId(out IntPtr id);
        void GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out uint channelCount);
        void SetMasterVolumeLevel(float level, ref Guid eventContext);
        void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        void GetMasterVolumeLevel(out float level);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        void GetChannelVolumeLevel(uint channel, out float level);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        void GetVolumeStepInfo(out uint step, out uint stepCount);
        void VolumeStepUp(ref Guid eventContext);
        void VolumeStepDown(ref Guid eventContext);
        void QueryHardwareSupport(out uint hardwareSupportMask);
        void GetVolumeRange(out float min, out float max, out float increment);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
