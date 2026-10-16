namespace XGrid.Cpu;

/// <summary>Implemented instruction mnemonics.</summary>
public enum Mnemonic
{
    /// <summary>No instruction (decoder default).</summary>
    None = 0,

    /// <summary>Opcode is not implemented.</summary>
    Unsupported = 1,

    /// <summary>No operation.</summary>
    Nop = 2,

    /// <summary>Copies the second operand to the first.</summary>
    Mov = 3,

    /// <summary>Pushes the operand onto the stack.</summary>
    Push = 4,

    /// <summary>Pops the top of the stack into the operand.</summary>
    Pop = 5,

    /// <summary>Exchanges the two operands.</summary>
    Xchg = 6,

    /// <summary>Loads the effective address of the memory operand.</summary>
    Lea = 7,

    /// <summary>Adds the second operand to the first.</summary>
    Add = 8,

    /// <summary>Bitwise OR of the second operand into the first.</summary>
    Or = 9,

    /// <summary>Adds with carry.</summary>
    Adc = 10,

    /// <summary>Subtracts with borrow.</summary>
    Sbb = 11,

    /// <summary>Bitwise AND of the second operand into the first.</summary>
    And = 12,

    /// <summary>Subtracts the second operand from the first.</summary>
    Sub = 13,

    /// <summary>Bitwise XOR of the second operand into the first.</summary>
    Xor = 14,

    /// <summary>Compares by subtracting without storing the result.</summary>
    Cmp = 15,

    /// <summary>Increments the operand by one.</summary>
    Inc = 16,

    /// <summary>Decrements the operand by one.</summary>
    Dec = 17,

    /// <summary>Bitwise AND that only sets flags.</summary>
    Test = 18,

    /// <summary>Bitwise complement of the operand.</summary>
    Not = 19,

    /// <summary>Two's complement negation of the operand.</summary>
    Neg = 20,

    /// <summary>Unconditional near jump.</summary>
    Jmp = 21,

    /// <summary>Conditional near jump.</summary>
    Jcc = 22,

    /// <summary>Near procedure call.</summary>
    Call = 23,

    /// <summary>Near return from a procedure.</summary>
    Ret = 24,

    /// <summary>Decrements ECX and jumps when ECX is nonzero.</summary>
    Loop = 25,

    /// <summary>Decrements ECX and jumps when ECX is nonzero and equal.</summary>
    LoopE = 26,

    /// <summary>Decrements ECX and jumps when ECX is nonzero and not equal.</summary>
    LoopNe = 27,

    /// <summary>Jumps when ECX is zero.</summary>
    Jecxz = 28,

    /// <summary>Halts the processor until an interrupt arrives.</summary>
    Hlt = 29,

    /// <summary>Clears the carry flag.</summary>
    Clc = 30,

    /// <summary>Sets the carry flag.</summary>
    Stc = 31,

    /// <summary>Clears the direction flag.</summary>
    Cld = 32,

    /// <summary>Sets the direction flag.</summary>
    Std = 33,

    /// <summary>Clears the interrupt flag.</summary>
    Cli = 34,

    /// <summary>Sets the interrupt flag.</summary>
    Sti = 35,

    /// <summary>Pushes EFLAGS onto the stack.</summary>
    Pushf = 36,

    /// <summary>Pops the top of the stack into EFLAGS.</summary>
    Popf = 37,

    /// <summary>Shifts left, filling with zeros.</summary>
    Shl = 38,

    /// <summary>Shifts right (logical), filling with zeros.</summary>
    Shr = 39,

    /// <summary>Shifts right (arithmetic), replicating the sign bit.</summary>
    Sar = 40,

    /// <summary>Rotates bits left.</summary>
    Rol = 41,

    /// <summary>Rotates bits right.</summary>
    Ror = 42,

    /// <summary>Rotates left through the carry flag.</summary>
    Rcl = 43,

    /// <summary>Rotates right through the carry flag.</summary>
    Rcr = 44,

    /// <summary>Unsigned multiply.</summary>
    Mul = 45,

    /// <summary>Signed multiply.</summary>
    Imul = 46,

    /// <summary>Unsigned divide.</summary>
    Div = 47,

    /// <summary>Signed divide.</summary>
    Idiv = 48,

