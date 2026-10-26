using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UDisplayPro
{
    public static class ImageProfiles
    {
        public static ProConfig Create(int preset, int colorMode, int captureFps)
        {
            ProConfig c = new ProConfig { ColorMode=colorMode, CaptureFps=captureFps };
            switch(preset)
            {
                case 0: break;
                case 1: c.Sharpness=25; break;
                case 2: c.Sharpness=20; c.Vibrance=18; break;
                case 3: c.ColorTemp=1; c.Brightness=-2; c.Saturation=92; c.GammaX100=105; break;
                case 4: c.EdgeSmoothing=30; c.Vibrance=10; break;
                default: throw new ArgumentOutOfRangeException("preset");
            }
            return c;
        }
    }

    public static class ShaderFactory
    {
        public static string Source(ProConfig cfg, bool uv)
        {
            if (cfg.ColorMode < 0 || cfg.ColorMode > 2 || cfg.ColorTemp < 0 || cfg.ColorTemp > 3 ||
                cfg.Brightness < -30 || cfg.Brightness > 30 || cfg.Contrast < 60 || cfg.Contrast > 140 ||
                cfg.Saturation < 0 || cfg.Saturation > 160 || cfg.GammaX100 < 75 || cfg.GammaX100 > 140 ||
                cfg.Vibrance < 0 || cfg.Vibrance > 40 || cfg.Sharpness < 0 || cfg.Sharpness > 50 ||
                cfg.EdgeSmoothing < 0 || cfg.EdgeSmoothing > 50 ||
                (cfg.CaptureFps != 30 && cfg.CaptureFps != 45 && cfg.CaptureFps != 60))
                throw new ArgumentOutOfRangeException("cfg", "Ajustes de cor fora dos limites.");
            double kr = cfg.ColorMode == 2 ? .299 : .2126;
            double kb = cfg.ColorMode == 2 ? .114 : .0722;
            double kg = 1 - kr - kb;
            bool full = cfg.ColorMode == 1;
            double ys = full ? 1 : 219.0 / 255;
            double cs = full ? 1 : 224.0 / 255;
            double[][] gains = { new double[] {1,1,1}, new double[] {1,.96,.87},
                new double[] {1,.90,.76}, new double[] {.93,.97,1} };
            // Identical RGB processing in BOTH planes. Clamp RGB before conversion,
            // so brightness/contrast cannot generate illegal YCbCr or inconsistent hues.
            string common = string.Format(CultureInfo.InvariantCulture, @"
Texture2D tx : register(t0);
SamplerState sam : register(s0);
struct PS_IN {{ float4 pos : SV_POSITION; float2 uv : TEXCOORD0; }};
float3 transform(float3 rgb) {{
    rgb = saturate(rgb);
    rgb = pow(rgb, {0:R});
    rgb = saturate((rgb * float3({1:R},{2:R},{3:R}) - 0.5) * {4:R} + 0.5 + {5:R});
    float gray = dot(rgb, float3({6:R},{7:R},{8:R}));
    float3 chroma = rgb - gray.xxx;
    float spread = max(rgb.r, max(rgb.g, rgb.b)) - min(rgb.r, min(rgb.g, rgb.b));
    float gain = {9:R} * (1.0 + {10:R} * (1.0 - spread));
    // Scale all three chroma components together. This keeps hue and luma
    // instead of clipping each RGB channel independently at high saturation.
    float positive = max(chroma.r, max(chroma.g, chroma.b));
    float negative = -min(chroma.r, min(chroma.g, chroma.b));
    float limit = min((1.0-gray)/max(positive, 0.000001), gray/max(negative, 0.000001));
    gain = min(gain, limit);
    return saturate(gray.xxx + chroma * gain);
}}
float3 adjusted(float2 coord) {{
    return transform(tx.Sample(sam, coord).xyz);
}}
", 100.0 / cfg.GammaX100, gains[cfg.ColorTemp][0], gains[cfg.ColorTemp][1], gains[cfg.ColorTemp][2],
                cfg.Contrast / 100.0, cfg.Brightness / 100.0, kr, kg, kb, cfg.Saturation / 100.0, cfg.Vibrance/100.0);
            // Preserve the cheap one-sample neutral path. Luma sharpening never
            // adds samples to the UV pass (avoids colored edge halos).
            if(cfg.Saturation <= 100 && cfg.Vibrance == 0)
            {
                int begin=common.IndexOf("    float3 chroma");
                int end=common.IndexOf("\n}",begin);
                common=common.Substring(0,begin)+string.Format(CultureInfo.InvariantCulture,
                    "    return lerp(gray.xxx, rgb, {0:R});",cfg.Saturation/100.0)+common.Substring(end);
            }
            if(!uv && (cfg.Sharpness > 0 || cfg.EdgeSmoothing > 0))
                return common + string.Format(CultureInfo.InvariantCulture, @"
float lumaAt(float2 coord, float2 texel) {{
    return dot(transform(tx.SampleLevel(sam, clamp(coord, texel*0.5, 1.0-texel*0.5), 0).xyz), float3({0:R},{1:R},{2:R}));
}}
float main(PS_IN inp) : SV_Target {{
    uint width, height; tx.GetDimensions(width, height);
    float2 step = 1.0 / float2(width, height);
    float c = lumaAt(inp.uv, step);
    float n = lumaAt(inp.uv-float2(0,step.y), step);
    float s = lumaAt(inp.uv+float2(0,step.y), step);
    float w = lumaAt(inp.uv-float2(step.x,0), step);
    float e = lumaAt(inp.uv+float2(step.x,0), step);
    float detail = c - 0.25*(n+s+w+e);
    float gate = saturate((abs(detail)-1.0/255.0)*255.0/3.0);
    float delta = clamp(detail * {3:R} * gate, -4.0/255.0, 4.0/255.0);
    // Both gradients must be present: preserve horizontal/vertical strokes
    // and smooth primarily diagonal stair steps. Share the existing 5 samples.
    float diagonal = saturate((min(abs(e-w),abs(n-s))-0.04)/0.16);
    float blend = {6:R} * diagonal;
    float filtered = lerp(c,0.25*(n+s+w+e),blend);
    delta *= 1.0-2.0*blend;
    float low = min(c,min(min(n,s),min(w,e)));
    float high = max(c,max(max(n,s),max(w,e)));
    float y = clamp(filtered+delta,low,high);
    return y * {4:R} + {5:R};
}}",kr,kg,kb,cfg.Sharpness/50.0,ys,full?0:16.0/255,cfg.EdgeSmoothing/100.0);
            if (!uv)
                return common + string.Format(CultureInfo.InvariantCulture, @"
float main(PS_IN inp) : SV_Target {{
    return dot(adjusted(inp.uv), float3({0:R},{1:R},{2:R})) + {3:R};
}}", kr * ys, kg * ys, kb * ys, full ? 0 : 16.0 / 255);
            return common + string.Format(CultureInfo.InvariantCulture, @"
float2 main(PS_IN inp) : SV_Target {{
    float3 rgb = adjusted(inp.uv);
    return clamp(float2(dot(rgb, float3({0:R},{1:R},{2:R})),
                        dot(rgb, float3({3:R},{4:R},{5:R}))) + 128.0/255.0, {6:R}, {7:R});
}}", -kr / (2 * (1-kb)) * cs, -kg / (2*(1-kb)) * cs, .5*cs,
                .5*cs, -kg / (2*(1-kr)) * cs, -kb / (2*(1-kr)) * cs,
                full ? 0 : 16.0/255, full ? 1 : 240.0/255);
        }
    }

    public static class SafePatcher
    {
        public const string OriginalHash = "C0AD5096F99B7C88F31C791C818FE5615F1300A1B63087995B4FF6B91CCABEB9";
        public static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        public static byte[] Original(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != OriginalHash)
                throw new InvalidDataException("Original desconhecido ou modificado. É necessário UDisplay 1.0.2.9 com SHA-256 verificado.");
            return bytes;
        }
        static int I(byte[] b, int p) { return BitConverter.ToInt32(b, p); }
        static void Put(byte[] b, int p, int n) { Array.Copy(BitConverter.GetBytes(n), 0, b, p, 4); }
        static int Align(int n, int a) { return checked((n + a - 1) / a * a); }
        public static void ValidateShader(byte[] b)
        {
            if (b == null || b.Length < 32 || b.Length > 65536 || Encoding.ASCII.GetString(b,0,4) != "DXBC" || I(b,24) != b.Length)
                throw new InvalidDataException("Shader DXBC inválido.");
        }
        public static byte[] Build(byte[] original, ProConfig cfg)
        {
            return TrayIntegration.Apply(BuildColorOnly(original,cfg));
        }
        internal static byte[] BuildColorOnly(byte[] original, ProConfig cfg)
        {
            if (Hash(original) != OriginalHash) throw new InvalidDataException("A base não é o original verificado.");
            byte[] y = D3DShaderCompiler.CompilePS(ShaderFactory.Source(cfg, false));
            byte[] uv = D3DShaderCompiler.CompilePS(ShaderFactory.Source(cfg, true));
            ValidateShader(y); ValidateShader(uv);
            // New read-only PE section. Do not overwrite adjacent resources when
            // gamma/saturation make shaders larger than the original 704/784 bytes.
            int pe = I(original, 0x3c), opt = pe + 24;
            int count = BitConverter.ToUInt16(original, pe+6);
            int table = opt + BitConverter.ToUInt16(original, pe+20);
            int header = table + count*40;
            if (header+40 > I(original,opt+60) || original.Skip(header).Take(40).Any(b => b != 0))
                throw new InvalidDataException("Sem espaço para cabeçalho da seção de cores.");
            int fileAlign = I(original,opt+36), sectionAlign = I(original,opt+32);
            int raw = Align(original.Length,fileAlign);
            int last = table+(count-1)*40;
            int rva = Align(I(original,last+12)+Math.Max(I(original,last+8),I(original,last+16)),sectionAlign);
            int uvOffset = Align(y.Length,16), length = uvOffset+uv.Length;
            int rawSize = Align(length,fileAlign);
            byte[] result = new byte[checked(raw+rawSize)];
            Array.Copy(original,result,original.Length);
            Array.Copy(Encoding.ASCII.GetBytes(".udcolor"),0,result,header,8);
            Put(result,header+8,length); Put(result,header+12,rva);
            Put(result,header+16,rawSize); Put(result,header+20,raw);
            Put(result,header+36,0x40000040); // initialized data, read-only
            Array.Copy(BitConverter.GetBytes((ushort)(count+1)),0,result,pe+6,2);
            Put(result,opt+8,checked(I(original,opt+8)+rawSize));
            Put(result,opt+56,Align(rva+length,sectionAlign));
            Put(result,opt+64,0); // checksum invalid after modification
            Array.Clear(result,opt+96+4*8,8); // PE32 Security directory, NOT generic RVA
            Array.Copy(y,0,result,raw,y.Length); Array.Copy(uv,0,result,raw+uvOffset,uv.Length);
            Put(result,0x179125,y.Length); Put(result,0x1791A0,uv.Length);
            // Original push-immediate operands already have HIGHLOW base relocations.
            // Keeping their locations preserves ASLR fixups for our new shader addresses.
            Put(result,0x17912A,checked(I(original,opt+28)+rva));
            Put(result,0x1791A5,checked(I(original,opt+28)+rva+uvOffset));
            // Only a validated immediate: C7 85 9C FE FF FF 1E 00 00 00.
            // Passed by reference into the controller's capture request. It is
            // a requested target, not a guarantee of encoded/USB/output FPS.
            Put(result,0x0B0F2A,cfg.CaptureFps);
            return result;
        }
    }

    public static class EngineUpdate
    {
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UDisplay");
        public static string ExePath { get { return Path.Combine(InstallDir,"UDisplay.exe"); } }
        public static string OriginalPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"UDisplay.Original.exe"); } }
        public static string LogPath { get { return Path.Combine(InstallDir,"UDisplayPro-update.log"); } }
        static void Log(string text)
        {
            // Logging must never prevent recovery when the disk is full or inaccessible.
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("s")+" "+text+Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        static string PathOf(Process p)
        {
            try { return p.MainModule.FileName; }
            catch (System.ComponentModel.Win32Exception)
            {
                // MainModule fails across privilege/bitness boundaries (the engine
                // runs as a LocalSystem service). Fall back to WMI, which the elevated
                // worker can read, so an unreadable process neither aborts the whole
                // stop sweep (the previous behaviour) nor is blindly skipped.
                using (var q = new System.Management.ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process WHERE ProcessId=" + p.Id))
                using (var r = q.Get())
                    foreach (System.Management.ManagementObject m in r) using (m)
                        return m["ExecutablePath"] as string;
                return null;
            }
        }
        static Process[] OwnedProcesses()
        {
            var owned = new System.Collections.Generic.List<Process>();
            foreach (Process p in Process.GetProcessesByName("UDisplay"))
            {
                try
                {
                    if (string.Equals(PathOf(p),ExePath,StringComparison.OrdinalIgnoreCase)) owned.Add(p);
                    else p.Dispose();
                }
                catch (InvalidOperationException) { p.Dispose(); } // Process exited during enumeration.
            }
            return owned.ToArray();
        }
        public static void Stop()
        {
            // The daemon can restart main; repeat a bounded sweep and verify handles closed.
            for (int pass=0; pass<5; pass++)
            {
                Process[] procs = OwnedProcesses();
                if (procs.Length == 0) return;
                foreach (Process p in procs) using(p)
                {
                    try { if (!p.HasExited) { p.Kill(); if (!p.WaitForExit(5000)) throw new IOException("Processo não encerrou."); } }
                    catch (InvalidOperationException) { }
                }
                Thread.Sleep(250);
            }
            throw new IOException("UDisplay continua em execução. Nenhum arquivo foi substituído.");
        }
        public static void Start()
        {
            using (Process daemon = Process.Start(new ProcessStartInfo(ExePath,"daemon") {
                WorkingDirectory=InstallDir,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden }))
            {
                Thread.Sleep(1200);
                // Some daemon versions start main themselves. Check command lines first.
                if (!HasMain())
                    using (Process main = Process.Start(new ProcessStartInfo(ExePath,"main") {
                        WorkingDirectory=InstallDir,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden })) { }
            }
            for(int i=0;i<10;i++) { Thread.Sleep(500); }
            if (!HasMain()) throw new IOException("A captura (main) não permaneceu em execução.");
        }
        public static bool HasMain()
        {
            using (var query = new System.Management.ManagementObjectSearcher("SELECT ExecutablePath,CommandLine FROM Win32_Process WHERE Name='UDisplay.exe'"))
            using (var results = query.Get())
                foreach (System.Management.ManagementObject p in results) using(p)
                {
                    string path = p["ExecutablePath"] as string, cmd = p["CommandLine"] as string;
                    if (string.Equals(path,ExePath,StringComparison.OrdinalIgnoreCase) && cmd != null &&
                        System.Text.RegularExpressions.Regex.IsMatch(cmd, @"\smain\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
                }
            return false;
        }
        static void AtomicWrite(string path, byte[] data)
        {
            string stage = path+".stage-"+Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                { stream.Write(data,0,data.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(stage,path,null); else File.Move(stage,path);
            }
            finally { if(File.Exists(stage)) File.Delete(stage); }
        }
        public static bool ConfirmImage()
        {
            bool accepted = false;
            int seconds = 25;
            Window w = new Window { Title="Confirmar imagem do monitor",Width=540,Height=260,ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.Manual,Topmost=true };
            // Keep the recovery prompt on the PRIMARY display, even if the USB monitor is black.
            w.Left = Math.Max(0,(SystemParameters.PrimaryScreenWidth-w.Width)/2);
            w.Top = Math.Max(0,(SystemParameters.PrimaryScreenHeight-w.Height)/2);
            StackPanel panel = new StackPanel { Margin=new Thickness(22) };
            TextBlock msg = new TextBlock { TextWrapping=TextWrapping.Wrap,FontSize=16,Margin=new Thickness(0,0,0,18) };
            Button yes = new Button { Content="A imagem está boa — manter ajustes",Height=38,Margin=new Thickness(0,0,0,10) };
            Button no = new Button { Content="Reverter para a versão anterior",Height=34 };
            yes.Click += (s,e)=> { accepted=true; w.Close(); }; no.Click += (s,e)=>w.Close();
            Action refresh = ()=> { msg.Text="Confira o terceiro monitor. Sem confirmação, a versão anterior será restaurada em "+seconds+" segundos."; };
            panel.Children.Add(msg);panel.Children.Add(yes);panel.Children.Add(no);w.Content=panel;
            DispatcherTimer timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
            timer.Tick += (s,e)=> { seconds--;refresh();if(seconds<=0)w.Close(); };
            refresh(); timer.Start(); try { w.ShowDialog(); } finally { timer.Stop(); }
            return accepted;
        }
        public static int Run(string mode, string settings)
        {
            bool created;
            using (Mutex mutex = new Mutex(true,"Global\\UDisplayProSafeUpdate",out created))
            {
                if(!created) throw new IOException("Já existe uma atualização em andamento.");
                Directory.CreateDirectory(InstallDir);
                string journal = Path.Combine(InstallDir,"UDisplayPro.pending");
                string backup = null;
                bool stopped = false, installed = false;
                try
                {
                    // A previous interrupted update is recovered before accepting another one.
                    if(File.Exists(journal))
                    {
                        string name=File.ReadAllText(journal).Trim();
                        if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^backup-[0-9a-f]{32}$")) throw new IOException("Registro de recuperação inválido.");
                        string previous=Path.Combine(InstallDir,name);
                        if(!File.Exists(Path.Combine(previous,"UDisplay.exe"))) throw new IOException("Backup de recuperação ausente.");
                        Stop(); AtomicWrite(ExePath,File.ReadAllBytes(Path.Combine(previous,"UDisplay.exe")));
                        string recoverIni=Path.Combine(InstallDir,"UDisplayPro.ini"), savedIni=Path.Combine(previous,"UDisplayPro.ini");
                        if(File.Exists(savedIni)) AtomicWrite(recoverIni,File.ReadAllBytes(savedIni)); else if(File.Exists(recoverIni)) File.Delete(recoverIni);
                        Start();
                        File.Delete(journal); Log("Recuperada transação interrompida: "+name);
                    }
                    if(mode=="--recover") return 0;
                    if(mode=="--restart") { Stop(); Start(); Log("Captura reiniciada."); return 0; }
                    if(!File.Exists(ExePath))
                    {
                        // The engine binary is missing (e.g. removed by antivirus, whose
                        // scan no longer trusts the patched copy because its Authenticode
                        // signature was invalidated). Factory restore must still work from
                        // the verified original, with no running capture to stop and no
                        // backup to roll back to.
                        if(mode=="--restore")
                        {
                            Stop(); AtomicWrite(ExePath,SafePatcher.Original(OriginalPath)); Start();
                            string gone=Path.Combine(InstallDir,"UDisplayPro.ini"); if(File.Exists(gone)) File.Delete(gone);
                            Log("Original reinstalado: o executável estava ausente."); return 0;
                        }
                        throw new FileNotFoundException("Instale primeiro o driver e o aplicativo original do adaptador. Para repor apenas o motor de fábrica, use Recuperar Original de Fábrica.",ExePath);
                    }
                    byte[] original = SafePatcher.Original(OriginalPath);
                    ProConfig cfg = new ProConfig();
                    if(mode=="--apply" || mode=="--test-rollback") { if(!File.Exists(settings)) throw new FileNotFoundException("Configuração ausente."); cfg.Load(settings); }
                    if(mode=="--install-tray") cfg.Load(Path.Combine(InstallDir,"UDisplayPro.ini"));
                    byte[] candidate = mode=="--restore" ? original : SafePatcher.Build(original,cfg);
                    if(mode=="--install-tray")
                    {
                        // Installation changes only the tray handler. Require exact proof
                        // that the current color engine matches the persisted settings.
                        string activeHash=SafePatcher.Hash(File.ReadAllBytes(ExePath));
                        byte[] originalWithTray=TrayIntegration.Apply(original);
                        bool factory=!File.Exists(Path.Combine(InstallDir,"UDisplayPro.ini")) &&
                            (activeHash==SafePatcher.OriginalHash || activeHash==SafePatcher.Hash(originalWithTray));
                        if(factory) candidate=originalWithTray;
                        if(!factory && activeHash!=SafePatcher.Hash(candidate) && activeHash!=SafePatcher.Hash(SafePatcher.BuildColorOnly(original,cfg)))
                            throw new IOException("O motor não corresponde aos ajustes salvos. Aplique um perfil pelo painel antes de integrar a bandeja.");
                        string panel=Process.GetCurrentProcess().MainModule.FileName;
                        string target=Path.Combine(InstallDir,"UDisplayConfig.exe");
                        if(!string.Equals(panel,target,StringComparison.OrdinalIgnoreCase))
                        {
                            byte[] panelBytes=File.ReadAllBytes(panel);
                            if(!File.Exists(target)||SafePatcher.Hash(File.ReadAllBytes(target))!=SafePatcher.Hash(panelBytes))AtomicWrite(target,panelBytes);
                        }
                        string originalTarget=Path.Combine(InstallDir,"UDisplay.Original.exe");
                        if(!File.Exists(originalTarget)||SafePatcher.Hash(File.ReadAllBytes(originalTarget))!=SafePatcher.OriginalHash)AtomicWrite(originalTarget,original);
                        if(activeHash==SafePatcher.Hash(candidate)) { Log("Bandeja já integrada; captura preservada.");return 0; }
                    }
                    backup=Path.Combine(InstallDir,"backup-"+Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(backup);
                    File.Copy(ExePath,Path.Combine(backup,"UDisplay.exe"));
                    string ini=Path.Combine(InstallDir,"UDisplayPro.ini");
                    if(File.Exists(ini)) File.Copy(ini,Path.Combine(backup,"UDisplayPro.ini"));
                    // Verify disk writes and build BEFORE stopping the working display.
                    string ready=Path.Combine(backup,"candidate.exe");
                    File.WriteAllBytes(ready,candidate);
                    if(SafePatcher.Hash(File.ReadAllBytes(ready))!=SafePatcher.Hash(candidate)) throw new IOException("Falha ao verificar arquivo preparado.");
                    Log("Preparado "+mode+"; backup="+backup+"; SHA256="+SafePatcher.Hash(candidate));
                    AtomicWrite(journal,Encoding.ASCII.GetBytes(Path.GetFileName(backup)));
                    stopped=true; Stop();
                    AtomicWrite(ExePath,candidate); installed=true;
                    Start();
                    if(mode=="--test-rollback") throw new OperationCanceledException("Teste de reversão solicitado após iniciar a captura.");
                    if(mode!="--restore" && mode!="--install-tray" && !ConfirmImage()) throw new OperationCanceledException("Configuração não confirmada; versão anterior restaurada.");
                    if(mode=="--apply")
                    {
                        cfg.Save(Path.Combine(backup,"accepted.ini"));
                        AtomicWrite(ini,File.ReadAllBytes(Path.Combine(backup,"accepted.ini")));
                    }
                    else if(mode=="--restore" && File.Exists(ini)) File.Delete(ini);
                    File.Delete(journal); // Commit only after process check + visual confirmation + settings save.
                    stopped=false; installed=false;
                    Log("Concluído "+mode); return 0;
                }
                catch(Exception ex)
                {
                    Log("Falha: "+ex);
                    if(stopped)
                    {
                        try
                        {
                            Stop();
                            if(installed) AtomicWrite(ExePath,File.ReadAllBytes(Path.Combine(backup,"UDisplay.exe")));
                            string ini=Path.Combine(InstallDir,"UDisplayPro.ini"), old=Path.Combine(backup,"UDisplayPro.ini");
                            if(File.Exists(old)) AtomicWrite(ini,File.ReadAllBytes(old)); else if(File.Exists(ini)) File.Delete(ini);
                            Start(); if(File.Exists(journal)) File.Delete(journal);
                            Log("Rollback concluído.");
                        }
                        catch(Exception rollback) { throw new IOException("Falha de atualização e recuperação. Backup: "+backup+". Log: "+LogPath, new AggregateException(ex,rollback)); }
                    }
                    if(ex is OperationCanceledException) return 2;
                    throw;
                }
            }
        }
    }
}
