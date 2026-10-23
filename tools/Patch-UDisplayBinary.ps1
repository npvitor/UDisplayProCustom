<#
.SYNOPSIS
    Reconstrói o binário otimizado UDisplay.exe (UDisplay Pro) a partir do executável
    original de fábrica (v1.0.2.9) do adaptador USB-C Lemorele LC06 Plus.

.DESCRIPTION
    Este script aplica todos os patches de engenharia reversa:
    1. Remoção da assinatura Authenticode para permitir modificações no PE32.
    2. Compilação e injeção dos novos Pixel Shaders Direct3D 11 (ps_5_0) BT.709 (Planos Y e UV/NV12).
    3. Desbloqueio de 60 FPS reais (30 -> 60 FPS) e aumento da resolução da barra lateral.
    4. Ativação do codificador H.264 CPU x264 Zero-Latency (Constrained Baseline, 0 B-frames, 4 threads).
    5. Aumento do Bitrate de vídeo de 10 Mbps para 24 Mbps (alta nitidez de texto).
    6. Redução do intervalo de atualização de quadro estático (500ms -> 80ms) para eliminar atraso de hover/foco do mouse.
    7. Desativação do loop infinito de verificação OTA (5s) e bloqueio de telemetria externa.
    8. Redirecionamento completo do ícone da bandeja do sistema (System Tray) para abrir o novo UDisplayConfig.exe.
#>

param(
    [string]$InputExe  = "$PSScriptRoot\..\original\UDisplay_Original_v1.0.2.9.exe",
    [string]$OutputExe = "$PSScriptRoot\..\dist\UDisplay.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InputExe)) {
    throw "Binário original não encontrado em: $InputExe"
}

Write-Host "[1/6] Lendo binário original ($InputExe)..." -ForegroundColor Cyan
[byte[]]$bin = [System.IO.File]::ReadAllBytes((Resolve-Path $InputExe))

# ----------------------------------------------------------------------------
# 1. Remover assinatura digital Authenticode (DataDirectory[4] Security)
# ----------------------------------------------------------------------------
$peOffset = [BitConverter]::ToInt32($bin, 0x3C)
$secDirOffset = $peOffset + 0x98 # OptionalHeader DataDirectory[4] (Certificate Table)
$certVA   = [BitConverter]::ToInt32($bin, $secDirOffset)
$certSize = [BitConverter]::ToInt32($bin, $secDirOffset + 4)

if ($certVA -gt 0 -and ($certVA + $certSize) -le $bin.Length) {
    Write-Host "[2/6] Removendo tabela de certificado Authenticode (Offset 0x$($certVA.ToString('X')))..." -ForegroundColor Cyan
    [Array]::Clear($bin, $secDirOffset, 8)
    $newLen = $certVA
    $trimmed = New-Object byte[] $newLen
    [Array]::Copy($bin, 0, $trimmed, 0, $newLen)
    $bin = $trimmed
}

