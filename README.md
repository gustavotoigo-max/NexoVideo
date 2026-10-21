# NexoVideo

Aplicativo Windows para analisar vídeos em lote e gerar candidatos separados de recuperação.
**Estado: 0.1.0-alpha.1, pré-release de teste.** Não é beta homologada nem garantia de recuperação.

Para outra máquina Windows x64, use o [release](https://github.com/gustavotoigo-max/NexoVideo/releases/tag/v0.1.0-alpha.1):
instalador ou ZIP com .NET incluído. [Instalação e primeiro uso](docs/USAR_RELEASE.md).

## Usar a versão de desenvolvimento

Neste ambiente, abra `artifacts/dev/NexoVideo.exe`. Precisa do .NET Desktop Runtime 10 x64.
As ferramentas da pesquisa já extraídas em `.research/tools/` são localizadas automaticamente no workspace.
Em outra pasta/máquina, configure os executáveis FFprobe/FFmpeg e, opcionalmente, Untrunc.
Eles não são incluídos no pacote do aplicativo; redistribuição depende da revisão de licenças.

1. Em **Configurações**, confira ferramentas, amostragem, timeout e limite de memória.
2. Em **Análise**, escolha a pasta, formatos e um dos três modos; clique em **Iniciar análise**.
3. A fila processa um vídeo por vez. **Pausar** conclui o atual; **Cancelar** interrompe subprocessos.
4. Use **Retomar fila** para continuar a sessão persistida. Itens concluídos e inalterados em tamanho/data são reutilizados.
5. Selecione um resultado e abra **Recuperação** para ver o diagnóstico e gerar candidato por remux ou Untrunc.
6. Escolha uma pasta separada para candidatos. Untrunc exige referência compatível e opera sobre cópias temporárias.
7. Em **Relatórios**, registre sua avaliação manual e exporte o JSON completo da sessão para um nome novo.
   O coletor compara sua avaliação com os alertas e inclui hashes, intervalos e logs: [como validar](docs/VALIDACAO_MANUAL.md).

Os dados ficam em `%LOCALAPPDATA%\NexoVideo`: `nexovideo.db`, configurações e logs.
`--data PASTA` permite escolher outro local. O banco deve ficar em armazenamento local saudável.
Resultados por teste ficam no JSON persistido de cada arquivo; a lista da interface consulta páginas de 100 registros.

**Concluído** é estado da fila. Leia o diagnóstico: estrutura identificada, amostragem ou decodificação completa
sem erros observados são evidências diferentes. Ausência legítima de áudio não é classificada como corrupção.
Qualquer aviso de decodificação é apresentado; retorno zero da ferramenta não basta para declarar integridade.

## Formatos para os primeiros testes

Prioridade: **MP4, MOV, AVI, MKV, TS, WEBM, MPG, FLV e WMV**.
Os pares de codecs já exercitados e como organizar amostras estão em [preparar vídeos](docs/PREPARAR_VIDEOS.md).
Outras extensões aparecem com `*` e tooltip de análise experimental; não representam suporte homologado.

## Compilar e verificar

Requer Windows e SDK .NET 10.0.401; dependências fixadas em `packages.lock.json`.

```powershell
dotnet restore NexoVideo.sln --locked-mode
dotnet build NexoVideo.sln --no-restore
dotnet test tests\NexoVideo.Tests\NexoVideo.Tests.csproj --no-restore
dotnet publish src\NexoVideo.App\NexoVideo.App.csproj -c Release --no-restore --no-self-contained -o artifacts\dev
```

No ambiente desta tarefa, o NuGet via .NET falhou no TLS. Foi usado um feed local preparado por Python,
com HTTPS/certificados habilitados e pacotes do serviço oficial:

```powershell
python scripts\preparar_feed_nuget.py
dotnet restore NexoVideo.sln --source .research\nuget-feed --locked-mode -p:NuGetAudit=false
```

O segundo comando é uma alternativa para o ambiente com TLS indisponível, sem auditoria online de vulnerabilidades.
Não é configuração global do NuGet. Os testes de integração exigem as ferramentas e mídias sintéticas descritas
em [experimentos](docs/pesquisa/EXPERIMENTOS.md); quando não disponíveis, esses testes são explicitamente ignorados.

## Linha de comando

```powershell
dotnet run --project src\NexoVideo.Cli -- analisar --source C:\VideosTeste --ffprobe C:\Ferramentas\ffprobe.exe --ffmpeg C:\Ferramentas\ffmpeg.exe --mode Sampled --data C:\NexoVideoDados
```

Modos: `Quick`, `Sampled`, `Full`. Acrescente `--extensions .mp4,.mov` para filtrar ou `--resume` para retomar.
Use `--help` para opções. A linha de comando compartilha o bloqueio de análise com a interface.

## Limites desta entrega

- Homologação de arquivos reais, acervo grande e uso prolongado ainda pendentes.
- Reparo é candidato experimental; não há substituição/exclusão, enxerto ou verificação automática suficiente de sincronia.
- Retomada reutiliza tamanho/data; cada nova análise coleta SHA-256 e a avaliação manual reconfere os bytes. Novos arquivos exigem nova sessão.
- Logs têm limite por subprocesso, mas ainda não há rotação/retenção global de histórico em disco.
- Memória da árvore de ferramentas é limitada por Windows Job Object; não há teto validado para todo o aplicativo.
- ETA, comparação detalhada original/candidato, perfis de mídia instável e ferramentas adicionais permanecem pendentes.
- Interface adaptada ao Figma fornecido, com tema escuro NexoDesk e Inter incluída; detalhes em [design](docs/DESIGN.md).
- Pacote alpha para Windows 10 22H2/11 x64; outras versões/arquiteturas e atualização automática seguem pendentes.
- Elevação contínua e licença final do produto seguem pendentes; o instalador atual funciona por usuário.

Mais detalhes: [arquitetura e estado](docs/DESENVOLVIMENTO.md), [índice](docs/INDEX.md).