    /// <summary>Software interrupt to the given vector.</summary>
    Interrupt = 49,

    /// <summary>Single-byte breakpoint trap (vector 3).</summary>
    Int3 = 50,

    /// <summary>Interrupt on overflow (vector 4) when OF is set.</summary>
    Into = 51,

    /// <summary>Interrupt return.</summary>
    Iret = 52,

    /// <summary>Loads the global descriptor table register.</summary>
    Lgdt = 53,

    /// <summary>Loads the interrupt descriptor table register.</summary>
    Lidt = 54,

    /// <summary>Stores the global descriptor table register.</summary>
    Sgdt = 55,

    /// <summary>Stores the interrupt descriptor table register.</summary>
    Sidt = 56,

    /// <summary>Moves a control register to a general-purpose register.</summary>
    MovCrRead = 57,

    /// <summary>Moves a general-purpose register to a control register.</summary>
    MovCrWrite = 58,

    /// <summary>Moves a debug register to a general-purpose register.</summary>
    MovDrRead = 59,

    /// <summary>Moves a general-purpose register to a debug register.</summary>
    MovDrWrite = 60,

    /// <summary>Invalidates the TLB entry for a linear address.</summary>
    Invlpg = 61,

    /// <summary>Moves with zero extension.</summary>
    Movzx = 62,

    /// <summary>Moves with sign extension.</summary>
    Movsx = 63,

    /// <summary>Pushes all general-purpose registers.</summary>
    Pusha = 64,

    /// <summary>Pops all general-purpose registers.</summary>
    Popa = 65,

    /// <summary>Returns processor identification and feature information.</summary>
    Cpuid = 66,

    /// <summary>Reads the time-stamp counter into EDX:EAX.</summary>
    Rdtsc = 67,

    /// <summary>Reads a model-specific register into EDX:EAX.</summary>
    Rdmsr = 68,

    /// <summary>Writes EDX:EAX to a model-specific register.</summary>
    Wrmsr = 69,

    /// <summary>Sets a byte to 1 when the condition holds, else 0.</summary>
    Setcc = 70,

    /// <summary>Conditionally moves when the condition holds.</summary>
    Cmovcc = 71,

    /// <summary>Moves a string element.</summary>
    Movs = 72,

    /// <summary>Compares string elements.</summary>
    Cmps = 73,

    /// <summary>Stores EAX/AX/AL to the string destination.</summary>
    Stos = 74,

    /// <summary>Loads EAX/AX/AL from the string source.</summary>
    Lods = 75,

    /// <summary>Compares EAX/AX/AL against the string destination.</summary>
    Scas = 76,

    /// <summary>Waits for pending x87 floating-point exceptions.</summary>
    Fwait = 77,

    /// <summary>x87 no operation.</summary>
    Fnop = 78,

    /// <summary>Pushes a floating-point value onto the x87 stack.</summary>
    Fld = 79,

    /// <summary>Stores the top of the x87 stack without popping.</summary>
    Fst = 80,

    /// <summary>Stores the top of the x87 stack and pops it.</summary>
    Fstp = 81,

    /// <summary>Pushes an integer converted to floating-point.</summary>
    Fild = 82,

    /// <summary>Stores the top of the stack as an integer without popping.</summary>
    Fist = 83,

    /// <summary>Stores the top of the stack as an integer and pops it.</summary>
    Fistp = 84,

    /// <summary>Pushes a packed BCD value converted to floating-point.</summary>
    Fbld = 85,

    /// <summary>Stores the top of the stack as packed BCD and pops it.</summary>
    Fbstp = 86,

    /// <summary>Exchanges ST(0) with another x87 register.</summary>
    Fxch = 87,

    /// <summary>Adds to ST(0).</summary>
    Fadd = 88,

    /// <summary>Multiplies ST(0).</summary>
    Fmul = 89,

    /// <summary>Subtracts from ST(0).</summary>
    Fsub = 90,

    /// <summary>Reverse-subtracts from ST(0).</summary>
    Fsubr = 91,

    /// <summary>Divides ST(0).</summary>
    Fdiv = 92,

    /// <summary>Reverse-divides ST(0).</summary>
    Fdivr = 93,

    /// <summary>Adds ST(0) to ST(1) and pops the stack.</summary>
    Faddp = 94,