# ----------------------------------------------------------------------------
# 2. Compilar e Injetar Pixel Shaders D3D11 (BT.709 Studio Swing)
# ----------------------------------------------------------------------------
Write-Host "[3/6] Compilando Pixel Shaders Direct3D 11 (ps_5_0) via d3dcompiler_47.dll..." -ForegroundColor Cyan

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class ShaderBuilder {
    [Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3DBlob {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] IntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(
        [MarshalAs(UnmanagedType.LPStr)] string pSrcData, IntPtr SrcDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string pSourceName, IntPtr pDefines, IntPtr pInclude,
        [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint, [MarshalAs(UnmanagedType.LPStr)] string pTarget,
        uint Flags1, uint Flags2, out ID3DBlob ppCode, out ID3DBlob ppErrorMsgs);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int D3DStripShader(
        IntPtr pShaderBytecode, IntPtr BytecodeLength, uint uStripFlags, out ID3DBlob ppStrippedBlob);

    public static byte[] Compile(string hlsl) {
        ID3DBlob code, err;
        int hr = D3DCompile(hlsl, (IntPtr)hlsl.Length, null, IntPtr.Zero, IntPtr.Zero, "main", "ps_5_0", (1u << 15), 0, out code, out err);
        if (hr < 0 || code == null) throw new Exception("D3DCompile failed: " + hr);
        ID3DBlob stripped;
        int hr2 = D3DStripShader(code.GetBufferPointer(), code.GetBufferSize(), 0x1Fu, out stripped);
        ID3DBlob finalBlob = (hr2 >= 0 && stripped != null) ? stripped : code;
        byte[] res = new byte[(int)finalBlob.GetBufferSize()];
        Marshal.Copy(finalBlob.GetBufferPointer(), res, 0, res.Length);
        return res;
    }
}
"@

$hlslY  = Get-Content -Raw "$PSScriptRoot\..\shaders\RGBToY_BT709.hlsl"
$hlslUV = Get-Content -Raw "$PSScriptRoot\..\shaders\RGBToUV_BT709.hlsl"

$yBytes  = [ShaderBuilder]::Compile($hlslY)
$uvBytes = [ShaderBuilder]::Compile($hlslUV)

# Plano Y: Offset 0x4E8FF0 (Max 704 bytes), instrução push size em 0x179125
[Array]::Clear($bin, 0x4E8FF0, 704)
[Array]::Copy($yBytes, 0, $bin, 0x4E8FF0, $yBytes.Length)
[Array]::Copy([BitConverter]::GetBytes([int]$yBytes.Length), 0, $bin, 0x179125, 4)

# Plano UV: Offset 0x4E8CE0 (Max 784 bytes), instrução push size em 0x1791A0
[Array]::Clear($bin, 0x4E8CE0, 784)
[Array]::Copy($uvBytes, 0, $bin, 0x4E8CE0, $uvBytes.Length)
[Array]::Copy([BitConverter]::GetBytes([int]$uvBytes.Length), 0, $bin, 0x1791A0, 4)

# ----------------------------------------------------------------------------
# 3. Aplicar Patches de Desempenho, 60 FPS, Zero-Latency e Nitidez
# ----------------------------------------------------------------------------
Write-Host "[4/6] Aplicando patches de 60 FPS, Zero-Latency x264 e 24 Mbps..." -ForegroundColor Cyan

# Taxa de quadros padrão: 30 -> 60 FPS (Offset 0x0B0F2A)
[Array]::Copy([BitConverter]::GetBytes([int]60), 0, $bin, 0x0B0F2A, 4)

# Limiar de resolução de captura rápida: 500 -> 960 (Offset 0x176A2E)
[Array]::Copy([BitConverter]::GetBytes([int]960), 0, $bin, 0x176A2E, 4)

# Forçar CPU x264 Zero-Latency (substituir JNZ 0x75 por JMP 0xEB em 0x15EC85)
$bin[0x15EC85] = 0xEB

# Intervalo de I-Frame / GOP: 0x09 -> 0x20 (Offset 0x15F698)
$bin[0x15F698] = 0x20

# Remover sleep artificial no loop de captura: 1ms -> 0ms (Offset 0x15F714)
$bin[0x15F714] = 0x00

# Tempo de atualização de quadro estático (hover do mouse): 500ms -> 80ms (Offset 0x15F7A5)
[Array]::Copy([BitConverter]::GetBytes([int]80), 0, $bin, 0x15F7A5, 4)

# Bitrate H.264: 10.000.000 -> 24.000.000 bps (24 Mbps) (Offset 0x0B0C4D)
[Array]::Copy([BitConverter]::GetBytes([int]24000000), 0, $bin, 0x0B0C4D, 4)

# Threads do codificador x264: 1 -> 4 Threads (Offsets 0x189316 e 0x189323)
[Array]::Copy([BitConverter]::GetBytes([int]4), 0, $bin, 0x189316, 4)
[Array]::Copy([BitConverter]::GetBytes([int]4), 0, $bin, 0x189323, 4)

# ----------------------------------------------------------------------------
# 4. Desativar Loop Infinito de OTA (5s) e Telemetria
# ----------------------------------------------------------------------------
Write-Host "[5/6] Desativando loop de atualização OTA e telemetria..." -ForegroundColor Cyan

# Saltar criação da thread OTA (0x0A3BB5: JZ -> JMP 0xEB)
$bin[0x0A3BB5] = 0xEB

# Neutralizar URL de telemetria (Offset 0x4ADF2C)
$bin[0x4ADF2C] = 0x00

# ----------------------------------------------------------------------------
# 5. Redirecionar Bandeja do Sistema (System Tray) para UDisplayConfig.exe
# ----------------------------------------------------------------------------
Write-Host "[6/6] Redirecionando handlers da bandeja (System Tray) para C:\ProgramData\UDisplay\UDisplayConfig.exe..." -ForegroundColor Cyan

# Gravar caminho "C:\ProgramData\UDisplay\UDisplayConfig.exe" em 0x4F3CBE (VA 0x008F48BE)
$cfgPathBytes = [System.Text.Encoding]::ASCII.GetBytes("C:\ProgramData\UDisplay\UDisplayConfig.exe`0")
[Array]::Copy($cfgPathBytes, 0, $bin, 0x4F3CBE, $cfgPathBytes.Length)

# Stub x86: ShellExecuteA(NULL, NULL, "C:\ProgramData\UDisplay\UDisplayConfig.exe", NULL, NULL, SW_SHOWNORMAL) + RET
# VA de C:\ProgramData\UDisplay\UDisplayConfig.exe = 0x008F48BE
# IAT de ShellExecuteA = 0x0086F6B0
[byte[]]$shellExecStub = @(
    0x6A, 0x01,                         # push 1 (SW_SHOWNORMAL)
    0x6A, 0x00,                         # push 0 (lpDirectory)
    0x6A, 0x00,                         # push 0 (lpParameters)
    0x68, 0xBE, 0x48, 0x8F, 0x00,       # push 0x008F48BE (lpFile)
    0x6A, 0x00,                         # push 0 (lpOperation = "open")
    0x6A, 0x00,                         # push 0 (hwnd)
    0xFF, 0x15, 0xB0, 0xF6, 0x86, 0x00, # call dword ptr [0x0086F6B0] (ShellExecuteA)
    0xC3                                # ret
)

# Substituir todos os pontos de entrada da UI antiga pelo stub que abre o UDisplayConfig.exe:
# - 0x07F3D0: Menu Tray 0x8020
# - 0x09A5A0: Menu Tray 0x8023
# - 0x0A4C10: CUIInterface::ShowMainDlg()
# - 0x09AE30, 0x09B570, 0x09BCB0, 0x09C3F0: Submenus de dispositivos 1..4
$uiEntryOffsets = @(0x07F3D0, 0x09A5A0, 0x0A4C10, 0x09AE30, 0x09B570, 0x09BCB0, 0x09C3F0)
foreach ($off in $uiEntryOffsets) {
    [Array]::Copy($shellExecStub, 0, $bin, $off, $shellExecStub.Length)
}

# Clique esquerdo no ícone da bandeja (WM_LBUTTONUP em 0x09A514):
# Redirecionar para chamar 0x09A5A0 (nosso stub) e sair limpa a função
[byte[]]$leftClickPatch = @(
    0xE8, 0x87, 0x00, 0x00, 0x00,       # call 0x09A5A0
    0xE9, 0x3E, 0xFE, 0xFF, 0xFF        # jmp 0x09A35C
)
[Array]::Copy($leftClickPatch, 0, $bin, 0x09A514, $leftClickPatch.Length)

# Impedir que CUIInterface::CheckCmd (0x0A4D40) defina m_bShowWindow = 1 e abra o painel antigo ao clicar em opções rápidas da bandeja:
$bin[0x0A4DEB] = 0x00
$bin[0x0A4DF2] = 0x00
$bin[0x0A4E27] = 0xEB

# Salvar binário final
$outDir = Split-Path -Parent $OutputExe
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
[System.IO.File]::WriteAllBytes($OutputExe, $bin)

Write-Host "Concluído! Binário otimizado salvo em: $OutputExe" -ForegroundColor Green
