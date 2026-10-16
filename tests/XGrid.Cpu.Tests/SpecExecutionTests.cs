using System;
using System.IO;
using System.Text;
using XGrid.Core.Devices;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Slice-1/slice-2 specialization tests (§139, §134 style): every covered
/// shape at 8/16/32 against the interpreter, segment overrides, mem-shape
/// fault precision, #UD termination, IRQ delivery at every member including an
/// IF flip, Step/budget transparency, idle NoteAccess replay, the before-
/// execute verify tripwire and cap discipline with a live cursor.
/// </summary>
public sealed class SpecExecutionTests
{
    private static readonly byte[] Loop = { 0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xEB, 0xF8 };

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    [Fact]
    public void Mov_Shapes_AtAllSizes_MatchInterpreter()
    {
        RunShapes(
            ("mov r32,r32", new byte[] { 0x89, 0xD8 }),
            ("mov r8,r8", new byte[] { 0x88, 0xC8 }),
            ("mov r16,r16", new byte[] { 0x66, 0x89, 0xD8 }),
            ("mov r32,imm32", new byte[] { 0xB8, 0x78, 0x56, 0x34, 0x12 }),
            ("mov r8,imm8", new byte[] { 0xB0, 0x5A }),
            ("mov r16,imm16", new byte[] { 0x66, 0xB8, 0x34, 0x12 }),
            ("mov r32,m32", new byte[] { 0x8B, 0x03 }),
            ("mov r8,m8", new byte[] { 0x8A, 0x03 }),
            ("mov r16,m16", new byte[] { 0x66, 0x8B, 0x03 }),
            ("mov m32,r32", new byte[] { 0x89, 0x03 }),
            ("mov m8,r8", new byte[] { 0x88, 0x03 }),
            ("mov m16,r16", new byte[] { 0x66, 0x89, 0x03 }));
    }

    [Fact]
    public void Alu_Shapes_AtAllSizes_MatchInterpreter()
    {
        RunShapes(
            ("add r32,r32", new byte[] { 0x01, 0xD8 }),
            ("add r8,r8", new byte[] { 0x00, 0xD8 }),
            ("add r16,r16", new byte[] { 0x66, 0x01, 0xD8 }),
            ("add r32,imm8", new byte[] { 0x83, 0xC0, 0x05 }),
            ("add r8,imm8", new byte[] { 0x04, 0x05 }),
            ("add r16,imm8", new byte[] { 0x66, 0x83, 0xC0, 0x05 }),
            ("add r32,imm32", new byte[] { 0x05, 0x78, 0x56, 0x34, 0x12 }),
            ("sub r32,r32", new byte[] { 0x29, 0xD8 }),
            ("sub r8,r8", new byte[] { 0x28, 0xD8 }),
            ("sub r16,r16", new byte[] { 0x66, 0x29, 0xD8 }),
            ("sub r32,imm8", new byte[] { 0x83, 0xE8, 0x05 }),
            ("sub r8,imm8", new byte[] { 0x2C, 0x05 }),
            ("sub r16,imm8", new byte[] { 0x66, 0x83, 0xE8, 0x05 }),
            ("sub r32,imm32", new byte[] { 0x2D, 0x78, 0x56, 0x34, 0x12 }),
            ("and r32,r32", new byte[] { 0x21, 0xD8 }),
            ("and r8,r8", new byte[] { 0x20, 0xD8 }),
            ("and r16,r16", new byte[] { 0x66, 0x21, 0xD8 }),
            ("and r32,imm8", new byte[] { 0x83, 0xE0, 0x05 }),
            ("and r8,imm8", new byte[] { 0x24, 0x05 }),
            ("and r16,imm8", new byte[] { 0x66, 0x83, 0xE0, 0x05 }),
            ("and r32,imm32", new byte[] { 0x25, 0x78, 0x56, 0x34, 0x12 }),
            ("or r32,r32", new byte[] { 0x09, 0xD8 }),
            ("or r8,r8", new byte[] { 0x08, 0xD8 }),
            ("or r16,r16", new byte[] { 0x66, 0x09, 0xD8 }),
            ("or r32,imm8", new byte[] { 0x83, 0xC8, 0x05 }),
            ("or r8,imm8", new byte[] { 0x0C, 0x05 }),
            ("or r16,imm8", new byte[] { 0x66, 0x83, 0xC8, 0x05 }),
            ("or r32,imm32", new byte[] { 0x0D, 0x78, 0x56, 0x34, 0x12 }),
            ("xor r32,r32", new byte[] { 0x31, 0xD8 }),
            ("xor r8,r8", new byte[] { 0x30, 0xD8 }),
            ("xor r16,r16", new byte[] { 0x66, 0x31, 0xD8 }),
            ("xor r32,imm8", new byte[] { 0x83, 0xF0, 0x05 }),
            ("xor r8,imm8", new byte[] { 0x34, 0x05 }),
            ("xor r16,imm8", new byte[] { 0x66, 0x83, 0xF0, 0x05 }),
            ("xor r32,imm32", new byte[] { 0x35, 0x78, 0x56, 0x34, 0x12 }));
    }