    /// <summary>Multiplies ST(1) by ST(0) and pops the stack.</summary>
    Fmulp = 95,

    /// <summary>Subtracts ST(0) from ST(1) and pops the stack.</summary>
    Fsubp = 96,

    /// <summary>Subtracts ST(1) from ST(0) and pops the stack.</summary>
    Fsubrp = 97,

    /// <summary>Divides ST(1) by ST(0) and pops the stack.</summary>
    Fdivp = 98,

    /// <summary>Divides ST(0) by ST(1) and pops the stack.</summary>
    Fdivrp = 99,

    /// <summary>Adds an integer memory operand to ST(0).</summary>
    Fiadd = 100,

    /// <summary>Multiplies ST(0) by an integer memory operand.</summary>
    Fimul = 101,

    /// <summary>Subtracts an integer memory operand from ST(0).</summary>
    Fisub = 102,

    /// <summary>Reverse-subtracts an integer memory operand from ST(0).</summary>
    Fisubr = 103,

    /// <summary>Divides ST(0) by an integer memory operand.</summary>
    Fidiv = 104,

    /// <summary>Reverse-divides ST(0) by an integer memory operand.</summary>
    Fidivr = 105,

    /// <summary>Compares ST(0) and sets the x87 condition flags.</summary>
    Fcom = 106,

    /// <summary>Compares ST(0) and pops the stack.</summary>
    Fcomp = 107,

    /// <summary>Compares ST(0) with ST(1) and pops twice.</summary>
    Fcompp = 108,

    /// <summary>Unordered compare of ST(0).</summary>
    Fucom = 109,

    /// <summary>Unordered compare of ST(0) and pop.</summary>
    Fucomp = 110,

    /// <summary>Unordered compare and pop twice.</summary>
    Fucompp = 111,

    /// <summary>Compares ST(0) and sets EFLAGS directly.</summary>
    Fcomi = 112,

    /// <summary>Compares ST(0), sets EFLAGS directly, and pops.</summary>
    Fcomip = 113,

    /// <summary>Unordered compare of ST(0) setting EFLAGS directly.</summary>
    Fucomi = 114,

    /// <summary>Unordered compare setting EFLAGS directly, and pops.</summary>
    Fucomip = 115,

    /// <summary>Compares ST(0) against zero.</summary>
    Ftst = 116,

    /// <summary>Examines ST(0) and reports its class in the condition flags.</summary>
    Fxam = 117,

    /// <summary>Changes the sign of ST(0).</summary>
    Fchs = 118,

    /// <summary>Takes the absolute value of ST(0).</summary>
    Fabs = 119,

    /// <summary>Computes the square root of ST(0).</summary>
    Fsqrt = 120,

    /// <summary>Rounds ST(0) to an integer per the rounding mode.</summary>
    Frndint = 121,

    /// <summary>Scales ST(0) by a power of two taken from ST(1).</summary>
    Fscale = 122,

    /// <summary>Extracts the exponent and significand of ST(0).</summary>
    Fxtract = 123,

    /// <summary>Computes the partial floating-point remainder.</summary>
    Fprem = 124,

    /// <summary>Computes the IEEE partial floating-point remainder.</summary>
    Fprem1 = 125,

    /// <summary>Decrements the x87 stack top pointer.</summary>
    Fdecstp = 126,

    /// <summary>Increments the x87 stack top pointer.</summary>
    Fincstp = 127,

    /// <summary>Marks an x87 register as empty.</summary>
    Ffree = 128,

    /// <summary>Marks an x87 register as empty and pops the stack.</summary>
    Ffreep = 129,

    /// <summary>Initializes the x87 unit without checking for pending exceptions.</summary>
    Fninit = 130,

    /// <summary>Clears pending x87 exceptions without checking first.</summary>
    Fnclex = 131,

    /// <summary>Loads the x87 control word.</summary>
    Fldcw = 132,

    /// <summary>Stores the x87 control word without checking for exceptions.</summary>
    Fnstcw = 133,

    /// <summary>Loads the x87 environment.</summary>
    Fldenv = 134,

    /// <summary>Stores the x87 environment without checking for exceptions.</summary>
    Fnstenv = 135,

