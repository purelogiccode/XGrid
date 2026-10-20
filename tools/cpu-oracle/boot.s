/*
 * CPU oracle boot sector (16-bit real mode).
 *
 * Loads the flat test image (sectors 1..N) to 0x00008000 and jumps to it.
 * N is patched into the image by tools/cpu-oracle/oracle.py at offset 2.
 *
 * The image is a 1.44 MB floppy: the first 512 bytes are this sector, the
 * remainder is the payload.  Only the sectors listed in [image_sectors] are
 * read, so a small corpus boots quickly.
 */

        .section .text
        .code16
        .globl _start
_start:
        cli
        xorw    %ax, %ax
        movw    %ax, %ds
        movw    %ax, %es
        movw    %ax, %ss
        movw    $0x7C00, %sp

        /* Fast A20 gate (port 0x92 bit 1).  Do not touch the reset bit. */
        inb     $0x92, %al
        orb     $0x02, %al
        andb    $0xFE, %al
        outb    %al, $0x92

        /* Destination linear address; ES:BX is recomputed per sector so a
         * 512-byte read never crosses a 64 KiB boundary and BIOS BX
         * advancement cannot corrupt the walk. */
        movl    $0x00008000, %eax
        movl    %eax, dest
        movw    $0, lba

load_loop:
        movw    lba, %ax
        cmpw    image_sectors, %ax
        jae     loaded

        movl    dest, %eax
        movl    %eax, %ebx
        andl    $0x000F, %ebx
        shrl    $4, %eax
        movw    %ax, %es

        /* LBA -> CHS: track = lba / 18, sector = lba % 18 + 1,
         * head = track % 2, cylinder = track / 2. */
        movw    lba, %ax
        xorw    %dx, %dx
        movw    $18, %cx
        divw    %cx
        incw    %dx
        movb    %dl, %cl
        xorw    %dx, %dx
        movw    $2, %si
        divw    %si
        movb    %al, %ch
        movb    %dl, %dh
        xorw    %dx, %dx
        movb    $0x02, %ah
        movb    $0x01, %al
        movb    $0x00, %dl
        int     $0x13
        jc      boot_error

        addl    $512, dest
        incw    lba
        jmp     load_loop

loaded:
        ljmp    $0x0000, $0x8000

boot_error:
        cli
        hlt
        jmp     boot_error

        .balign 4
lba:            .word 0
image_sectors:  .word 8
dest:           .long 0x00008000

        .org    510
        .byte   0x55, 0xAA
