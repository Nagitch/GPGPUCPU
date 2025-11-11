# see README for ISA and usage
#!/usr/bin/env python3
import re, sys, json, argparse
from pathlib import Path
from typing import List

ISA = {'NOP':0x0,'LDI':0x1,'ADD':0x2,'XOR':0x3,'ROTL':0x4,'MIX':0x5,'MOV':0x6,'ADDR':0x7,'XORR':0x8,'TSTZ':0x9,'JZ':0xA,'JNZ':0xB,'JMP':0xC,'ACCW':0xD,'HASH':0xE,'OUT':0xF}
REG_A = {'r0':0,'r1':1,'r2':2,'r3':3,'R':4,'G':5,'B':6}
SEL   = REG_A.copy()

int_re=re.compile(r'^[+-]?\d+$'); hex_re=re.compile(r'^0x[0-9a-fA-F]+$'); bin_re=re.compile(r'^0b[01]+$')
def parse_imm(tok:str)->int:
    tok=tok.strip()
    if hex_re.match(tok): return int(tok,16)
    if bin_re.match(tok): return int(tok,2)
    if int_re.match(tok): return int(tok,10)
    raise ValueError(f"Immediate '{tok}' is not a valid number")

def tokenize_line(line:str):
    for mark in (';','//'):
        cpos=line.find(mark)
        if cpos!=-1: line=line[:cpos]
    line=line.strip(); label=""
    if not line: return "",[], ""
    if ':' in line:
        parts=line.split(':',1)
        if parts[0].strip() and not parts[1].strip().startswith(':'):
            label=parts[0].strip(); line=parts[1].strip()
    if not line: return label,[], ""
    if ' ' in line: mnem,ops=line.split(None,1)
    else: mnem,ops=line,""
    mnem=mnem.strip().upper()
    ops_list=[o.strip() for o in ops.split(',')] if ops else []
    return label,ops_list,mnem

def assemble(lines:List[str])->List[int]:
    labels={}; pc=0; parsed=[]
    for raw in lines:
        label,ops,mnem=tokenize_line(raw)
        if label:
            if label in labels: raise ValueError(f"Duplicate label: {label}")
            labels[label]=pc
        if mnem:
            if mnem not in ISA: raise ValueError(f"Unknown mnemonic: {mnem}")
            parsed.append((pc,mnem,ops,raw)); pc+=1
    words=[]
    for (pc,mnem,ops,raw) in parsed:
        op=ISA[mnem]; A_code=0; imm=0
        def need(nmin,nmax=None):
            if nmax is None: nmax=nmin
            if not (nmin<=len(ops)<=nmax):
                raise ValueError(f"{mnem}: expected {nmin if nmin==nmax else f'{nmin}..{nmax}'} operands, got {len(ops)} in '{raw}'")
        if mnem in ('NOP','OUT'):
            need(0)
        elif mnem in ('LDI','ADD','XOR','ROTL','HASH','TSTZ'):
            need(2 if mnem not in ('HASH','TSTZ') else 1)
            reg=ops[0]
            if reg not in REG_A: raise ValueError(f"{mnem}: invalid A register '{reg}'")
            A_code=REG_A[reg]
            if mnem in ('LDI','ADD','XOR','ROTL'):
                imm=parse_imm(ops[1]) & 0xFF
        elif mnem in ('MOV','ADDR','XORR'):
            need(2)
            regA=ops[0]
            if regA not in REG_A: raise ValueError(f"{mnem}: invalid A register '{regA}'")
            A_code=REG_A[regA]
            src=ops[1]
            if src not in SEL: raise ValueError(f"{mnem}: invalid source selector '{src}'")
            imm=SEL[src] & 0xFF
        elif mnem=='MIX':
            need(2)
            if ops[0].upper()!='ACC': raise ValueError("MIX: first operand must be 'ACC'")
            reg=ops[1]
            if reg not in REG_A: raise ValueError(f"MIX: invalid source register '{reg}'")
            A_code=REG_A[reg]
        elif mnem=='ACCW':
            need(2)
            reg=ops[0]
            if reg not in REG_A: raise ValueError(f"ACCW: invalid source register '{reg}'")
            A_code=REG_A[reg]
            m=ops[1]
            if m.lower().startswith('mask='): val=parse_imm(m.split('=',1)[1])
            else: val=parse_imm(m)
            if not (0<=val<=7): raise ValueError("ACCW: mask must be 0..7")
            imm=val & 0xFF
        elif mnem in ('JZ','JNZ','JMP'):
            need(1)
            t=ops[0]
            if t in labels: ofs=labels[t]-(pc+1)
            else: ofs=parse_imm(t)
            if not (-128<=ofs<=127): raise ValueError(f"{mnem}: relative offset {ofs} out of int8 range")
            imm=ofs & 0xFF
        word=((op & 0xF)<<12) | ((A_code & 0xF)<<8) | (imm & 0xFF)
        words.append(word)
    return words

def build_from_manifest(manifest_path: Path, out_dir: Path):
    man = json.loads(manifest_path.read_text(encoding='utf-8'))
    entries = man["entries"] if "entries" in man else man
    all_words=[]; programs=[{"offset":0,"length":0} for _ in range(256)]
    for ent in entries:
        slot = int(ent["slot"])
        asm_path = manifest_path.parent / ent["file"]
        lines = asm_path.read_text(encoding='utf-8').splitlines()
        words = assemble(lines)
        programs[slot]["offset"] = len(all_words)
        programs[slot]["length"] = len(words)
        all_words.extend(words)
    out_dir.mkdir(parents=True, exist_ok=True)
    with (out_dir / "bytecode.hex").open("w") as f:
        for w in all_words: f.write(f"0x{w:04X}\n")
    (out_dir / "programs.json").write_text(json.dumps(programs, ensure_ascii=False, indent=2), encoding="utf-8")

def main():
    ap = argparse.ArgumentParser(description="Assemble VM programs from manifest.json")
    ap.add_argument("--manifest", "-m", required=True, help="Path to manifest.json")
    ap.add_argument("--out", "-o", default="out", help="Output directory (default: ./out)")
    args = ap.parse_args()
    build_from_manifest(Path(args.manifest), Path(args.out))

if __name__ == "__main__":
    main()