    [Fact]
    public void CmpTest_Shapes_AtAllSizes_MatchInterpreter()
    {
        RunShapes(
            ("cmp r32,r32", new byte[] { 0x39, 0xD8 }),
            ("cmp r8,r8", new byte[] { 0x38, 0xC8 }),
            ("cmp r16,r16", new byte[] { 0x66, 0x39, 0xD8 }),
            ("cmp r32,imm8", new byte[] { 0x83, 0xF8, 0x05 }),
            ("cmp r8,imm8", new byte[] { 0x3C, 0x05 }),
            ("cmp r16,imm8", new byte[] { 0x66, 0x83, 0xF8, 0x05 }),
            ("cmp r32,imm32", new byte[] { 0x3D, 0x78, 0x56, 0x34, 0x12 }),
            ("test r32,r32", new byte[] { 0x85, 0xC0 }),
            ("test r8,r8", new byte[] { 0x84, 0xC8 }),
            ("test r16,r16", new byte[] { 0x66, 0x85, 0xC0 }),
            ("test r32,imm32", new byte[] { 0xF7, 0xC0, 0x78, 0x56, 0x34, 0x12 }),
            ("test r8,imm8", new byte[] { 0xA8, 0x05 }),
            ("test r16,imm16", new byte[] { 0x66, 0xF7, 0xC0, 0x34, 0x12 }));
    }

    [Fact]
    public void IncDecLeaPushPopNop_Shapes_MatchInterpreter()
    {
        RunShapes(
            ("inc r32", new byte[] { 0x40 }),
            ("inc r8", new byte[] { 0xFE, 0xC0 }),
            ("inc r16", new byte[] { 0x66, 0x40 }),
            ("dec r32", new byte[] { 0x48 }),
            ("dec r8", new byte[] { 0xFE, 0xC8 }),
            ("dec r16", new byte[] { 0x66, 0x48 }),
            ("lea r32,m32", new byte[] { 0x8D, 0x03 }),
            ("lea r16,m16", new byte[] { 0x66, 0x8D, 0x03 }),
            ("push r32; pop r32", new byte[] { 0x53, 0x58 }),
            ("push r16; pop r16", new byte[] { 0x66, 0x53, 0x66, 0x58 }),
            ("push imm32; pop r32", new byte[] { 0x68, 0x78, 0x56, 0x34, 0x12, 0x58 }),
            ("push imm8; pop r32", new byte[] { 0x6A, 0x05, 0x58 }),
            ("nop; nop", new byte[] { 0x90, 0x90 }));
    }

    [Fact]
    public void AluMem_Shapes_AtAllSizes_MatchInterpreter()
    {
        // Slice-3 family (§142): ALU with a memory operand. modrm 0x03 reads
        // [EBX] (Preset maps 0x2000 with known content); 0x18 swaps reg/mem.
        RunShapes(
            ("mov m8,imm8", new byte[] { 0xC6, 0x03, 0x05 }),
            ("mov m16,imm16", new byte[] { 0x66, 0xC7, 0x03, 0x34, 0x12 }),
            ("mov m32,imm32", new byte[] { 0xC7, 0x03, 0x78, 0x56, 0x34, 0x12 }),
            ("add r32,m32", new byte[] { 0x03, 0x03 }),
            ("add r8,m8", new byte[] { 0x02, 0x03 }),
            ("add r16,m16", new byte[] { 0x66, 0x03, 0x03 }),
            ("add m32,r32", new byte[] { 0x01, 0x03 }),
            ("add m8,r8", new byte[] { 0x00, 0x03 }),
            ("add m16,r16", new byte[] { 0x66, 0x01, 0x03 }),
            ("add m32,imm8", new byte[] { 0x83, 0x03, 0x05 }),
            ("add m8,imm8", new byte[] { 0x80, 0x03, 0x05 }),
            ("add m32,imm32", new byte[] { 0x81, 0x03, 0x78, 0x56, 0x34, 0x12 }),
            ("sub r32,m32", new byte[] { 0x2B, 0x03 }),
            ("sub r8,m8", new byte[] { 0x2A, 0x03 }),
            ("sub r16,m16", new byte[] { 0x66, 0x2B, 0x03 }),
            ("sub m32,r32", new byte[] { 0x29, 0x03 }),
            ("sub m8,r8", new byte[] { 0x28, 0x03 }),
            ("sub m16,r16", new byte[] { 0x66, 0x29, 0x03 }),
            ("sub m32,imm8", new byte[] { 0x83, 0x2B, 0x05 }),
            ("sub m8,imm8", new byte[] { 0x80, 0x2B, 0x05 }),
            ("sub m32,imm32", new byte[] { 0x81, 0x2B, 0x78, 0x56, 0x34, 0x12 }),
            ("and r32,m32", new byte[] { 0x23, 0x03 }),
            ("and r8,m8", new byte[] { 0x22, 0x03 }),
            ("and r16,m16", new byte[] { 0x66, 0x23, 0x03 }),
            ("and m32,r32", new byte[] { 0x21, 0x03 }),
            ("and m8,r8", new byte[] { 0x20, 0x03 }),
            ("and m16,r16", new byte[] { 0x66, 0x21, 0x03 }),
            ("and m32,imm8", new byte[] { 0x83, 0x23, 0x05 }),
            ("and m8,imm8", new byte[] { 0x80, 0x23, 0x05 }),
            ("and m32,imm32", new byte[] { 0x81, 0x23, 0x78, 0x56, 0x34, 0x12 }),
            ("or r32,m32", new byte[] { 0x0B, 0x03 }),
            ("or r8,m8", new byte[] { 0x0A, 0x03 }),
            ("or r16,m16", new byte[] { 0x66, 0x0B, 0x03 }),
            ("or m32,r32", new byte[] { 0x09, 0x03 }),
            ("or m8,r8", new byte[] { 0x08, 0x03 }),
            ("or m16,r16", new byte[] { 0x66, 0x09, 0x03 }),
            ("or m32,imm8", new byte[] { 0x83, 0x0B, 0x05 }),
            ("or m8,imm8", new byte[] { 0x80, 0x0B, 0x05 }),
            ("or m32,imm32", new byte[] { 0x81, 0x0B, 0x78, 0x56, 0x34, 0x12 }),
            ("xor r32,m32", new byte[] { 0x33, 0x03 }),
            ("xor r8,m8", new byte[] { 0x32, 0x03 }),
            ("xor r16,m16", new byte[] { 0x66, 0x33, 0x03 }),
            ("xor m32,r32", new byte[] { 0x31, 0x03 }),
            ("xor m8,r8", new byte[] { 0x30, 0x03 }),
            ("xor m16,r16", new byte[] { 0x66, 0x31, 0x03 }),
            ("xor m32,imm8", new byte[] { 0x83, 0x33, 0x05 }),
            ("xor m8,imm8", new byte[] { 0x80, 0x33, 0x05 }),
            ("xor m32,imm32", new byte[] { 0x81, 0x33, 0x78, 0x56, 0x34, 0x12 }),
            ("cmp r32,m32", new byte[] { 0x3B, 0x03 }),
            ("cmp r8,m8", new byte[] { 0x3A, 0x03 }),
            ("cmp r16,m16", new byte[] { 0x66, 0x3B, 0x03 }),
            ("cmp m32,r32", new byte[] { 0x39, 0x03 }),
            ("cmp m8,r8", new byte[] { 0x38, 0x03 }),
            ("cmp m16,r16", new byte[] { 0x66, 0x39, 0x03 }),
            ("cmp m32,imm8", new byte[] { 0x83, 0x3B, 0x05 }),
            ("cmp m8,imm8", new byte[] { 0x80, 0x3B, 0x05 }),
            ("cmp m32,imm32", new byte[] { 0x81, 0x3B, 0x78, 0x56, 0x34, 0x12 }),
            ("test r32,m32", new byte[] { 0x85, 0x03 }),
            ("test r8,m8", new byte[] { 0x84, 0x03 }),
            ("test r16,m16", new byte[] { 0x66, 0x85, 0x03 }),
            ("test m32,r32", new byte[] { 0x85, 0x1B }),
            ("test m8,r8", new byte[] { 0x84, 0x1B }),
            ("test m16,r16", new byte[] { 0x66, 0x85, 0x1B }),
            ("test m8,imm8", new byte[] { 0xF6, 0x03, 0x05 }),
            ("test m16,imm16", new byte[] { 0x66, 0xF7, 0x03, 0x34, 0x12 }),
            ("test m32,imm32", new byte[] { 0xF7, 0x03, 0x78, 0x56, 0x34, 0x12 }));
    }

