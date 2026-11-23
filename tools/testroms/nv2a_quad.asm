; nv2a_quad.asm — ROM-A: NV2A textured triangle through the PFIFO pusher.
;
; Assembled with: C:\msys64\mingw64\bin\nasm.exe -f bin nv2a_quad.asm -o nv2a_quad.bin
; (absolute path; NASM is not in PATH. The .bin is checked in next to this
; source; tests embed the bytes and must NOT shell out to NASM.)
;
; Flat 32-bit, ORG 0x1000 (matches --base/--entry defaults). No BIOS, no
; kernel, no paging: the TestRomRunner enters flat protected mode for us.
;
; What it does (mirrors PgraphTransformTests.PushTexturedLegacyDraw, linear
; LU_IMAGE_A8R8G8B8 path, scaled to 64x64):
;   1. fills a 64x64 8x8-cell checker (transparent-black / opaque-white) at 0x10000,
;   2. writes 3 screen-space verts at 0x8000 + texel-unit texcoords at 0x8100,
;   3. builds a 39-method pushbuffer at 0x9000 (surface, vertex arrays, a
;      3-instruction vertex program with c0=(1,1,1,1), linear-texture state,
;      begin/element16/element32/end, FLIP_INCREMENT_WRITE),
;   4. performs the PfifoPusherTests.CreateChannel setup + explicit GET, then
;      writes PUT (the pusher runs synchronously),
;   5. ASSERT: reads two interior surface pixels back and checks the exact
;      checker values, proving draw + texture sample + present all happened.
;
; Exit: EAX=0 + HLT (pass) or EAX=1 + HLT (fail). Console on port 0xE9.
BITS 32
ORG 0x1000

%define NV2A      0xFD000000
%define VERTS     0x00008000
%define TEXCOORDS 0x00008100
%define PUSHBUF   0x00009000
%define TEXELS    0x00010000
%define SURFACE   0x00020000

; Header word for a 1-word method call, as in the pusher tests.
%macro METHOD 2
    mov dword [edi], (1 << 18) | (%1)
    mov dword [edi + 4], (%2)
    add edi, 8
%endmacro

start:
    mov esi, msg_start
    call puts

    ; --- 64x64 checker at TEXELS: cell (x>>3, y>>3), white iff (cx+cy)&1 ---
    xor ecx, ecx                    ; y = 0
.fill_y:
    xor ebx, ebx                    ; x = 0
.fill_x:
    mov eax, ebx
    shr eax, 3
    mov edx, ecx
    shr edx, 3
    add eax, edx
    and eax, 1
    mov edx, 0x00000000             ; black cell: exact zero
    test eax, eax
    jz .store
    mov edx, 0xFFFFFFFF             ; white cell
