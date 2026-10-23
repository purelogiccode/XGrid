// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c (the
// nonparallel_opcodes table) and interp/dsp_emu.c.inc (the opcodes_alu and
// opcodes_parmove tables).
// Original copyright (c) 2015 espes; (c) 2020-2025 Matt Borgerson.
// Adapted from Hatari DSP M56001 emulation (C) 2001-2008 ARAnyM developer team.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>
/// One row of the DSP56300 decode table: a 24-bit template with fixed and
/// wildcard bits, the reference mnemonic and the handler.
/// </summary>
internal sealed class DspOpcode
{
    /// <summary>Initializes a new instance of the <see cref="DspOpcode"/> class.</summary>
    /// <param name="template">The 24-character template (0/1 fixed, other = wildcard).</param>
    /// <param name="name">The reference mnemonic.</param>
    /// <param name="handler">The handler, or null for an undefined instruction.</param>
    /// <param name="matcher">The extra predicate, or null.</param>
    public DspOpcode(string template, string name, Action? handler, Func<uint, bool>? matcher)
    {
        Name = name;
        Handler = handler;
        Matcher = matcher;

        uint mask = 0;
        uint match = 0;
        for (int j = 0; j < 24; j++)
        {
            char c = template[j];
            if (c is '0' or '1')
            {
                mask |= 1u << (24 - j - 1);
                match |= (uint)(c - '0') << (24 - j - 1);
            }
        }

        Mask = mask;
        Match = match;
    }

    /// <summary>Gets the reference mnemonic.</summary>
    public string Name { get; }

    /// <summary>Gets the handler, or null for an undefined instruction.</summary>
    public Action? Handler { get; }

    /// <summary>Gets the extra predicate, or null.</summary>
    public Func<uint, bool>? Matcher { get; }

    /// <summary>Gets or sets the table index (assigned when the table is built).</summary>
    public int Index { get; set; }

    /// <summary>Gets the fixed-bit mask.</summary>
    public uint Mask { get; }

    /// <summary>Gets the fixed-bit values.</summary>
    public uint Match { get; }
}

/// <summary>The DSP56300 decode and ALU dispatch tables.</summary>
public sealed partial class Dsp56300Core
{
    private static readonly ushort[] InterruptVector = [0x00, 0x04, 0x02, 0x08];

    private static readonly (uint Source1, uint Source2)[] MacGroups =
    [
        (Dsp56300State.RegX0, Dsp56300State.RegX0),
        (Dsp56300State.RegY0, Dsp56300State.RegY0),
        (Dsp56300State.RegX1, Dsp56300State.RegX0),
        (Dsp56300State.RegY1, Dsp56300State.RegY0),
        (Dsp56300State.RegX0, Dsp56300State.RegY1),
        (Dsp56300State.RegY0, Dsp56300State.RegX0),
        (Dsp56300State.RegX1, Dsp56300State.RegY0),
        (Dsp56300State.RegY1, Dsp56300State.RegX1),
    ];

    private DspOpcode[] _nonParallel = [];
    private Action?[] _alu = [];

    private static bool MatchMMMRRR(uint op)
    {
        uint rrr = (op >> 8) & 7u;
        uint mmm = (op >> 11) & 7u;
        if (mmm == 6)
        {
            return rrr is 0 or 4;
        }

        return true;
    }

