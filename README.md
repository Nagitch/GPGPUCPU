# GPGPUCPU

VM Filter Palette

A GPU-based **mini virtual machine** for per-pixel procedural effects, implemented as a Unity Compute Shader.  
Each pixel executes a small 16-bit program chosen from a **palette** of up to 256 programs.

This version includes a **jump suppression toggle**, allowing you to switch between chaotic (JMP enabled) and stable (JMP disabled) visual modes directly from the Unity Inspector.

---

## 🧩 1. Architecture Overview

The VM executes a small, fixed-length instruction stream stored in GPU buffers (`Bytecode`, `Programs`).  
Each pixel (thread) executes independently and deterministically.

### Components

| Component | Type | Description |
|------------|------|-------------|
| **Registers** | `r0..r3`, `R,G,B` | 8-bit unsigned working registers |
| **Flags** | `Z` (Zero flag) | Set when arithmetic result = 0 |
| **Program Counter** | `pc` | Points to current instruction in `Bytecode` |
| **Programs Table** | `uint2 offset,length` | Defines start and length of each slot |
| **Bytecode** | `uint16[]` | Packed 16-bit instructions (0xOPAA IMM) |
| **MaxSteps** | uniform | Step limit per pixel (prevents infinite loops) |
| **DisableJumps** | uniform | 0=allow jumps, 1=suppress jumps |

### Execution Flow

```
for each pixel:
    idx = IndexTex(x,y).r
    load program[offset..offset+length]
    init r0..r2 from SourceTex RGB, r3 from coordinates
    for up to MaxSteps:
        decode 16-bit instruction
        execute operation
        update pc
        stop if OUT reached or end of program
    write color (R,G,B)/255 to output
```

---

## ⚙️ 2. Instruction Set

Each instruction is 16 bits: `OOOO AAAA IIIIIIII`  
- `O`: 4-bit opcode (0–15)
- `A`: 4-bit register selector
- `I`: 8-bit immediate or operand selector

| Opcode | Mnemonic | Operands | Description |
|--------|-----------|-----------|-------------|
| 0x0 | **NOP** | — | No operation |
| 0x1 | **LDI A, imm** | 8-bit | Load immediate into register |
| 0x2 | **ADD A, imm** | 8-bit | Add immediate |
| 0x3 | **XOR A, imm** | 8-bit | Bitwise XOR with immediate |
| 0x4 | **ROTL A, imm** | 3-bit | Rotate bits left |
| 0x5 | **MIX ACC, A** | reg | Mix accumulator (R,G,B) using A |
| 0x6 | **MOV A, sel** | reg | Copy register/accumulator value |
| 0x7 | **ADDR A, sel** | reg | Add value from another register |
| 0x8 | **XORR A, sel** | reg | XOR with another register |
| 0x9 | **TSTZ A** | — | Set Zero flag if A == 0 |
| 0xA | **JZ off** | rel | Jump if Z = 1 |
| 0xB | **JNZ off** | rel | Jump if Z = 0 |
| 0xC | **JMP off** | rel | Unconditional relative jump |
| 0xD | **ACCW A, mask** | mask(1:R,2:G,4:B) | Write A into accumulator channels |
| 0xE | **HASH A** | — | Hash transform (nonlinear mix) |
| 0xF | **OUT** | — | Terminate program |

---

## ✨ 3. Features

- 16‑bit minimal ISA optimized for GPU compute  
- 256 slot palette, selected per pixel via IndexTex  
- Jump suppression toggle (`DisableJumps`) for safety or effect control  
- Configurable step limit (`MaxSteps`)  
- Host-side assembler to build custom `.asm` → bytecode  
- Deterministic and sandboxed execution (no memory access)

---

## 🎛️ 4. Unity Setup

1. Import files into Unity.  
2. Attach `FilterPaletteDemo.cs` to a GameObject.  
3. Assign:  
   - `SourceTex` → base image  
   - `IndexTex` → slot map (R=0..255)  
   - `BytecodeHex` → `out/bytecode.hex`  
   - `ProgramsJson` → `out/programs.json`  
4. Play Scene.  

Inspector controls:  
- **DisableJumps** → suppress or allow jumps  
- **MaxSteps** → limit instruction count  

---

## 🧩 5. Example Programs

| Slot | File | Effect |
|------|-----------|---------|
| 0 | `identity.asm` | Identity (no change) |
| 1 | `invert.asm` | Color inversion |
| 2 | `posterize.asm` | Posterized color |
| 3 | `tint_red.asm` | Warm tint |
| 4 | `hash_glow.asm` | Pseudo-random glow |

---

## 🧠 6. Artistic Usage

Because the VM treats all data as valid code, **you can feed arbitrary byte sequences** (noise, textures, random data) into `Bytecode` to produce unique glitch art and generative visuals.

---

## 🧾 License

MIT License © 2025 Nagitch
