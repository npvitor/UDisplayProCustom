# Auditoria da engenharia reversa — 23/10/2026

Esta revisão substitui as conclusões anteriores. Os valores abaixo foram lidos do executável presente no repositório e conferidos com disassembly x86 e `D3DDisassemble`. Não se deve reaproveitar offsets em outros executáveis, mesmo que tenham o mesmo número de versão.

## Identidade da base

- Arquivo: `original/UDisplay_Original_v1.0.2.9.exe`.
- Tamanho: 7.759.136 bytes.
- SHA-256: `C0AD5096F99B7C88F31C791C818FE5615F1300A1B63087995B4FF6B91CCABEB9`.
- PE32 x86; ImageBase `0x00400000`.
- O backup instalado `UDisplay.exe.bak` tinha exatamente o mesmo hash.
- A cópia instalada antes da recuperação tinha SHA-256 `80FB56C82694376EBD11CE5F9A6FF4B112A26798B7B5C626D8803F2B752596F2` e foi preservada em `obj/audit/installed-before-recovery.exe`.

Mapeamento de seções do original:

| Seção | File offset | RVA | Raw size |
|---|---:|---:|---:|
| .text | 0x400 | 0x1000 | 0x46D200 |
| .rdata | 0x46D600 | 0x46F000 | 0xC6000 |
| .data | 0x533600 | 0x535000 | 0xAE00 |
| .rsrc | 0x53E400 | 0x641000 | 0x1EA400 |
| .reloc | 0x728800 | 0x82C000 | 0x3AE00 |

A conversão file offset → VA depende da seção. A documentação antiga somava `0xC00` também aos dados; para `.rdata` o deslocamento é `0x1A00`.

## Causa do apagamento

O daemon permanecia aberto, mas a captura `main` estava ausente e o Windows listava apenas dois monitores. Restaurar a base verificada e iniciar `daemon`/`main` fez reaparecer `DISPLAY\@SG0000` como terceiro monitor. O usuário confirmou a volta da imagem.

Falhas nos patches anteriores:

| Offset usado | Evidência no original | Problema |
|---|---|---|
| `0x0B0C4D` | Parte de `83 7A 38 00` (`cmp dword ptr [edx+38h],0`) iniciada em `0x0B0C4C` | Gravar 4 bytes de bitrate altera a instrução e o salto seguinte; não é um campo de bitrate. |
| `0x189316` | Dentro de `C7 81 8C 01 00 00 04 00 00 00` | Sobrescreve ModR/M e deslocamento. O imediato 4 só começa em `0x18931B`. Não foi comprovado que esse campo seja threads. |
| `0x189323` | Dentro de `C7 82 6C 01 00 00 04 00 00 00` | Mesmo tipo de corrupção; imediato em `0x189328`, já igual a 4. |
| `0x0A3BB5` | `3B F7` (`cmp esi,edi`) | Substituir `3B` por `EB` cria um salto; não era uma condição de OTA. |
| `0x176A2E` | Imediato 1000 em `mov eax,1000`, seguido de divisão e cálculo de espera | É parte de temporização, não um limiar de resolução 500→960. |
| `0x15F698` | Imediato 100 em `push 64h` | Não sustenta a interpretação anterior de GOP 9. |
| `0x15F714` | Imediato 2 de `cmp eax,2` | Não é um `sleep(1)`. |
| `0x15F7A5` | Imediato 3000 de `cmp eax,0BB8h` | Não corresponde ao alegado intervalo original de 500 ms. |
| `0x15EC85` | `75 48` | O salto existe, mas sua mudança não prova x264 sem latência; retirado. |
| `0x0B0F2A` | Imediato 30 em `mov [ebp-164h],30` | É um alvo configurado, não medição de FPS; preservado. |

Os stubs de bandeja, URL e demais alterações antigas também foram retirados. Suas interpretações não estavam demonstradas, e alguns endereços de dados usavam o mapeamento de seção incorreto. A implementação nova reconstrói sempre a partir do original: não tenta corrigir um executável já corrompido.

