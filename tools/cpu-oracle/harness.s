/*
 * CPU oracle harness (32-bit protected mode, flat segments).
 *
 * Loaded at 0x00008000 by boot.s.  Layout:
 *
 *   0x00008000  this code
 *   0x00009000  576-byte record page (GPRs, EFLAGS, EIP, vector, FXSAVE)
 *   0x00009800  IDT (256 entries)
 *   0x0000A000  GDT
 *   0x0000A800  64-byte descriptor scratch
 *   0x0001C000  harness stack top
 *   0x0000B000  case descriptor table (64 bytes per case)
 *   0x0001B000  case count (dword)
 *   0x00040000  case code blob (128 bytes per case, generated)
 *   0x00100000  result records (576 bytes per case)
 *   0x00200000  case pages (8 KiB each: data at +0, stack at +0x1800)
 *
 * A case descriptor is:
 *   0 entry, 4 eflags, 8 eax, 12 ecx, 16 edx, 20 ebx, 24 esp,
 *   28 ebp, 32 esi, 36 edi, 40..63 reserved
 *
 * Each case ends with `fxsave [0x9030]` + `int $0x30`.  The handler for
 * every installed vector builds the same 576-byte record, so the oracle
 * also captures faults (vector, EIP) without special-casing them.
 */

        .set DUMP,      0x00009000
        .set IDT,       0x00009800
        .set GDT,       0x0000A000
        .set SCR,       0x0000A800
        .set STACK,     0x0001C000
        .set DESC,      0x0000B000
        .set CASECOUNT, 0x0001B000
        .set RESULT,    0x00100000
        .set PAGEBASE,  0x00200000
        .set PAGESTRIDE, 0x00002000
        .set RECSTRIDE, 832

        .section .text
        .code16
        .globl _start32
_start32:
        cli
        lgdt    gdt_ptr
        movl    %cr0, %eax
        orl     $1, %eax
        movl    %eax, %cr0
        ljmp    $0x08, $pm_entry

        .code32
pm_entry:
        movw    $0x10, %ax
        movw    %ax, %ds
        movw    %ax, %es
        movw    %ax, %fs
        movw    %ax, %gs
        movw    %ax, %ss
        movl    $STACK, %esp

        /* FXSAVE/FXRSTOR and SSE need CR4.OSFXSR|OSXMMEXCPT. */
        movl    %cr4, %eax
        orl     $0x600, %eax
        movl    %eax, %cr4

        /* Zero the IDT, then point every entry at default_stub. */
        xorl    %eax, %eax
        movl    $IDT, %edi
        movl    $(256 * 2), %ecx
        rep stosl

        movl    $default_stub, %ebx
        movl    $IDT, %edi
        movl    $256, %ecx
1:
        movw    %bx, (%edi)
        movw    $0x08, 2(%edi)
        movw    $0x8E00, 4(%edi)
        movl    %ebx, %eax
        shrl    $16, %eax
        movw    %ax, 6(%edi)
        addl    $8, %edi
        loop    1b

        /* Install the handlers we expect to see. */
        movl    $vector_table, %esi
2:
        movl    (%esi), %eax
        cmpl    $-1, %eax
        je      3f
        movl    4(%esi), %ebx
        leal    IDT(,%eax,8), %edi
        movw    %bx, (%edi)
        movl    %ebx, %eax
        shrl    $16, %eax
        movw    %ax, 6(%edi)
        movw    $0x08, 2(%edi)
        movw    $0x8E00, 4(%edi)
        addl    $8, %esi
        jmp     2b
3:
        lidt    idt_ptr

case_loop:
        movl    case_index, %esi
        cmpl    CASECOUNT, %esi
        jae     all_done
        shll    $6, %esi
        addl    $DESC, %esi
        movl    $SCR, %edi
        movl    $12, %ecx
        rep movsl

        fninit
        emms
        ldmxcsr mxcsr_default

        movl    SCR+24, %esp
        movl    SCR+8, %eax
        movl    SCR+12, %ecx
        movl    SCR+16, %edx
        movl    SCR+20, %ebx
        movl    SCR+28, %ebp
        movl    SCR+32, %esi
        movl    SCR+36, %edi
        pushl   SCR+4
        popfl
        jmp     *SCR

default_stub:
        pushl   $0xFF
        jmp     common