    [Fact]
    public void Jcc_AllConditions_TakenAndFallthrough_MatchInterpreter()
    {
        // jcc +1 over inc eax: taken skips the inc, so a wrong direction
        // shows up in EAX. Two flag setups split every condition across
        // taken and fallthrough (Pf covered by setup 2).
        uint[] flagSetups =
        {
            FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf,
            FlagBits.Reserved1 | FlagBits.Sf | FlagBits.Of | FlagBits.Pf,
        };
        for (int condition = 0; condition < 16; condition++)
        {
            RunJcc(new byte[] { (byte)(0x70 + condition), 0x01, 0x40, 0xF4 }, $"jcc rel8 cond {condition}", flagSetups);
            RunJcc(
                new byte[] { 0x0F, (byte)(0x80 + condition), 0x01, 0x00, 0x00, 0x00, 0x40, 0xF4 },
                $"jcc rel32 cond {condition}",
                flagSetups);
        }

        RunJcc(
            new byte[] { 0x66, 0x0F, 0x84, 0x01, 0x00, 0x40, 0xF4 },
            "jcc rel16 (size-insensitive)",
            flagSetups);
        return;

        static void RunJcc(byte[] code, string name, uint[] setups)
        {
            foreach (uint flags in setups)
            {
                XboxCpu plain = Create(false, code);
                XboxCpu specCpu = Create(true, code);
                Preset(plain);
                Preset(specCpu);
                plain.Context.Eflags = flags;
                specCpu.Context.Eflags = flags;
                try
                {
                    Assert.Equal(plain.Execute(16), specCpu.Execute(16));
                    AssertSameState(plain, specCpu);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Shape '{name}' diverged (flags 0x{flags:X})", exception);
                }

                Assert.True(plain.Context.Halted && specCpu.Context.Halted);
            }
        }
    }

    [Fact]
    public void Jcc_Resolver_CoversAllConditions()
    {
        for (int condition = 0; condition < 16; condition++)
        {
            Assert.NotNull(XboxCpu.ResolveSpec(DecodeRel8(condition)));
            Assert.NotNull(XboxCpu.ResolveSpec(DecodeRel32(condition)));
        }

        return;

        static DecodedInstruction DecodeRel8(int condition)
        {
            return Decode((byte)(0x70 + condition), 0x01);
        }

        static DecodedInstruction DecodeRel32(int condition)
        {
            return Decode(0x0F, (byte)(0x80 + condition), 0x01, 0x00, 0x00, 0x00);
        }

        static DecodedInstruction Decode(params byte[] code)
        {
            Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
            window.Clear();
            code.CopyTo(window);
            return Decoder.Decode(window, 32, 32);
        }
    }