O painel antigo ainda tinha problemas independentes: ausência de limites dos blobs, gravação direta sem backup, encerramento de captura antes de validar permissões/saída, erros ignorados na inicialização e no salvamento, substituição completa de `GlobalConfig.ini` e mensagens de sucesso sem checar captura. Essas rotinas foram substituídas.

## Evidência dos shaders originais

Shader Y: file offset `0x4E8FF0`, 704 bytes, VA `0x008EA9F0`.

```text
sample ... r0.xyz ... t0 ... s0
dp3 o0.x, r0.xyzx, l(0.299000, 0.587000, 0.114000, 0)
ret
```

Shader UV: file offset `0x4E8CE0`, 784 bytes, VA `0x008EA6E0`.

```text
dp3 r0.w, r0.xyzx, l(-0.147130, -0.288860, 0.436000, 0)
dp3 r0.x, r0.xyzx, l(0.615000, -0.514990, -0.100010, 0)
add o0.xy, r0.wxww, l(0.5, 0.5, 0, 0)
```

Não há os supostos ganhos `1.15x` e `1.28x`. Y usa coeficientes de luma BT.601 sem escala limitada; UV usa coeficientes YUV analógicos, diferentes dos coeficientes Cb/Cr digitais. Isso sustenta revisar a conversão, mas não prova sozinho como o decoder interpreta faixa e matriz.

## Conversão revisada

`ShaderFactory` em `src/DisplayEngine.cs` é a fonte utilizada no build e no painel. As referências HLSL em `shaders/` mostram BT.709 neutro; não há outro patcher independente.

Para RGB normalizado, `Y'=Kr R'+Kg G'+Kb B'`; `Cb=(B'-Y')/[2(1-Kb)]`; `Cr=(R'-Y')/[2(1-Kr)]`.

- BT.601: `Kr=.299`, `Kb=.114`; BT.709: `Kr=.2126`, `Kb=.0722`.
- Limitado: `Y=16/255+(219/255)Y'`; `UV=128/255+(224/255)(Cb,Cr)`, limitado a 16..240.
- Completo: Y em 0..1 e UV centrado em 128/255, limitado a 0..1.
- Ambos os planos recebem a mesma transformação RGB: gamma `pow(rgb,1/gamma)`, ganhos RGB, contraste/brilho, recorte, saturação em torno da luma e recorte final.
- Gamma não levanta artificialmente o preto por usar um piso `0.0001`.
- O modo neutro não impõe contraste, saturação ou temperatura adicionais.

O padrão inicial BT.601 limitado mantém a família de luma original e troca a representação digital/faixa. O usuário confirmou melhora visual nesta máquina. BT.709/full continuam experimentais: os metadados VUI e a interpretação do decoder/HDMI não foram medidos ou alterados. Não há promessa de Delta E, temperatura física ou calibração ICC.

## Injeção e recuperação

A seção `.udcolor` é somente de leitura, com alinhamentos PE respeitados. O original inteiro é preservado, exceto campos de cabeçalho necessários, quatro operandos de criação dos shaders e o alvo opcional de captura:

| Operando | Finalidade |
|---|---|
| `0x179125` | Tamanho do shader Y |
| `0x17912A` | Ponteiro do shader Y |
| `0x1791A0` | Tamanho do shader UV |
| `0x1791A5` | Ponteiro do shader UV |
| `0x0B0F2A` | Alvo de captura, 30 por padrão; 45/60 experimentais |

Os ponteiros já têm relocations `HIGHLOW` no original, conferidas na auditoria. Atualizar seus valores sem mudar a localização preserva os ajustes de ASLR. A tabela Security é desreferenciada, pois a assinatura de fábrica não autentica bytes modificados; os bytes originais do certificado/overlay são preservados. Não há alteração do driver.

