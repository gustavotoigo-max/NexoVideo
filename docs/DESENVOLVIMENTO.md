# Desenvolvimento — 0.1.0-alpha.1

Aplicação WPF, motor e CLI executáveis em Windows. Interface adaptada ao quadro Compliance Engine do Figma fornecido, com fonte Inter incluída, ícones exportados e tema escuro na paleta NexoDesk.

Detalhes de referência, adaptações e conferência: [design](DESIGN.md).

| Projeto | Responsabilidade |
|---|---|
| NexoVideo.Core | Inventário, SQLite, análise, fila, supervisor e candidatos |
| NexoVideo.App | Análise, recuperação, relatórios e configurações |
| NexoVideo.Cli | Mesmo motor sem interface gráfica |
| NexoVideo.Tests | Persistência, sequência, processos, decode e candidatos |

Executável: `artifacts/dev/NexoVideo.exe`. Primeiro pré-release autorizado com instalador Inno Setup e ZIP self-contained; [primeiro uso](USAR_RELEASE.md).

## Comportamentos implementados

- AN-01: JSON de contêiner/streams, codecs, resolução, canais, duração, taxa e logs.
- AN-02: decodificação real via FFprobe `show_frames`, por stream/janela; tempos solicitados e observados.
  N configurável ou quantidade derivada de MB; MB é estimativa temporal, com até 200 janelas/trilha.
  Cobertura insuficiente, janela vazia e timeout são inconclusivos; seek pode incluir preroll.
- AN-03/04: decode completo por trilha, contagem de frames/blocos e samples de áudio; avisos preservados.
  Ainda há releitura por trilha. Retorno zero não é usado isoladamente como comprovação de integridade.
- AN-05/08, PF-06/07: worker único entre UI/CLI na mesma sessão Windows; banco impede segunda reivindicação
  enquanto existir item ativo. Pausa entre arquivos, cancelamento da árvore e retomada persistida.
- PF-08/09: SQLite nativo 3.53.4, WAL, transações curtas; resultado e estado gravados juntos.
  Tabela paginada em 100 registros. Testes ficam no JSON por arquivo persistido no banco.
- PF-10/DS-08: subprocesso criado suspenso, associado ao Job Object antes de executar; pipes drenados,
  captura/log limitados, memória da árvore configurável. Recursos fechados antes do próximo arquivo.
- FM/FL: seleção persistida, recursão, exclusão de saída, auxiliares, ocultos/system e reparse points.
  Enumeração em profundidade, limite de 256 diretórios aninhados e eventos de falha no banco.
- RC: remux e Untrunc com referência manual e cópias; candidato separado, novo diagnóstico completo e
  classificação experimental. Nenhum original é substituído/apagado.
- Coletor: SHA-256, comandos/tempos/mensagens, histórico de análises e revisões humanas; comparação com exclusões explícitas, exportação com snapshot SQLite e logs embutidos. [Procedimento e limites](VALIDACAO_MANUAL.md).
- UI: configurações persistidas, estado/progresso, detalhes e exportação JSON paginada em streaming.

São comportamentos observados nesta entrega, sem aprovação integral de todos os requisitos dos módulos.

## Verificação

26 testes aprovados, nenhum ignorado, com ferramentas/corpus sintético da pesquisa. Build sem erros/avisos.
Quatro telas renderizadas e conferidas, incluindo diagnóstico de uma sessão de teste.

Casos: amostragem início/fim; limite de janelas; exclusões; recuperação de item interrompido;
paginação e reivindicação única; término da árvore por timeout; cancelamento e reutilização do supervisor;
MP4 sem índice/sem áudio; FLV com timestamps/fim efetivo; payload danificado com retorno zero;
pausa/retomada sem repetir concluído; remux preservando origem/hierarquia; bloqueio entre coordenadores;
saída grande limitada; Untrunc sobre cópias, preservando classificação experimental.
Coletor: matriz/exclusões/denominadores, políticas de alerta, alteração de bytes com tamanho/data restaurados,
exportação portátil e histórico, evidência do decoder real, cancelamento persistido e limites das mensagens.

TRX: `.research/test-results/`. Mídias/logs: `.research/tests/`. Capturas: `.research/ui-page-0.png` a `ui-page-3.png`.
Pacotes travados em lock files. Auditoria online NuGet não executada no fallback de TLS.

## Pendências

- Corpus real, várias trilhas, qualidade perceptual e comparação original/candidato: continuidade, timestamps,
  sincronia A/V, priming e perdas.
- Cache com assinatura além de tamanho/data; novos itens na sessão existente; checkpoints e ETA.
- Estresse prolongado de RAM/handles da aplicação, falta de espaço e falha de banco injetada.
- Retenção global de logs/histórico, gestão de cópias temporárias e orçamento de disco de recuperação.
- Perfis de mídia instável, ferramentas adicionais, enxertos e operações transacionais na origem.
- DS-07: `asInvoker` para desenvolvimento; elevação contínua segue pendente conforme o PRD.
- Direitos de distribuição dos recursos Figma, acessibilidade, alternância de temas, Windows mínimo e ARM64.
- Licença final do produto, assinatura, homologação ampla e atualização segura.

Empacotamento reproduzível: `scripts/empacotar_release.ps1`; exige SDK .NET e Inno Setup 6.
O script gera manifesto do commit/arquivos e checksums; recusa sobrescrever uma pasta de saída existente.
`-NuGetSource .research/nuget-feed` aplica o fallback local de TLS, sem auditoria online somente nessa execução.
Locks separados `packages.win-x64.lock.json` preservam a restauração normal de desenvolvimento.

## Rastreabilidade

12 módulos em `docs/requisitos/`; o PRD da raiz aponta para eles, sem duplicar requisitos.
Backup anterior ao desenvolvimento: `.research/before-development-20261021.zip`.
NexoDesk foi apenas consultado; seus tokens XAML de marca/tema claro foram reutilizados localmente.
