# Decisões e estado

## Confirmado e adotado

- Usuário autorizou iniciar o desenvolvimento após a pesquisa. Nome: NexoVideo; repositório `gustavotoigo-max/NexoVideo`.
- Stack adotada após estudo: C#/.NET 10 LTS, WPF, SQLite; ferramentas de mídia em subprocessos.
- AN-08/PF-06: exatamente um vídeo por vez; persistir resultado e liberar recursos antes do próximo.
- Cores verificadas no NexoDesk e reutilizadas: azul-marinho, azul e ciano. NexoDesk não foi alterado.
- Referência Figma recebida: arquivo `fQRvjiFcx7wXJw9932eLLh`, página Cover `16:730`, quadro `16:1153`. UI adaptada com Inter, ícones exportados e tema escuro NexoDesk.
- Release autorizado: `v0.1.0-alpha.1`, Windows x64, instalador Inno Setup por usuário e ZIP com .NET 10.0.13 incluído. Sem homologação beta.
- Saída de recuperação separada; originais não são substituídos/excluídos nesta etapa.

## Implementado e verificado

- WPF, motor e CLI compilam. Executável em `artifacts/dev/NexoVideo.exe`.
- Três modos, filtros, fila persistida, pausa, cancelamento de árvore e retomada.
- SQLite nativo 3.53.4, resultados por teste e consultas paginadas.
- Remux e Untrunc com referência, sempre candidatos experimentais e com novo diagnóstico.
- 26 testes aprovados, nenhum ignorado; quatro telas renderizadas e conferidas.
- PRD modularizado em 12 arquivos sem duplicação dos requisitos. Backup anterior em `.research/`.

- Coletor autorizado: identidade SHA-256, evidências por processo/teste, histórico, revisão humana e JSON portátil. Política `alerts-v1` separa alertas de corrupção confirmada.
- Avaliação humana só entra na matriz após confirmação de bytes, execução correspondente e reprodução completa ou falha confirmada ao abrir.

- FFmpeg/FFprobe não são redistribuídos: preparação assistida baixa/importa o ZIP original com SHA-256 fixo. Untrunc segue opcional externo.

## Pendências

- Usuário está preparando vídeos corrompidos reais: priorizar MP4, MOV, AVI, MKV, TS, WEBM, MPG, FLV e WMV.
- Gate de beta: corpus real, comparação A/V, estresse, falhas de infraestrutura e regressão ampla.
- Retomada ainda usa tamanho/data; análises novas coletam SHA-256. Checkpoints, ETA e perfis de mídia instável pendentes.
- Licença final do produto, assinatura de código, homologação dos Windows alvo e ARM64.
- DS-07: desenvolvimento usa `asInvoker`; necessidade de elevação contínua continua pendente conforme PRD.
- Releases por versão formal de teste; usuário solicitou o primeiro para usar em outras máquinas.
- Recursos gráficos Figma fora do build distribuído; ícones vetoriais próprios. Inter acompanha OFL.
- Substituição/exclusão transacional, enxertos, ferramentas adicionais e atualização segura.

- Git local usa backend TLS OpenSSL, com verificação de certificado habilitada, após falha do Schannel. Autoria local: login GitHub confirmado, substituindo o placeholder global.

## Falhas conhecidas do ambiente

PowerShell/Gyan e NuGet/.NET falharam no TLS. Usar o espelho GitHub com checksum para FFmpeg e, quando necessário,
`preparar_feed_nuget.py` com HTTPS/certificados habilitados para preparar feed local. Não repetir rotas falhas sem mudança.
Auditoria online NuGet não executada no fallback local. Nenhuma verificação de certificado foi desativada.