    [Fact]
    public void ShiftExtend_Shapes_AtAllSizes_MatchInterpreter()
    {
        // Slice-8 family (§145): C1-group shifts by immediate and 0F B6/B7/BE/BF
        // extension. modrm 0xE0/0xC0 addresses EAX, 0x23/0x03 address [EBX].
        RunShapes(
            ("shl r32,imm8", new byte[] { 0xC1, 0xE0, 0x05 }),
            ("shl r8,imm8", new byte[] { 0xC0, 0xE0, 0x05 }),
            ("shl r16,imm8", new byte[] { 0x66, 0xC1, 0xE0, 0x05 }),
            ("shl m32,imm8", new byte[] { 0xC1, 0x23, 0x05 }),
            ("shl m8,imm8", new byte[] { 0xC0, 0x23, 0x05 }),
            ("shl m16,imm8", new byte[] { 0x66, 0xC1, 0x23, 0x05 }),
            ("shr r32,imm8", new byte[] { 0xC1, 0xE8, 0x05 }),
            ("shr r8,imm8", new byte[] { 0xC0, 0xE8, 0x05 }),
            ("shr r16,imm8", new byte[] { 0x66, 0xC1, 0xE8, 0x05 }),
            ("shr m32,imm8", new byte[] { 0xC1, 0x2B, 0x05 }),
            ("shr m8,imm8", new byte[] { 0xC0, 0x2B, 0x05 }),
            ("shr m16,imm8", new byte[] { 0x66, 0xC1, 0x2B, 0x05 }),
            ("sar r32,imm8", new byte[] { 0xC1, 0xF8, 0x05 }),
            ("sar r8,imm8", new byte[] { 0xC0, 0xF8, 0x05 }),
            ("sar r16,imm8", new byte[] { 0x66, 0xC1, 0xF8, 0x05 }),
            ("sar m32,imm8", new byte[] { 0xC1, 0x3B, 0x05 }),
            ("sar m8,imm8", new byte[] { 0xC0, 0x3B, 0x05 }),
            ("sar m16,imm8", new byte[] { 0x66, 0xC1, 0x3B, 0x05 }),
            ("rol r32,imm8", new byte[] { 0xC1, 0xC0, 0x05 }),
            ("rol r8,imm8", new byte[] { 0xC0, 0xC0, 0x05 }),
            ("rol r16,imm8", new byte[] { 0x66, 0xC1, 0xC0, 0x05 }),
            ("rol m32,imm8", new byte[] { 0xC1, 0x03, 0x05 }),
            ("rol m8,imm8", new byte[] { 0xC0, 0x03, 0x05 }),
            ("rol m16,imm8", new byte[] { 0x66, 0xC1, 0x03, 0x05 }),
            ("ror r32,imm8", new byte[] { 0xC1, 0xC8, 0x05 }),
            ("ror r8,imm8", new byte[] { 0xC0, 0xC8, 0x05 }),
            ("ror r16,imm8", new byte[] { 0x66, 0xC1, 0xC8, 0x05 }),
            ("ror m32,imm8", new byte[] { 0xC1, 0x0B, 0x05 }),
            ("ror m8,imm8", new byte[] { 0xC0, 0x0B, 0x05 }),
            ("ror m16,imm8", new byte[] { 0x66, 0xC1, 0x0B, 0x05 }),
            ("movzx r32,r8", new byte[] { 0x0F, 0xB6, 0xC0 }),
            ("movzx r32,m8", new byte[] { 0x0F, 0xB6, 0x03 }),
            ("movzx r16,r8", new byte[] { 0x66, 0x0F, 0xB6, 0xC0 }),
            ("movzx r16,m8", new byte[] { 0x66, 0x0F, 0xB6, 0x03 }),
            ("movzx r32,r16", new byte[] { 0x0F, 0xB7, 0xC0 }),
            ("movzx r32,m16", new byte[] { 0x0F, 0xB7, 0x03 }),
            ("movzx r16,r16", new byte[] { 0x66, 0x0F, 0xB7, 0xC0 }),
            ("movzx r16,m16", new byte[] { 0x66, 0x0F, 0xB7, 0x03 }),
            ("movsx r32,r8", new byte[] { 0x0F, 0xBE, 0xC0 }),
            ("movsx r32,m8", new byte[] { 0x0F, 0xBE, 0x03 }),
            ("movsx r16,r8", new byte[] { 0x66, 0x0F, 0xBE, 0xC0 }),
            ("movsx r16,m8", new byte[] { 0x66, 0x0F, 0xBE, 0x03 }),
            ("movsx r32,r16", new byte[] { 0x0F, 0xBF, 0xC0 }),
            ("movsx r32,m16", new byte[] { 0x0F, 0xBF, 0x03 }),
            ("movsx r16,r16", new byte[] { 0x66, 0x0F, 0xBF, 0xC0 }),
            ("movsx r16,m16", new byte[] { 0x66, 0x0F, 0xBF, 0x03 }));
    }

