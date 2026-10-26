using System;
using System.IO;
using UDisplayPro;

class EngineTests
{
    static int assertions;
    static void Check(bool condition, string message) { assertions++; if(!condition) throw new Exception(message); }
    static int I(byte[] b,int p) {return BitConverter.ToInt32(b,p);}
    static bool ShaderOperand(int p) {return (p>=0x179125&&p<0x179129)||(p>=0x17912A&&p<0x17912E)||
        (p>=0x1791A0&&p<0x1791A4)||(p>=0x1791A5&&p<0x1791A9)||(p>=0xB0F2A&&p<0xB0F2E)||
        (p>=TrayIntegration.HookOffset&&p<TrayIntegration.HookOffset+5);}
    static void Validate(byte[] original,byte[] b)
    {
        int pe=I(b,0x3c),opt=pe+24,table=opt+BitConverter.ToUInt16(b,pe+20);
        int n=BitConverter.ToUInt16(b,pe+6),section=table+(n-2)*40,tray=section+40;
        int raw=I(b,section+20),rva=I(b,section+12),fileAlign=I(b,opt+36),size=I(b,section+16);
        Check(n==7,"Section count");
        Check(raw%fileAlign==0 && size%fileAlign==0 && raw+size==I(b,tray+20),"File alignment and length");
        Check(I(b,tray+36)==0x60000020 && I(b,tray+20)+I(b,tray+16)==b.Length,"Tray section RX and bounded");
        Check(b[TrayIntegration.HookOffset]==0xe9 && TrayIntegration.HookRva+5+I(b,TrayIntegration.HookOffset+1)==I(b,tray+12),"Tray relative branch");
        Check(I(b,section+36)==0x40000040,"Shader section must be read-only data");
        Check(I(b,opt+128)==0 && I(b,opt+132)==0,"Security directory must be cleared");
        Check(I(b,opt+56)%I(b,opt+32)==0,"SizeOfImage alignment");
        Check(I(b,0xB0F2A)==30 || I(b,0xB0F2A)==45 || I(b,0xB0F2A)==60,"Capture target allowlist");
        for(int k=0x400;k<0x46D600;k++)
            if(!ShaderOperand(k)&&b[k]!=original[k])throw new Exception("Unexpected code edit at "+k.ToString("X"));
        // Original shaders/resources/relocations, including overlay, remain byte-for-byte intact.
        for(int k=0x46D600;k<original.Length;k++)if(b[k]!=original[k])throw new Exception("Unexpected data edit");
        for(int plane=0;plane<2;plane++)
        {
            int operand=plane==0?0x179125:0x1791A0,len=I(b,operand),ptr=I(b,operand+5);
            int off=ptr-I(b,opt+28)-rva+raw;
            Check(off>=raw&&off+len<=raw+I(b,section+8),"Shader bounds");
            byte[] shader=new byte[len];Array.Copy(b,off,shader,0,len);SafePatcher.ValidateShader(shader);
        }
    }
    static void Main(string[] args)
    {
        byte[] original=SafePatcher.Original(args[0]);
        int largest=0;
        int builds=0;
        for(int mode=0;mode<3;mode++)for(int temp=0;temp<4;temp++)for(int preset=0;preset<7;preset++)
        {
            ProConfig c=new ProConfig {ColorMode=mode,ColorTemp=temp};
            if(preset==1){c.Brightness=-30;c.Contrast=60;c.Saturation=0;c.GammaX100=75;}
            if(preset==2){c.Brightness=30;c.Contrast=140;c.Saturation=160;c.GammaX100=140;}
            if(preset==3){c.Vibrance=40;c.Sharpness=50;c.CaptureFps=60;}
            if(preset==4){c.Vibrance=18;c.Sharpness=20;c.CaptureFps=45;}
            if(preset==5){c.EdgeSmoothing=50;}
            if(preset==6){c.EdgeSmoothing=50;c.Sharpness=50;c.Vibrance=40;c.GammaX100=140;}
            byte[] b=SafePatcher.Build(original,c);Validate(original,b);
            builds++; Check(I(b,0xB0F2A)==c.CaptureFps,"Requested capture immediate");
            largest=Math.Max(largest,Math.Max(I(b,0x179125),I(b,0x1791A0)));
            if(temp==0&&preset==0)File.WriteAllBytes(Path.Combine(args[1],"mode-"+mode+".exe"),b);
        }
        byte[] bad=(byte[])original.Clone();bad[100]^=1;
        try{SafePatcher.Build(bad,new ProConfig());throw new Exception("Accepted modified original");}catch(InvalidDataException){assertions++;}
        try{SafePatcher.Build(original,new ProConfig {GammaX100=0});throw new Exception("Accepted invalid settings");}catch(ArgumentOutOfRangeException){assertions++;}
        foreach(ProConfig invalid in new[] {new ProConfig {Sharpness=51},new ProConfig {Vibrance=-1},new ProConfig {CaptureFps=90},new ProConfig {EdgeSmoothing=51}})
            try{SafePatcher.Build(original,invalid);throw new Exception("Accepted invalid enhancement");}catch(ArgumentOutOfRangeException){assertions++;}
        try{SafePatcher.ValidateShader(new byte[32]);throw new Exception("Accepted invalid DXBC");}catch(InvalidDataException){assertions++;}
        ProConfig saved=new ProConfig {ColorMode=1,GammaX100=125,Brightness=3,Vibrance=18,Sharpness=20,CaptureFps=45,EdgeSmoothing=30};
        string path=Path.Combine(args[1],"roundtrip.ini");saved.Save(path);
        ProConfig loaded=new ProConfig();loaded.Load(path);Check(loaded.ColorMode==1&&loaded.GammaX100==125&&loaded.Brightness==3&&loaded.Vibrance==18&&loaded.Sharpness==20&&loaded.CaptureFps==45&&loaded.EdgeSmoothing==30,"INI round trip");
        try{saved.Save(args[1]);throw new Exception("Swallowed IO error");}catch(IOException){assertions++;}
        ProConfig vivid=ImageProfiles.Create(2,2,30),natural=ImageProfiles.Create(0,1,60);
        Check(vivid.Vibrance==18&&vivid.Sharpness==20&&vivid.Contrast==100&&vivid.Brightness==0,"Vivid preserves neutral contrast");
        Check(natural.Sharpness==0&&natural.Vibrance==0&&natural.ColorMode==1&&natural.CaptureFps==60,"Natural resets enhancements and preserves matrix/capture");
        ProConfig smooth=ImageProfiles.Create(4,2,30);
        Check(smooth.EdgeSmoothing==30&&smooth.Sharpness==0,"Smooth preset avoids sharpening jaggies");
        string[] names={"neutral","vivid","sharp","saturation","smooth","combined"};
        ProConfig[] configs={new ProConfig(),vivid,new ProConfig {Sharpness=50},new ProConfig {Saturation=160,Vibrance=40},smooth,new ProConfig {EdgeSmoothing=50,Sharpness=50}};
        for(int n=0;n<names.Length;n++)for(int plane=0;plane<2;plane++)
            File.WriteAllText(Path.Combine(args[1],names[n]+(plane==0?"-y":"-uv")+".hlsl"),ShaderFactory.Source(configs[n],plane==1));
        // With sharpening off, do not pay for extra texture samples.
        Check(!ShaderFactory.Source(new ProConfig(),false).Contains("GetDimensions"),"Neutral fast path");
        Check(!ShaderFactory.Source(new ProConfig {Sharpness=50},true).Contains("lumaAt"),"Chroma pass never sharpened");
        foreach(ProConfig chroma in configs)
            Check(ShaderFactory.Source(chroma,true).Contains("ddx(inp.uv.x)")&&ShaderFactory.Source(chroma,true).Split(new[]{"adjustedAt(inp.uv"},StringSplitOptions.None).Length==5,"Chroma pass averages 2x2 block");
        Console.WriteLine("PASS: "+builds+" shader configurations, "+assertions+" assertions; largest shader="+largest+" bytes; original code/data preserved except allowlisted operands.");
    }
}