.store:
    mov eax, ecx
    shl eax, 8                      ; y * 256 (pitch)
    lea eax, [eax + ebx * 4 + TEXELS]
    mov [eax], edx
    inc ebx
    cmp ebx, 64
    jl .fill_x
    inc ecx
    cmp ecx, 64
    jl .fill_y

    ; --- verts: (0,0), (64,0), (0,64) screen space, w = 1 ---
    mov dword [VERTS + 0x00], 0x00000000
    mov dword [VERTS + 0x04], 0x00000000
    mov dword [VERTS + 0x08], 0x00000000
    mov dword [VERTS + 0x0C], 0x3F800000
    mov dword [VERTS + 0x10], 0x42800000 ; 64.0f
    mov dword [VERTS + 0x14], 0x00000000
    mov dword [VERTS + 0x18], 0x00000000
    mov dword [VERTS + 0x1C], 0x3F800000
    mov dword [VERTS + 0x20], 0x00000000
    mov dword [VERTS + 0x24], 0x42800000 ; 64.0f
    mov dword [VERTS + 0x28], 0x00000000
    mov dword [VERTS + 0x2C], 0x3F800000

    ; --- texcoords in texel units (linear formats take raw texel coords) ---
    mov dword [TEXCOORDS + 0x00], 0x00000000
    mov dword [TEXCOORDS + 0x04], 0x00000000
    mov dword [TEXCOORDS + 0x08], 0x42800000
    mov dword [TEXCOORDS + 0x0C], 0x00000000
    mov dword [TEXCOORDS + 0x10], 0x00000000
    mov dword [TEXCOORDS + 0x14], 0x42800000

    ; --- pushbuffer: surface / arrays / program / constants / texture ---
    mov edi, PUSHBUF
    METHOD 0x200, 0x00400000        ; clipH: 64x64
    METHOD 0x204, 0x00400000        ; clipV: 64x64
    METHOD 0x208, 0x00000104        ; format: A8R8G8B8
    METHOD 0x20C, 0x00000100        ; pitch: 256
    METHOD 0x210, 0x00020000        ; color offset: SURFACE
    METHOD 0x214, 0x00030000        ; zeta offset (scratch)
    METHOD 0x1720, 0x00008000      ; array 0 offset: positions
    METHOD 0x1760, 0x00001042      ; array 0 format: F32x4
    METHOD 0x1724, 0x00008100      ; array 1 offset: texcoords
    METHOD 0x1764, 0x00000822      ; array 1 format: F32x2
    METHOD 0x1E9C, 0x00000000      ; program load ptr = 0
    METHOD 0x0B00, 0x00000000      ; oPos = V0*C0+C0 (identity for c0=1)
    METHOD 0x0B04, 0x0020001B
    METHOD 0x0B08, 0x0836006C
    METHOD 0x0B0C, 0x0000F800
    METHOD 0x0B10, 0x00000000      ; oD0 = white from c0
    METHOD 0x0B14, 0x0020001B
    METHOD 0x0B18, 0x0C36006C
    METHOD 0x0B1C, 0x0000F818
    METHOD 0x0B20, 0x00000000      ; oT0 = V1 (x, y)
    METHOD 0x0B24, 0x0020021B
    METHOD 0x0B28, 0x0836006C
    METHOD 0x0B2C, 0x0000C849
    METHOD 0x1EA4, 0x00000000      ; constant load ptr = 0
    METHOD 0x0B80, 0x3F800000      ; c0 = (1, 1, 1, 1)
    METHOD 0x0B84, 0x3F800000
    METHOD 0x0B88, 0x3F800000
    METHOD 0x0B8C, 0x3F800000
    METHOD 0x1B00, 0x00010000      ; texture offset: TEXELS
    METHOD 0x1B04, 0x00001200      ; format: linear A8R8G8B8
    METHOD 0x1B08, 0x00000002      ; address mode
    METHOD 0x1B0C, 0x40000000      ; control0: enable
    METHOD 0x1B10, 0x01000000      ; control1: pitch 256
    METHOD 0x1B1C, 0x00400040      ; image rect: 64x64
    METHOD 0x17FC, 0x00000005      ; begin triangles
    METHOD 0x1800, 0x00010000      ; elements 0..2
    METHOD 0x1808, 0x00000002
    METHOD 0x17FC, 0x00000000      ; end
    METHOD 0x012C, 0x00000000      ; FLIP_INCREMENT_WRITE: present
    mov dword [edi], 0x00001001    ; terminator
    add edi, 4

    ; --- PFIFO channel setup (same writes as the pusher tests) ---
    mov dword [NV2A + 0x2504], 1
    mov dword [NV2A + 0x3200], 1
    mov dword [NV2A + 0x3204], 0x100
    mov dword [NV2A + 0x3220], 1
    mov dword [NV2A + 0x322C], 0x111C
    mov dword [NV2A + 0x3250], 1
    mov dword [NV2A + 0x400720], 1
    mov dword [NV2A + 0x7111C0], 2
    mov dword [NV2A + 0x7111C4], 0xFFFF
    mov dword [NV2A + 0x7111C8], 0

    ; --- kick: explicit GET, then PUT runs the pusher synchronously ---
    mov dword [NV2A + 0x800044], PUSHBUF
    mov [NV2A + 0x800040], edi

    mov esi, msg_drawn
    call puts

    ; --- ASSERT: interior pixels must hold exact checker texels ---
    ; (16,8)  -> texel cell (2,1) -> white; (16,16) -> cell (2,2) -> black.
    mov eax, [SURFACE + 8 * 256 + 16 * 4]
    cmp eax, 0xFFFFFFFF
    jne .fail
    mov eax, [SURFACE + 16 * 256 + 16 * 4]
    test eax, eax
    jnz .fail

    mov esi, msg_pass
    call puts
    xor eax, eax
    hlt

.fail:
    mov esi, msg_fail
    call puts
    mov eax, 1
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

msg_start db 'nv2a-quad: fill+push', 0x0A, 0
msg_drawn db 'nv2a-quad: pushed, checking', 0x0A, 0
msg_pass  db 'nv2a-quad: surface matches', 0x0A, 0
msg_fail  db 'nv2a-quad: surface mismatch', 0x0A, 0
