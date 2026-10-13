; pad_poll.asm — ROM-C: OHCI periodic interrupt-IN poll of the XID gamepad.
;
; Assembled with: C:\msys64\mingw64\bin\nasm.exe -f bin pad_poll.asm -o pad_poll.bin
; (absolute path; NASM is not in PATH. The .bin is checked in next to this
; source; tests embed the bytes and must NOT shell out to NASM.)
;
; Flat 32-bit, ORG 0x1000 (matches --base/--entry defaults). No BIOS, no
; kernel, no paging: the TestRomRunner enters flat protected mode for us.
;
; What it does (mirrors OhciUsbTests.HubTopology_EnumeratesDownstreamGamepad
; and PeriodicInterruptIn_DeliversNeutralReport, §69):
;   1. soft-resets USB0, sets operational + control-list enable, HCCA at
;      0x30000 (done head at HCCA+0x84),
;   2. enumerates the hub chain with one chained control ED list (five EDs at
;      0x31000, each with a setup TD + zero-length IN status TD). The whole
;      list is serviced in one frame boundary; CLF clears on the next:
;        a. SET_ADDRESS(1) to address 0  -> 4-port daughterboard hub,
;        b. SET_FEATURE(PORT_RESET) port 3 -> 3-port controller hub enabled,
;        c. SET_ADDRESS(2) to address 0  -> 3-port hub,
;        d. SET_FEATURE(PORT_RESET) port 1 -> XID gamepad enabled,
;        e. SET_ADDRESS(3) to address 0  -> XID gamepad,
;   3. clears WDH, crosses a frame so the done queue writes back, and asserts
;      HcDoneHead == 0x0 (the idle done queue) — the "idle" observation,
;   4. builds a periodic ED (address 3, endpoint 2 IN, 32-byte packets) and a
;      20-byte interrupt-IN TD at 0x32010 -> report buffer 0x33000, fills all
;      32 HCCA interrupt-table entries, clears WDH and rings the doorbell
;      (HcControl.PLE),
;   5. polls HcDoneHead / HCCA+0x84 across 1 ms frames until the TD retires,
;      then checks condition == NoError and the data-toggle advanced to 1,
;   6. ASSERT: the report is 20 bytes and the button word is 0x0002 (DOWN,
;      --pad-buttons down) -> prints the exact line
;      "TESTROM PASS pad-poll idle=0x0 pressed=0x2" and exits EAX=0; any
;      failure exits EAX=3.
;
; Exit: EAX=0 + HLT (pass) or EAX=3 + HLT (fail). Console on port 0xE9.
BITS 32
ORG 0x1000

%define OHCI      0xFED00000
%define HCCA      0x00030000
%define CED       0x00031000
%define SETUP_BUF 0x00031200
%define PED       0x00032000
%define PTD       0x00032010
%define PTAIL     0x00032030
%define REPORT    0x00033000

start:
    mov esi, msg_start
    call puts

    ; --- OHCI soft reset, then operational + control-list enable ---
    mov dword [OHCI + 0x08], 1      ; HcCommandStatus: HCR
    mov dword [OHCI + 0x18], HCCA   ; HcHCCA
    mov dword [OHCI + 0x04], 0x90   ; operational | CLE

    ; --- build the five control EDs (no-data transfers) ---
    ; slot 0: SET_ADDRESS(1) to address 0 (daughterboard hub)
    mov word [SETUP_BUF + 0x00], 0x0500    ; bmRequestType 0x00, bRequest 0x05
    mov word [SETUP_BUF + 0x02], 0x0001    ; wValue = address 1
    mov word [SETUP_BUF + 0x04], 0
    mov word [SETUP_BUF + 0x06], 0
    xor eax, eax
    xor edi, edi
    call ctl_add

    ; slot 1: SET_FEATURE(PORT_RESET) on daughterboard port 3
    mov word [SETUP_BUF + 0x08], 0x0323    ; bmRequestType 0x23, bRequest 0x03
    mov word [SETUP_BUF + 0x0A], 0x0004    ; wValue = PORT_RESET
    mov word [SETUP_BUF + 0x0C], 0x0003    ; wIndex = port 3 (1-based)
    mov word [SETUP_BUF + 0x0E], 0
    mov eax, 1
    mov edi, 0x40
    call ctl_add

    ; slot 2: SET_ADDRESS(2) to address 0 (3-port controller hub)
    mov word [SETUP_BUF + 0x10], 0x0500
    mov word [SETUP_BUF + 0x12], 0x0002
    mov word [SETUP_BUF + 0x14], 0
    mov word [SETUP_BUF + 0x16], 0
    xor eax, eax
    mov edi, 0x80
    call ctl_add

    ; slot 3: SET_FEATURE(PORT_RESET) on controller hub port 1
    mov word [SETUP_BUF + 0x18], 0x0323
    mov word [SETUP_BUF + 0x1A], 0x0004
    mov word [SETUP_BUF + 0x1C], 0x0001
    mov word [SETUP_BUF + 0x1E], 0
    mov eax, 2
    mov edi, 0xC0
    call ctl_add

    ; slot 4: SET_ADDRESS(3) to address 0 (XID gamepad)
    mov word [SETUP_BUF + 0x20], 0x0500
    mov word [SETUP_BUF + 0x22], 0x0003
    mov word [SETUP_BUF + 0x24], 0
    mov word [SETUP_BUF + 0x26], 0
    xor eax, eax
    mov edi, 0x100
    call ctl_add

    ; --- link the EDs, ring the doorbell once, wait for CLF to clear ---
    mov dword [CED + 0x000 + 12], CED + 0x040
    mov dword [CED + 0x040 + 12], CED + 0x080
    mov dword [CED + 0x080 + 12], CED + 0x0C0
    mov dword [CED + 0x0C0 + 12], CED + 0x100
    mov dword [CED + 0x100 + 12], 0
    mov dword [OHCI + 0x20], CED
    mov dword [OHCI + 0x08], 0x02

    mov ecx, 0x100000