    /// <summary>Saves the x87 state and reinitializes, without checking for exceptions.</summary>
    Fnsave = 136,

    /// <summary>Restores saved x87 state.</summary>
    Frstor = 137,

    /// <summary>Stores the x87 status word without checking for pending exceptions.</summary>
    Fnstsw = 138,

    /// <summary>Stores the x87 status word.</summary>
    Fstsw = 139,

    /// <summary>Moves when below (CF is set).</summary>
    Fcmovb = 140,

    /// <summary>Moves when equal (ZF is set).</summary>
    Fcmove = 141,

    /// <summary>Moves when below or equal.</summary>
    Fcmovbe = 142,

    /// <summary>Moves when unordered (PF is set).</summary>
    Fcmovu = 143,

    /// <summary>Moves when not below.</summary>
    Fcmovnb = 144,

    /// <summary>Moves when not equal.</summary>
    Fcmovne = 145,

    /// <summary>Moves when not below or equal.</summary>
    Fcmovnbe = 146,

    /// <summary>Moves when not unordered.</summary>
    Fcmovnu = 147,

    /// <summary>Pushes +1.0 onto the x87 stack.</summary>
    Fld1 = 148,

    /// <summary>Pushes log2(10) onto the x87 stack.</summary>
    Fldl2T = 149,

    /// <summary>Pushes log2(e) onto the x87 stack.</summary>
    Fldl2E = 150,

    /// <summary>Pushes pi onto the x87 stack.</summary>
    Fldpi = 151,

    /// <summary>Pushes log10(2) onto the x87 stack.</summary>
    Fldlg2 = 152,

    /// <summary>Pushes loge(2) onto the x87 stack.</summary>
    Fldln2 = 153,

    /// <summary>Pushes +0.0 onto the x87 stack.</summary>
    Fldz = 154,

    /// <summary>Replaces ST(0) with 2 raised to ST(0), minus 1.</summary>
    F2Xm1 = 155,

    /// <summary>Replaces ST(1) with ST(1) times log2(ST(0)) and pops.</summary>
    Fyl2X = 156,

    /// <summary>Replaces ST(1) with ST(1) times log2(ST(0) + 1) and pops.</summary>
    Fyl2Xp1 = 157,

    /// <summary>Replaces ST(0) with its partial tangent and pushes 1.0.</summary>
    Fptan = 158,

    /// <summary>Replaces ST(1) with atan(ST(1) / ST(0)) and pops.</summary>
    Fpatan = 159,

    /// <summary>Replaces ST(0) with its sine.</summary>
    Fsin = 160,

    /// <summary>Replaces ST(0) with its cosine.</summary>
    Fcos = 161,

    /// <summary>Replaces ST(0) with its sine and pushes its cosine.</summary>
    Fsincos = 162,

    /// <summary>Moves a 32-bit value between a general-purpose and an MMX register.</summary>
    Movd = 163,

    /// <summary>Moves a 64-bit MMX value.</summary>
    Movq = 164,

    /// <summary>Packs words into bytes with signed saturation.</summary>
    Packsswb = 165,

    /// <summary>Packs doublewords into words with signed saturation.</summary>
    Packssdw = 166,

    /// <summary>Packs words into bytes with unsigned saturation.</summary>
    Packuswb = 167,

    /// <summary>Unpacks and interleaves the low bytes.</summary>
    Punpcklbw = 168,

    /// <summary>Unpacks and interleaves the low words.</summary>
    Punpcklwd = 169,

    /// <summary>Unpacks and interleaves the low doublewords.</summary>
    Punpckldq = 170,

    /// <summary>Unpacks and interleaves the high bytes.</summary>
    Punpckhbw = 171,

    /// <summary>Unpacks and interleaves the high words.</summary>
    Punpckhwd = 172,

    /// <summary>Unpacks and interleaves the high doublewords.</summary>
    Punpckhdq = 173,

    /// <summary>Adds packed bytes.</summary>
    Paddb = 174,

    /// <summary>Adds packed words.</summary>
    Paddw = 175,

    /// <summary>Adds packed doublewords.</summary>
    Paddd = 176,

    /// <summary>Adds packed bytes with signed saturation.</summary>
    Paddsb = 177,

    /// <summary>Adds packed words with signed saturation.</summary>
    Paddsw = 178,