    [Fact]
    public void NearControl_Shapes_MatchInterpreter()
    {
        // Jumps skip an inc, calls return onto a hlt: a wrong target shows
        // up in EAX, ESP or the halt itself. All end at a hlt.
        RunControl("jmp rel8", new byte[] { 0xEB, 0x01, 0x40, 0xF4 }, static _ => { });
        RunControl("jmp rel32", new byte[] { 0xE9, 0x01, 0x00, 0x00, 0x00, 0x40, 0xF4 }, static _ => { });
        RunControl(
            "jmp eax",
            new byte[] { 0xB8, 0x08, 0x10, 0x00, 0x00, 0xFF, 0xE0, 0x40, 0xF4 },
            static _ => { });
        RunControl(
            "jmp [ebx]",
            new byte[] { 0xFF, 0x23, 0x40, 0xF4 },
            static cpu => cpu.Ram().Write32(0x2000, 0x1003));
        RunControl(
            "call rel32; ret",
            new byte[] { 0xE8, 0x02, 0x00, 0x00, 0x00, 0xF4, 0x40, 0xC3 },
            static _ => { });
        RunControl(
            "call eax; ret",
            new byte[] { 0xB8, 0x09, 0x10, 0x00, 0x00, 0xFF, 0xD0, 0xF4, 0x40, 0xC3 },
            static _ => { });
        RunControl(
            "call [ebx]; ret",
            new byte[] { 0xFF, 0x13, 0xF4, 0x40, 0xC3 },
            static cpu => cpu.Ram().Write32(0x2000, 0x1004));
        RunControl(
            "call; ret imm16",
            new byte[] { 0xE8, 0x03, 0x00, 0x00, 0x00, 0xF4, 0x90, 0x90, 0xC2, 0x04, 0x00 },
            static _ => { });
        return;

        static void RunControl(string name, byte[] code, Action<XboxCpu> setup)
        {
            XboxCpu plain = Create(false, code);
            XboxCpu specCpu = Create(true, code);
            Preset(plain);
            Preset(specCpu);
            setup(plain);
            setup(specCpu);
            try
            {
                Assert.Equal(plain.Execute(32), specCpu.Execute(32));
                AssertSameState(plain, specCpu);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Shape '{name}' diverged", exception);
            }

            Assert.True(plain.Context.Halted && specCpu.Context.Halted);
        }
    }

    [Fact]
    public void SegmentOverride_MemShapes_MatchInterpreter()
    {
        // FS base differs from DS base: the override must select the FS byte.
        RunShapes(
            ("mov r32,FS:m32", new byte[] { 0x64, 0x8B, 0x03 }),
            ("mov FS:m32,r32", new byte[] { 0x64, 0x89, 0x03 }),
            ("lea r32,FS:m32", new byte[] { 0x64, 0x8D, 0x03 }));
    }

