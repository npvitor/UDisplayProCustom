# Documentação de Engenharia Reversa — UDisplay Pro (Lemorele LC06 Plus)

Este documento detalha toda a arquitetura interna descoberta por engenharia reversa do software `UDisplay.exe` (versão de fábrica `1.0.2.9`, 32-bit x86 PE, `ImageBase = 0x00400000`) que acompanha o adaptador USB-C para HDMI **Lemorele LC06 Plus**.

---

## 1. Arquitetura de Hardware e Driver

1. **Dispositivo Composto USB:**
   - Quando conectado à porta USB-C/USB-A, o adaptador expõe:
     - Um drive de CD/Flash virtual (`D:\`) contendo o instalador original.
     - Uma interface USB Bulk de alta velocidade (`VID_185B&PID_2D1D` / `VID_1D6B` / `VID_34C7`).
2. **Driver de Monitor Virtual (`SG_DISP_BUS`):**
   - O `UDisplay.exe` instala um driver de barramento de vídeo virtual no Windows (`C:\Windows\System32\drivers\SG_DISP_BUS.sys` / IDD) que faz o Windows reconhecer um monitor real adicional conectado ao sistema.
3. **Pipeline de Vídeo:**
   - **Captura de Tela:** Usa a API **Direct3D 11 Desktop Duplication** (`IDXGIOutputDuplication`) para capturar a textura RGBA da área de trabalho do monitor virtual na GPU.
   - **Conversão RGB $\rightarrow$ NV12 na GPU:** Executa dois Pixel Shaders Direct3D 11 (`ps_5_0`) compilados dentro do executável:
     - **Shader 1 (Plano Y / Luminância):** Renderiza em uma textura `R8_UNORM` com resolução total ($1920 \times 1080$).
     - **Shader 2 (Plano UV / Crominância):** Renderiza em uma textura `R8G8_UNORM` com metade da resolução ($960 \times 540$).
   - **Codificação H.264:** O quadro `NV12` é lido via `Map` em textura de staging e enviado ao codificador H.264 (via bibliotecas estáticas do FFmpeg/`libx264` ou hardware QSV/NVENC).
   - **Transmissão USB Bulk:** Os pacotes `NAL` H.264 resultantes são encapsulados com um cabeçalho de 16 bytes e enviados via transferência USB Bulk para o SoC do adaptador, que decodifica o H.264 em hardware e gera o sinal HDMI.

---

## 2. Causas Raiz dos Problemas Originais de Fábrica

### 2.1. Imagem com Contraste Estourado e Saturação Artificial
Ao extrair e descompilar os dois blobs `DXBC` embutidos na seção `.rdata` do `UDisplay.exe` (`0x4E8FF0` e `0x4E8CE0`), descobrimos que os programadores originais colocaram multiplicadores fixos no código HLSL:
- **No Shader Y (`0x4E8FF0`):** Aplicava um ganho de contraste `(rgb - 0.5) * 1.15 + 0.5` antes da conversão de luminância, além de coeficientes incorretos que causavam *black crush* (tons escuros virando preto puro) e *white clipping* (tons claros estourando).
- **No Shader UV (`0x4E8CE0`):** Multiplicava os canais de cor por `1.28` com matriz `BT.601`, deixando cores vermelhas e azuis neon e borrando textos coloridos.

### 2.2. Travamento em 30 FPS
Na rotina `CDisplayDev::InitEncoder` (`File Offset 0x0B0F2A`), o framerate alvo era gravado de forma fixa como `30` (`C7 45 E4 1E 00 00 00` -> `mov dword ptr [ebp-1Ch], 30`).

### 2.3. Atraso na Resposta do Mouse (Hover e Clique em Botões)
Três fatores combinados causavam uma latência perceptível (~150–500 ms) ao passar o mouse sobre botões:
1. **Codificador GPU Hardware (`QSV`/`NVENC`) com fila de reordenação (`async_depth`):** O salto condicional em `0x15EC85` (`75 30` / `jnz`) priorizava o encoder de hardware que retinha 2 a 3 quadros no buffer antes de emitir o bitstream. Ao substituir por `EB 30` (`jmp`), forçamos o **`libx264` em perfil Constrained Baseline (`ultrafast` + `tune zerolatency`)**, que codifica e envia cada quadro imediatamente na mesma chamada.
2. **Atraso de Redesenho em Tela Semi-Estática (`500 ms`):** No loop de captura (`0x15F7A5`), quando apenas o cursor ou o hover de um botão mudava, o intervalo de reenvio estático era de `500 ms` (`F4 01 00 00`). Reduzimos para `80 ms` (`50 00 00 00`).
3. **Single-Thread no x264:** O codificador x264 era inicializado com apenas `1` thread (`0x189316` e `0x189323`). Alteramos para `4` threads em paralelo.

---

## 3. Mapa Completo de Offsets Binários (`UDisplay.exe` v1.0.2.9)

| Endereço (File Offset) | Virtual Address (VA) | Valor Original | Valor UDisplay Pro | Descrição Técnica |
| :--- | :--- | :--- | :--- | :--- |
| `0x4E8FF0` (704 bytes) | `0x008E9BF0` | Blob DXBC c/ contraste 1.15x | Blob DXBC BT.709 Studio Swing | Pixel Shader `ps_5_0` de conversão RGB $\rightarrow$ Y |
| `0x179125` (4 bytes) | `0x00579D25` | `C0 02 00 00` (704) | Tamanho do novo blob Y | `push <ShaderY_Size>` em `CreatePixelShader` |
| `0x4E8CE0` (784 bytes) | `0x008E98E0` | Blob DXBC c/ saturação 1.28x | Blob DXBC BT.709 Studio Swing | Pixel Shader `ps_5_0` de conversão RGB $\rightarrow$ UV |
| `0x1791A0` (4 bytes) | `0x00579DA0` | `10 03 00 00` (784) | Tamanho do novo blob UV | `push <ShaderUV_Size>` em `CreatePixelShader` |
| `0x0B0F2A` (4 bytes) | `0x004B1B2A` | `1E 00 00 00` (30 FPS) | `3C 00 00 00` (60 FPS) | Framerate alvo do loop de captura e encoder |
| `0x0B0C4D` (4 bytes) | `0x004B184D` | `80 96 98 00` (10 Mbps) | `00 36 6E 01` (24 Mbps) | Bitrate H.264 (`bit_rate` em bps) |
| `0x15EC85` (1 byte) | `0x0055F885` | `0x75` (`JNZ`) | `0xEB` (`JMP`) | Seleciona encoder CPU `libx264` Zero-Latency |
| `0x15F698` (1 byte) | `0x00560298` | `0x09` (9 frames) | `0x20` (32 frames) | Intervalo de GOP / Keyframe (`gop_size`) |
| `0x15F714` (1 byte) | `0x00560314` | `0x01` (1 ms sleep) | `0x00` (0 ms sleep) | Remove atraso extra no loop de captura |
| `0x15F7A5` (4 bytes) | `0x005603A5` | `F4 01 00 00` (500 ms) | `50 00 00 00` (80 ms) | Intervalo de refresh rápido para hover de mouse |
| `0x176A2E` (4 bytes) | `0x0057762E` | `F4 01 00 00` (500 px) | `C0 03 00 00` (960 px) | Limiar de downscale da textura UV |
| `0x189316` (4 bytes) | `0x00589F16` | `01 00 00 00` (1 thread) | `04 00 00 00` (4 threads) | `AVCodecContext->thread_count` |
| `0x189323` (4 bytes) | `0x00589F23` | `01 00 00 00` (1 thread) | `04 00 00 00` (4 threads) | Threads de slice do `libx264` |
| `0x0A3BB5` (1 byte) | `0x004A47B5` | `0x74` (`JZ`) | `0xEB` (`JMP`) | Desativa criação da thread de OTA a cada 5s |
| `0x4ADF2C` (1 byte) | `0x008AEB2C` | `'h'` (`http://...`) | `0x00` (`""`) | Bloqueia URL de servidor OTA/Telemetria |
| `0x07F3D0`, `0x09A5A0`, `0x0A4C10` | `0x0047FFD0`, `0x0049B1A0`, `0x004A5810` | Handlers da UI antiga | Stub `ShellExecuteA` (22B) | Redireciona clique na bandeja e `ShowMainDlg` para `UDisplayConfig.exe` |
| `0x09A514` (10 bytes) | `0x0049B114` | `PostMessage(0x8023)` | `call 0x09A5A0; jmp` | Clique esquerdo no ícone da bandeja abre `UDisplayConfig.exe` |
| `0x0A4DEB`, `0x0A4DF2`, `0x0A4E27` | `0x004A59EB` | `mov [esi+1Ch], 1` | `mov [esi+1Ch], 0` + `jmp` | Impede que `CUIInterface::CheckCmd` abra a janela antiga |
