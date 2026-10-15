using NexoHub.Models;

namespace NexoHub.Adapters;

public interface IDeviceAdapter
{
    bool Matches(HardwareDevice device);
    AdapterInfo Describe(HardwareDevice device);
}
