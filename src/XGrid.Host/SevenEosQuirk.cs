using System;

namespace XGrid.Host;

/// <summary>
/// Compatibility quirk for 007: Agent Under Fire (TitleId 0x4541000D) parked
/// STRM end-of-stream stall (research notes §89-§90).
/// No emulation layer is at fault; the guest's single-threaded pipeline stalls
/// with the final chunk memcpy'd but never linked. The quirk synthesizes exactly
/// what the missing completion would have produced (state 2-&gt;0) so the waiter
/// exits through its own (b) path (0x2A830 != 0), never bypassing game code.
/// Gated to the exact parked signature + TitleId, stable-park delayed, default-on
/// with opt-out.
/// </summary>
public static class SevenEosQuirk
{
    /// <summary>007: Agent Under Fire title identifier.</summary>
    private const uint TitleId007 = 0x4541000Du;

    /// <summary>XBE header base virtual address.</summary>
    private const uint XbeHeaderVa = 0x10000u;

    /// <summary>Offset of the certificate pointer in the XBE header.</summary>
    private const uint CertPointerOffset = 0x118u;

    /// <summary>Offset of TitleId within the certificate.</summary>
    private const uint TitleIdOffsetInCert = 0x08u;

    /// <summary>Parked stream struct virtual address.</summary>
    private const uint StrmVa = 0x80B6C800u;

    /// <summary>Waiter handle virtual address (points at <see cref="StrmVa"/>).</summary>
    private const uint HandleVa = 0x80B6CBE8u;

    /// <summary>Offset of state field within STRM (2 = refill-pending).</summary>
    private const uint StrmStateOffset = 0x44u;

    /// <summary>Offset of remaining field within the handle (0 = empty).</summary>
    private const uint HandleRemainingOffset = 0x08u;

    /// <summary>Offset of the current-chunk pointer within the handle.</summary>
    private const uint HandleCurrentOffset = 0x0Cu;

    /// <summary>Offset of the staged (landed but unlinked) buffer pointer within STRM.</summary>
    private const uint StrmStagedOffset = 0x60u;

    /// <summary>STRM struct magic ("STRM").</summary>
    private const uint StrmMagic = 0x4D525453u;

    /// <summary>Stream chunk tag ("MPCh").</summary>
    private const uint MpchMagic = 0x6843504Du;

    /// <summary>Consumed queue-entry marker (written by the release path).</summary>
    // ReSharper disable once UnusedMember.Global
    public const uint ConsumedMarker = 0xFFFFFFFEu;

    /// <summary>Stability delay in guest steps before firing (filters transients).</summary>
    public const ulong StabilityThresholdSteps = 10_000_000uL;

    /// <summary>
    /// Checks the pure parked signature (no MMU). Returns true only for the exact
    /// parked state: correct title, STRM magic, handle linkage, remaining 0,
    /// state 2, staged MPCh present, current entry not MPCh (unlinked).
    /// </summary>
    public static bool IsParked(
        uint titleId,
        uint strmMagic,
        uint handleTarget,
        uint remaining,
        uint state,
        bool stagedPresent,
        uint stagedTag,
        bool currentPresent,
        uint currentMarker)
    {
        if (titleId != TitleId007)
        {
            return false;
        }

        if (strmMagic != StrmMagic)
        {
            return false;
        }

        if (handleTarget != StrmVa)
        {
            return false;
        }

        if (remaining != 0)
        {
            return false;
        }

        if (state != 2)
        {
            return false;
        }

        if (!stagedPresent || stagedTag != MpchMagic)
        {
            return false;
        }

        if (!currentPresent)
        {
            return false;
        }

        // Tag present in the staged buffer but not linked into the consumable
        // queue: the current entry must not be an MPCh chunk.
        return currentMarker != MpchMagic;
    }

    /// <summary>
    /// Probes guest memory through delegates and synthesizes the missing EOS
    /// completion (state 2-&gt;0) when the parked signature holds.
    /// </summary>
    /// <param name="readVa">Reads a guest virtual address; null when unmapped.</param>
    /// <param name="writeVa">Writes a guest virtual address.</param>
    /// <returns>True when the quirk fired (state was advanced).</returns>
    public static bool TryApply(Func<uint, uint?> readVa, Action<uint, uint> writeVa)
    {
        ArgumentNullException.ThrowIfNull(readVa);
        ArgumentNullException.ThrowIfNull(writeVa);

        if (!IsParkedLive(readVa))
        {
            return false;
        }

        writeVa(StrmVa + StrmStateOffset, 0u);
        return true;
    }

    /// <summary>
    /// Checks the live parked signature through a reader delegate without writing.
    /// </summary>
    /// <param name="readVa">Reads a guest virtual address; null when unmapped.</param>
    /// <returns>True when the exact parked signature holds.</returns>
    public static bool IsParkedLive(Func<uint, uint?> readVa)
    {
        ArgumentNullException.ThrowIfNull(readVa);

        uint? certPtr = readVa(XbeHeaderVa + CertPointerOffset);
        if (certPtr is null)
        {
            return false;
        }

        uint? titleId = readVa(certPtr.Value + TitleIdOffsetInCert);
        if (titleId is null)
        {
            return false;
        }

        uint? strmMagic = readVa(StrmVa);
        if (strmMagic is null)
        {
            return false;
        }

        uint? handleTarget = readVa(HandleVa);
        if (handleTarget is null)
        {
            return false;
        }

        uint? remaining = readVa(HandleVa + HandleRemainingOffset);
        if (remaining is null)
        {
            return false;
        }

        uint? state = readVa(StrmVa + StrmStateOffset);
        if (state is null)
        {
            return false;
        }

        uint? stagedPtr = readVa(StrmVa + StrmStagedOffset);
        if (stagedPtr is null)
        {
            return false;
        }

        uint? stagedTag = readVa(stagedPtr.Value);
        uint? currentPtr = readVa(HandleVa + HandleCurrentOffset);
        if (currentPtr is null)
        {
            return false;
        }

        uint? currentMarker = readVa(currentPtr.Value);

        return IsParked(
            titleId.Value,
            strmMagic.Value,
            handleTarget.Value,
            remaining.Value,
            state.Value,
            stagedTag.HasValue,
            stagedTag.GetValueOrDefault(),
            currentMarker.HasValue,
            currentMarker.GetValueOrDefault());
    }
}