    /// <summary>Builds the decode and ALU tables (called once per core).</summary>
    private void InitializeTables()
    {
        Array.Fill(_pramOpcode, (short)-1);

        // Zero is a valid instruction word (nop), so the lookup cache needs a
        // sentinel that no 24-bit instruction can equal.
        Array.Fill(_opcodeLookupInstruction, 0xFFFFFFFFu);

        _nonParallel =
        [

            new("0000000101iiiiii1000d000", "add #xx, D", AddImm, null),
            new("00000001010000001100d000", "add #xxxx, D", AddLong, null),
            new("0000000101iiiiii1000d110", "and #xx, D", AndImm, null),
            new("00000001010000001100d110", "and #xxxx, D", AndLong, null),
            new("00000000iiiiiiii101110EE", "andi #xx, D", Andi, null),
            new("0000110000011101SiiiiiiD", "asl #ii, S2, D", AslImm, null),
            new("0000110000011110010SsssD", "asl S1, S2, D", null, null),
            new("0000110000011100SiiiiiiD", "asr #ii, S2, D", AsrImm, null),
            new("0000110000011110011SsssD", "asr S1, S2, D", null, null),
            new("00001101000100000100CCCC", "bcc xxxx", BccLong, null),
            new("00000101CCCC01aaaa0aaaaa", "bcc xxx", BccImm, null),
            new("0000110100011RRR0100CCCC", "bcc Rn", null, null),
            new("0000101101MMMRRR0S00bbbb", "bchg #n, [X or Y]:ea", BchgEa, MatchMMMRRR),
            new("0000101100aaaaaa0S00bbbb", "bchg #n, [X or Y]:aa", BchgAa, null),
            new("0000101110pppppp0S00bbbb", "bchg #n, [X or Y]:pp", BchgPp, null),
            new("0000000101qqqqqq0S0bbbbb", "bchg #n, [X or Y]:qq", null, null),
            new("0000101111DDDDDD010bbbbb", "bchg, #n, D", BchgReg, null),
            new("0000101001MMMRRR0S00bbbb", "bclr #n, [X or Y]:ea", BclrEa, MatchMMMRRR),
            new("0000101000aaaaaa0S00bbbb", "bclr #n, [X or Y]:aa", BclrAa, null),
            new("0000101010pppppp0S00bbbb", "bclr #n, [X or Y]:pp", BclrPp, null),
            new("0000000100qqqqqq0S00bbbb", "bclr #n, [X or Y]:qq", null, null),
            new("0000101011DDDDDD010bbbbb", "bclr #n, D", BclrReg, null),
            new("000011010001000011000000", "bra xxxx", BraLong, null),
            new("00000101000011aaaa0aaaaa", "bra xxx", BraImm, null),
            new("0000110100011RRR11000000", "bra Rn", null, null),
            new("0000110010MMMRRR0S0bbbbb", "brclr #n, [X or Y]:ea, xxxx", null, MatchMMMRRR),
            new("0000110010aaaaaa1S0bbbbb", "brclr #n, [X or Y]:aa, xxxx", null, null),
            new("0000110011pppppp0S0bbbbb", "brclr #n, [X or Y]:pp, xxxx", BrclrPp, null),
            new("0000010010qqqqqq0S0bbbbb", "brclr #n, [X or Y]:qq, xxxx", null, null),
            new("0000110011DDDDDD100bbbbb", "brclr #n, S, xxxx", BrclrReg, null),
            new("00000000000000100001CCCC", "brkcc", null, null),
            new("0000110010MMMRRR0S1bbbbb", "brset #n, [X or Y]:ea, xxxx", null, MatchMMMRRR),
            new("0000110010aaaaaa1S1bbbbb", "brset #n, [X or Y]:aa, xxxx", null, null),
            new("0000110011pppppp0S1bbbbb", "brset #n, [X or Y]:pp, xxxx", BrsetPp, null),
            new("0000010010qqqqqq0S1bbbbb", "brset #n, [X or Y]:qq, xxxx", null, null),
            new("0000110011DDDDDD101bbbbb", "brset #n, S, xxxx", BrsetReg, null),
            new("00001101000100000000CCCC", "bscc xxxx", null, null),
            new("00000101CCCC00aaaa0aaaaa", "bscc xxx", null, null),
            new("0000110100011RRR0000CCCC", "bscc Rn", null, null),
            new("0000110110MMMRRR0S0bbbbb", "bsclr #n, [X or Y]:ea, xxxx", null, MatchMMMRRR),
            new("0000110110aaaaaa1S0bbbbb", "bsclr #n, [X or Y]:aa, xxxx", null, null),
            new("0000010010qqqqqq1S0bbbbb", "bsclr #n, [X or Y]:qq, xxxx", null, null),
            new("0000110111pppppp0S0bbbbb", "bsclr #n, [X or Y]:pp, xxxx", null, null),
            new("0000110111DDDDDD100bbbbb", "bsclr, #n, S, xxxx", null, null),
            new("0000101001MMMRRR0S1bbbbb", "bset #n, [X or Y]:ea", BsetEa, MatchMMMRRR),
            new("0000101000aaaaaa0S1bbbbb", "bset #n, [X or Y]:aa", BsetAa, null),
            new("0000101010pppppp0S1bbbbb", "bset #n, [X or Y]:pp", BsetPp, null),
            new("0000000100qqqqqq0S1bbbbb", "bset #n, [X or Y]:qq", null, null),
            new("0000101011DDDDDD011bbbbb", "bset, #n, D", BsetReg, null),
            new("000011010001000010000000", "bsr xxxx", BsrLong, null),
            new("00000101000010aaaa0aaaaa", "bsr xxx", BsrImm, null),
            new("0000110100011RRR10000000", "bsr Rn", null, null),
            new("0000110110MMMRRR0S1bbbbb", "bsset #n, [X or Y]:ea, xxxx", null, MatchMMMRRR),
            new("0000110110aaaaaa1S1bbbbb", "bsset #n, [X or Y]:aa, xxxx", null, null),
            new("0000110111pppppp0S1bbbbb", "bsset #n, [X or Y]:pp, xxxx", null, null),
            new("0000010010qqqqqq1S1bbbbb", "bsset #n, [X or Y]:qq, xxxx", null, null),
            new("0000110111DDDDDD101bbbbb", "bsset #n, S, xxxx", null, null),
            new("0000101101MMMRRR0S10bbbb", "btst #n, [X or Y]:ea", BtstEa, MatchMMMRRR),
            new("0000101100aaaaaa0S10bbbb", "btst #n, [X or Y]:aa", BtstAa, null),
            new("0000101110pppppp0S10bbbb", "btst #n, [X or Y]:pp", BtstPp, null),
            new("0000000101qqqqqq0S10bbbb", "btst #n, [X or Y]:qq", null, null),
            new("0000101111DDDDDD0110bbbb", "btst #n, D", BtstReg, null),
            new("0000110000011110000000SD", "clb S, D", null, null),
            new("0000000101iiiiii1000d101", "cmp #xx, S2", CmpImm, null),
            new("00000001010000001100d101", "cmp #xxxx, S2", CmpLong, null),
            new("00001100000111111111gggd", "cmpu S1, S2", Cmpu, null),
            new("000000000000001000000000", "debug", null, null),
            new("00000000000000110000CCCC", "debugcc", null, null),
            new("00000000000000000000101d", "dec D", Dec, null),
            new("000000011000000001JJd000", "div S, D", Div, null),
            new("000000010010010s1sdkQQQQ", "dmac S1, S2, D", null, null),
            new("0000011001MMMRRR0S000000", "do [X or Y]:ea, expr", DoEa, MatchMMMRRR),
            new("0000011000aaaaaa0S000000", "do [X or Y]:aa, expr", DoAa, null),
            new("00000110iiiiiiii1000hhhh", "do #xxx, expr", DoImm, null),
            new("0000011011DDDDDD00000000", "do S, expr", DoReg, null),
            new("000000000000001000000011", "do_f", null, null),
            new("0000011001MMMRRR0S010000", "dor [X or Y]:ea, label", null, MatchMMMRRR),
            new("0000011000aaaaaa0S010000", "dor [X or Y]:aa, label", null, null),
            new("00000110iiiiiiii1001hhhh", "dor #xxx, label", DorImm, null),
            new("0000011011DDDDDD00010000", "dor S, label", DorReg, null),
            new("000000000000001000000010", "dor_f", null, null),
            new("000000000000000010001100", "enddo", Enddo, null),
            new("0000000101iiiiii1000d011", "eor #xx, D", null, null),
            new("00000001010000001100d011", "eor #xxxx, D", null, null),
            new("0000110000011010000sSSSD", "extract S1, S2, D", null, null),
            new("0000110000011000000s000D", "extract #CO, S2, D", null, null),
            new("0000110000011010100sSSSD", "extractu S1, S2, D", null, null),
            new("0000110000011000100s000D", "extractu #CO, S2, D", null, null),
            new("000000000000000000000101", "ill", Illegal, null),
            new("00000000000000000000100d", "inc D", Inc, null),
            new("00001100000110110qqqSSSD", "insert S1, S2, D", null, null),
            new("00001100000110010qqq000D", "insert #CO, S2, D", null, null),
            new("00001110CCCCaaaaaaaaaaaa", "jcc xxx", JccImm, null),
            new("0000101011MMMRRR1010CCCC", "jcc ea", JccEa, MatchMMMRRR),
            new("0000101001MMMRRR1S00bbbb", "jclr #n, [X or Y]:ea, xxxx", JclrEa, MatchMMMRRR),
            new("0000101000aaaaaa1S00bbbb", "jclr #n, [X or Y]:aa, xxxx", JclrAa, null),
            new("0000101010pppppp1S00bbbb", "jclr #n, [X or Y]:pp, xxxx", JclrPp, null),
            new("0000000110qqqqqq1S00bbbb", "jclr #n, [X or Y]:qq, xxxx", null, null),
            new("0000101011DDDDDD0000bbbb", "jclr #n, S, xxxx", JclrReg, null),
            new("0000101011MMMRRR10000000", "jmp ea", JmpEa, MatchMMMRRR),
            new("000011000000aaaaaaaaaaaa", "jmp xxx", JmpImm, null),
            new("00001111CCCCaaaaaaaaaaaa", "jscc xxx", JsccImm, null),
            new("0000101111MMMRRR1010CCCC", "jscc ea", JsccEa, MatchMMMRRR),
            new("0000101101MMMRRR1S00bbbb", "jsclr #n, [X or Y]:ea, xxxx", JsclrEa, MatchMMMRRR),
            new("0000101100MMMRRR1S00bbbb", "jsclr #n, [X or Y]:aa, xxxx", JsclrAa, MatchMMMRRR),
            new("0000101110pppppp1S0bbbbb", "jsclr #n, [X or Y]:pp, xxxx", JsclrPp, null),
            new("0000000111qqqqqq1S0bbbbb", "jsclr #n, [X or Y]:qq, xxxx", null, null),
            new("0000101111DDDDDD000bbbbb", "jsclr #n, S, xxxx", JsclrReg, null),
            new("0000101001MMMRRR1S10bbbb", "jset #n, [X or Y]:ea, xxxx", JsetEa, MatchMMMRRR),
            new("0000101000MMMRRR1S10bbbb", "jset #n, [X or Y]:aa, xxxx", JsetAa, MatchMMMRRR),
            new("0000101010pppppp1S10bbbb", "jset #n, [X or Y]:pp, xxxx", JsetPp, null),
            new("0000000110qqqqqq1S10bbbb", "jset #n, [X or Y]:qq, xxxx", null, null),
            new("0000101011DDDDDD0010bbbb", "jset #n, S, xxxx", JsetReg, null),
            new("0000101111MMMRRR10000000", "jsr ea", JsrEa, MatchMMMRRR),
            new("000011010000aaaaaaaaaaaa", "jsr xxx", JsrImm, null),
            new("0000101101MMMRRR1S10bbbb", "jsset #n, [X or Y]:ea, xxxx", JssetEa, MatchMMMRRR),
            new("0000101100aaaaaa1S10bbbb", "jsset #n, [X or Y]:aa, xxxx", JssetAa, null),
            new("0000101110pppppp1S1bbbbb", "jsset #n, [X or Y]:pp, xxxx", JssetPp, null),
            new("0000000111qqqqqq1S1bbbbb", "jsset #n, [X or Y]:qq, xxxx", null, null),
            new("0000101111DDDDDD001bbbbb", "jsset #n, S, xxxx", JssetReg, null),
            new("0000010011000RRR000ddddd", "lra Rn, D", null, null),
            new("0000010001000000010ddddd", "lra xxxx, D", null, null),
            new("000011000001111010iiiiiD", "lsl #ii, D", LslImm, null),
            new("00001100000111100001sssD", "lsl S, D", null, null),
            new("000011000001111011iiiiiD", "lsr #ii, D", null, null),
            new("00001100000111100011sssD", "lsr S, D", null, null),
            new("00000100010MMRRR000ddddd", "lua ea, D", Lua, null),
            new("0000010000aaaRRRaaaadddd", "lua (Rn + aa), D", LuaRel, null),
            new("00000001000sssss11QQdk10", "mac S, #n, D", null, null),
            new("000000010100000111qqdk10", "maci #xxxx, S, D", null, null),
            new("00000001001001101sdkQQQQ", "mac_s_u S1, S2, D", null, null),
            new("00000001000sssss11QQdk11", "macr S1, S2, D", null, null),
            new("000000010100000111qqdk11", "macri #xxxx, S, D", null, null),
            new("00001100000110111000sssD", "merge S, D", null, null),
            new("0000101001110RRR1WDDDDDD", "move X:(Rn + xxxx) <-> R", MoveXLong, null),
            new("0000101101110RRR1WDDDDDD", "move Y:(Rn + xxxx) <-> R", null, null),
            new("0000001aaaaaaRRR1a0WDDDD", "move X:(Rn + xxx) <-> R", MoveXImm, null),
            new("0000001aaaaaaRRR1a1WDDDD", "move Y:(Rn + xxx) <-> R", MoveYImm, null),
            new("00000101W1MMMRRR0s1ddddd", "movec [X or Y]:ea <-> R", MovecEa, MatchMMMRRR),
            new("00000101W0aaaaaa0s1ddddd", "movec [X or Y]:aa <-> R", MovecAa, MatchMMMRRR),
            new("00000100W1eeeeee101ddddd", "movec R1, R2", MovecReg, null),
            new("00000101iiiiiiii101ddddd", "movec #xx, D1", MovecImm, null),
            new("00000111W1MMMRRR10dddddd", "movem P:ea <-> R", MovemEa, MatchMMMRRR),
            new("00000111W0aaaaaa00dddddd", "movem P:ea <-> R", MovemAa, MatchMMMRRR),
            new("0000100sW1MMMRRR1Spppppp", "movep [X or Y]:ea <-> [X or Y]:pp", Movep23, MatchMMMRRR),
            new("00000111W1MMMRRR0Sqqqqqq", "movep [X or Y]:ea <-> X:qq", MovepXQq, MatchMMMRRR),
            new("00000111W0MMMRRR1Sqqqqqq", "movep [X or Y]:ea <-> Y:qq", null, MatchMMMRRR),
            new("0000100sW1MMMRRR01pppppp", "movep [X or Y]:pp <-> P:ea", Movep1, MatchMMMRRR),
            new("000000001WMMMRRR0sqqqqqq", "movep [X or Y]:qq <-> P:ea", null, MatchMMMRRR),
            new("0000100sW1dddddd00pppppp", "movep [X or Y]:pp <-> R", Movep0, null),
            new("00000100W1dddddd1q0qqqqq", "movep X:qq <-> R", null, null),
            new("00000100W1dddddd0q1qqqqq", "movep Y:qq <-> R", null, null),
            new("00000001000sssss11QQdk00", "mpy S, #n, D", null, null),
            new("00000001001001111sdkQQQQ", "mpy_s_u S1, S2, D", null, null),
            new("000000010100000111qqdk00", "mpyi #xxxx, S, D", Mpyi, null),
            new("00000001000sssss11QQdk01", "mpyr S, #n, D", null, null),
            new("000000010100000111qqdk01", "mpyri #xxxx, S, D", null, null),
            new("000000000000000000000000", "nop", Nop, null),
            new("0000000111011RRR0001d101", "norm Rn, D", Norm, null),
            new("00001100000111100010sssD", "normf S, D", null, null),
            new("0000000101iiiiii1000d010", "or #xx, D", null, null),
            new("00000001010000001100d010", "or #xxxx, D", OrLong, null),
            new("00000000iiiiiiii111110EE", "ori #xx, D", Ori, null),
            new("000000000000000000000011", "pflush", null, null),
            new("000000000000000000000001", "pflushun", null, null),
            new("000000000000000000000010", "pfree", null, null),
            new("0000101111MMMRRR10000001", "plock ea", null, MatchMMMRRR),
            new("000000000000000000001111", "plockr xxxx", null, null),
            new("0000101011MMMRRR10000001", "punlock ea", null, MatchMMMRRR),
            new("000000000000000000001110", "punlockr xxxx", null, null),
            new("0000011001MMMRRR0S100000", "rep [X or Y]:ea", RepEa, MatchMMMRRR),
            new("0000011000aaaaaa0S100000", "rep [X or Y]:aa", RepAa, null),
            new("00000110iiiiiiii1010hhhh", "rep #xxx", RepImm, null),
            new("0000011011dddddd00100000", "rep S", RepReg, null),
            new("000000000000000010000100", "reset", ResetInstruction, null),
            new("000000000000000000000100", "rti", Rti, null),
            new("000000000000000000001100", "rts", Rts, null),
            new("000000000000000010000111", "stop", Stop, null),
            new("0000000101iiiiii1000d100", "sub #xx, D", SubImm, null),
            new("00000001010000001100d100", "sub #xxxx, D", SubLong, null),
            new("00000010CCCC00000JJJd000", "tcc S1, D1", Tcc, null),
            new("00000011CCCC0ttt0JJJdTTT", "tcc S1,D2 S2,D2", Tcc, null),
            new("00000010CCCC1ttt00000TTT", "tcc S2, D2", Tcc, null),
            new("000000000000000000000110", "trap", null, null),
            new("00000000000000000001CCCC", "trapcc", null, null),
            new("0000101S11MMMRRR110i0000", "vsl", null, MatchMMMRRR),
            new("000000000000000010000110", "wait", Wait, null),

        ];

        for (int i = 0; i < _nonParallel.Length; i++)
        {
            _nonParallel[i].Index = i;
        }

        _alu = new Action?[256];

        Action?[] lowAlu =
        [
            Move, TfrBA, AddrBA, TstA, Undefined, CmpBA, SubrA, CmpmBA,
            Undefined, TfrAB, AddrAB, TstB, Undefined, CmpAB, SubrB, CmpmAB,
            AddBA, RndA, AddlBA, ClrA, SubBA, Undefined, SublA, NotA,
            AddAB, RndB, AddlAB, ClrB, SubAB, Max, SublB, NotB,
            AddXA, AdcXA, AsrA, LsrA, SubXA, SbcXA, AbsA, RorA,
            AddXB, AdcXB, AsrB, LsrB, SubXB, SbcXB, AbsB, RorB,
            AddYA, AdcYA, AslA, LslA, SubYA, SbcYA, NegA, RolA,
            AddYB, AdcYB, AslB, LslB, SubYB, SbcYB, NegB, RolB,
            AddX0A, TfrX0A, OrX0A, EorX0A, SubX0A, CmpX0A, AndX0A, CmpmX0A,
            AddX0B, TfrX0B, OrX0B, EorX0B, SubX0B, CmpX0B, AndX0B, CmpmX0B,
            AddY0A, TfrY0A, OrY0A, EorY0A, SubY0A, CmpY0A, AndY0A, CmpmY0A,
            AddY0B, TfrY0B, OrY0B, EorY0B, SubY0B, CmpY0B, AndY0B, CmpmY0B,
            AddX1A, TfrX1A, OrX1A, EorX1A, SubX1A, CmpX1A, AndX1A, CmpmX1A,
            AddX1B, TfrX1B, OrX1B, EorX1B, SubX1B, CmpX1B, AndX1B, CmpmX1B,
            AddY1A, TfrY1A, OrY1A, EorY1A, SubY1A, CmpY1A, AndY1A, CmpmY1A,
            AddY1B, TfrY1B, OrY1B, EorY1B, SubY1B, CmpY1B, AndY1B, CmpmY1B,
        ];

        for (int i = 0; i < lowAlu.Length; i++)
        {
            _alu[i] = lowAlu[i];
        }

        // 0x80 - 0xff: eight (source1, source2) groups; each group has the
        // A and B destination in eight-entry blocks laid out as the
        // reference table does: mpy, mpyr, mac, macr for the positive sign
        // and then the same four for the negative sign.
        for (int group = 0; group < MacGroups.Length; group++)
        {
            (uint source1, uint source2) = MacGroups[group];
            for (int dest = 0; dest < 2; dest++)
            {
                uint destReg = dest == 0 ? (uint)Dsp56300State.RegA : Dsp56300State.RegB;
                int b = 0x80 + (group * 16) + (dest * 8);
                _alu[b + 0] = () => MpyMac(source1, source2, SignPlus, destReg, round: false, accumulate: false);
                _alu[b + 1] = () => MpyMac(source1, source2, SignPlus, destReg, round: true, accumulate: false);
                _alu[b + 2] = () => MpyMac(source1, source2, SignPlus, destReg, round: false, accumulate: true);
                _alu[b + 3] = () => MpyMac(source1, source2, SignPlus, destReg, round: true, accumulate: true);
                _alu[b + 4] = () => MpyMac(source1, source2, SignMinus, destReg, round: false, accumulate: false);
                _alu[b + 5] = () => MpyMac(source1, source2, SignMinus, destReg, round: true, accumulate: false);
                _alu[b + 6] = () => MpyMac(source1, source2, SignMinus, destReg, round: false, accumulate: true);
                _alu[b + 7] = () => MpyMac(source1, source2, SignMinus, destReg, round: true, accumulate: true);
            }
        }

        // Reference quirk: emu_macr_p_x1_y0_b calls dsp_rnd56 before it
        // loads the accumulator, so its rounding is lost and the handler
        // behaves as a plain MAC.  Reproduce it exactly.
        _alu[0xEB] = () => MpyMac(
            Dsp56300State.RegX1,
            Dsp56300State.RegY0,
            SignPlus,
            Dsp56300State.RegB,
            round: false,
            accumulate: true);
    }
}