    /// <summary>Adds packed bytes with unsigned saturation.</summary>
    Paddusb = 179,

    /// <summary>Adds packed words with unsigned saturation.</summary>
    Paddusw = 180,

    /// <summary>Subtracts packed bytes.</summary>
    Psubb = 181,

    /// <summary>Subtracts packed words.</summary>
    Psubw = 182,

    /// <summary>Subtracts packed doublewords.</summary>
    Psubd = 183,

    /// <summary>Subtracts packed bytes with signed saturation.</summary>
    Psubsb = 184,

    /// <summary>Subtracts packed words with signed saturation.</summary>
    Psubsw = 185,

    /// <summary>Subtracts packed bytes with unsigned saturation.</summary>
    Psubusb = 186,

    /// <summary>Subtracts packed words with unsigned saturation.</summary>
    Psubusw = 187,

    /// <summary>Multiplies packed words, storing the low halves.</summary>
    Pmullw = 188,

    /// <summary>Multiplies signed packed words, storing the high halves.</summary>
    Pmulhw = 189,

    /// <summary>Multiplies unsigned packed words, storing the high halves.</summary>
    Pmulhuw = 190,

    /// <summary>Multiplies packed words and adds adjacent pairs into doublewords.</summary>
    Pmaddwd = 191,

    /// <summary>Compares packed bytes for equality.</summary>
    Pcmpeqb = 192,

    /// <summary>Compares packed words for equality.</summary>
    Pcmpeqw = 193,

    /// <summary>Compares packed doublewords for equality.</summary>
    Pcmpeqd = 194,

    /// <summary>Compares packed bytes for greater-than.</summary>
    Pcmpgtb = 195,

    /// <summary>Compares packed words for greater-than.</summary>
    Pcmpgtw = 196,

    /// <summary>Compares packed doublewords for greater-than.</summary>
    Pcmpgtd = 197,

    /// <summary>Bitwise AND of 64-bit MMX values.</summary>
    Pand = 198,

    /// <summary>Bitwise AND-NOT of 64-bit MMX values.</summary>
    Pandn = 199,

    /// <summary>Bitwise OR of 64-bit MMX values.</summary>
    Por = 200,

    /// <summary>Bitwise XOR of 64-bit MMX values.</summary>
    Pxor = 201,

    /// <summary>Shifts packed words left.</summary>
    Psllw = 202,

    /// <summary>Shifts packed doublewords left.</summary>
    Pslld = 203,

    /// <summary>Shifts the 64-bit value left.</summary>
    Psllq = 204,

    /// <summary>Shifts packed words right (logical).</summary>
    Psrlw = 205,

    /// <summary>Shifts packed doublewords right (logical).</summary>
    Psrld = 206,

    /// <summary>Shifts the 64-bit value right (logical).</summary>
    Psrlq = 207,

    /// <summary>Shifts packed words right (arithmetic).</summary>
    Psraw = 208,

    /// <summary>Shifts packed doublewords right (arithmetic).</summary>
    Psrad = 209,

    /// <summary>Empties the MMX state, freeing the x87 registers.</summary>
    Emms = 210,

    /// <summary>Averages packed unsigned bytes.</summary>
    Pavgb = 211,

    /// <summary>Averages packed unsigned words.</summary>
    Pavgw = 212,

    /// <summary>Minimum of packed unsigned bytes.</summary>
    Pminub = 213,

    /// <summary>Maximum of packed unsigned bytes.</summary>
    Pmaxub = 214,

    /// <summary>Minimum of packed unsigned words.</summary>
    Pminuw = 215,

    /// <summary>Maximum of packed unsigned words.</summary>
    Pmaxuw = 216,

    /// <summary>Sums absolute byte differences into words.</summary>
    Psadbw = 217,

    /// <summary>Builds a byte mask from the high bits of packed bytes.</summary>
    Pmovmskb = 218,

    /// <summary>Extracts a word from an MMX register.</summary>
    Pextrw = 219,

    /// <summary>Inserts a word into an MMX register.</summary>
    Pinsrw = 220,

    /// <summary>Shuffles packed words.</summary>
    Pshufw = 221,

    /// <summary>Non-temporal store of an MMX value.</summary>
    Movntq = 222,

