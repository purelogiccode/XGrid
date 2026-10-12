namespace XGrid.Bus.Net;

/// <summary>
/// Guest-physical byte access used by the nvnet packet engine for descriptor-ring
/// walks and packet DMA. Same shape as the IDE <c>IGuestMemory</c> pattern, kept as a
/// separate interface in <c>XGrid.Bus</c> because <c>XGrid.Storage</c> already references
/// <c>XGrid.Bus</c> (a shared reference would be a project cycle).
/// </summary>
public interface INvNetMemory
{
    /// <summary>Reads one byte at a guest physical address.</summary>
    byte ReadPhysical(uint address);

    /// <summary>Writes one byte at a guest physical address.</summary>
    void WritePhysical(uint address, byte value);
}
