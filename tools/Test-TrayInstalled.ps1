# Integration test: sends the known tray callback to the verified owner window,
# opens the panel, checks reuse, then closes only the panel created by this test.
# Run elevated if the capture process is elevated. Never restarts capture.
$ErrorActionPreference='Stop'
if(Get-Process UDisplayConfig -ErrorAction SilentlyContinue) {throw 'Close the panel before this test.'}
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class UDisplayTrayTest {
 [DllImport("kernel32",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p,IntPtr address,byte[] bytes,int size,out IntPtr read);
 [DllImport("kernel32")] static extern bool CloseHandle(IntPtr p);
 [DllImport("user32",SetLastError=true)] static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 static int Read(IntPtr p,IntPtr at){byte[] b=new byte[4];IntPtr n;if(!ReadProcessMemory(p,at,b,4,out n)||n.ToInt32()!=4)throw new Exception("ReadProcessMemory failed");return BitConverter.ToInt32(b,0);}
 public static IntPtr FindOwner(int pid){using(Process proc=Process.GetProcessById(pid)){
  IntPtr p=OpenProcess(0x10,false,pid);if(p==IntPtr.Zero)throw new Exception("Cannot read tray owner");
  try {IntPtr module=proc.MainModule.BaseAddress;
   int tray=Read(p,IntPtr.Add(module,0x5b8ccc));if(tray==0)return IntPtr.Zero;
   IntPtr window=new IntPtr(Read(p,new IntPtr((long)(uint)tray+0x220)));uint owner;
   GetWindowThreadProcessId(window,out owner);if(owner!=pid)throw new Exception("Unexpected tray window owner");return window;
  }finally{CloseHandle(p);}
 }}
 public static void Click(IntPtr owner){
  if(!PostMessage(owner,0x464,new IntPtr(0x80),new IntPtr(0x201))||!PostMessage(owner,0x464,new IntPtr(0x80),new IntPtr(0x202)))throw new Exception("Tray callback failed");
 }
}
'@
$engine=Join-Path $env:ProgramData 'UDisplay\UDisplay.exe'
$expectedPanel=Join-Path $env:ProgramData 'UDisplay\UDisplayConfig.exe'
$binary=[IO.File]::ReadAllBytes($engine)
if($binary[0x9a300] -ne 0xe9 -or -not [Text.Encoding]::ASCII.GetString($binary,0,1024).Contains('.udtray')) {throw 'Tray integration not installed.'}
$engines=@(Get-Process UDisplay | Where-Object {$_.Path -eq $engine})
$ids=@($engines.Id | Sort-Object)
$owner=[IntPtr]::Zero
foreach($capture in $engines) {
    $window=[UDisplayTrayTest]::FindOwner($capture.Id)
    if($window -ne [IntPtr]::Zero) {$owner=$window;break}
}
if($owner -eq [IntPtr]::Zero) {throw 'Tray owner not found.'}
$panel=$null
try {
    [UDisplayTrayTest]::Click($owner)
    for($i=0;$i -lt 50;$i++) {
        $panels=@(Get-Process UDisplayConfig -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $expectedPanel})
        if($panels.Count -eq 1 -and $panels[0].MainWindowHandle -ne [IntPtr]::Zero) {$panel=$panels[0];break}
        Start-Sleep -Milliseconds 200
    }
    if($null -eq $panel) {throw 'Tray did not open the installed panel.'}
    $first=$panel.Id
    [UDisplayTrayTest]::Click($owner)
    Start-Sleep -Seconds 2
    $panels=@(Get-Process UDisplayConfig -ErrorAction SilentlyContinue)
    if($panels.Count -ne 1 -or $panels[0].Id -ne $first) {throw 'Second click did not reuse panel.'}
    $panel.Refresh()
    [pscustomobject]@{TrayOwner=$owner.ToInt64();PanelPid=$first;PanelTitle=$panel.MainWindowTitle;RepeatedClickReusesPanel=$true;PanelWorkingSetMB=[Math]::Round($panel.WorkingSet64/1MB,1)} | ConvertTo-Json
} finally {
    if($null -ne $panel -and -not $panel.HasExited) {
        if(-not $panel.CloseMainWindow() -or -not $panel.WaitForExit(10000)) {throw 'Panel did not exit after closing.'}
    }
}
if(Get-Process UDisplayConfig -ErrorAction SilentlyContinue) {throw 'Panel remained resident.'}
$after=@(Get-Process UDisplay | Where-Object {$_.Path -eq $engine} | Select-Object -ExpandProperty Id | Sort-Object)
if(Compare-Object $ids $after) {throw 'Capture processes changed during tray test.'}
'PASS: actual tray callback opens installed panel, second click reuses it, closing leaves no panel process; capture PIDs unchanged.'