    /// <summary>Conditionally stores bytes selected by a mask.</summary>
    Maskmovq = 223,

    /// <summary>Moves aligned packed single-precision values.</summary>
    Movaps = 224,

    /// <summary>Moves unaligned packed single-precision values.</summary>
    Movups = 225,

    /// <summary>Moves a scalar single-precision value.</summary>
    Movss = 226,

    /// <summary>Moves the high 64 bits of packed singles.</summary>
    Movhps = 227,

    /// <summary>Moves the low 64 bits of packed singles.</summary>
    Movlps = 228,

    /// <summary>Moves packed singles from the high to the low half.</summary>
    Movhlps = 229,

    /// <summary>Moves packed singles from the low to the high half.</summary>
    Movlhps = 230,

    /// <summary>Builds a mask from the sign bits of packed singles.</summary>
    Movmskps = 231,

    /// <summary>Adds packed single-precision values.</summary>
    Addps = 232,

    /// <summary>Adds scalar single-precision values.</summary>
    Addss = 233,

    /// <summary>Subtracts packed single-precision values.</summary>
    Subps = 234,

    /// <summary>Subtracts scalar single-precision values.</summary>
    Subss = 235,

    /// <summary>Multiplies packed single-precision values.</summary>
    Mulps = 236,

    /// <summary>Multiplies scalar single-precision values.</summary>
    Mulss = 237,

    /// <summary>Divides packed single-precision values.</summary>
    Divps = 238,

    /// <summary>Divides scalar single-precision values.</summary>
    Divss = 239,

    /// <summary>Square root of packed single-precision values.</summary>
    Sqrtps = 240,

    /// <summary>Square root of a scalar single-precision value.</summary>
    Sqrtss = 241,

    /// <summary>Reciprocal square-root approximation of packed singles.</summary>
    Rsqrtps = 242,

    /// <summary>Reciprocal square-root approximation of a scalar single.</summary>
    Rsqrtss = 243,

    /// <summary>Reciprocal approximation of packed singles.</summary>
    Rcpps = 244,

    /// <summary>Reciprocal approximation of a scalar single.</summary>
    Rcpss = 245,

    /// <summary>Maximum of packed single-precision values.</summary>
    Maxps = 246,

    /// <summary>Maximum of scalar single-precision values.</summary>
    Maxss = 247,

    /// <summary>Minimum of packed single-precision values.</summary>
    Minps = 248,

    /// <summary>Minimum of scalar single-precision values.</summary>
    Minss = 249,

    /// <summary>Bitwise AND of packed single-precision values.</summary>
    Andps = 250,

    /// <summary>Bitwise AND-NOT of packed single-precision values.</summary>
    Andnps = 251,

    /// <summary>Bitwise OR of packed single-precision values.</summary>
    Orps = 252,

    /// <summary>Bitwise XOR of packed single-precision values.</summary>
    Xorps = 253,

    /// <summary>Compares packed single-precision values.</summary>
    Cmpps = 254,

    /// <summary>Compares scalar single-precision values.</summary>
    Cmpss = 255,

    /// <summary>Ordered compare of scalar singles, setting EFLAGS.</summary>
    Comiss = 256,

    /// <summary>Unordered compare of scalar singles, setting EFLAGS.</summary>
    Ucomiss = 257,

    /// <summary>Shuffles packed single-precision values.</summary>
    Shufps = 258,

    /// <summary>Unpacks and interleaves the high packed singles.</summary>
    Unpckhps = 259,

    /// <summary>Unpacks and interleaves the low packed singles.</summary>
    Unpcklps = 260,

    /// <summary>Converts packed integers to packed single-precision values.</summary>
    Cvtpi2Ps = 261,

    /// <summary>Converts packed singles to packed integers.</summary>
    Cvtps2Pi = 262,

    /// <summary>Converts an integer to a scalar single-precision value.</summary>
    Cvtsi2Ss = 263,

    /// <summary>Converts a scalar single to an integer.</summary>
    Cvtss2Si = 264,

    /// <summary>Converts packed singles to packed integers with truncation.</summary>
    Cvttps2Pi = 265,

    /// <summary>Converts a scalar single to an integer with truncation.</summary>
    Cvttss2Si = 266,

