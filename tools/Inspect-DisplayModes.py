"""Read current display modes via user32. Never changes display settings."""
import ctypes as c
import struct
import json

class Display(c.Structure):
    _fields_=[('cb',c.c_uint),('name',c.c_wchar*32),('description',c.c_wchar*128),('flags',c.c_uint),('id',c.c_wchar*128),('key',c.c_wchar*128)]
u=c.WinDLL('user32.dll')
u.EnumDisplayDevicesW.argtypes=[c.c_wchar_p,c.c_uint,c.POINTER(Display),c.c_uint]
u.EnumDisplaySettingsW.argtypes=[c.c_wchar_p,c.c_uint,c.c_void_p]
result=[]
for index in range(16):
    device=Display();device.cb=c.sizeof(device)
    if not u.EnumDisplayDevicesW(None,index,c.byref(device),0):break
    if not device.flags&1:continue
    mode=c.create_string_buffer(220);struct.pack_into('<H',mode,68,220)
    if not u.EnumDisplaySettingsW(device.name,0xffffffff,mode):continue
    monitor=Display();monitor.cb=c.sizeof(monitor)
    u.EnumDisplayDevicesW(device.name,0,c.byref(monitor),0)
    result.append(dict(display=device.name,adapter=device.description,monitor=monitor.id,
        width=struct.unpack_from('<I',mode,172)[0],height=struct.unpack_from('<I',mode,176)[0],
        refresh=struct.unpack_from('<I',mode,184)[0],bpp=struct.unpack_from('<I',mode,168)[0]))
print(json.dumps(result,indent=2))
