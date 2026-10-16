using System.IO;

namespace XGrid.Core;

/// <summary>V1 snapshot contract (plan V1). Implemented by CPU, RAM, and
/// every device with restorable state.</summary>
public interface ISnapshotable
{
    /// <summary>Writes the current device state to the snapshot stream.</summary>
    /// <param name="writer">The snapshot writer to write state to.</param>
    void SaveState(BinaryWriter writer);
    /// <summary>Restores device state from the snapshot stream.</summary>
    /// <param name="reader">The snapshot reader to read state from.</param>
    void LoadState(BinaryReader reader);
}
