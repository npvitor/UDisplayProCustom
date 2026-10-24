"""Read-only PE/x86 and DXBC audit. Requires pefile and capstone."""
import ctypes as C
import pathlib
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / 'obj/audit-deps'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

root = pathlib.Path(__file__).resolve().parents[1]
data = (root / 'original/UDisplay_Original_v1.0.2.9.exe').read_bytes()
pe = pefile.PE(data=data)
dis = Cs(CS_ARCH_X86, CS_MODE_32)
out = root / 'obj/audit'
out.mkdir(parents=True, exist_ok=True)
lines = []
for section in pe.sections:
    lines.append(f'{section.Name!r}: raw={section.PointerToRawData:X} RVA={section.VirtualAddress:X} size={section.SizeOfRawData:X}')
for start, size in [(0xB0C4C,60),(0xB0F24,40),(0x15EC78,140),(0x15F68D,75),(0x15F712,80),(0x15F7A4,50),(0x176A2A,54),(0x189312,84),(0x179124,30),(0x17919F,30),(0xA3BB5,40),(0x7F3D0,30),(0x9A5A0,30),(0xA4C10,40)]:
    lines.append(f'\nFILE OFFSET {start:X}')
    for ins in dis.disasm(data[start:start+size], pe.OPTIONAL_HEADER.ImageBase+pe.get_rva_from_offset(start)):
        lines.append(f'{pe.get_offset_from_rva(ins.address-pe.OPTIONAL_HEADER.ImageBase):08X}: {ins.bytes.hex():24} {ins.mnemonic} {ins.op_str}')
(out / 'x86-original.txt').write_text('\n'.join(lines))

# ID3DBlob inherits IUnknown: GetBufferPointer/GetBufferSize are slots 3/4.
def blob_text(ptr):
    vt = C.cast(ptr, C.POINTER(C.POINTER(C.c_void_p))).contents
    addr = C.WINFUNCTYPE(C.c_void_p, C.c_void_p)(vt[3])(ptr)
    size = C.WINFUNCTYPE(C.c_size_t, C.c_void_p)(vt[4])(ptr)
    text = C.string_at(addr,size).decode('utf-8',errors='replace')
    C.WINFUNCTYPE(C.c_ulong,C.c_void_p)(vt[2])(ptr)
    return text
fn = C.WinDLL('d3dcompiler_47.dll').D3DDisassemble
fn.argtypes = [C.c_void_p,C.c_size_t,C.c_uint,C.c_char_p,C.POINTER(C.c_void_p)]
fn.restype = C.c_long
for name, b in [('original',data),('dist',(root/'dist/UDisplay.exe').read_bytes())]+([
    ('installed-before-recovery',(out/'installed-before-recovery.exe').read_bytes())] if (out/'installed-before-recovery.exe').exists() else []):
    this_pe = pefile.PE(data=b)
    for operand in [0x179125,0x1791A0]:
        va=struct.unpack_from('<I',b,operand+5)[0]
        off=this_pe.get_offset_from_rva(va-this_pe.OPTIONAL_HEADER.ImageBase)
        size = struct.unpack_from('<I',b,off+24)[0]
        ptr = C.c_void_p()
        hr = fn(b[off:off+size],size,0,None,C.byref(ptr))
        result = blob_text(ptr) if hr >= 0 else f'Disassembly failed: {hr:X}; declared length={size}'
        (out/f'{name}-{off:X}.txt').write_text(result)
        print(name,hex(off),size,'OK' if hr>=0 else 'INVALID')
    if name=='dist':
        relocations={e.rva for block in this_pe.DIRECTORY_ENTRY_BASERELOC for e in block.entries if e.type==3}
        for ptr in [0x17912A,0x1791A5]:
            assert this_pe.get_rva_from_offset(ptr) in relocations, 'Missing ASLR relocation'
        print('Shader pointers have HIGHLOW relocations; ASLR preserved')
print('\n'.join(lines))
