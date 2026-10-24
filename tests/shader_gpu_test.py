"""Execute exported pixel shaders on D3D11 WARP. No desktop/device changes.
Run tools/Test.ps1 first. Python standard library only; Windows required.
"""
import ctypes as c
import math
import sys
from pathlib import Path

P=c.c_void_p; U=c.c_uint; F=c.c_float; HRESULT=c.c_long
def check(hr):
    if hr<0: raise RuntimeError(f'HRESULT {hr & 0xffffffff:08X}')
def call(obj,slot,result,args,*values):
    table=c.cast(obj,c.POINTER(c.POINTER(P))).contents
    return c.WINFUNCTYPE(result,P,*args)(table[slot])(obj,*values)
def release(obj):
    if obj: call(obj,2,U,[])

class Texture(c.Structure):
    _fields_=[(n,U) for n in ('width','height','mips','array','format','samples','quality','usage','bind','cpu','misc')]
class Initial(c.Structure):
    _fields_=[('data',P),('pitch',U),('slice',U)]
class Sampler(c.Structure):
    _fields_=[('filter',U),('u',U),('v',U),('w',U),('bias',F),('aniso',U),('comparison',U),('border',F*4),('minlod',F),('maxlod',F)]
class Viewport(c.Structure):
    _fields_=[(n,F) for n in ('x','y','width','height','near','far')]
class Mapped(c.Structure):
    _fields_=[('data',P),('row',U),('depth',U)]

compile_fn=c.WinDLL('d3dcompiler_47.dll').D3DCompile
compile_fn.restype=HRESULT
compile_fn.argtypes=[P,c.c_size_t,P,P,P,c.c_char_p,c.c_char_p,U,U,c.POINTER(P),c.POINTER(P)]
def compile_shader(source,target):
    data=source.encode('ascii'); blob=P(); errors=P()
    hr=compile_fn(data,len(data),None,None,None,b'main',target,1<<15,0,c.byref(blob),c.byref(errors))
    if errors:
        msg=c.string_at(call(errors,3,P,[])).decode(errors='replace');release(errors)
        if hr<0: raise RuntimeError(msg)
    check(hr)
    return blob

device=P(); context=P(); feature=U()
create=c.WinDLL('d3d11.dll').D3D11CreateDevice
create.restype=HRESULT
create.argtypes=[P,U,P,U,P,U,U,c.POINTER(P),c.POINTER(U),c.POINTER(P)]
hardware='--hardware' in sys.argv
check(create(None,1 if hardware else 5,None,0,None,0,7,c.byref(device),c.byref(feature),c.byref(context)))

vertex_source='''
struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
V main(uint id:SV_VertexID){V o;o.uv=float2((id<<1)&2,id&2);o.pos=float4(o.uv.x*2-1,1-o.uv.y*2,0,1);return o;}
'''
blob=compile_shader(vertex_source,b'vs_5_0'); vertex=P()
check(call(device,12,HRESULT,[P,c.c_size_t,P,c.POINTER(P)],call(blob,3,P,[]),call(blob,4,c.c_size_t,[]),None,c.byref(vertex)));release(blob)
sampler=P(); sd=Sampler(0x15,3,3,3,0,1,8,(F*4)(0,0,0,0),0,3.402823466e38)
check(call(device,23,HRESULT,[c.POINTER(Sampler),c.POINTER(P)],c.byref(sd),c.byref(sampler)))
call(context,11,None,[P,P,U],vertex,None,0)
call(context,24,None,[U],4) # triangle list
call(context,10,None,[U,U,c.POINTER(P)],0,1,c.byref(sampler))
root=Path(__file__).resolve().parents[1]/'obj/tests'
shaders={}
for name in ('neutral','vivid','sharp','saturation','smooth','combined'):
    for plane in ('y','uv'):
        blob=compile_shader((root/f'{name}-{plane}.hlsl').read_text(),b'ps_5_0'); ps=P()
        check(call(device,15,HRESULT,[P,c.c_size_t,P,c.POINTER(P)],call(blob,3,P,[]),call(blob,4,c.c_size_t,[]),None,c.byref(ps)))
        release(blob);shaders[name,plane]=ps

