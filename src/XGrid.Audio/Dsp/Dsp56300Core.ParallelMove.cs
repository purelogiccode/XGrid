// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc.
// Original copyright (c) 2015 espes.
// Adapted from Hatari DSP M56001 emulation (C) 2003-2008 ARAnyM developer team.

namespace XGrid.Audio.Dsp;

/// <summary>
/// The DSP56300 parallel-move handlers (<c>emu_pm_0</c> .. <c>emu_pm_8</c>).
/// A parallel-move instruction word is an ALU opcode in the low byte plus a
/// move field; the handler performs the move around the ALU opcode.
/// </summary>
public sealed partial class Dsp56300Core
{
    private void ParallelMove(int index)
    {
        switch (index)
        {
            case 0:
                Pm0();
                break;
            case 1:
                Pm1();
                break;
            case 2:
                Pm2();
                break;
            case 3:
                Pm3();
                break;
            case 4:
                Pm4();
                break;
            case 5:
            case 6:
            case 7:
                Pm5();
                break;
            default:
                Pm8();
                break;
        }
    }

    private void Alu(int index) => _alu[index]?.Invoke();

    private void Pm0()
    {
        uint memspace = (State.CurrentInstruction >> 15) & 1;
        uint numreg = (State.CurrentInstruction >> 16) & 1;
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
        _ = PmReadAccu24(numreg, out uint saveAccu);
        uint saveXy0 = R[Dsp56300State.RegX0 + (memspace << 1)];

        Alu((int)(State.CurrentInstruction & Mask(8)));

        WriteMemory(memspace, addr, saveAccu);
        R[Dsp56300State.RegA0 + numreg] = 0;
        R[Dsp56300State.RegA1 + numreg] = saveXy0;
        R[Dsp56300State.RegA2 + numreg] = Flag(saveXy0, 23) ? 0xFFu : 0u;
    }

    private void Pm1()
    {
        uint value = (State.CurrentInstruction >> 8) & Mask(6);
        int retour = CalcEffectiveAddress(value, out uint xyAddr);
        uint memspace = (State.CurrentInstruction >> 14) & 1;
        uint numreg1 = 0;
        uint numreg2;

        if (memspace != 0)
        {
            numreg1 = ((State.CurrentInstruction >> 16) & Mask(2)) switch
            {
                0 => Dsp56300State.RegY0,
                1 => Dsp56300State.RegY1,
                2 => Dsp56300State.RegA,
                _ => Dsp56300State.RegB,
            };
        }
        else
        {
            numreg1 = ((State.CurrentInstruction >> 18) & Mask(2)) switch
            {
                0 => Dsp56300State.RegX0,
                1 => Dsp56300State.RegX1,
                2 => Dsp56300State.RegA,
                _ => Dsp56300State.RegB,
            };
        }

        uint save1;
        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            save1 = retour != 0 ? xyAddr : ReadMemory(memspace, xyAddr);
        }
        else
        {
            save1 = numreg1 == Dsp56300State.RegA || numreg1 == Dsp56300State.RegB
                ? ReadReg24(numreg1)
                : R[numreg1];
        }

        numreg2 = memspace != 0
            ? Dsp56300State.RegA + ((State.CurrentInstruction >> 19) & 1)
            : Dsp56300State.RegA + ((State.CurrentInstruction >> 17) & 1);
        _ = PmReadAccu24(numreg2, out uint save2);

        Alu((int)(State.CurrentInstruction & Mask(8)));

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            if (numreg1 == Dsp56300State.RegA)
            {
                R[Dsp56300State.RegA0] = 0;
                R[Dsp56300State.RegA1] = save1;
                R[Dsp56300State.RegA2] = Flag(save1, 23) ? 0xFFu : 0u;
            }
            else if (numreg1 == Dsp56300State.RegB)
            {
                R[Dsp56300State.RegB0] = 0;
                R[Dsp56300State.RegB1] = save1;
                R[Dsp56300State.RegB2] = Flag(save1, 23) ? 0xFFu : 0u;
            }

