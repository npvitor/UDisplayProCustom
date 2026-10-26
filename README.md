# UDisplay Pro — calibração de cores com recuperação

Painel WPF para o adaptador usado com **UDisplay 1.0.2.9**. Mantém o tema escuro e as três abas do painel original do projeto, com aplicação segura de cores, nitidez e suavização. Preserva codificação/USB e oferece um ajuste experimental do alvo de captura.

## Correção do monitor apagado

A revisão de 23/10/2026 encontrou patches que escreviam em instruções erradas. O antigo ajuste de bitrate e os ajustes de threads corrompiam código x86. O painel também encerrava a captura antes de validar a atualização e escondia erros ao reiniciar.

Nesta máquina, restaurar o original recuperou o terceiro monitor. Em seguida, o perfil corrigido **BT.601 limitado, ajustes neutros** foi aplicado e o usuário confirmou cores e contraste mais naturais.

O fluxo novo:

1. Confere o SHA-256 do original e compila os dois shaders antes de interromper a captura.
2. Cria uma seção PE somente de leitura para os shaders, sem sobrescrever recursos vizinhos.
3. Prepara e verifica o candidato e faz backup do executável e das configurações.
4. Substitui o executável de forma atômica e verifica que a captura `main` continua executando.
5. Mostra uma confirmação no monitor principal. Sem confirmação em 25 segundos, ou em caso de falha, restaura o backup e reinicia a captura anterior.

O atualizador roda separado do painel. Uma transação interrompida deixa um registro de recuperação, que é processado na próxima abertura do painel. Não é um serviço de vigilância: se o próprio atualizador for encerrado à força ou faltar energia, a recuperação ocorre na próxima abertura.

## Usar nesta máquina

Abra `C:\ProgramData\UDisplay\UDisplayConfig.exe`, ou `dist\UDisplayConfig.exe` no repositório. Mantenha `UDisplay.Original.exe` ao lado do painel.

**Bandeja perto do relógio:** o clique esquerdo no ícone do UDisplay abre este painel. Se já estiver aberto, a janela existente é restaurada. Fechar o painel encerra seu processo; a captura continua. O botão direito mantém o menu original. Se o painel estiver ausente ou não puder iniciar, o clique mantém o comportamento original.

`dist\Instalar.bat` instala o painel e integra a bandeja em uma instalação reconhecida do UDisplay, com elevação e backup. Pode reiniciar a captura brevemente, mas não altera os ajustes de imagem. Reexecutar com a mesma versão não reinicia a captura. O driver original precisa estar instalado primeiro.

- **Perfil Natural:** BT.601 limitado, brilho 0, contraste e saturação 100%, gamma 1, sem ganho de temperatura.
- **Texto Nítido:** nitidez moderada de 25%, sem alterar contraste ou saturação.
- **Vivo e Nítido:** vivacidade 18% e nitidez 20%, mantendo contraste neutro.
- **Bordas Suaves:** suavização seletiva 30%, vivacidade 10% e nitidez desligada. Experimente este perfil quando o problema for serrilhado. Pode suavizar detalhes muito pequenos; compare com Natural.
- **Aplicar e Atualizar Monitor:** testa os ajustes com confirmação e recuperação. O Windows pode solicitar elevação para atualizar a instalação em ProgramData.
- **Monitores, Diagnóstico e Sistema → Abrir Padrão de Teste:** mova a janela ao terceiro monitor e compare os degraus próximos do preto e do branco, a escala de cinza e as cores. F11 alterna tela cheia.
- **Recuperar Original de Fábrica:** repõe o aplicativo original verificado, mantendo o driver instalado.

BT.709 limitado e BT.709 completo continuam disponíveis para comparação. A matriz/faixa precisa corresponder ao decodificador e à saída HDMI; mudar só o shader não altera os metadados H.264. O perfil validado visualmente nesta máquina não é uma calibração instrumental. Temperaturas são ganhos RGB aproximados, sem promessa de Kelvin medido. Gamma acima de 1 clareia os meios-tons.

## Desempenho e limites

Os patches antigos de bitrate, threads, OTA e bandeja foram retirados. O padrão continua sendo o alvo original de 30 FPS. A aba Captura e Desempenho oferece **45/60 FPS experimentais**, alterando somente o imediato verificado em `0x0B0F2A`. O alvo solicitado não mede a taxa realmente entregue; pode aumentar carga e reduzir os bits disponíveis por quadro. Essas opções não foram validadas visualmente no monitor do usuário. Não há promessa de latência zero ou bloqueio de telemetria.

Filtros espaciais desligados não adicionam amostras ao plano Y. Ao ligar nitidez e/ou suavização, o plano Y usa cinco amostras compartilhadas. O plano UV (crominância NV12, metade da resolução) sempre faz a média do bloco 2×2 de origem que cada amostra de cor cobre, em vez de ler um único pixel. Com um sampler "point", a leitura única descartava três de cada quatro pixels de cor e serrilhava bordas coloridas. As quatro leituras ficam nos centros dos texels, então o resultado não depende do filtro do sampler do motor. Nitidez e suavização continuam sem atuar no UV. A vivacidade aumenta menos cores já saturadas e limita o ganho conjunto dos canais para preservar a tonalidade. O painel compila a pré-validação em segundo plano e tem manifesto de DPI por monitor; isso melhora o comportamento do próprio painel, não altera a escala de outros aplicativos.

