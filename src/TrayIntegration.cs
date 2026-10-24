using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace UDisplayPro
{
    // UDisplay 1.0.2.9 only. OnShowTask's notification handler is a thiscall
    // LRESULT(UINT id, LPARAM event), ret 8. The original icon uses id 0x80
    // and callback WM_USER+100 (0x464), without NOTIFYICON_VERSION_4.
    public static class TrayIntegration
    {
        public const int HookOffset = 0x9A300;
        public const int HookRva = 0x9AF00;
        static int I(byte[] b,int p) { return BitConverter.ToInt32(b,p); }
        static void Put(byte[] b,int p,int n) { Array.Copy(BitConverter.GetBytes(n),0,b,p,4); }
        static int Align(int n,int a) { return checked((n+a-1)/a*a); }

        internal static byte[] Apply(byte[] colorImage)
        {
            byte[] prologue={0x55,0x8b,0xec,0x6a,0xff};
            for(int k=0;k<prologue.Length;k++)
                if(colorImage[HookOffset+k]!=prologue[k]) throw new InvalidDataException("Entrada de bandeja inesperada.");
            int pe=I(colorImage,0x3c),opt=pe+24,count=BitConverter.ToUInt16(colorImage,pe+6);
            int table=opt+BitConverter.ToUInt16(colorImage,pe+20),header=table+count*40;
            if(header+40>I(colorImage,opt+60)) throw new InvalidDataException("Sem espaço para a seção da bandeja.");
            for(int k=header;k<header+40;k++) if(colorImage[k]!=0) throw new InvalidDataException("Cabeçalho de seção ocupado.");
            int sectionAlign=I(colorImage,opt+32),fileAlign=I(colorImage,opt+36);
            int rva=Align(I(colorImage,opt+56),sectionAlign),raw=Align(colorImage.Length,fileAlign);
            byte[] stub=Stub(rva); int rawSize=Align(stub.Length,fileAlign);
            byte[] result=new byte[checked(raw+rawSize)];Array.Copy(colorImage,result,colorImage.Length);
            Array.Copy(Encoding.ASCII.GetBytes(".udtray"),0,result,header,7);
            Put(result,header+8,stub.Length);Put(result,header+12,rva);
            Put(result,header+16,rawSize);Put(result,header+20,raw);
            Put(result,header+36,0x60000020); // Execute + read; never writable.
            Array.Copy(BitConverter.GetBytes((ushort)(count+1)),0,result,pe+6,2);
            Put(result,opt+4,checked(I(colorImage,opt+4)+rawSize));
            Put(result,opt+56,Align(rva+stub.Length,sectionAlign));Put(result,opt+64,0);
            Array.Copy(stub,0,result,raw,stub.Length);
            result[HookOffset]=0xe9;Put(result,HookOffset+1,rva-HookRva-5);
            return result;
        }

        // All addresses are relative to the runtime module base, obtained with
        // call/pop. No absolute pointers or new base relocations. Existing IAT
        // entries are resolved by Windows. Original SEH prologue stays intact
        // in the fallback gateway, including its relocation at HookRva+6.
        static byte[] Stub(int rva)
        {
            Code c=new Code(rva);
            c.Bytes(0x81,0x7c,0x24,0x04);c.Int(0x80); // cmp [esp+4], icon id
            c.Branch(0x85,"original");
            c.Bytes(0x81,0x7c,0x24,0x08);c.Int(0x202); // WM_LBUTTONUP only
            c.Branch(0x85,"original");
            c.Bytes(0x55,0x8b,0xec,0x53,0x56,0x57,0x51); // save frame/nonvolatiles/this
            c.Bytes(0x81,0xec);c.Int(2208); // path (1024 wchar), SI, PI; < one stack page
            c.Bytes(0xe8);c.Int(0);int next=c.Position;
            c.Bytes(0x5b,0x81,0xeb);c.Int(rva+next); // ebx = actual module base
            c.Bytes(0x8b,0xf4); // esi = local buffer
            c.Bytes(0x8d,0xbe);c.Int(2048); // zero STARTUPINFOW and PROCESS_INFORMATION
            c.Bytes(0x33,0xc0,0xb9);c.Int(21);c.Bytes(0xfc,0xf3,0xab);
            c.Bytes(0xc7,0x86);c.Int(2048);c.Int(68); // SI.cb
            c.Bytes(0x68);c.Int(1024);c.Bytes(0x56,0x6a,0x00);
            c.CallImport(0x46F548); // GetModuleFileNameW(NULL, path, 1024)
            c.Bytes(0x85,0xc0);c.Branch(0x84,"failed");
            c.Bytes(0x3d);c.Int(1024);c.Branch(0x83,"failed");
            c.Bytes(0x8d,0x3c,0x46); // edi = path + length*2
            c.Label("slash");c.Bytes(0x3b,0xfe);c.Branch(0x86,"failed");
            c.Bytes(0x83,0xef,0x02,0x66,0x83,0x3f,0x5c);c.Branch(0x85,"slash");
            c.Bytes(0x83,0xc7,0x02,0x8b,0xc7,0x2b,0xc6,0x3d);c.Int(2048-38);
            c.Branch(0x87,"failed"); // reserve filename incl NUL (19 wchar)
            c.Bytes(0x8d,0x93);c.Address("filename");
            c.Label("copy");c.Bytes(0x66,0x8b,0x02,0x66,0x89,0x07,0x83,0xc2,0x02,0x83,0xc7,0x02,0x66,0x85,0xc0);
            c.Branch(0x85,"copy");
            c.Bytes(0x8d,0x86);c.Int(2116);c.Bytes(0x50); // PI
            c.Bytes(0x8d,0x86);c.Int(2048);c.Bytes(0x50); // SI
            for(int k=0;k<7;k++)c.Bytes(0x6a,0x00); // cwd, env, flags, inherit, security, cmdline
            c.Bytes(0x56);c.CallImport(0x46F3B4); // CreateProcessW(explicit full Unicode path)
            c.Bytes(0x85,0xc0);c.Branch(0x84,"failed");
            c.Bytes(0xff,0xb6);c.Int(2116);c.CallImport(0x46F5E0); // CloseHandle(process)
            c.Bytes(0xff,0xb6);c.Int(2120);c.CallImport(0x46F5E0); // CloseHandle(thread)
            c.Restore();c.Bytes(0x33,0xc0,0xc2,0x08,0x00);
            c.Label("failed");c.Restore();
            c.Label("original");c.Bytes(0x55,0x8b,0xec,0x6a,0xff);
            c.Bytes(0xe9);c.Int(HookRva+5-(rva+c.Position+4));
            c.Label("filename");c.Bytes(Encoding.Unicode.GetBytes("UDisplayConfig.exe\0"));
            return c.Finish();
        }

        sealed class Code
        {
            readonly List<byte> bytes=new List<byte>();
            readonly Dictionary<string,int> labels=new Dictionary<string,int>();
            readonly List<Tuple<int,string,bool>> fixups=new List<Tuple<int,string,bool>>();
            readonly int rva;
            public Code(int rva) { this.rva=rva; }
            public int Position { get { return bytes.Count; } }
            public void Bytes(params byte[] b) { bytes.AddRange(b); }
            public void Int(int n) { Bytes(BitConverter.GetBytes(n)); }
            public void Label(string name) { labels.Add(name,Position); }
            public void Branch(byte condition,string label) { Bytes(0x0f,condition);fixups.Add(Tuple.Create(Position,label,false));Int(0); }
            public void Address(string label) { fixups.Add(Tuple.Create(Position,label,true));Int(0); }
            public void CallImport(int offset) { Bytes(0xff,0x93);Int(offset); }
            public void Restore() { Bytes(0x8d,0x65,0xf0,0x59,0x5f,0x5e,0x5b,0x5d); }
            public byte[] Finish()
            {
                byte[] result=bytes.ToArray();
                foreach(var f in fixups)Put(result,f.Item1,f.Item3?rva+labels[f.Item2]:labels[f.Item2]-f.Item1-4);
                return result;
            }
        }
    }
}
