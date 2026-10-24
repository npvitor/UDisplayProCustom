using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

// Execute the actual emitted x86 handler at randomized addresses, with mocked
// IAT functions. No injection into UDisplay, no monitor/driver changes.
class TrayTests
{
    [DllImport("kernel32")] static extern IntPtr VirtualAlloc(IntPtr p,UIntPtr size,uint type,uint protect);
    [DllImport("kernel32")] static extern bool VirtualFree(IntPtr p,UIntPtr size,uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Handler(int id,int message);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ModuleName(IntPtr module,IntPtr buffer,int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Create(IntPtr name,IntPtr command,IntPtr pa,IntPtr ta,int inherit,int flags,IntPtr env,IntPtr dir,IntPtr si,IntPtr pi);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Close(IntPtr handle);
    static int assertions,creates,closes;
    static string modulePath,expectedPath;
    static bool failCreate,failModule,truncated;
    static void Check(bool yes,string message) {assertions++;if(!yes)throw new Exception(message);}
    static int I(byte[] b,int n) {return BitConverter.ToInt32(b,n);}
    static void Main(string[] args)
    {
        Check(IntPtr.Size==4,"Tests must run as x86");
        byte[] b=File.ReadAllBytes(args[0]);int pe=I(b,0x3c),opt=pe+24,table=opt+BitConverter.ToUInt16(b,pe+20);
        int n=BitConverter.ToUInt16(b,pe+6),header=table+(n-1)*40,rva=I(b,header+12),raw=I(b,header+20),length=I(b,header+8);
        ModuleName getName=(module,buffer,count)=> {
            Check(module==IntPtr.Zero&&count==1024,"Module path API arguments");
            if(failModule)return 0;if(truncated)return count;
            byte[] path=Encoding.Unicode.GetBytes(modulePath+"\0");Marshal.Copy(path,0,buffer,path.Length);return modulePath.Length;
        };
        Create create=(name,command,pa,ta,inherit,flags,env,dir,si,pi)=> {
            creates++;Check(Marshal.PtrToStringUni(name)==expectedPath,"Full sibling Unicode path: "+Marshal.PtrToStringUni(name)+"; expected: "+expectedPath);
            Check(command==IntPtr.Zero&&pa==IntPtr.Zero&&ta==IntPtr.Zero&&inherit==0&&flags==0&&env==IntPtr.Zero&&dir==IntPtr.Zero,"No shell, arguments or inherited handles");
            Check(Marshal.ReadInt32(si)==68,"STARTUPINFO size");
            for(int k=4;k<68;k+=4)Check(Marshal.ReadInt32(si,k)==0,"STARTUPINFO zero initialized");
            if(failCreate)return 0;
            Marshal.WriteInt32(pi,111);Marshal.WriteInt32(pi,4,222);return 1;
        };
        Close close=handle=>{closes++;Check(handle.ToInt32()==111||handle.ToInt32()==222,"Only child handles closed");return 1;};
        // Several nonpreferred bases exercise the position-independent code.
        foreach(int requested in new[]{0x18000000,0x28000000,0x38000000})
        {
            IntPtr mem=VirtualAlloc(new IntPtr(requested),(UIntPtr)(rva+4096),0x3000,0x40);
            Check(mem!=IntPtr.Zero,"Allocate test image");
            try
            {
                Marshal.Copy(b,raw,IntPtr.Add(mem,rva),length);
                Marshal.WriteIntPtr(mem,0x46f548,Marshal.GetFunctionPointerForDelegate(getName));
                Marshal.WriteIntPtr(mem,0x46f3b4,Marshal.GetFunctionPointerForDelegate(create));
                Marshal.WriteIntPtr(mem,0x46f5e0,Marshal.GetFunctionPointerForDelegate(close));
                // Continue the original prologue with a sentinel return, proving
                // the gateway recreates its frame and cleans both parameters.
                byte[] fallback={0x58,0x8b,0xe5,0x5d,0xb8,0x78,0x56,0x34,0x12,0xc2,0x08,0x00};
                Marshal.Copy(fallback,0,IntPtr.Add(mem,0x9af05),fallback.Length);
                Handler fn=(Handler)Marshal.GetDelegateForFunctionPointer(IntPtr.Add(mem,rva),typeof(Handler));
                foreach(string folder in new[]{@"C:\ProgramData\UDisplay\",@"C:\Usuários\João Teste\显示器\"})
                {
                    modulePath=folder+"UDisplay.exe";expectedPath=folder+"UDisplayConfig.exe";
                    creates=closes=0;Check(fn(0x80,0x202)==0,"Left click consumed");Check(creates==1&&closes==2,"Exactly one launch, both handles released");
                }
                creates=closes=0;
                foreach(int message in new[]{0x201,0x203,0x204,0x205,0x200,0x400,0x7fff})Check(fn(0x80,message)==0x12345678,"Other events use original handler");
                Check(fn(0x81,0x202)==0x12345678,"Other icon id preserved");Check(creates==0&&closes==0,"No launch outside left click");
                failCreate=true;Check(fn(0x80,0x202)==0x12345678,"Missing panel falls back");failCreate=false;Check(closes==0,"No invalid handles closed");
                failModule=true;Check(fn(0x80,0x202)==0x12345678,"Module API failure falls back");failModule=false;
                truncated=true;Check(fn(0x80,0x202)==0x12345678,"Truncated module path rejected");truncated=false;
                modulePath="UDisplay.exe";Check(fn(0x80,0x202)==0x12345678,"No path separator rejected");
                modulePath="C:\\"+new string('a',1002)+"\\UDisplay.exe";Check(fn(0x80,0x202)==0x12345678,"Insufficient filename room rejected");
                string boundary="C:\\"+new string('a',1001)+"\\";
                modulePath=boundary+"UDisplay.exe";expectedPath=boundary+"UDisplayConfig.exe";
                Check(fn(0x80,0x202)==0,"Exact buffer boundary accepted with NUL");
                modulePath=@"C:\ProgramData\UDisplay\UDisplay.exe";expectedPath=@"C:\ProgramData\UDisplay\UDisplayConfig.exe";
                creates=closes=0;for(int k=0;k<200;k++)Check(fn(0x80,0x202)==0,"Repeated clicks keep stack balanced");
                Check(creates==200&&closes==400,"Repeated clicks release all handles");
                GC.KeepAlive(fn);
            }
            finally {VirtualFree(mem,UIntPtr.Zero,0x8000);}
        }
        GC.KeepAlive(getName);GC.KeepAlive(create);GC.KeepAlive(close);
        Console.WriteLine("PASS: tray x86 execution, "+assertions+" assertions, 3 relocated bases, Unicode/spaces, fallback, repeated clicks and child handle cleanup.");
    }
}
