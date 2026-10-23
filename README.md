# UDisplay Pro — Open Source Mod & Configurator (Lemorele LC06 Plus)

**UDisplay Pro** é uma modificação de engenharia reversa e um painel de calibração moderno em **WPF (.NET)** para adaptadores de vídeo USB-C para HDMI baseados no software **UDisplay** (como o **Lemorele LC06 Plus**, `VID_185B&PID_2D1D`).

O software original de fábrica (`UDisplay v1.0.2.9`) apresenta diversos problemas graves que degradam a experiência de uso:
- **Contraste estourado e cores saturadas artificialmente** (devido a multiplicadores fixos `1.15x` e `1.28x` embutidos nos Pixel Shaders Direct3D 11 originais).
- **Taxa de quadros travada em 30 FPS**, deixando a movimentação do mouse pesada.
- **Atraso perceptível no foco/clique do mouse** (causado pela fila de reordenação de quadros do codificador e pelo intervalo de atualização estática de `500 ms`).
- **Interface original limitada e com falhas de DPI**, além de um loop infinito de verificação OTA a cada 5 segundos.

---

## Principais Melhorias do UDisplay Pro

1. **Pixel Shaders Direct3D 11 (`ps_5_0`) Recodificados do Zero:**
   - Conversão de cores **ITU-R BT.709 Studio Swing (16–235)** ou **Full Range (0–255)** matematicamente precisa, eliminando o contraste estourado (*black crush* / *white clipping*) e a saturação neon.
   - Compilador HLSL em tempo real integrado ao painel (`d3dcompiler_47.dll`), permitindo ajustar **Brilho, Contraste, Saturação, Gamma e Temperatura de Cor (6500K, 5500K, 4500K, 7500K)** diretamente na GPU com custo zero de CPU.
2. **60 FPS Reais + Zero-Latency Mouse Response:**
   - Desbloqueio de **60 FPS** (com suporte a 30, 45, 60, 75 e 90 FPS).
   - Ativação do codificador **CPU x264 Zero-Latency (Constrained Baseline, 4 Threads)** e redução do tempo de resposta de hover do mouse de `500 ms` para `80 ms` (ou `50 ms`).
   - Elevação do Bitrate H.264 de `10 Mbps` para **`24 Mbps`** (ou até `32 Mbps`), garantindo nitidez cristalina em textos pequenos e IDEs de programação.
3. **Painel de Configuração Moderno em WPF (`UDisplayConfig.exe`):**
   - Interface 100% vetorial, tema Dark Slate responsivo a qualquer escala de DPI do Windows e totalmente integrada ao ícone da bandeja do sistema (*System Tray*).
4. **Correções de Bugs e Privacidade:**
   - Loop infinito de atualização OTA (que disparava a cada 5 segundos) desativado e telemetria bloqueada.

---

## Estrutura do Repositório

```text
UDisplay_Pro_GitHub/
├── src/
│   └── UDisplayConfig.cs          # Código-fonte completo em C# (WPF UI + Compilador HLSL D3D11 + Patcher Binário)
├── shaders/
│   ├── RGBToY_BT709.hlsl          # Pixel Shader D3D11 (ps_5_0) para o plano de luminância (Y)
│   └── RGBToUV_BT709.hlsl         # Pixel Shader D3D11 (ps_5_0) para o plano de crominância (UV / NV12)
├── tools/
│   └── Patch-UDisplayBinary.ps1   # Script PowerShell documentado que reconstrói o UDisplay.exe a partir do original
├── docs/
│   └── REVERSE_ENGINEERING.md     # Documentação técnica completa dos offsets x86, shaders e protocolo USB
├── original/
│   └── UDisplay_Original_v1.0.2.9.exe # Backup do binário original de fábrica sem modificações
├── dist/
│   ├── UDisplay.exe               # Binário já otimizado com todos os patches aplicados
│   ├── UDisplayConfig.exe         # Painel de configuração e calibração compilado
│   └── Instalar_Em_Outro_PC.bat   # Instalador automático de 1 clique
├── UDisplayPro.sln                # Solution do Visual Studio
├── UDisplayPro.csproj             # Projeto C# (.NET Framework 4.8 WPF)
├── Build.bat                      # Compilador rápido de 1 clique (usa o csc.exe nativo do Windows)
└── LICENSE                        # Licença MIT
```

---

## Como Compilar e Modificar

### Opção 1: Compilação Rápida (Sem instalar nada)
O Windows 10/11 já possui o compilador C# nativo (`csc.exe` do .NET Framework 4.8). Basta dar dois cliques em **`Build.bat`** ou rodar no terminal:
```bat
.\Build.bat
```
Isso compila `src\UDisplayConfig.cs` diretamente para `dist\UDisplayConfig.exe`.

### Opção 2: Visual Studio / VS Code / JetBrains Rider
Abra o arquivo **`UDisplayPro.sln`** no Visual Studio (ou Rider / VS Code) e compile normalmente (`Ctrl+Shift+B`).

### Como Reaplicar os Patches no Binário Original (`UDisplay.exe`)
Se você quiser alterar os patches de assembly x86 ou testar novos offsets a partir do executável de fábrica (`original\UDisplay_Original_v1.0.2.9.exe`), edite e execute o script:
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Patch-UDisplayBinary.ps1
```

---

## Como Instalar em Qualquer Computador

1. Copie o conteúdo da pasta **`dist/`** (`UDisplay.exe`, `UDisplayConfig.exe` e `Instalar_Em_Outro_PC.bat`).
2. Execute **`Instalar_Em_Outro_PC.bat`** (ou abra o `UDisplayConfig.exe`, vá na aba **3. Monitores, Diagnóstico e Sistema** e clique em **"Instalar Driver + App neste PC"**).

---

## Licença

Distribuído sob a licença **MIT**. Sinta-se livre para estudar, modificar e compartilhar!
