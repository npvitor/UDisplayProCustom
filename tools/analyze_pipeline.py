"""Read-only, version-specific investigation of capture/encoder settings."""
from pathlib import Path
import sys, struct, re
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'obj/audit-deps'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
b=(root/'original/UDisplay_Original_v1.0.2.9.exe').read_bytes()
pe=pefile.PE(data=b); base=pe.OPTIONAL_HEADER.ImageBase
d=Cs(CS_ARCH_X86,CS_MODE_32)
out=root/'obj/pipeline'; out.mkdir(parents=True,exist_ok=True)
def string(va):
    try:
        off=pe.get_offset_from_rva(va-base)
        raw=b[off:off+250].split(b'\0')[0]
        if len(raw)>3 and all(32<=x<127 or x in [9,10,13] for x in raw): return repr(raw.decode())
    except (pefile.PEFormatError,OverflowError): pass
    return ''
for start,size in [(0xB0B00,0x650),(0x15EA00,0x400),(0x189000,0x800),(0x176800,0x280),(0x179000,0x900)]:
    # Find first conventional prologue or start explicitly at a known instruction.
    if start==0xB0B00:start=0xB0F24;size=0x220
    if start==0x189000:start=0x189312;size=0x500
    if start==0x176800:start=0x176A2A;size=0x30
    if start==0x179000:start=0x179124;size=0x700
    lines=[]
    for i in d.disasm(b[start:start+size],base+pe.get_rva_from_offset(start)):
        annotation=''
        for literal in re.findall(r'0x[0-9a-f]{6,8}',i.op_str):
            annotation+=' '+string(int(literal,16))
        lines.append(f'{pe.get_offset_from_rva(i.address-base):08X}: {i.bytes.hex():24} {i.mnemonic:8} {i.op_str}{annotation}')
    (out/f'{start:X}.txt').write_text('\n'.join(lines))
terms=re.compile(rb'[^\x00-\x1f\x7f-\xff]{5,}')
lines=[]
for m in terms.finditer(b):
    text=m.group().decode('ascii')
    if re.search(r'bit.?rate|framerate|frame.?rate|zerolatency|ultrafast|superfast|libx264|fps|quality|crf|preset|encoder|avcodec',text,re.I):
        off=m.start();va=base+pe.get_rva_from_offset(off); needle=struct.pack('<I',va)
        refs=[hex(x.start()) for x in re.finditer(re.escape(needle),b[:0x46D600])]
        lines.append(f'{off:X} VA {va:X}: {text} | refs={refs[:30]}')
(out/'strings.txt').write_text('\n'.join(lines))
print('\n'.join(lines[:180]))
for n in [10000000,8000000,16000000,24000000]:
    print(n,[hex(m.start()) for m in re.finditer(re.escape(struct.pack('<I',n)),b[:0x46D600])][:60])
