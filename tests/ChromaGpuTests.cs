using System;
using System.Runtime.InteropServices;
using UDisplayPro;

// Executes the generated UV shaders on Direct3D 11 (WARP; --hardware for the
// local GPU). NV12 chroma is half resolution, so each half-resolution sample
// must equal the mean of the 2x2 full-resolution samples it covers, with both
// linear and point samplers, since the engine's sampler filter is not known.
// No desktop/device changes.
class ChromaGpuTests
{
    [StructLayout(LayoutKind.Sequential)] struct TextureDesc { public uint Width,Height,Mips,Array,Format,Samples,Quality,Usage,Bind,Cpu,Misc; }
    [StructLayout(LayoutKind.Sequential)] struct Initial { public IntPtr Data; public uint Pitch,Slice; }
    [StructLayout(LayoutKind.Sequential)] struct SamplerDesc { public uint Filter,U,V,W; public float Bias; public uint Aniso,Comparison; public float B0,B1,B2,B3,MinLod,MaxLod; }
    [StructLayout(LayoutKind.Sequential)] struct Viewport { public float X,Y,Width,Height,Near,Far; }
    [StructLayout(LayoutKind.Sequential)] struct Mapped { public IntPtr Data; public uint Row,Depth; }

    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3dcompiler_47.dll",CharSet=CharSet.Ansi)] static extern int D3DCompile(string src,IntPtr size,string name,IntPtr defines,IntPtr include,string entry,string target,uint f1,uint f2,out IntPtr code,out IntPtr errors);

    delegate uint ReleaseFn(IntPtr self);
    delegate IntPtr BlobFn(IntPtr self);
    delegate int CreateTextureFn(IntPtr self,ref TextureDesc d,IntPtr init,out IntPtr tex);
    delegate int CreateViewFn(IntPtr self,IntPtr res,IntPtr desc,out IntPtr view);
    delegate int CreateShaderFn(IntPtr self,IntPtr code,IntPtr size,IntPtr linkage,out IntPtr shader);
    delegate int CreateSamplerFn(IntPtr self,ref SamplerDesc d,out IntPtr s);
    delegate void SetArrayFn(IntPtr self,uint start,uint count,ref IntPtr items);
    delegate void SetShaderFn(IntPtr self,IntPtr shader,IntPtr inst,uint count);
    delegate void DrawFn(IntPtr self,uint count,uint start);
    delegate int MapFn(IntPtr self,IntPtr res,uint sub,uint type,uint flags,out Mapped m);
    delegate void UnmapFn(IntPtr self,IntPtr res,uint sub);
    delegate void SetUIntFn(IntPtr self,uint v);
    delegate void SetTargetsFn(IntPtr self,uint count,ref IntPtr rtv,IntPtr dsv);
    delegate void SetViewportsFn(IntPtr self,uint count,ref Viewport vp);
    delegate void CopyFn(IntPtr self,IntPtr dst,IntPtr src);