.wait:
    mov edx, [OHCI + 0x08]
    test edx, 0x02
    jz .enumerated
    dec ecx
    jnz .wait
    mov esi, msg_timeout
    jmp .fail

.enumerated:
    ; every setup TD must have retired NoError
    xor edi, edi
    mov ebx, 5
.check:
    mov edx, [CED + edi + 0x10]
    shr edx, 28
    test edx, edx
    jnz .stalled
    add edi, 0x40
    dec ebx
    jnz .check

    ; --- flush the done queue, then read the idle DoneHead ---
    mov dword [OHCI + 0x0C], 0x02   ; clear WDH so the writeback can run
    mov ecx, 0x80000
.spin:
    dec ecx
    jnz .spin

    mov eax, [OHCI + 0x30]          ; HcDoneHead
    test eax, eax
    jnz .fail

    ; --- periodic ED/TD and the HCCA interrupt table ---
    mov edi, HCCA
    mov ecx, 32
.fill:
    mov dword [edi], PED
    add edi, 4
    dec ecx
    jnz .fill

    mov dword [PED + 0], 3 | (2 << 7) | (2 << 11) | (32 << 16)
    mov dword [PED + 4], PTAIL
    mov dword [PED + 8], PTD
    mov dword [PED + 12], 0

    mov dword [PTD + 0], 0
    mov dword [PTD + 4], REPORT
    mov dword [PTD + 8], PTAIL
    mov dword [PTD + 12], REPORT + 19

    ; clear WDH again so the periodic writeback is armed
    mov dword [OHCI + 0x0C], 0x02

    ; --- ring the doorbell: enable the periodic list ---
    mov dword [OHCI + 0x04], 0x94   ; operational | CLE | PLE

    ; --- poll for the retired TD across 1 ms frames ---
    mov ecx, 0x400000
.poll:
    mov eax, [OHCI + 0x30]
    and eax, 0xFFFFFFF0
    cmp eax, PTD
    je .retired
    mov eax, [HCCA + 0x84]
    and eax, 0xFFFFFFF0
    cmp eax, PTD
    je .retired
    dec ecx
    jnz .poll
    mov esi, msg_timeout
    jmp .fail

.retired:
    ; condition code must be NoError and the data toggle must have advanced
    mov eax, [PTD]
    mov edx, eax
    shr eax, 28
    test eax, eax
    jnz .stalled
    and edx, 0x03000000
    cmp edx, 0x03000000
    jne .fail

    ; report must be well-formed and carry the DOWN button word
    cmp byte [REPORT], 0
    jne .report
    cmp byte [REPORT + 1], 20
    jne .report
    mov ax, [REPORT + 2]
    cmp ax, 0x0002
    jne .report

    mov esi, msg_pass
    call puts
    xor eax, eax
    hlt

.stalled:
    mov esi, msg_stalled
    jmp .fail

.report:
    mov esi, msg_report
    jmp .fail

.fail:
    call puts
    mov eax, 3
    hlt

; ctl_add: fills one no-data control ED slot.
; In:  EAX = function address (7-bit), EDI = slot offset (0x00,0x40,...,0x100);
;      the 8-byte setup packet lives at SETUP_BUF + (EDI >> 3).
; Out: the ED + setup TD + zero-length IN status TD + dummy tail are written;
;      the ED's next pointer is left for the link step.
ctl_add:
    mov ebx, eax
    or ebx, 64 << 16
    mov dword [CED + edi + 0], ebx
    mov ecx, CED
    add ecx, edi
    lea edx, [ecx + 0x30]
    mov [ecx + 4], edx              ; tail = dummy
    lea edx, [ecx + 0x10]
    mov [ecx + 8], edx              ; head = setup TD
    mov dword [ecx + 12], 0         ; next ED (linked later)

    mov dword [ecx + 0x10], 0       ; setup TD: direction/endpoint from the ED
    mov edx, edi
    shr edx, 3
    add edx, SETUP_BUF
    mov [ecx + 0x14], edx           ; cbp
    lea edx, [ecx + 0x20]
    mov [ecx + 0x18], edx           ; next = status TD
    mov edx, edi
    shr edx, 3
    add edx, SETUP_BUF + 7
    mov [ecx + 0x1C], edx           ; be

    mov dword [ecx + 0x20], (2 << 19) ; status TD: zero-length IN
    mov dword [ecx + 0x24], 0
    lea edx, [ecx + 0x30]
    mov [ecx + 0x28], edx           ; next = tail
    mov dword [ecx + 0x2C], 0
    ret

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

msg_start   db 'pad-poll: enumerate+periodic', 0x0A, 0
msg_pass    db 'TESTROM PASS pad-poll idle=0x0 pressed=0x2', 0x0A, 0
msg_timeout db 'TESTROM FAIL pad-poll td-timeout', 0x0A, 0
msg_stalled db 'TESTROM FAIL pad-poll td-stalled', 0x0A, 0
msg_report  db 'TESTROM FAIL pad-poll bad-report', 0x0A, 0
