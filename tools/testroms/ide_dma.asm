; ide_dma.asm — ROM-B: IDE bus-master READ DMA from a synthetic pattern HDD.
;
; Assembled with: C:\msys64\mingw64\bin\nasm.exe -f bin ide_dma.asm -o ide_dma.bin
; (absolute path; NASM is not in PATH. The .bin is checked in next to this
; source; tests embed the bytes and must NOT shell out to NASM.)
;
; Flat 32-bit, ORG 0x1000 (matches --base/--entry defaults). No BIOS, no
; kernel, no paging: the TestRomRunner enters flat protected mode for us.
;
; What it does (mirrors IdeDmaTests / the kernel's bus-master path, §31):
;   1. PCI config: assigns IDE BAR4 = 0xC000 (dev 9 fn 0), enables I/O +
;      bus-master decoding in the command register,
;   2. builds a one-entry PRDT at 0x30000 -> 4096 bytes at 0x40000 (end bit),
;   3. programs the primary-master taskfile: LBA 0x100, 8 sectors, READ DMA
;      (0xC8) — the drive stages the transfer,
;   4. starts the bus master (command bit 0) and polls BM status for the
;      INTERRUPT bit (bounded), checking ERROR is clear,
;   5. recomputes the golden pattern for LBA 0x100..0x107 and memcmps the
;      4096-byte buffer, counting mismatches,
;   6. ASSERT: mismatch == 0 -> prints the exact line
;      "TESTROM PASS ide-dma lba=0x100 sectors=8 mismatch=0" and exits EAX=0;
;      any failure exits EAX=2.
;
; Golden pattern (mirrored by the host generator): the byte at linear disk
; offset n = LBA*512 + i is ((n * 0x21) ^ 0x5A) & 0xFF. The whole 8 MB image
; is filled with it, so a wrong LBA or a shifted byte fails the compare.
;
; Exit: EAX=0 + HLT (pass) or EAX=2 + HLT (fail). Console on port 0xE9.
BITS 32
ORG 0x1000

%define IDE_CMD  0x1F0
%define BM_BASE  0xC000
%define PRDT     0x00030000
%define DMA_BUF  0x00040000
%define SECTORS  8
%define LBA      0x100

start:
    mov esi, msg_start
    call puts

    ; --- PCI config: BAR4 = 0xC000, then IO space + bus master enable ---
    mov eax, 0x80004820             ; bus 0, dev 9, fn 0, register 0x20 (BAR4)
    mov dx, 0xCF8
    out dx, eax
    mov eax, BM_BASE
    mov dx, 0xCFC
    out dx, eax

    mov eax, 0x80004804             ; command register
    mov dx, 0xCF8
    out dx, eax
    mov eax, 0x00000007             ; I/O space + bus master
    mov dx, 0xCFC
    out dx, eax

    ; --- PRDT: one entry covering the whole 4096-byte buffer, end bit set ---
    mov dword [PRDT], DMA_BUF
    mov dword [PRDT + 4], 0x80001000

    ; --- bus master: stop, clear status, program PRDT address ---
    mov dx, BM_BASE
    xor al, al
    out dx, al                      ; command = 0
    mov dx, BM_BASE + 2
    mov al, 0x06
    out dx, al                      ; clear ERROR + INTERRUPT
    mov dx, BM_BASE + 4
    mov eax, PRDT
    out dx, eax                     ; PRDT base (dword)

    ; --- taskfile: master + LBA mode, LBA 0x100, 8 sectors, READ DMA ---
    mov dx, IDE_CMD + 6
    mov al, 0xE0
    out dx, al
    mov dx, IDE_CMD + 2
    mov al, SECTORS
    out dx, al
    mov dx, IDE_CMD + 3
    xor al, al
    out dx, al
    mov dx, IDE_CMD + 4
    mov al, 0x01
    out dx, al
    mov dx, IDE_CMD + 5
    xor al, al
    out dx, al
    mov dx, IDE_CMD + 7
    mov al, 0xC8
    out dx, al

    ; --- start the bus master; the engine runs the transfer synchronously ---
    mov dx, BM_BASE
    mov al, 0x01
    out dx, al

    ; --- poll BM status for INTERRUPT, bounded ---
    mov ecx, 0x100000
.poll:
    mov dx, BM_BASE + 2
    in al, dx
    test al, 0x04
    jnz .done
    dec ecx
    jnz .poll
    mov esi, msg_timeout
    jmp .fail

.done:
    test al, 0x02
    jnz .error

    ; --- memcmp DMA_BUF against the golden pattern ---
    xor edi, edi                    ; mismatch count
    xor ecx, ecx                    ; linear index within the 4096 bytes
    xor ebx, ebx                    ; sector j
.sector:
    xor edx, edx                    ; byte i within the sector
.byte:
    mov eax, ebx
    add eax, LBA
    shl eax, 9
    add eax, edx
    imul eax, eax, 0x21
    xor eax, 0x5A
    and eax, 0xFF
    cmp al, [DMA_BUF + ecx]
    je .match
    inc edi
.match:
    inc ecx
    inc edx
    cmp edx, 512
    jl .byte
    inc ebx
    cmp ebx, SECTORS
    jl .sector

    test edi, edi
    jnz .mismatch

    mov esi, msg_pass
    call puts
    xor eax, eax
    hlt

.mismatch:
    mov esi, msg_mismatch
    jmp .fail

.error:
    mov esi, msg_error
    jmp .fail

.fail:
    call puts
    mov eax, 2
    hlt

; puts: prints NUL-terminated string at ESI to port 0xE9 (0x0A ends a line).
puts:
    push eax
    push edx
    push esi
.putc:
    lodsb
    test al, al
    jz .done
    mov dx, 0xE9
    out dx, al
    jmp .putc
.done:
    pop esi
    pop edx
    pop eax
    ret

msg_start    db 'ide-dma: pci+prdt+taskfile', 0x0A, 0
msg_pass     db 'TESTROM PASS ide-dma lba=0x100 sectors=8 mismatch=0', 0x0A, 0
msg_timeout  db 'TESTROM FAIL ide-dma bm-status-timeout', 0x0A, 0
msg_error    db 'TESTROM FAIL ide-dma bm-error-bit', 0x0A, 0
msg_mismatch db 'TESTROM FAIL ide-dma data-mismatch', 0x0A, 0