O worker elevado prepara candidato e backup antes de parar processos, substitui via `File.Replace`, verifica `main` e pede confirmação no monitor principal por 25 segundos. Cancelar, fechar ou deixar expirar reverte. Configurações só são persistidas após confirmação. `GlobalConfig.ini` e configurações por dispositivo não são sobrescritas.

O journal registra uma atualização pendente para recuperação na próxima abertura. Não existe watchdog independente caso o próprio worker seja morto. Restaurar o original usa o hash fixado, sem shaders modificados.

## Validação da recuperação inicial

- Build com o compilador .NET Framework instalado.
- 36 combinações de matriz/temperatura e extremos dos controles; 257 verificações.
- Maior shader observado: 852 bytes, demonstrando por que os slots antigos eram insuficientes.
- Verificação byte a byte do código original, permitindo apenas os quatro operandos documentados; dados/recursos originais preservados.
- DXBC, alinhamentos, ponteiros, seção somente de leitura e Security directory conferidos.
- Assinaturas de entrada/saída dos shaders verificadas por disassembly; Y mantém saída escalar, UV mantém dois componentes.
- Três páginas do painel renderizadas e inspecionadas em `obj/screenshots`.
- Aplicação no hardware real: captura permaneceu aberta; usuário confirmou imagem melhor, com cores e contraste naturais.
- Teste de falha após iniciar captura (`--test-rollback`): saída 2, hashes do executável e INI restaurados exatamente, journal removido, `daemon`/`main` ativos e três monitores com status OK. A primeira aplicação foi confirmada pelo usuário antes do prazo; o vencimento do temporizador não foi observado no teste real.

Ferramentas: `tools/Test.ps1`, `tests/EngineTests.cs`, `tools/audit_binary.py`. Resultados locais em `obj/` não são versionados. A auditoria de offsets usa `capstone`/`pefile`; o teste principal usa somente .NET/Windows.