    static IntPtr device, context;
    static int assertions;
    static T Slot<T>(IntPtr obj,int slot) { return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size)); }
    static void Release(IntPtr obj) { if(obj!=IntPtr.Zero) Slot<ReleaseFn>(obj,2)(obj); }
    static void Hr(int hr) { if(hr<0) throw new Exception("HRESULT "+hr.ToString("X8")); }
    static void Check(bool condition,string message) { assertions++; if(!condition) throw new Exception(message); }

    static IntPtr Shader(string source,string target,int slot)
    {
        IntPtr code,errors,shader;
        int hr=D3DCompile(source,(IntPtr)source.Length,null,IntPtr.Zero,IntPtr.Zero,"main",target,1u<<15,0,out code,out errors);
        if(hr<0) throw new Exception(errors==IntPtr.Zero ? hr.ToString("X8") : Marshal.PtrToStringAnsi(Slot<BlobFn>(errors,3)(errors)));
        Release(errors);
        try { Hr(Slot<CreateShaderFn>(device,slot)(device,Slot<BlobFn>(code,3)(code),Slot<BlobFn>(code,4)(code),IntPtr.Zero,out shader)); }
        finally { Release(code); }
        return shader;
    }

    static float[][] Render(IntPtr ps,IntPtr sampler,float[][] rgb,uint width,uint height,uint outWidth,uint outHeight)
    {
        float[] data=new float[rgb.Length*4];
        for(int i=0;i<rgb.Length;i++) { data[i*4]=rgb[i][0]; data[i*4+1]=rgb[i][1]; data[i*4+2]=rgb[i][2]; data[i*4+3]=1; }
        GCHandle pin=GCHandle.Alloc(data,GCHandleType.Pinned);
        IntPtr init=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Initial)));
        IntPtr source=IntPtr.Zero,srv=IntPtr.Zero,target=IntPtr.Zero,rtv=IntPtr.Zero,staging=IntPtr.Zero,none=IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(new Initial { Data=pin.AddrOfPinnedObject(),Pitch=width*16 },init,false);
            // R32G32B32A32_FLOAT source/target, as in shader_gpu_test.py.
            TextureDesc desc=new TextureDesc { Width=width,Height=height,Mips=1,Array=1,Format=2,Samples=1,Bind=8 };
            Hr(Slot<CreateTextureFn>(device,5)(device,ref desc,init,out source));
            Hr(Slot<CreateViewFn>(device,7)(device,source,IntPtr.Zero,out srv));
            desc.Width=outWidth; desc.Height=outHeight; desc.Bind=32;
            Hr(Slot<CreateTextureFn>(device,5)(device,ref desc,IntPtr.Zero,out target));
            Hr(Slot<CreateViewFn>(device,9)(device,target,IntPtr.Zero,out rtv));
            desc.Bind=0; desc.Usage=3; desc.Cpu=0x20000;
            Hr(Slot<CreateTextureFn>(device,5)(device,ref desc,IntPtr.Zero,out staging));
            Viewport vp=new Viewport { Width=outWidth,Height=outHeight,Far=1 };
            Slot<SetViewportsFn>(context,44)(context,1,ref vp);
            Slot<SetTargetsFn>(context,33)(context,1,ref rtv,IntPtr.Zero);
            Slot<SetArrayFn>(context,8)(context,0,1,ref srv);
            Slot<SetArrayFn>(context,10)(context,0,1,ref sampler);
            Slot<SetShaderFn>(context,9)(context,ps,IntPtr.Zero,0);
            Slot<DrawFn>(context,13)(context,3,0);
            Slot<CopyFn>(context,47)(context,staging,target);
            Mapped mapped;
            Hr(Slot<MapFn>(context,14)(context,staging,0,1,0,out mapped));
            float[][] result=new float[outWidth*outHeight][];
            float[] row=new float[outWidth*4];
            for(int y=0;y<outHeight;y++)
            {
                Marshal.Copy(mapped.Data+(int)(y*mapped.Row),row,0,row.Length);
                for(int x=0;x<outWidth;x++) result[y*outWidth+x]=new[] { row[x*4],row[x*4+1] };
            }
            Slot<UnmapFn>(context,15)(context,staging,0);
            return result;
        }
        finally
        {
            Slot<SetArrayFn>(context,8)(context,0,1,ref none);
            Slot<SetTargetsFn>(context,33)(context,0,ref none,IntPtr.Zero);
            foreach(IntPtr obj in new[] { rtv,srv,staging,target,source }) Release(obj);
            Marshal.FreeHGlobal(init); pin.Free();
        }
    }

    static void Main(string[] args)
    {
        bool hardware=Array.IndexOf(args,"--hardware")>=0;
        int level;
        Hr(D3D11CreateDevice(IntPtr.Zero,hardware?1:5,IntPtr.Zero,0,IntPtr.Zero,0,7,out device,out level,out context));
        IntPtr vs=Shader("struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};"+
            "V main(uint id:SV_VertexID){V o;o.uv=float2((id<<1)&2,id&2);o.pos=float4(o.uv.x*2-1,1-o.uv.y*2,0,1);return o;}","vs_5_0",12);
        Slot<SetShaderFn>(context,11)(context,vs,IntPtr.Zero,0);
        Slot<SetUIntFn>(context,24)(context,4); // triangle list
        IntPtr[] samplers=new IntPtr[2];
        uint[] filters={0x15,0}; // linear, point
        for(int i=0;i<2;i++)
        {
            SamplerDesc sd=new SamplerDesc { Filter=filters[i],U=3,V=3,W=3,Aniso=1,Comparison=8,MaxLod=float.MaxValue };
            Hr(Slot<CreateSamplerFn>(device,23)(device,ref sd,out samplers[i]));
        }
        const uint W=16,H=10;
        float[][] varied=new float[W*H][], checker=new float[W*H][];
        for(int y=0;y<H;y++) for(int x=0;x<W;x++)
        {
            int n=(int)((x*37+y*101)%729);
            varied[y*W+x]=new[] { n/81/8f,n/9%9/8f,n%9/8f };
            checker[y*W+x]=(x+y)%2==1 ? new[] {1f,0f,0f} : new[] {0f,0f,1f};
        }
        ProConfig[] configs={ new ProConfig(), ImageProfiles.Create(2,2,30), ImageProfiles.Create(4,2,30),
            new ProConfig {Saturation=160,Vibrance=40}, new ProConfig {ColorMode=1,GammaX100=125,ColorTemp=2},
            new ProConfig {ColorMode=0,Brightness=-30,Contrast=140,Saturation=0} };
        IntPtr[] shaders=new IntPtr[configs.Length];
        try
        {
            for(int c=0;c<configs.Length;c++)
            {
                shaders[c]=Shader(ShaderFactory.Source(configs[c],true),"ps_5_0",15);
                foreach(float[][] source in new[] { varied,checker })
                {
                    float[][] full=Render(shaders[c],samplers[0],source,W,H,W,H);
                    float[][] fullPoint=Render(shaders[c],samplers[1],source,W,H,W,H);
                    for(int i=0;i<full.Length;i++)
                    {
                        Check(Math.Abs(full[i][0]-fullPoint[i][0])<1e-6 && Math.Abs(full[i][1]-fullPoint[i][1])<1e-6,"Full-resolution chroma is one centered tap");
                        Check(full[i][0]>=16/255f-1e-5 && full[i][0]<=240/255f+1e-5 || configs[c].ColorMode==1,"Chroma range");
                    }
                    foreach(IntPtr sampler in samplers)
                    {
                        float[][] half=Render(shaders[c],sampler,source,W,H,W/2,H/2);
                        for(int by=0;by<H/2;by++) for(int bx=0;bx<W/2;bx++)
                        {
                            float[][] block={ full[2*by*W+2*bx],full[2*by*W+2*bx+1],full[(2*by+1)*W+2*bx],full[(2*by+1)*W+2*bx+1] };
                            float[] z=half[by*(W/2)+bx];
                            for(int k=0;k<2;k++)
                            {
                                float mean=(block[0][k]+block[1][k]+block[2][k]+block[3][k])/4;
                                // The shader clamps after averaging. A block with a clamped
                                // full-resolution value (full-range Cr of pure red exceeds
                                // 1.0) can only be bounded by its neighbors.
                                float lo=configs[c].ColorMode==1?0:16/255f, hi=configs[c].ColorMode==1?1:240/255f;
                                bool clamped=false;
                                foreach(float[] v in block) clamped|=v[k]<=lo+1e-6||v[k]>=hi-1e-6;
                                if(clamped)
                                {
                                    Check(z[k]>=Math.Min(Math.Min(block[0][k],block[1][k]),Math.Min(block[2][k],block[3][k]))-3e-5 &&
                                          z[k]<=Math.Max(Math.Max(block[0][k],block[1][k]),Math.Max(block[2][k],block[3][k]))+3e-5,"Clamped chroma bounded by block");
                                    continue;
                                }
                                Check(Math.Abs(z[k]-mean)<3e-5,"Chroma 2x2 average: config "+c+(source==checker?" checker":" varied")+
                                    (sampler==samplers[1]?" point":" linear")+" block "+bx+","+by+" got "+z[k]+" expected "+mean+
                                    " from "+block[0][k]+" "+block[1][k]+" "+block[2][k]+" "+block[3][k]);
                            }
                            if(source==checker && configs[c].Saturation>0)
                                foreach(float[] v in block)
                                    Check(Math.Max(Math.Abs(z[0]-v[0]),Math.Abs(z[1]-v[1]))>.05,"Half-resolution chroma is not a single source pixel");
                        }
                    }
                }
            }
            Console.WriteLine("PASS: D3D11 "+(hardware?"hardware":"WARP")+" chroma "+configs.Length+" shaders x linear/point; "+assertions+" assertions; no desktop changes.");
        }
        finally
        {
            foreach(IntPtr obj in shaders) Release(obj);
            foreach(IntPtr obj in samplers) Release(obj);
            Release(vs); Release(context); Release(device);
        }
    }
}
