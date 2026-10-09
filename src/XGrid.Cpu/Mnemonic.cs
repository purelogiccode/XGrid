namespace XGrid.Cpu;

/// <summary>Implemented instruction mnemonics.</summary>
public enum Mnemonic
{
    /// <summary>No instruction (decoder default).</summary>
    None = 0,

    /// <summary>Opcode is not implemented.</summary>
    Unsupported = 1,

    Nop = 2,
    Mov = 3,
    Push = 4,
    Pop = 5,
    Xchg = 6,
    Lea = 7,

    Add = 8,
    Or = 9,
    Adc = 10,
    Sbb = 11,
    And = 12,
    Sub = 13,
    Xor = 14,
    Cmp = 15,
    Inc = 16,
    Dec = 17,
    Test = 18,
    Not = 19,
    Neg = 20,

    Jmp = 21,
    Jcc = 22,
    Call = 23,
    Ret = 24,
    Loop = 25,
    LoopE = 26,
    LoopNe = 27,
    Jecxz = 28,
    Hlt = 29,

    Clc = 30,
    Stc = 31,
    Cld = 32,
    Std = 33,
    Cli = 34,
    Sti = 35,
    Pushf = 36,
    Popf = 37,

    Shl = 38,
    Shr = 39,
    Sar = 40,
    Rol = 41,
    Ror = 42,
    Rcl = 43,
    Rcr = 44,
    Mul = 45,
    Imul = 46,
    Div = 47,
    Idiv = 48,
    Aam = 309,
    Aad = 310,

    Interrupt = 49,
    Int3 = 50,
    Into = 51,
    Iret = 52,

    Lgdt = 53,
    Lidt = 54,
    Sgdt = 55,
    Sidt = 56,

    MovCrRead = 57,
    MovCrWrite = 58,
    MovDrRead = 59,
    MovDrWrite = 60,
    Invlpg = 61,

    Movzx = 62,
    Movsx = 63,
    Pusha = 64,
    Popa = 65,
    Cpuid = 66,
    Rdtsc = 67,
    Rdmsr = 68,
    Wrmsr = 69,
    Setcc = 70,
    Cmovcc = 71,

    Movs = 72,
    Cmps = 73,
    Stos = 74,
    Lods = 75,
    Scas = 76,

    Fwait = 77,
    Fnop = 78,
    Fld = 79,
    Fst = 80,
    Fstp = 81,
    Fild = 82,
    Fist = 83,
    Fistp = 84,
    Fbld = 85,
    Fbstp = 86,
    Fxch = 87,

    Fadd = 88,
    Fmul = 89,
    Fsub = 90,
    Fsubr = 91,
    Fdiv = 92,
    Fdivr = 93,
    Faddp = 94,
    Fmulp = 95,
    Fsubp = 96,
    Fsubrp = 97,
    Fdivp = 98,
    Fdivrp = 99,
    Fiadd = 100,
    Fimul = 101,
    Fisub = 102,
    Fisubr = 103,
    Fidiv = 104,
    Fidivr = 105,

    Fcom = 106,
    Fcomp = 107,
    Fcompp = 108,
    Fucom = 109,
    Fucomp = 110,
    Fucompp = 111,
    Fcomi = 112,
    Fcomip = 113,
    Fucomi = 114,
    Fucomip = 115,
    Ftst = 116,
    Fxam = 117,

    Fchs = 118,
    Fabs = 119,
    Fsqrt = 120,
    Frndint = 121,
    Fscale = 122,
    Fxtract = 123,
    Fprem = 124,
    Fprem1 = 125,
    Fdecstp = 126,
    Fincstp = 127,
    Ffree = 128,
    Ffreep = 129,
    Fninit = 130,
    Fnclex = 131,

    Fldcw = 132,
    Fnstcw = 133,
    Fldenv = 134,
    Fnstenv = 135,
    Fnsave = 136,
    Frstor = 137,
    Fnstsw = 138,
    Fstsw = 139,

    Fcmovb = 140,
    Fcmove = 141,
    Fcmovbe = 142,
    Fcmovu = 143,
    Fcmovnb = 144,
    Fcmovne = 145,
    Fcmovnbe = 146,
    Fcmovnu = 147,

    Fld1 = 148,
    Fldl2t = 149,
    Fldl2e = 150,
    Fldpi = 151,
    Fldlg2 = 152,
    Fldln2 = 153,
    Fldz = 154,

    F2xm1 = 155,
    Fyl2x = 156,
    Fyl2xp1 = 157,
    Fptan = 158,
    Fpatan = 159,
    Fsin = 160,
    Fcos = 161,
    Fsincos = 162,

    Movd = 163,
    Movq = 164,
    Packsswb = 165,
    Packssdw = 166,
    Packuswb = 167,
    Punpcklbw = 168,
    Punpcklwd = 169,
    Punpckldq = 170,
    Punpckhbw = 171,
    Punpckhwd = 172,
    Punpckhdq = 173,
    Paddb = 174,
    Paddw = 175,
    Paddd = 176,
    Paddsb = 177,
    Paddsw = 178,
    Paddusb = 179,
    Paddusw = 180,
    Psubb = 181,
    Psubw = 182,
    Psubd = 183,
    Psubsb = 184,
    Psubsw = 185,
    Psubusb = 186,
    Psubusw = 187,
    Pmullw = 188,
    Pmulhw = 189,
    Pmulhuw = 190,
    Pmaddwd = 191,
    Pcmpeqb = 192,
    Pcmpeqw = 193,
    Pcmpeqd = 194,
    Pcmpgtb = 195,
    Pcmpgtw = 196,
    Pcmpgtd = 197,
    Pand = 198,
    Pandn = 199,
    Por = 200,
    Pxor = 201,
    Psllw = 202,
    Pslld = 203,
    Psllq = 204,
    Psrlw = 205,
    Psrld = 206,
    Psrlq = 207,
    Psraw = 208,
    Psrad = 209,
    Emms = 210,
    Pavgb = 211,
    Pavgw = 212,
    Pminub = 213,
    Pmaxub = 214,
    Pminuw = 215,
    Pmaxuw = 216,
    Psadbw = 217,
    Pmovmskb = 218,
    Pextrw = 219,
    Pinsrw = 220,
    Pshufw = 221,
    Movntq = 222,
    Maskmovq = 223,

    Movaps = 224,
    Movups = 225,
    Movss = 226,
    Movhps = 227,
    Movlps = 228,
    Movhlps = 229,
    Movlhps = 230,
    Movmskps = 231,
    Addps = 232,
    Addss = 233,
    Subps = 234,
    Subss = 235,
    Mulps = 236,
    Mulss = 237,
    Divps = 238,
    Divss = 239,
    Sqrtps = 240,
    Sqrtss = 241,
    Rsqrtps = 242,
    Rsqrtss = 243,
    Rcpps = 244,
    Rcpss = 245,
    Maxps = 246,
    Maxss = 247,
    Minps = 248,
    Minss = 249,
    Andps = 250,
    Andnps = 251,
    Orps = 252,
    Xorps = 253,
    Cmpps = 254,
    Cmpss = 255,
    Comiss = 256,
    Ucomiss = 257,
    Shufps = 258,
    Unpckhps = 259,
    Unpcklps = 260,
    Cvtpi2ps = 261,
    Cvtps2pi = 262,
    Cvtsi2ss = 263,
    Cvtss2si = 264,
    Cvttps2pi = 265,
    Cvttss2si = 266,
    Ldmxcsr = 267,
    Stmxcsr = 268,
    Fxsave = 269,
    Fxrstor = 270,
    Prefetch = 271,
    Sfence = 272,
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
}