            // The reference's else-branch brace closes before this store, so the
            // store is unconditional (it also writes the virtual A/B register).
            R[numreg1] = save1;
        }
        else
        {
            WriteMemory(memspace, xyAddr, save1);
        }

        numreg2 = memspace != 0
            ? Dsp56300State.RegX0 + ((State.CurrentInstruction >> 18) & 1)
            : Dsp56300State.RegY0 + ((State.CurrentInstruction >> 16) & 1);
        R[numreg2] = save2;
    }

    private void Pm2()
    {
        if ((State.CurrentInstruction & 0xFFFF00) == 0x200000)
        {
            Alu((int)(State.CurrentInstruction & Mask(8)));
            return;
        }

        if ((State.CurrentInstruction & 0xFFE000) == 0x204000)
        {
            _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(5), out _);
            Alu((int)(State.CurrentInstruction & Mask(8)));
            return;
        }

        if ((State.CurrentInstruction & 0xFC0000) == 0x200000)
        {
            Pm2_2();
            return;
        }

        Pm3();
    }

    private void Pm2_2()
    {
        uint srcreg = (State.CurrentInstruction >> 13) & Mask(5);
        uint dstreg = (State.CurrentInstruction >> 8) & Mask(5);
        uint saveReg = srcreg == Dsp56300State.RegA || srcreg == Dsp56300State.RegB
            ? ReadReg24(srcreg)
            : R[srcreg];

        Alu((int)(State.CurrentInstruction & Mask(8)));

        if (dstreg == Dsp56300State.RegA)
        {
            R[Dsp56300State.RegA0] = 0;
            R[Dsp56300State.RegA1] = saveReg;
            R[Dsp56300State.RegA2] = Flag(saveReg, 23) ? 0xFFu : 0u;
        }
        else if (dstreg == Dsp56300State.RegB)
        {
            R[Dsp56300State.RegB0] = 0;
            R[Dsp56300State.RegB1] = saveReg;
            R[Dsp56300State.RegB2] = Flag(saveReg, 23) ? 0xFFu : 0u;
        }
        else
        {
            R[dstreg] = saveReg & Mask((int)RegisterMask[dstreg]);
        }
    }

    private void Pm3()
    {
        Alu((int)(State.CurrentInstruction & Mask(8)));

        uint dstreg = (State.CurrentInstruction >> 16) & Mask(5);
        uint srcvalue = (State.CurrentInstruction >> 8) & Mask(8);
        switch (dstreg)
        {
            case Dsp56300State.RegX0:
            case Dsp56300State.RegX1:
            case Dsp56300State.RegY0:
            case Dsp56300State.RegY1:
            case Dsp56300State.RegA:
            case Dsp56300State.RegB:
                srcvalue <<= 16;
                break;
        }

        if (dstreg == Dsp56300State.RegA)
        {
            R[Dsp56300State.RegA0] = 0;
            R[Dsp56300State.RegA1] = srcvalue;
            R[Dsp56300State.RegA2] = Flag(srcvalue, 23) ? 0xFFu : 0u;
        }
        else if (dstreg == Dsp56300State.RegB)
        {
            R[Dsp56300State.RegB0] = 0;
            R[Dsp56300State.RegB1] = srcvalue;
            R[Dsp56300State.RegB2] = Flag(srcvalue, 23) ? 0xFFu : 0u;
        }
        else
        {
            R[dstreg] = srcvalue & Mask((int)RegisterMask[dstreg]);
        }
    }

    private void Pm4()
    {
        if ((State.CurrentInstruction & 0xF40000) == 0x400000)
        {
            Pm4X();
            return;
        }

        Pm5();
    }

    private void Pm4X()
    {
        uint value = (State.CurrentInstruction >> 8) & Mask(6);
        uint lAddr;
        if ((State.CurrentInstruction & (1u << 14)) != 0)
        {
            _ = CalcEffectiveAddress(value, out lAddr);
        }
        else
        {
            lAddr = value;
        }

        uint numreg = (State.CurrentInstruction >> 16) & Mask(2);
        numreg |= (State.CurrentInstruction >> 17) & (1u << 2);

        uint saveLx;
        uint saveLy;
        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            saveLx = ReadMemory(Dsp56300State.SpaceX, lAddr);
            saveLy = ReadMemory(Dsp56300State.SpaceY, lAddr);
        }
        else
        {
            switch (numreg)
            {
                case 0:
                    saveLx = R[Dsp56300State.RegA1];
                    saveLy = R[Dsp56300State.RegA0];
                    break;
                case 1:
                    saveLx = R[Dsp56300State.RegB1];
                    saveLy = R[Dsp56300State.RegB0];
                    break;
                case 2:
                    saveLx = R[Dsp56300State.RegX1];
                    saveLy = R[Dsp56300State.RegX0];
                    break;
                case 3:
                    saveLx = R[Dsp56300State.RegY1];
                    saveLy = R[Dsp56300State.RegY0];
                    break;
                case 4:
                    if (PmReadAccu24(Dsp56300State.RegA, out saveLx) != 0)
                    {
                        saveLy = Flag(saveLx, 23) ? 0u : 0xFFFFFFu;
                    }
                    else
                    {
                        saveLy = R[Dsp56300State.RegA0];
                    }

                    break;
                case 5:
                    if (PmReadAccu24(Dsp56300State.RegB, out saveLx) != 0)
                    {
                        saveLy = Flag(saveLx, 23) ? 0u : 0xFFFFFFu;
                    }
                    else
                    {
                        saveLy = R[Dsp56300State.RegB0];
                    }

                    break;
                case 6:
                    _ = PmReadAccu24(Dsp56300State.RegA, out saveLx);
                    _ = PmReadAccu24(Dsp56300State.RegB, out saveLy);
                    break;
                default:
                    _ = PmReadAccu24(Dsp56300State.RegB, out saveLx);
                    _ = PmReadAccu24(Dsp56300State.RegA, out saveLy);
                    break;
            }
        }

        Alu((int)(State.CurrentInstruction & Mask(8)));

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            switch (numreg)
            {
                case 0:
                    R[Dsp56300State.RegA1] = saveLx;
                    R[Dsp56300State.RegA0] = saveLy;
                    break;
                case 1:
                    R[Dsp56300State.RegB1] = saveLx;
                    R[Dsp56300State.RegB0] = saveLy;
                    break;
                case 2:
                    R[Dsp56300State.RegX1] = saveLx;
                    R[Dsp56300State.RegX0] = saveLy;
                    break;
                case 3:
                    R[Dsp56300State.RegY1] = saveLx;
                    R[Dsp56300State.RegY0] = saveLy;
                    break;
                case 4:
                    R[Dsp56300State.RegA0] = saveLy;
                    R[Dsp56300State.RegA1] = saveLx;
                    R[Dsp56300State.RegA2] = Flag(saveLx, 23) ? 0xFFu : 0u;
                    break;
                case 5:
                    R[Dsp56300State.RegB0] = saveLy;
                    R[Dsp56300State.RegB1] = saveLx;
                    R[Dsp56300State.RegB2] = Flag(saveLx, 23) ? 0xFFu : 0u;
                    break;
                case 6:
                    R[Dsp56300State.RegA0] = 0;
                    R[Dsp56300State.RegA1] = saveLx;
                    R[Dsp56300State.RegA2] = Flag(saveLx, 23) ? 0xFFu : 0u;
                    R[Dsp56300State.RegB0] = 0;
                    R[Dsp56300State.RegB1] = saveLy;
                    R[Dsp56300State.RegB2] = Flag(saveLy, 23) ? 0xFFu : 0u;
                    break;
                default:
                    R[Dsp56300State.RegB0] = 0;
                    R[Dsp56300State.RegB1] = saveLx;
                    R[Dsp56300State.RegB2] = Flag(saveLx, 23) ? 0xFFu : 0u;
                    R[Dsp56300State.RegA0] = 0;
                    R[Dsp56300State.RegA1] = saveLy;
                    R[Dsp56300State.RegA2] = Flag(saveLy, 23) ? 0xFFu : 0u;
                    break;
            }
        }
        else
        {
            WriteMemory(Dsp56300State.SpaceX, lAddr, saveLx);
            WriteMemory(Dsp56300State.SpaceY, lAddr, saveLy);
        }
    }

    private void Pm5()
    {
        uint value = (State.CurrentInstruction >> 8) & Mask(6);
        uint xyAddr;
        int retour;
        if ((State.CurrentInstruction & (1u << 14)) != 0)
        {
            retour = CalcEffectiveAddress(value, out xyAddr);
        }
        else
        {
            xyAddr = value;
            retour = 0;
        }

        uint memspace = (State.CurrentInstruction >> 19) & 1;
        uint numreg = (State.CurrentInstruction >> 16) & Mask(3);
        numreg |= (State.CurrentInstruction >> 17) & (Mask(2) << 3);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            value = retour != 0 ? xyAddr : ReadMemory(memspace, xyAddr);
        }
        else
        {
            value = numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB
                ? ReadReg24(numreg)
                : R[numreg];
        }

        Alu((int)(State.CurrentInstruction & Mask(8)));

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            if (numreg == Dsp56300State.RegA)
            {
                R[Dsp56300State.RegA0] = 0;
                R[Dsp56300State.RegA1] = value;
                R[Dsp56300State.RegA2] = Flag(value, 23) ? 0xFFu : 0u;
            }
            else if (numreg == Dsp56300State.RegB)
            {
                R[Dsp56300State.RegB0] = 0;
                R[Dsp56300State.RegB1] = value;
                R[Dsp56300State.RegB2] = Flag(value, 23) ? 0xFFu : 0u;
            }
            else
            {
                R[numreg] = value & Mask((int)RegisterMask[numreg]);
            }
        }
        else
        {
            WriteMemory(memspace, xyAddr, value);
        }
    }

    private void Pm8()
    {
        uint ea1 = (State.CurrentInstruction >> 8) & Mask(5);
        if ((ea1 >> 3) == 0)
        {
            ea1 |= 1u << 5;
        }

        uint ea2 = (State.CurrentInstruction >> 13) & Mask(2);
        ea2 |= (State.CurrentInstruction >> 17) & (Mask(2) << 3);
        if ((ea1 & (1u << 2)) == 0)
        {
            ea2 |= 1u << 2;
        }

        if ((ea2 >> 3) == 0)
        {
            ea2 |= 1u << 5;
        }

        _ = CalcEffectiveAddress(ea1, out uint xAddr);
        _ = CalcEffectiveAddress(ea2, out uint yAddr);

        uint numreg1 = ((State.CurrentInstruction >> 18) & Mask(2)) switch
        {
            0 => Dsp56300State.RegX0,
            1 => Dsp56300State.RegX1,
            2 => Dsp56300State.RegA,
            _ => Dsp56300State.RegB,
        };

        uint numreg2 = ((State.CurrentInstruction >> 16) & Mask(2)) switch
        {
            0 => Dsp56300State.RegY0,
            1 => Dsp56300State.RegY1,
            2 => Dsp56300State.RegA,
            _ => Dsp56300State.RegB,
        };

        uint saveReg1;
        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            saveReg1 = ReadMemory(Dsp56300State.SpaceX, xAddr);
        }
        else
        {
            saveReg1 = numreg1 == Dsp56300State.RegA || numreg1 == Dsp56300State.RegB
                ? ReadReg24(numreg1)
                : R[numreg1];
        }

        uint saveReg2;
        if ((State.CurrentInstruction & (1u << 22)) != 0)
        {
            saveReg2 = ReadMemory(Dsp56300State.SpaceY, yAddr);
        }
        else
        {
            saveReg2 = numreg2 == Dsp56300State.RegA || numreg2 == Dsp56300State.RegB
                ? ReadReg24(numreg2)
                : R[numreg2];
        }

        Alu((int)(State.CurrentInstruction & Mask(8)));

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            if (numreg1 == Dsp56300State.RegA)
            {
                R[Dsp56300State.RegA0] = 0;
                R[Dsp56300State.RegA1] = saveReg1;
                R[Dsp56300State.RegA2] = Flag(saveReg1, 23) ? 0xFFu : 0u;
            }
            else if (numreg1 == Dsp56300State.RegB)
            {
                R[Dsp56300State.RegB0] = 0;
                R[Dsp56300State.RegB1] = saveReg1;
                R[Dsp56300State.RegB2] = Flag(saveReg1, 23) ? 0xFFu : 0u;
            }
            else
            {
                R[numreg1] = saveReg1;
            }
        }
        else
        {
            WriteMemory(Dsp56300State.SpaceX, xAddr, saveReg1);
        }

        if ((State.CurrentInstruction & (1u << 22)) != 0)
        {
            if (numreg2 == Dsp56300State.RegA)
            {
                R[Dsp56300State.RegA0] = 0;
                R[Dsp56300State.RegA1] = saveReg2;
                R[Dsp56300State.RegA2] = Flag(saveReg2, 23) ? 0xFFu : 0u;
            }
            else if (numreg2 == Dsp56300State.RegB)
            {
                R[Dsp56300State.RegB0] = 0;
                R[Dsp56300State.RegB1] = saveReg2;
                R[Dsp56300State.RegB2] = Flag(saveReg2, 23) ? 0xFFu : 0u;
            }
            else
            {
                R[numreg2] = saveReg2;
            }
        }
        else
        {
            WriteMemory(Dsp56300State.SpaceY, yAddr, saveReg2);
        }
    }
}
