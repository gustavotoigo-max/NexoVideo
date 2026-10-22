# Decisões e estado

## Confirmado e adotado

- Usuário autorizou iniciar o desenvolvimento após a pesquisa. Nome: NexoVideo; repositório `gustavotoigo-max/NexoVideo`.
- Stack adotada após estudo: C#/.NET 10 LTS, WPF, SQLite; ferramentas de mídia em subprocessos.
- AN-08/PF-06: exatamente um vídeo por vez; persistir resultado e liberar recursos antes do próximo.
- Cores verificadas no NexoDesk e reutilizadas: azul-marinho, azul e ciano. NexoDesk não foi alterado.
- Referência Figma recebida: arquivo `fQRvjiFcx7wXJw9932eLLh`, página Cover `16:730`, quadro `16:1153`. UI adaptada com Inter, ícones exportados e tema escuro NexoDesk.
- Release autorizado: `v0.1.0-alpha.3`, Windows x64, instalador Inno Setup por usuário e ZIP com .NET 10.0.13 incluído. Sem homologação beta.
- Saída de recuperação separada; originais não são substituídos/excluídos nesta etapa.

## Implementado e verificado

- WPF, motor e CLI compilam. Executável em `artifacts/releases/0.1.0-alpha.3/NexoVideo-0.1.0-alpha.3-win-x64/NexoVideo.exe`.
- Três modos, filtros, fila persistida, pausa, cancelamento de árvore e retomada.
- SQLite nativo 3.53.4, resultados por teste e consultas paginadas.
- Remux e Untrunc com referência, sempre candidatos experimentais e com novo diagnóstico.
- Alpha.3: 26 testes passaram, sem falhas ou ignorados; exportação de 205 arquivos e tela sem avaliação individual verificadas. Pacotes: anexo de validação do release.
- PRD modularizado em 12 arquivos sem duplicação dos requisitos. Backup anterior em `.research/`.

- Coletor mantido: identidade SHA-256, evidências por processo/teste, histórico e JSON portátil. Política `alerts-v1` separa alertas de corrupção confirmada.
- 2026-10-22: usuário abandonou avaliação individual por não escalar para mais de 200 arquivos. Removidos formulário, abertura do player para avaliação, avanço e comparação manual.
- Referência será a lista gerada pelo script corruptor do usuário. Importação e comparação ficam para depois; não presumir formato da lista. Avaliações antigas são preservadas como legado.

- FFmpeg/FFprobe não são redistribuídos: preparação assistida baixa/importa o ZIP original com SHA-256 fixo. Untrunc segue opcional externo.



## Pendências

- Usuário está preparando vídeos corrompidos reais: priorizar MP4, MOV, AVI, MKV, TS, WEBM, MPG, FLV e WMV.
- Gate de beta: corpus real, comparação A/V, estresse, falhas de infraestrutura e regressão ampla.
- Retomada ainda usa tamanho/data; análises novas coletam SHA-256. Checkpoints, ETA e perfis de mídia instável pendentes.
- Licença final do produto, assinatura de código, homologação dos Windows alvo e ARM64.
- DS-07: desenvolvimento usa `asInvoker`; necessidade de elevação contínua continua pendente conforme PRD.
- Releases por versão formal de teste; entrega em GitHub necessária porque os testes ocorrem em outro PC.
- Recursos gráficos Figma fora do build distribuído; ícones vetoriais próprios. Inter acompanha OFL.
- Substituição/exclusão transacional, enxertos, ferramentas adicionais e atualização segura.

- Git local usa backend TLS OpenSSL, com verificação de certificado habilitada, após falha do Schannel. Autoria local: login GitHub confirmado, substituindo o placeholder global.

## Falhas conhecidas do ambiente

PowerShell/Gyan e NuGet/.NET falharam no TLS. Usar o espelho GitHub com checksum para FFmpeg e, quando necessário,
`preparar_feed_nuget.py` com HTTPS/certificados habilitados para preparar feed local. Não repetir rotas falhas sem mudança.
Auditoria online NuGet não executada no fallback local. Nenhuma verificação de certificado foi desativada.