    /// <summary>Loads the MXCSR control register.</summary>
    Ldmxcsr = 267,

    /// <summary>Stores the MXCSR control register.</summary>
    Stmxcsr = 268,

    /// <summary>Saves x87 and SSE state to memory.</summary>
    Fxsave = 269,

    /// <summary>Restores x87 and SSE state from memory.</summary>
    Fxrstor = 270,

    /// <summary>Prefetch hint that a memory location will be read.</summary>
    Prefetch = 271,

    /// <summary>Orders non-temporal stores.</summary>
    Sfence = 272,

    /// <summary>Non-temporal store of packed single-precision values.</summary>
    Movntps = 273,

    /// <summary>x87 integer compare (appended to keep the existing values stable).</summary>
    Ficom = 274,

    /// <summary>x87 integer compare and pop.</summary>
    Ficomp = 275,

    /// <summary>LFENCE (no-op in the interpreter).</summary>
    Lfence = 276,

    /// <summary>MFENCE (no-op in the interpreter).</summary>
    Mfence = 277,

    /// <summary>IN (I/O port read).</summary>
    In = 278,

    /// <summary>OUT (I/O port write).</summary>
    Out = 279,

    /// <summary>Far JMP (ptr16:16/32 or m16:16/32).</summary>
    JmpFar = 280,

    /// <summary>Far CALL (ptr16:16/32 or m16:16/32).</summary>
    CallFar = 281,

    /// <summary>Far return.</summary>
    Retf = 282,

    /// <summary>MOV segment register from r/m16.</summary>
    MovSreg = 283,

    /// <summary>MOV r/m16 from segment register.</summary>
    MovSregRead = 284,

    /// <summary>PUSH segment register.</summary>
    PushSegment = 285,

    /// <summary>POP segment register.</summary>
    PopSegment = 286,

    /// <summary>Invalidate internal caches.</summary>
    Invd = 287,

    /// <summary>Write back and invalidate internal caches.</summary>
    Wbinvd = 288,

    /// <summary>Adjust RPL field of segment selector.</summary>
    Arpl = 289,

    /// <summary>High-level procedure exit (MOV ESP, EBP then POP EBP).</summary>
    Leave = 290,

    /// <summary>High-level procedure entry (frame allocation with nesting).</summary>
    Enter = 291,

    /// <summary>Byte swap of a 32-bit register.</summary>
    Bswap = 292,

    /// <summary>Store local descriptor table register.</summary>
    Sldt = 293,

    /// <summary>Store task register.</summary>
    Str = 294,

    /// <summary>Load local descriptor table register.</summary>
    Lldt = 295,

    /// <summary>Load task register.</summary>
    Ltr = 296,

    /// <summary>Verify a segment for reading.</summary>
    Verr = 297,

    /// <summary>Verify a segment for writing.</summary>
    Verw = 298,

    /// <summary>Input string from port.</summary>
    Ins = 299,

    /// <summary>Output string to port.</summary>
    Outs = 300,

    /// <summary>Exchange and add.</summary>
    Xadd = 301,

    /// <summary>Compare and exchange.</summary>
    Cmpxchg = 302,

    /// <summary>Bit scan forward.</summary>
    Bsf = 303,

    /// <summary>Bit scan reverse.</summary>
    Bsr = 304,

    /// <summary>Bit test.</summary>
    Bt = 305,

    /// <summary>Bit test and set.</summary>
    Bts = 306,

    /// <summary>Bit test and reset.</summary>
    Btr = 307,

    /// <summary>Bit test and complement.</summary>
    Btc = 308,

    /// <summary>ASCII adjust AX after multiply.</summary>
    Aam = 309,

    /// <summary>ASCII adjust AX before division.</summary>
    Aad = 310,

    /// <summary>Convert byte to word / word to doubleword (sign extension).</summary>
    Cbw = 311,

    /// <summary>Convert word to doubleword / doubleword to quadword (sign extension).</summary>
    Cdq = 312,

    /// <summary>Double-precision shift left.</summary>
    Shld = 313,

    /// <summary>Double-precision shift right.</summary>
    Shrd = 314,

    /// <summary>Compare and exchange 8 bytes.</summary>
    Cmpxchg8B = 315,

    /// <summary>Complement carry flag.</summary>
    Cmc = 316,
}
