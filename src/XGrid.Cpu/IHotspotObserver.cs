namespace XGrid.Cpu;

/// <summary>
/// Optional per-instruction hotspot observer for JIT scoping (§126).
/// The CPU holds the reference as null by default, so flag-off runs are
/// byte-identical; when set, the CPU reports every executed instruction,
/// fetch window, SoftTLB outcome, and translated data access.
/// </summary>
public interface IHotspotObserver
{
    /// <summary>
    /// Records one executed instruction. Called after every instruction from
    /// <c>XboxCpu.Step()</c>, at the same site as the idle-skip observer.
    /// </summary>
    void Observe(in DecodedInstruction instruction, uint eipBefore, uint eipAfter);

    /// <summary>
    /// Records one instruction-fetch window. Called once per step before the
    /// fetch bytes are read; <paramref name="crossesPage"/> is true when the
    /// 15-byte fetch window spans a 4 KB page boundary.
    /// </summary>
    void NoteFetch(bool crossesPage);

    /// <summary>Records one <c>SoftTlb.TryLookup</c> hit (paging enabled, side effects on).</summary>
    void NoteTlbHit();

    /// <summary>
    /// Records one <c>SoftTlb.TryLookup</c> miss (page-walk and fault paths;
    /// 4 MB large pages never sit in the TLB so they always miss here).
    /// </summary>
    void NoteTlbMiss();

    /// <summary>
    /// Records a batch of <c>SoftTlb.TryLookup</c> hits (batch tallies, §131:
    /// the 15-byte fetch loop accumulates per-byte outcomes and reports once,
    /// so profile runs pay one interface dispatch instead of fifteen; the
    /// reported total is identical to fifteen <c>NoteTlbHit</c> calls).
    /// </summary>
    void NoteTlbHits(int count);

    /// <summary>
    /// Records a batch of <c>SoftTlb.TryLookup</c> misses (batch tallies, §131:
    /// same batching contract as <see cref="NoteTlbHits"/>).
    /// </summary>
    void NoteTlbMisses(int count);

    /// <summary>
    /// Records one translated data access through <c>ReadMemory</c>/
    /// <c>WriteMemory</c> (operand and stack traffic, paging on or off;
    /// instruction fetches and direct descriptor/gate reads excluded).
    /// One call per access, except cross-page splits under paging, which
    /// report one call per translated byte alongside a single
    /// <c>NoteSplitAccess</c> (with paging off there is no per-byte
    /// translation, so a split reports one access plus the split marker).
    /// </summary>
    void NoteDataAccess(bool isWrite, bool isRam);

    /// <summary>Records one multi-byte data access that crossed a 4 KB page (byte-by-byte path).</summary>
    void NoteSplitAccess();

    /// <summary>
    /// Records one fetch window served by the slow per-byte path while the
    /// fetch fast path was enabled (page-crossing fallback, §131 leg (c)).
    /// Fast-path windows report nothing here, so fast coverage is
    /// fetches-minus-fallbacks (see <c>HotspotProfiler.FetchSlowFallbacks</c>).
    /// </summary>
    void NoteFetchSlow();
}