Na consulta local, o monitor USB (`Sage Display Manager`, `@SG0000`) estava em **1920×1080, 60 Hz**, igual ao modo preferido anunciado. A qualidade original estava em `quality=1`. Isso não mede a resolução interna do vídeo comprimido nem comprova uma causa única do serrilhado. Mais nitidez não é a solução para toda borda irregular.

A compressão e a subamostragem de crominância do caminho NV12/H.264 continuam limitando texto colorido e a equivalência com HDMI direto. Não foram modificados firmware nem driver do dispositivo.

## Compilar e verificar

Windows com .NET Framework 4.8 e `d3dcompiler_47.dll`:

```powershell
.\Build.bat
.\tools\Test.ps1
```

`Build.bat` compila o painel, copia o original verificado para `dist\UDisplay.Original.exe` e constrói `dist\UDisplay.exe`. Não modifica a instalação em uso. Os testes cobrem 84 combinações de shaders, 944 verificações de integridade/parâmetros e exportam os shaders para `obj/tests`. `ChromaGpuTests` executa seis shaders UV em Direct3D 11 WARP (`--hardware` para a GPU local), com samplers linear e point, em alvo de resolução cheia e de meia resolução: 7.360 verificações de que cada amostra de meia resolução é a média do bloco 2×2. Também executam o código x86 da bandeja em três endereços de carga, com 14.137 verificações de caminhos Unicode, limites, retorno ao comportamento original e liberação de handles.

Testes de renderização opcionais, usando Python no Windows (apenas biblioteca padrão):

```powershell
python .\tests\shader_gpu_test.py
python .\tests\shader_gpu_test.py --hardware
python .\tools\Inspect-DisplayModes.py
```

Foram renderizados 12 shaders tanto em WARP quanto na GPU local, com 7.584 verificações de pixels em cada execução. Os testes verificam faixa, cinzas, preservação de tonalidade, limite de nitidez e suavização de diagonais sem borrar traços retos nos padrões usados. Não substituem a avaliação visual após compressão e decodificação no adaptador.

Para construir outra configuração sem instalar:

```powershell
.\tools\Patch-UDisplayBinary.ps1 -Settings .\meu-perfil.ini -OutputExe .\obj\candidato.exe
```

O projeto do Visual Studio compila o painel; `Build.bat` também prepara os binários distribuíveis. A auditoria opcional `tools\audit_binary.py` requer Python, `pefile` e `capstone` e usa `D3DDisassemble` do Windows; apenas lê binários e grava evidências em `obj\audit`.

## Recuperação e diagnóstico

**Antivírus:** em 26/10/2026 o Surfshark Antivirus pôs em quarentena o motor modificado (`Drop.Win32.MemChangeSelf`), cerca de 1,5 min após o boot. A chave `Run` ficou sem executável e o terceiro monitor não iniciou. Também foram para a quarentena a cópia do backup e `dist\UDisplayConfig.exe` durante uma aplicação. O original de fábrica não foi marcado. Consulte `C:\ProgramData\Surfshark\antivirusLog.log`. Se o backup de uma transação não tiver mais `UDisplay.exe`, a recuperação repõe o original verificado em vez de falhar.

Instalação: `%ProgramData%\UDisplay`. Backups: subpastas `backup-<id>`. Log: `UDisplayPro-update.log`. Registro de transação: `UDisplayPro.pending`. Não apague backups enquanto houver uma transação pendente.

Comandos do painel (executar elevado quando necessário):

```powershell
# Recuperar somente uma transação interrompida
.\dist\UDisplayConfig.exe --recover
# Repor o original de fábrica
.\dist\UDisplayConfig.exe --restore
```

`--test-rollback <ini>` é um teste de integração: aplica temporariamente, simula uma falha após iniciar a captura e restaura o backup. Interrompe brevemente o monitor; não é necessário no uso normal. Código de saída 0 = concluído; 2 = revertido; 1 = falha. Ao automatizar, use `Process.WaitForExit()`; `Start-Process -Wait` também espera os processos de captura descendentes, que permanecem abertos.

Para outro computador, instale primeiro o driver pelo instalador original do adaptador e execute `Instalar.bat`, mantendo os arquivos de `dist` juntos. A integração aceita a versão original verificada ou o motor gerado pelo projeto que corresponde aos ajustes salvos. Uma instalação desconhecida é recusada antes de interromper a captura.

O teste opcional `tools\Test-TrayInstalled.ps1` abre o painel pela mensagem real da bandeja, verifica a reutilização e fecha a janela criada pelo teste. Execute com o painel fechado e elevado se a captura estiver elevada. Não reinicia a captura nem aplica perfis.

Veja [a auditoria técnica](docs/REVERSE_ENGINEERING.md) para evidências, offsets e limitações.