def render(name,plane,pixels,width,height):
    data=(F*(len(pixels)*4))(*(v for rgb in pixels for v in (*rgb,1)))
    initial=Initial(c.cast(data,P),width*16,0)
    desc=Texture(width,height,1,1,2,1,0,0,8,0,0)
    source=P();target=P();staging=P();srv=P();rtv=P()
    try:
        check(call(device,5,HRESULT,[c.POINTER(Texture),P,c.POINTER(P)],c.byref(desc),c.byref(initial),c.byref(source)))
        check(call(device,7,HRESULT,[P,P,c.POINTER(P)],source,None,c.byref(srv)))
        desc.bind=32
        check(call(device,5,HRESULT,[c.POINTER(Texture),P,c.POINTER(P)],c.byref(desc),None,c.byref(target)))
        check(call(device,9,HRESULT,[P,P,c.POINTER(P)],target,None,c.byref(rtv)))
        desc.bind=0;desc.usage=3;desc.cpu=0x20000
        check(call(device,5,HRESULT,[c.POINTER(Texture),P,c.POINTER(P)],c.byref(desc),None,c.byref(staging)))
        vp=Viewport(0,0,width,height,0,1)
        call(context,44,None,[U,c.POINTER(Viewport)],1,c.byref(vp))
        call(context,33,None,[U,c.POINTER(P),P],1,c.byref(rtv),None)
        call(context,8,None,[U,U,c.POINTER(P)],0,1,c.byref(srv))
        call(context,9,None,[P,P,U],shaders[name,plane],None,0)
        call(context,13,None,[U,U],3,0)
        call(context,47,None,[P,P],staging,target)
        mapped=Mapped()
        check(call(context,14,HRESULT,[P,U,U,U,c.POINTER(Mapped)],staging,0,1,0,c.byref(mapped)))
        result=[]
        for y in range(height):
            row=(F*(width*4)).from_address(mapped.data+y*mapped.row)
            result.extend(tuple(row[x*4:x*4+(1 if plane=='y' else 2)]) for x in range(width))
        call(context,15,None,[P,U],staging,0)
        return result
    finally:
        null=P();call(context,8,None,[U,U,c.POINTER(P)],0,1,c.byref(null))
        call(context,33,None,[U,P,P],0,None,None)
        for obj in (rtv,srv,staging,target,source):release(obj)

assertions=0
def expect(condition,message):
    global assertions
    assertions+=1
    if not condition:raise AssertionError(message)

try:
    # Grid covers neutrals, boundaries, pastels, primaries and mixed colors.
    colors=[(r/8,g/8,b/8) for r in range(9) for g in range(9) for b in range(9)]
    neutral_y=render('neutral','y',colors,27,27)
    neutral_uv=render('neutral','uv',colors,27,27)
    for name in ('neutral','vivid','saturation'):
        # vivid includes spatial sharpening, so test uniform areas independently
        # below instead of demanding constant Y on a spatially varying chart.
        uv=render(name,'uv',colors,27,27)
        for rgb,a,z in zip(colors,neutral_uv,uv):
            expect(all(16/255-1e-5<=v<=240/255+1e-5 and math.isfinite(v) for v in z),'Chroma range')
            da=(a[0]-128/255,a[1]-128/255);dz=(z[0]-128/255,z[1]-128/255)
            expect(abs(da[0]*dz[1]-da[1]*dz[0])<2e-5,'Hue preserved under saturation')
            expect(sum(v*v for v in dz)+1e-6>=sum(v*v for v in da),'Chroma strength must not decrease')
    for rgb in [(0,0,0),(1,1,1),(.5,.5,.5),(.8,.4,.3),(1,0,0),(.03,.03,.03)]:
        pixels=[rgb]*81
        reference=render('neutral','y',pixels,9,9)
        for name in ('vivid','sharp','saturation','smooth','combined'):
            output=render(name,'y',pixels,9,9)
            expect(max(abs(a[0]-z[0]) for a,z in zip(reference,output))<2e-5,'Flat-field luma preserved')
    for rgb,y in zip(colors,neutral_y):
        expected=(.299*rgb[0]+.587*rgb[1]+.114*rgb[2])*219/255+16/255
        expect(abs(y[0]-expected)<2e-5,'Reference BT.601 luma')
    # Blurred edge, smooth ramp and extremes: no new local extrema or ringing.
    row=[0,0,.1,.3,.7,.9,1,1]+[i/16 for i in range(17)]
    width=len(row);pixels=[(v,v,v) for _ in range(5) for v in row]
    before=render('neutral','y',pixels,width,5);after=render('sharp','y',pixels,width,5)
    changed=False
    for i,(a,z) in enumerate(zip(before,after)):
        x=i%width;y=i//width
        neighbors=[before[yy*width+xx][0] for xx,yy in [(x,y),(max(x-1,0),y),(min(x+1,width-1),y),(x,max(y-1,0)),(x,min(y+1,4))]]
        expect(min(neighbors)-2e-5<=z[0]<=max(neighbors)+2e-5,'No local overshoot')
        expect(abs(z[0]-a[0])<=4/255*219/255+2e-5,'Sharpening amplitude cap')
        changed |= abs(z[0]-a[0])>1e-4
    expect(changed,'Sharpening actually changes a blurred edge')
    expect(render('sharp','uv',pixels,width,5)==render('neutral','uv',pixels,width,5),'Sharpness leaves chroma unchanged')
    for shape in ('diagonal','vertical','horizontal'):
        pixels=[(float(x>y) if shape=='diagonal' else float((x if shape=='vertical' else y)>6),)*3 for y in range(13) for x in range(13)]
        baseline=render('neutral','y',pixels,13,13)
        for name in ('smooth','combined'):
            output=render(name,'y',pixels,13,13)
            changed=sum(abs(a[0]-z[0])>1e-4 for a,z in zip(baseline,output))
            expect(changed>0 if shape=='diagonal' else changed==0,'Smooth diagonals; preserve straight strokes')
            expect(all(16/255-1e-5<=p[0]<=235/255+1e-5 for p in output),'Smoothing remains in range')
    print(f'PASS: D3D11 {"hardware" if hardware else "WARP"} rendered 12 shaders; {assertions} pixel/color assertions; no desktop changes.')
finally:
    for obj in shaders.values():release(obj)
    for obj in (sampler,vertex,context,device):release(obj)
