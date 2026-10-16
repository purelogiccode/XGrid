using System;
using System.IO;
using XGrid.Core.Devices;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class CpuSnapshotTests
{
    [Fact]
    public void CpuState_RoundTrip_PreservesEveryField()
    {
        XboxCpu cpu = CpuHarness.Create(
            0xB9, 0x74, 0x01, 0x00, 0x00, // mov ecx, 0x174
            0xB8, 0x11, 0x11, 0x11, 0x11, // mov eax, 0x11111111
            0x31, 0xD2, // xor edx, edx
            0x0F, 0x30, // wrmsr (SYSENTER_CS)
            0xB9, 0x75, 0x01, 0x00, 0x00, // mov ecx, 0x175
            0xB8, 0x22, 0x22, 0x22, 0x22, // mov eax, 0x22222222
            0x0F, 0x30, // wrmsr (SYSENTER_ESP)
            0xB9, 0x76, 0x01, 0x00, 0x00, // mov ecx, 0x176
            0xB8, 0x33, 0x33, 0x33, 0x33, // mov eax, 0x33333333
            0x0F, 0x30, // wrmsr (SYSENTER_EIP)
            0xF4); // hlt
        cpu.Execute(64);
        Assert.True(cpu.Context.Halted);

        // Dirty every directly-settable field with unmistakable values.
        uint[] gprs =
            { 0xA1A1A1A1, 0xB2B2B2B2, 0xC3C3C3C3, 0xD4D4D4D4, 0xE5E5E5E5, 0xF6F6F6F6, 0x17171717, 0x28282828 };
        for (int i = 0; i < 8; i++)
        {
            cpu.Context.WriteRegister((Gpr)i, gprs[i]);
        }

        cpu.Context.Eip = 0x12345678;
        cpu.Context.Eflags = 0x00003246;

        ushort[] selectors = { 0x10, 0x08, 0x18, 0x20, 0x28, 0x30 };
        for (int i = 0; i < 6; i++)
        {
            cpu.Context.SetSegment((SegmentRegister)i, selectors[i], 0x10000000u + (uint)(i * 0x111111));
        }

        cpu.Context.CodeSegment32 = true;
        cpu.Context.Tr = 0x38;
        cpu.Context.Ldtr = 0x40;
        cpu.Context.Cr0 = 0x00000011; // Protected mode, paging off (page tables are RAM state).
        cpu.Context.Cr2 = 0xDEADBEEF;
        cpu.Context.Cr3 = 0x00300000;
        cpu.Context.Cr4 = 0x00000690;

        for (int i = 0; i < 8; i++)
        {
            cpu.Context.WriteDebugRegister(i, 0xD0000000u + (uint)i);
        }

        cpu.Context.Gdtr.Base = 0x00007000;
        cpu.Context.Gdtr.Limit = 0x00FF;
        cpu.Context.Idtr.Base = 0x00004000;
        cpu.Context.Idtr.Limit = 0x07FF;

        cpu.Context.Tsc = 0x123456789ABCDEF;
        cpu.Context.Mxcsr = 0x00009FC0;

        FpuState fpu = cpu.Context.Fpu;
        fpu.ControlWord = 0x0A7F;
        fpu.Push(Fpu80.FromDouble(1.0));
        fpu.Push(Fpu80.FromDouble(-2.5));
        fpu.Push(Fpu80.FromDouble(Math.PI));
        fpu.Push(Fpu80.FromDouble(double.NaN));
        fpu.SetConditionCodes(c3: true, c2: false, c0: true);
        fpu.SetC1(true);

        for (int i = 0; i < 8; i++)
        {
            cpu.Context.WriteXmm128(i, 0x1111111100000000UL + (uint)i, 0x2222222200000000UL + (uint)i);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        // Corrupt everything, then restore.
        cpu.Context.Eip = 0;
        cpu.Context.Eflags = 0;
        for (int i = 0; i < 8; i++)
        {
            cpu.Context.WriteRegister((Gpr)i, 0);
            cpu.Context.WriteDebugRegister(i, 0);
            cpu.Context.WriteXmm128(i, 0, 0);
        }

        for (int i = 0; i < 6; i++)
        {
            cpu.Context.SetSegment((SegmentRegister)i, 0, 0);
        }

        cpu.Context.CodeSegment32 = false;
        cpu.Context.Tr = 0;
        cpu.Context.Ldtr = 0;
        cpu.Context.Cr0 = 0;
        cpu.Context.Cr2 = 0;
        cpu.Context.Cr3 = 0;
        cpu.Context.Cr4 = 0;
        cpu.Context.Tsc = 0;
        cpu.Context.Mxcsr = 0x1F80;
        cpu.Context.Fpu.Init();
        ZeroSysenterMsrs(cpu);

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            cpu.LoadState(reader);
        }

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(gprs[i], cpu.Context.ReadRegister((Gpr)i));
            Assert.Equal(0xD0000000u + (uint)i, cpu.Context.ReadDebugRegister(i));
            (ulong low, ulong high) = cpu.Context.ReadXmm128(i);
            Assert.Equal(0x1111111100000000UL + (uint)i, low);
            Assert.Equal(0x2222222200000000UL + (uint)i, high);
        }

        Assert.Equal(0x12345678u, cpu.Context.Eip);
        Assert.Equal(0x00003246u, cpu.Context.Eflags);

        for (int i = 0; i < 6; i++)
        {
            SegmentRegister segment = (SegmentRegister)i;
            Assert.Equal(selectors[i], cpu.Context.GetSegmentSelector(segment));
            Assert.Equal(0x10000000u + (uint)(i * 0x111111), cpu.Context.GetSegmentBase(segment));
        }

        Assert.True(cpu.Context.CodeSegment32);
        Assert.Equal(0x38, cpu.Context.Tr);
        Assert.Equal(0x40, cpu.Context.Ldtr);
        Assert.Equal(0x00000011u, cpu.Context.Cr0);
        Assert.Equal(0xDEADBEEFu, cpu.Context.Cr2);
        Assert.Equal(0x00300000u, cpu.Context.Cr3);
        Assert.Equal(0x00000690u, cpu.Context.Cr4);

        Assert.Equal(0x00007000u, cpu.Context.Gdtr.Base);
        Assert.Equal(0x00FF, cpu.Context.Gdtr.Limit);
        Assert.Equal(0x00004000u, cpu.Context.Idtr.Base);
        Assert.Equal(0x07FF, cpu.Context.Idtr.Limit);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x123456789ABCDEFu, cpu.Context.Tsc);
        Assert.Equal(0x00009FC0u, cpu.Context.Mxcsr);

        Assert.Equal(0x0A7F, fpu.ControlWord);
        Assert.Equal(4, fpu.Top);
        Assert.Equal(Fpu80.FromDouble(double.NaN), fpu.GetRegister(0));
        Assert.Equal(Fpu80.FromDouble(Math.PI), fpu.GetRegister(1));
        Assert.Equal(Fpu80.FromDouble(-2.5), fpu.GetRegister(2));
        Assert.Equal(Fpu80.FromDouble(1.0), fpu.GetRegister(3));

        // SYSENTER MSRs have no public getter; verify via RDMSR execution.
        AssertSysenter(cpu, 0x174, 0x11111111);
        AssertSysenter(cpu, 0x175, 0x22222222);
        AssertSysenter(cpu, 0x176, 0x33333333);
    }

    [Fact]
    public void CpuState_HaltedAndInterruptCount_RoundTrip()
    {
        XboxCpu cpu = CpuHarness.Create(
            0xB8, 0x34, 0x12, 0x00, 0x00, // mov eax, 0x1234
            0xF4); // hlt
        InstallIdt(cpu, 0x21, 0x3000);
        cpu.Bus().Write8(0x3000, 0xF4); // handler: hlt
        cpu.Context.Eflags |= FlagBits.If;
        cpu.InterruptSource = new SingleShotInterruptSource(0x21);

        cpu.Step();

        Assert.Equal(1, cpu.InterruptCount);
        Assert.Equal(0x3000u, cpu.Context.Eip);
        Assert.False(cpu.Context.Halted);

        cpu.Step();

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x3001u, cpu.Context.Eip);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        cpu.Context.Eip = 0x9999;
        cpu.Context.Halted = false;

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            cpu.LoadState(reader);
        }

        Assert.Equal(1, cpu.InterruptCount);
        Assert.Equal(0x3001u, cpu.Context.Eip);
        Assert.True(cpu.Context.Halted);
    }

    [Fact]
    public void CpuState_LastFault_RoundTrips()
    {
        XboxCpu cpu = CpuHarness.Create(0xD6); // salc: invalid opcode, no IDT installed

        Assert.Throws<UnsupportedInstructionException>(() => cpu.Step());
        Assert.Equal(6, cpu.LastFaultVector);
        Assert.Equal(CpuHarness.CodeAddress, cpu.LastFaultEip);
        Assert.Equal(0u, cpu.LastFaultErrorCode);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        // Cause a different fault at a different EIP.
        cpu.Bus().Write8(0x2000, 0xD6);
        cpu.Context.Eip = 0x2000;
        Assert.Throws<UnsupportedInstructionException>(() => cpu.Step());
        Assert.Equal(0x2000u, cpu.LastFaultEip);

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            cpu.LoadState(reader);
        }

        Assert.Equal(6, cpu.LastFaultVector);
        Assert.Equal(CpuHarness.CodeAddress, cpu.LastFaultEip);
        Assert.Equal(0u, cpu.LastFaultErrorCode);
        Assert.Equal(CpuHarness.CodeAddress, cpu.Context.Eip);
    }

    [Fact]
    public void CpuState_BadVersion_Throws()
    {
        XboxCpu cpu = CpuHarness.Create(0x90);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(99u);
        }

        stream.Position = 0;
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Assert.Throws<InvalidDataException>(() => cpu.LoadState(reader));
        }
    }

    private static void ZeroSysenterMsrs(XboxCpu cpu)
    {
        const uint code = 0x5000;
        byte[] program =
        {
            0xB9, 0x74, 0x01, 0x00, 0x00, // mov ecx, 0x174
            0x31, 0xC0, // xor eax, eax
            0x31, 0xD2, // xor edx, edx
            0x0F, 0x30, // wrmsr
            0xB9, 0x75, 0x01, 0x00, 0x00, // mov ecx, 0x175
            0x0F, 0x30, // wrmsr
            0xB9, 0x76, 0x01, 0x00, 0x00, // mov ecx, 0x176
            0x0F, 0x30, // wrmsr
            0xF4, // hlt
        };

        for (int i = 0; i < program.Length; i++)
        {
            cpu.Bus().Write8(code + (uint)i, program[i]);
        }

        uint savedEip = cpu.Context.Eip;
        bool savedHalted = cpu.Context.Halted;
        cpu.Context.Eip = code;
        cpu.Context.Halted = false;
        cpu.Execute(16);
        cpu.Context.Eip = savedEip;
        cpu.Context.Halted = savedHalted;
    }

    private static void AssertSysenter(XboxCpu cpu, uint msr, uint expected)
    {
        const uint code = 0x5100;
        byte[] program =
        {
            0xB9, (byte)msr, (byte)(msr >> 8), (byte)(msr >> 16), (byte)(msr >> 24), // mov ecx, msr
            0x0F, 0x32, // rdmsr
            0xF4, // hlt
        };

        AddressSpace bus = cpu.Bus();
        for (int i = 0; i < program.Length; i++)
        {
            bus.Write8(code + (uint)i, program[i]);
        }

        uint savedEip = cpu.Context.Eip;
        bool savedHalted = cpu.Context.Halted;
        uint savedEax = cpu.Context.Eax;
        uint savedEcx = cpu.Context.Ecx;
        uint savedEdx = cpu.Context.Edx;
        ushort savedCs = cpu.Context.GetSegmentSelector(SegmentRegister.Cs);
        uint savedCsBase = cpu.Context.GetSegmentBase(SegmentRegister.Cs);
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.Eip = code;
        cpu.Context.Halted = false;
        cpu.Execute(8);

        Assert.Equal(expected, cpu.Context.Eax);

        cpu.Context.Eip = savedEip;
        cpu.Context.Halted = savedHalted;
        cpu.Context.Eax = savedEax;
        cpu.Context.Ecx = savedEcx;
        cpu.Context.Edx = savedEdx;
        cpu.Context.SetSegment(SegmentRegister.Cs, savedCs, savedCsBase);
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

    private sealed class SingleShotInterruptSource : IInterruptSource
    {
        private readonly int _vector;
        private bool _fired;

        public SingleShotInterruptSource(int vector)
        {
            _vector = vector;
        }

        public int Acknowledge()
        {
            if (_fired)
            {
                return -1;
            }

            _fired = true;
            return _vector;
        }
    }
}