Referências oficiais: [formato PE e tabela de certificados](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format), [D3DDisassemble](https://learn.microsoft.com/en-us/windows/win32/api/d3dcompiler/nf-d3dcompiler-d3ddisassemble).

## Revisão de nitidez, serrilhado e fluidez

O usuário pediu mais definição/cores vivas e depois relatou serrilhado. A investigação adicional e os filtros abaixo foram preparados após a recuperação inicial. Nessa etapa, o usuário saiu antes de comparar os perfis; por isso, o motor e o INI aprovados foram mantidos na instalação ativa, e somente o painel foi atualizado. A integração posterior da bandeja, descrita abaixo, preservou os mesmos shaders e o INI.

### Constatações

- O Windows enumerou `Sage Display Manager / @SG0000` em 1920×1080 a 60 Hz, 32 bpp; o modo preferido WMI também é 1920×1080 a 60 Hz. Isso descreve o desktop/sinal anunciado, não FPS da captura nem a amostragem interna do codec.
- `quality=1` já estava ativo. No caminho do controlador, `quality=0` executa `shr eax,1` no argumento em `[ebp+14h]` (`0xB0C52..0xB0C57`); esse corte não ocorre com quality=1. Não foi inventado outro valor de qualidade.
- Os 30 em `0xB0F2A` são um imediato de quatro bytes de `mov [ebp-164h],30`. Seu endereço é passado em `0xB0F59..0xB0F5F` à construção da solicitação do controlador, seguida de envio ao módulo de captura. A modificação é limitada a 30/45/60, com hash da base obrigatório. O teste estático comprova onde o alvo é gravado; não comprova que 60 quadros chegam ao HDMI.
- O literal 10.000.000 encontrado em `0x10424B` não foi assumido como bitrate sem rastreamento semântico. Bitrate, threads e seleção de encoder foram preservados.
- Não foi feita troca para H.264 4:4:4: exigiria comprovar suporte do encoder, transporte e decodificador. O pipeline existente reduz crominância, uma limitação especialmente relevante para texto colorido. Consulte a [documentação Microsoft sobre NV12/4:2:0](https://learn.microsoft.com/en-us/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering).

### Filtros implementados

- **Vivacidade (0–40):** ganho adicional proporcional a `1-(max(RGB)-min(RGB))`. Saturação e vivacidade escalam a mesma direção cromática em torno da luma. Um limite conjunto de gamut evita recorte independente dos canais, preservando a tonalidade e a luma dos campos uniformes.
- **Nitidez (0–50):** diferença entre a luma central e a média dos quatro vizinhos, com limiar para ruído fino, variação máxima de 4/255 antes da conversão e limite pelos extremos locais. Não modifica UV. O controle não cria resolução nem resolve perda de crominância.
- **Suavização de bordas (0–50):** detecta gradientes nos dois eixos e mistura uma fração da média local nas diagonais. Preserva bordas puramente horizontais/verticais nos testes. Não é FXAA completo, reconstrução de subpixels nem garantia de remoção de todos os serrilhados. Pode suavizar detalhes; fica desligada por padrão.
- Os filtros espaciais compartilham cinco amostras de Y. Quando ambos estão em zero, o código espacial não é emitido. Nitidez é atenuada nas bordas que estão sendo suavizadas.
- **Redução de crominância 2×2 (26/10/2026, sempre ativa):** o shader UV original e as versões anteriores liam uma única amostra por pixel do alvo UV de meia resolução. O filtro do sampler criado pelo motor não foi auditado; se for point, três de cada quatro pixels de cor eram descartados. O shader agora obtém a área de cobertura de `ddx/ddy` das coordenadas e lê quatro texels deslocados de ±0,5 texel, nos centros do bloco 2×2. Converte cada um com a mesma `transform()` e faz a média antes da matriz Cb/Cr. Com alvo de resolução cheia, o deslocamento é zero e o resultado equivale à leitura única anterior. Isso não altera o encoder, a subamostragem 4:2:0 nem os metadados H.264.
- Perfis completos evitam herdar ajustes de outro perfil. A matriz e o alvo de captura escolhidos pelo usuário são preservados ao alternar o perfil de imagem.
- Pré-validação no painel é assíncrona, antes da elevação/parada; monitoramento de status fica mais espaçado e não executa minimizado. O manifesto declara PerMonitorV2 para reduzir problemas de escala do próprio painel.

### Validação adicional

- 84 combinações de filtros/matriz/temperatura/limites, 770 verificações; maior shader de teste: 6.028 bytes, contido na seção nova.
- 12 shaders executados realmente em Direct3D11 WARP e na GPU local. Em cada backend, 7.584 verificações cobriram 729 cores, campos uniformes, pretos/brancos, matiz, faixa legal, bordas desfocadas, diagonais e traços retos.
- Redução de crominância: `tests/ChromaGpuTests.cs` (C#, sem Python), 7.360 verificações em WARP e na GPU local (Intel Iris Xe), com samplers linear e point. O padrão xadrez vermelho/azul comprova que a amostra de meia resolução não é mais um único pixel de origem. `shader_gpu_test.py` não foi reexecutado nessa revisão porque não havia Python instalado. A comparação visual no adaptador ainda não foi feita.
- Integridade de código conferida byte a byte: somente os cinco operandos permitidos podem mudar, sem incluir os opcodes `push` entre tamanho e ponteiro. Relocações e dados originais permanecem intactos.
- Nenhuma medição de FPS entregue, latência USB ou qualidade pós-decoder foi realizada nesta fase. Os perfis novos e 45/60 FPS aguardam comparação visual do usuário; não foram ativados durante sua ausência.

Ferramentas adicionais: `tools/analyze_pipeline.py` (somente leitura, requer pefile/capstone), `tools/Inspect-DisplayModes.py` (somente leitura, ctypes), `tests/shader_gpu_test.py` (D3D11; Python padrão). Evidências de disassembly em `obj/pipeline`.

## Integração da bandeja sob demanda

Implementada após autorização do usuário, com foco em baixo consumo. Não adiciona serviço, timer, ícone duplicado ou processo residente. O painel WPF só inicia quando solicitado e encerra ao fechar; seu custo enquanto aberto continua existindo. O motor recebe 512 bytes adicionais no arquivo (uma página de imagem para a nova seção).

### Evidências e alteração

- Em `0xA5E24..0xA5E32`, a inicialização de `NOTIFYICONDATA` usa id `0x80`, flags 7 e callback `0x464`. O owner HWND vem de `CTray+0x220`; o ponteiro global está em RVA `0x5B8CCC`.
- O handler em file offset `0x9A300` / RVA `0x9AF00` confere id `0x80` e despacha eventos `0x201..0x205`. `WM_LBUTTONUP` (`0x202`) originalmente vai a `0x9A447`; botão direito permanece no caminho original.
- Substituem-se somente os cinco bytes completos `55 8B EC 6A FF` da entrada por um `jmp rel32` para `.udtray`. A seção é executável e somente leitura (`0x60000020`); `.udcolor` continua somente leitura, sem permissão de execução.
- O novo handler intercepta apenas `id=0x80,event=0x202`. Usa `GetModuleFileNameW`, forma o caminho completo do irmão `UDisplayConfig.exe` e chama `CreateProcessW` com `lpApplicationName` explícito. Sem shell, PATH ou concatenação de comandos. Espaços e Unicode são preservados. Os handles do processo/thread filho são fechados imediatamente.
- Os RVAs de IAT verificados são `0x46F548` (GetModuleFileNameW), `0x46F3B4` (CreateProcessW) e `0x46F5E0` (CloseHandle). O stub obtém a base real com call/pop; não contém ponteiros absolutos nem exige novas relocações. A relocação original em `0x9AF06` não é tocada.
- Falha ao localizar/iniciar o painel ou caminhos além da capacidade do buffer restauram registradores e executam o prólogo original, retornando à função em RVA `0x9AF05`. Outros ids/eventos seguem esse mesmo gateway sem chamar APIs extras.

Referências: [CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw) e [eventos da área de notificação](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area).

### Instalação e validação

`--install` solicita elevação para `--install-tray`. Este confere a correspondência exata entre motor e ajustes salvos (ou o original conhecido sem INI), instala painel/original de referência, prepara backup/journal e reinicia a captura. Como não altera a imagem, não exige confirmação visual de perfil; falha na partida aciona recuperação. O INI não é regravado. A operação é idempotente. Restaurar o original de fábrica também restaura a bandeja original; reaplicar um perfil ou instalar novamente integra o painel outra vez.

- 84 builds de shaders e 938 verificações de integridade/parâmetros. A lista de exceções na comparação de código inclui agora os cinco bytes do hook, além dos cinco operandos anteriores; todo o restante dos dados/código originais continua idêntico.
- `tests/TrayTests.cs`: execução nativa x86 do stub em três bases diferentes, APIs simuladas, 14.137 verificações. Cobre Unicode/espaços, eventos não interceptados, ausência do painel, limites exatos do buffer, falhas de API, 600 cliques repetidos e fechamento dos handles. Não injeta código no processo instalado.
- Comparação do motor anterior com o novo: zero diferenças fora dos cabeçalhos PE e dos cinco bytes do hook em toda a extensão anterior. Shaders aprovados preservados byte a byte.
- Instalação real: motor `8F7E07A26A22B182C828E98CC5035033ADAD9B3CED626D8D245A496A75F35CB1`, INI com hash inalterado, journal removido e `daemon/main` ativos. Windows continuou enumerando três monitores, incluindo Sage 1920×1080 a 60 Hz.
- `tools/Test-TrayInstalled.ps1`: mensagem real enviada ao HWND obtido do objeto CTray, painel instalado aberto, segundo clique reutilizou o mesmo PID. Fechar o painel deixou zero processos UDisplayConfig; os PIDs da captura não mudaram durante o teste. É um teste do callback, não um clique físico no Explorer.
