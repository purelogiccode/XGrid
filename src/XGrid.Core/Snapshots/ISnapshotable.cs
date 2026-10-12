using System.IO;

namespace XGrid.Core;

/// <summary>V1 snapshot contract (plan V1). Implemented by CPU, RAM, and
/// every device with restorable state.</summary>
public interface ISnapshotable
{
    void SaveState(BinaryWriter writer);
    void LoadState(BinaryReader reader);
}