stub_de:
        pushl   $0x00
        jmp     common
stub_of:
        pushl   $0x04
        jmp     common
stub_br:
        pushl   $0x05
        jmp     common
stub_ud:
        pushl   $0x06
        jmp     common
stub_nm:
        pushl   $0x07
        jmp     common
stub_mf:
        pushl   $0x10
        jmp     common
stub_xm:
        pushl   $0x13
        jmp     common
stub_stop:
        pushl   $0x30
        jmp     common
stub_gp:
        pushl   $0x10D
        jmp     common
stub_pf:
        pushl   $0x10E
        jmp     common

common:
        pushal
        movl    32(%esp), %edx          /* vector tag */
        testl   $0x100, %edx
        jnz     1f
        movl    36(%esp), %eax          /* EIP */
        movl    44(%esp), %ebx          /* EFLAGS */
        movl    12(%esp), %ecx          /* ESP before the stub pushes */
        addl    $16, %ecx
        jmp     2f
1:
        movl    40(%esp), %eax          /* EIP (error-code frame) */
        movl    48(%esp), %ebx
        movl    12(%esp), %ecx
        addl    $20, %ecx
2:
        andl    $0xFF, %edx
        movl    28(%esp), %edi
        movl    %edi, DUMP+0
        movl    24(%esp), %edi
        movl    %edi, DUMP+4
        movl    20(%esp), %edi
        movl    %edi, DUMP+8
        movl    16(%esp), %edi
        movl    %edi, DUMP+12
        movl    %ecx, DUMP+16
        movl    8(%esp), %edi
        movl    %edi, DUMP+20
        movl    4(%esp), %edi
        movl    %edi, DUMP+24
        movl    0(%esp), %edi
        movl    %edi, DUMP+28
        movl    %ebx, DUMP+32
        movl    %eax, DUMP+36
        movl    %edx, DUMP+40
        movl    $0, DUMP+44
        fxsave  DUMP+48

        movl    $DUMP, %esi
        movl    case_index, %edi
        imull   $RECSTRIDE, %edi, %edi
        addl    $RESULT, %edi
        movl    $144, %ecx
        rep movsl

        /* Append the first 256 bytes of the case's data page. */
        movl    case_index, %eax
        imull   $PAGESTRIDE, %eax, %eax
        addl    $PAGEBASE, %eax
        movl    %eax, %esi
        movl    case_index, %edi
        imull   $RECSTRIDE, %edi, %edi
        addl    $RESULT, %edi
        addl    $576, %edi
        movl    $64, %ecx
        rep movsl

        incl    case_index
        jmp     case_loop

all_done:
        movl    $STACK, %esp
        movl    $RESULT, %esi
        movl    CASECOUNT, %ecx
        imull   $RECSTRIDE, %ecx, %ecx
        testl   %ecx, %ecx
        jz      2f
1:
        movw    $0x3FD, %dx
        inb     %dx, %al
        testb   $0x20, %al
        jz      1b
        movw    $0x3F8, %dx
        movb    (%esi), %al
        outb    %al, %dx
        incl    %esi
        decl    %ecx
        jnz     1b
2:
        /* isa-debug-exit: status = (value << 1) | 1 */
        movw    $0x501, %dx
        xorb    %al, %al
        outb    %al, %dx
3:
        hlt
        jmp     3b

        .balign 4
gdt:
        .quad   0x0000000000000000
        .quad   0x00CF9A000000FFFF        /* 0x08: flat 32-bit code */
        .quad   0x00CF92000000FFFF        /* 0x10: flat 32-bit data */
gdt_end:
gdt_ptr:
        .word   gdt_end - gdt - 1
        .long   gdt
idt_ptr:
        .word   0x07FF
        .long   IDT
mxcsr_default:
        .long   0x00001F80
case_index:
        .long   0

vector_table:
        .long   0x00, stub_de
        .long   0x04, stub_of
        .long   0x05, stub_br
        .long   0x06, stub_ud
        .long   0x07, stub_nm
        .long   0x0D, stub_gp
        .long   0x0E, stub_pf
        .long   0x10, stub_mf
        .long   0x13, stub_xm
        .long   0x30, stub_stop
        .long   -1, 0

        .balign 4
        .org    CASECOUNT - 0x8000
case_count:
        .long   0