    [Fact]
    public void Resolver_CoversOnlyTheInitialShapes()
    {
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x90)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x89, 0xD8)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x8B, 0x03)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x01, 0xD8)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x83, 0xC0, 0x05)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x39, 0xD8)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x85, 0xC0)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x40)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x48)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x8D, 0x03)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x50)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x58)));

        // Uncovered control (far/int), privileged, string/REP, FPU, invalid
        // and uncovered shapes keep the interpreter.
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xEA, 0x00, 0x00, 0x08, 0x00))); // jmp far
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xEB, 0xFE))); // jmp rel8
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xE9, 0x00, 0x00, 0x00, 0x00))); // jmp rel32
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xE8, 0x00, 0x00, 0x00, 0x00))); // call rel32
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xC3))); // ret
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xC2, 0x04, 0x00))); // ret imm16
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xF4)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xCD, 0x21)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0x0F, 0x0B)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0x62, 0x00, 0x00)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0x10, 0xD8))); // adc: carry live
        Assert.Null(XboxCpu.ResolveSpec(Decode(0x1B, 0xD8))); // sbb: carry live
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xFF, 0x30))); // push m
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xFF, 0x03))); // inc m
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xD3, 0xE0))); // shl r,cl: uncovered
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD1, 0xE0))); // shl r,1: implicit imm
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF6, 0xC0, 0x01))); // test r8,imm8 via F6 grp
        return;

        static DecodedInstruction Decode(params byte[] code)
        {
            Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
            window.Clear();
            code.CopyTo(window);
            return Decoder.Decode(window, 32, 32);
        }
    }

    [Fact]
    public void ControlMix_Loop_MatchesInterpreter()
    {
        // inc/xor-imm/dec/nop/jmp all specialize; the loop runs fully
        // through handlers with no interpreter fallback.
        XboxCpu plain = Create(false, Loop);
        XboxCpu spec = Create(true, Loop);
        Assert.Equal(plain.Execute(64), spec.Execute(64));
        AssertSameState(plain, spec);
        Assert.True(spec.SpecStatistics!.ShapeHits > 0);
        Assert.Equal(0UL, spec.SpecStatistics.ShapeFallbacks);
        Assert.True(spec.SpecStatistics.SpecRan > 0);
    }

    [Fact]
    public void MemFault_PreciseEipVectorCr2_MatchesInterpreter()
    {
        // mov eax,1 (commits) then mov ebx,[0x2000] faults: VPN2 unmapped.
        // The vector-14 handler is entered once per fault; the EIP is rewound
        // into the warm blocks so the second fault runs the faulting member
        // through its pre-specialized handler. (A bare IRET would pop the
        // pushed #PF error code as EIP, so no round-trip is attempted.)
        byte[] code = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0x8B, 0x1D, 0x00, 0x20, 0x00, 0x00, 0x90, 0xF4 };

        XboxCpu plain = CreatePaged(false, code);
        XboxCpu specPaged = CreatePaged(true, code);

        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(0x1005u, specPaged.Context.Eip);

        // First fault (miss path decodes fresh on both sides).
        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(0x1500u, specPaged.Context.Eip);

        // Rewind into the warm blocks: the faulting member is now served from
        // a formed block (via its handler on the spec side).
        plain.Context.Eip = 0x1000;
        specPaged.Context.Eip = 0x1000;
        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(0x1005u, specPaged.Context.Eip);

        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(14, specPaged.LastFaultVector);
        Assert.Equal(0x1005u, specPaged.LastFaultEip);
        Assert.Equal(0x2000u, specPaged.Context.Cr2);
        Assert.Equal(1u, specPaged.Context.Eax);
        Assert.Equal(0x1500u, specPaged.Context.Eip);
        return;

        static XboxCpu CreatePaged(bool spec, byte[] bytes)
        {
            XboxCpu cpu = Create(spec, bytes);
            MapIdentity(cpu.Bus(), 1, 1);
            MapIdentity(cpu.Bus(), 4, 1);
            MapIdentity(cpu.Bus(), 7, 2); // IDT/stacked frame pages (frame grows below 0x8000)
            EnablePaging(cpu);
            cpu.Ram().Write8(0x1500, 0x90); // handler: nop (never returned through)
            InstallIdt(cpu, 14, 0x1500);
            return cpu;
        }
    }

    [Fact]
    public void UdTerminator_ExactVectorAndEip_MatchesInterpreter()
    {
        byte[] code = { 0x90, 0x40, 0x62, 0x00, 0x00, 0xF4 };

        XboxCpu plain = CreateHandled(false, code);
        XboxCpu specCpu = CreateHandled(true, code);
        for (int i = 0; i < 4; i++)
        {
            plain.Step();
            specCpu.Step();
            AssertSameState(plain, specCpu);
        }

        Assert.Equal(6, specCpu.LastFaultVector);
        Assert.Equal(0x1002u, specCpu.LastFaultEip);
        return;

        static XboxCpu CreateHandled(bool spec, byte[] bytes)
        {
            XboxCpu cpu = Create(spec, bytes);
            cpu.Ram().Write8(0x3000, 0xCF);
            InstallIdt(cpu, 6, 0x3000);
            return cpu;
        }
    }

    [Fact]
    public void Irq_AtEveryMember_MatchesInterpreter()
    {
        // Six NOPs + HLT; the source fires on the 3rd acknowledge (before the
        // 3rd member). The handler IRETs back into the stream.
        byte[] code = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xF4 };

        XboxCpu plain = CreateIrq(false, code, new CountedInterruptSource(0x21, fireAt: 3));
        XboxCpu specCpu = CreateIrq(true, code, new CountedInterruptSource(0x21, fireAt: 3));

        // Two quiet members, then delivery lands before the third member.
        plain.Step();
        specCpu.Step();
        AssertSameState(plain, specCpu);
        plain.Step();
        specCpu.Step();
        AssertSameState(plain, specCpu);
        plain.Step();
        specCpu.Step();
        Assert.Equal(0x3000u, plain.Context.Eip);
        Assert.Equal(0x3000u, specCpu.Context.Eip);
        Assert.Equal(1, plain.InterruptCount);
        Assert.Equal(1, specCpu.InterruptCount);

        Assert.Equal(plain.Execute(16), specCpu.Execute(16));
        AssertSameState(plain, specCpu);
        Assert.Equal(1, specCpu.InterruptCount);
        return;

        static XboxCpu CreateIrq(bool spec, byte[] bytes, CountedInterruptSource source)
        {
            XboxCpu cpu = Create(spec, bytes);
            cpu.Ram().Write8(0x3000, 0xCF);
            InstallIdt(cpu, 0x21, 0x3000);
            cpu.Context.Eflags |= FlagBits.If;
            cpu.InterruptSource = source;
            return cpu;
        }
    }

    [Fact]
    public void Irq_IfFlipMidBlock_MatchesInterpreter()
    {
        // IF starts clear with an always-firing source: delivery waits for the
        // STI (a fallback terminator), then lands before the following member.
        byte[] code = { 0x90, 0x90, 0xFB, 0x90, 0x90, 0xF4 };

        XboxCpu plain = CreateSti(false, code);
        XboxCpu specCpu = CreateSti(true, code);

        // Two NOPs with IF clear (no poll observes the source), STI sets IF,
        // then delivery lands before the following member.
        for (int i = 0; i < 3; i++)
        {
            plain.Step();
            specCpu.Step();
            AssertSameState(plain, specCpu);
        }

        Assert.Equal(0x1003u, specCpu.Context.Eip);
        plain.Step();
        specCpu.Step();
        Assert.Equal(0x3000u, plain.Context.Eip);
        Assert.Equal(0x3000u, specCpu.Context.Eip);

        Assert.Equal(plain.Execute(16), specCpu.Execute(16));
        AssertSameState(plain, specCpu);
        Assert.Equal(1, specCpu.InterruptCount);
        return;

        static XboxCpu CreateSti(bool spec, byte[] bytes)
        {
            XboxCpu cpu = Create(spec, bytes);
            cpu.Ram().Write8(0x3000, 0xCF);
            InstallIdt(cpu, 0x21, 0x3000);
            cpu.Context.Eflags &= ~FlagBits.If;
            cpu.InterruptSource = new CountedInterruptSource(0x21, fireAt: 1);
            return cpu;
        }
    }

    [Fact]
    public void EveryStepAndBudget_MatchesCache_StateAndInstructionCounts()
    {
        XboxCpu plain = Create(false, Loop);
        XboxCpu specCpu = Create(true, Loop);
        var plainProfile = new HotspotProfiler();
        var specProfile = new HotspotProfiler();
        plain.HotspotObserver = plainProfile;
        specCpu.HotspotObserver = specProfile;
        for (int i = 0; i < 200; i++)
        {
            if ((i & 1) == 0)
            {
                plain.Step();
                specCpu.Step();
            }
            else
            {
                Assert.Equal(plain.Execute(i), specCpu.Execute(i));
            }

            AssertSameState(plain, specCpu);
            Assert.Equal(plain.DecodeCacheHits, specCpu.DecodeCacheHits);
            Assert.Equal(plain.DecodeCount, specCpu.DecodeCount);
            Assert.Equal(plainProfile.TotalInstructions, specProfile.TotalInstructions);
        }

        Assert.True(specCpu.SpecStatistics!.ShapeHits > 1000);
        Assert.True(specCpu.SpecStatistics.SpecRan > 1000);
    }

    [Fact]
    public void Verify_RejectsStaleMemberBeforeExecution()
    {
        XboxCpu cpu = Create(true, Loop);
        cpu.BlockVerify = true;
        cpu.Execute(5);
        cpu.Step();
        cpu.Ram().Write8(0x1001, 0x90);
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip);
        Assert.Equal(1UL, cpu.SpecStatistics!.VerifyFailures);
        Assert.Equal(1UL, cpu.DecodeCacheVerifyFailures);
    }

    [Fact]
    public void Cap_BoundsArraysAndInvalidatesCursor_WithSpec()
    {
        var blocks = new DecodedBlockCache { SpecEnabled = true };
        var decodes = new DecodeCache { Blocks = blocks };
        for (uint i = 0; i <= DecodedBlockCache.MaxBlocks; i++)
        {
            uint address = (i + 1) * 32;
            Fill(decodes, address, 0x90);
            Fill(decodes, address + 1, 0xC3);
            Assert.True(blocks.TryNext(decodes, address, 32, 32, out _));
        }

        Assert.Equal(DecodedBlockCache.MaxBlocks, blocks.PeakCount);
        Assert.Equal(1UL, blocks.CapFlushes);
        Assert.Equal(1, blocks.Count);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        Assert.NotNull(blocks.CurrentHandler);
        Assert.True(blocks.ShapeHits > 0);
    }

    [Fact]
    public void ModeChange_RedirectsCursor_MatchesInterpreter()
    {
        XboxCpu plain = Create(false, Loop);
        XboxCpu specCpu = Create(true, Loop);
        Assert.Equal(plain.Execute(32), specCpu.Execute(32));
        plain.Context.CodeSegment32 = false;
        specCpu.Context.CodeSegment32 = false;
        for (int i = 0; i < 8; i++)
        {
            plain.Step();
            specCpu.Step();
            AssertSameState(plain, specCpu);
        }

        Assert.True(specCpu.SpecStatistics!.Redirects > 0);
    }

    [Fact]
    public void FlagOff_LeavesSpecDisabled()
    {
        XboxCpu cpu = CpuHarness.Create(Loop);
        Assert.False(cpu.SpecExecutionEnabled);
        Assert.Null(cpu.SpecStatistics);
        Assert.Null(cpu.BlockStatistics);
        cpu.Execute(16);
        Assert.False(cpu.SpecExecutionEnabled);
    }

    [Fact]
    public void FallbackHistogram_TalliesServeTimeShapes()
    {
        // shl r,cl is the only uncovered shape in this loop (the D1
        // implicit-count form carries an immediate and already specializes):
        // inc/xor-imm/dec/nop specialize and the backward jmp specializes
        // too, so the whole fallback total must sit on the Shl shape.
        byte[] code = { 0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xD3, 0xE0, 0xEB, 0xF7 };
        XboxCpu cpu = Create(true, code);
        cpu.Execute(64);
        DecodedBlockCache stats = cpu.SpecStatistics!;
        Assert.True(stats.ShapeFallbacks > 0);
        ulong total = 0;
        XboxCpu.SpecShape jmp = default;
        bool found = false;
        foreach (System.Collections.Generic.KeyValuePair<XboxCpu.SpecShape, ulong> tally in stats.FallbackShapes)
        {
            total += tally.Value;
            if (tally.Key.Mnemonic == Mnemonic.Shl)
            {
                jmp = tally.Key;
                found = true;
            }
            else
            {
                Assert.NotEqual(Mnemonic.Nop, tally.Key.Mnemonic);
            }
        }

        Assert.True(found);
        Assert.Equal(stats.ShapeFallbacks, total);
        Assert.Equal(stats.ShapeFallbacks, stats.FallbackShapes[jmp]);
        (XboxCpu.SpecShape shape, ulong count)[] top = stats.TopFallbackShapes(8);
        Assert.Equal(stats.ShapeFallbacks, top[0].count);
        Assert.Equal(Mnemonic.Shl, top[0].shape.Mnemonic);
    }

    [Fact]
    public void FallbackHistogram_EmptyWhenSpecDisabled()
    {
        var blocks = new DecodedBlockCache();
        var decodes = new DecodeCache { Blocks = blocks };
        Fill(decodes, 0x1000, 0xEB);
        Fill(decodes, 0x1002, 0x90);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        Assert.True(blocks.TryNext(decodes, 0x1002, 32, 32, out _));
        Assert.Equal(0UL, blocks.ShapeFallbacks);
        Assert.Empty(blocks.FallbackShapes);
        Assert.Empty(blocks.TopFallbackShapes(8));
    }

    private static void RunShapes(params (string Name, byte[] Code)[] shapes)
    {
        foreach ((string name, byte[] code) in shapes)
        {
            byte[] withHalt = new byte[code.Length + 1];
            code.CopyTo(withHalt, 0);
            withHalt[^1] = 0xF4;

            XboxCpu plain = Create(false, withHalt);
            XboxCpu specCpu = Create(true, withHalt);
            Preset(plain);
            Preset(specCpu);
            try
            {
                Assert.Equal(plain.Execute(16), specCpu.Execute(16));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Shape '{name}' diverged in run", exception);
            }

            try
            {
                AssertSameState(plain, specCpu);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Shape '{name}' diverged in state", exception);
            }

            Assert.True(plain.Context.Halted && specCpu.Context.Halted);
        }
    }

    private static XboxCpu Create(bool spec, byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        cpu.DecodeCacheEnabled = true;
        if (spec)
        {
            cpu.SpecExecutionEnabled = true;
        }

        return cpu;
    }

    private static void Preset(XboxCpu cpu)
    {
        cpu.Context.Eax = 0x11111111;
        cpu.Context.Ebx = 0x00002000;
        cpu.Context.Ecx = 0x33333333;
        cpu.Context.Edx = 0x44444444;
        cpu.Context.Esi = 0x55555555;
        cpu.Context.Edi = 0x66666666;
        cpu.Context.Ebp = 0x00007000;
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf;
        cpu.Ram().Write32(0x2000, 0xAABBCCDD);
        cpu.Ram().Write32(0x5000, 0x11223344);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x20, 0x3000);
    }

    private static void AssertSameState(XboxCpu expected, XboxCpu actual)
    {
        Assert.Equal(expected.Context.Eip, actual.Context.Eip);
        Assert.Equal(expected.Context.Eflags, actual.Context.Eflags);
        Assert.Equal(expected.Context.Eax, actual.Context.Eax);
        Assert.Equal(expected.Context.Ebx, actual.Context.Ebx);
        Assert.Equal(expected.Context.Ecx, actual.Context.Ecx);
        Assert.Equal(expected.Context.Edx, actual.Context.Edx);
        Assert.Equal(expected.Context.Esi, actual.Context.Esi);
        Assert.Equal(expected.Context.Edi, actual.Context.Edi);
        Assert.Equal(expected.Context.Ebp, actual.Context.Ebp);
        Assert.Equal(expected.Context.Esp, actual.Context.Esp);
        Assert.Equal(expected.Context.Tsc, actual.Context.Tsc);
        Assert.Equal(expected.Context.Halted, actual.Context.Halted);
        Assert.Equal(expected.Context.Cr2, actual.Context.Cr2);
        Assert.Equal(expected.InterruptCount, actual.InterruptCount);
        Assert.Equal(expected.LastFaultVector, actual.LastFaultVector);
        Assert.Equal(expected.LastFaultEip, actual.LastFaultEip);
        Assert.Equal(expected.LastFaultErrorCode, actual.LastFaultErrorCode);
        Assert.Equal(StateBytes(expected), StateBytes(actual));

        PhysicalMemory plainRam = expected.Ram();
        PhysicalMemory specRam = actual.Ram();
        for (uint address = 0; address < 0x10000; address += 4)
        {
            Assert.Equal(plainRam.Read32(address), specRam.Read32(address));
        }
    }

    private static byte[] StateBytes(XboxCpu cpu)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        return stream.ToArray();
    }

    private static void Fill(DecodeCache cache, uint linear, byte opcode)
    {
        Span<byte> bytes = stackalloc byte[15];
        bytes.Clear();
        bytes[0] = opcode;
        cache.Insert(linear, Decoder.Decode(bytes, 32, 32), linear, bytes, 32, 32);
    }

    private static void MapIdentity(AddressSpace bus, uint firstVpn, uint count)
    {
        bus.Write32(PageDirectory, PageTable | 3u);
        for (uint vpn = firstVpn; vpn < firstVpn + count; vpn++)
        {
            bus.Write32(PageTable + vpn * 4, (vpn << 12) | 3u);
        }
    }

    private static void EnablePaging(XboxCpu cpu)
    {
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    private static void InstallIdt(XboxCpu cpu, int vector, uint handler)
    {
        const uint idt = 0x4000;
        uint gate = idt + (uint)vector * 8;
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = idt;
        cpu.Context.Idtr.Limit = 0x7FF;
    }

    /// <summary>Fires one vector at the Nth acknowledge.</summary>
    private sealed class CountedInterruptSource : IInterruptSource
    {
        private readonly int _vector;
        private readonly int _fireAt;
        private int _acks;

        public CountedInterruptSource(int vector, int fireAt)
        {
            _vector = vector;
            _fireAt = fireAt;
        }

        public int Acknowledge()
        {
            _acks++;
            return _acks == _fireAt ? _vector : -1;
        }
    }
}
