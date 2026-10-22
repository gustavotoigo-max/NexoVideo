# Coleta de evidências para testes em lote

O aplicativo analisa a pasta e exporta um JSON com as evidências de todos os arquivos da sessão.
A avaliação manual por vídeo foi removida. Não há formulário, reprodução pelo aplicativo, avanço após salvar ou cálculo de precisão baseado em avaliações individuais.

## Executar uma rodada

1. Preserve cópias dos vídeos e a lista produzida pelo corruptor. Não converta nem renomeie as amostras durante a rodada.
2. Em **Análise**, selecione a pasta e as extensões. Para a primeira comparação, use **Completo**, com áudio e vídeo selecionados.
3. Aguarde concluir ou pause a fila. Em **Relatórios**, clique em **Exportar relatório completo JSON** e escolha um nome novo.
4. Guarde o JSON junto da lista do corruptor. A comparação com essa referência será feita posteriormente; ainda não há importação da lista no aplicativo.

Não é necessário abrir os vídeos para gerar o relatório. O banco e os logs ficam locais; não há upload automático das amostras.

## Dados por arquivo

- Caminho, tamanho, data de modificação, SHA-256 do conteúdo e `AnalysisId` da execução.
- Estado da fila, conclusão, motivo de falha/inconclusão e resultado automático (`Clear`, `Alert` ou `NotComparable`).
- Formato e trilhas identificados, codecs, resolução/canais, duração e início declarado.
- Testes por trilha e janela: intervalo solicitado, início/fim observado, frames/blocos e amostras de áudio, retorno e avisos.
- Processos: executável, argumentos, UTC inicial, duração, memória disponível, timeout, cancelamento e truncamentos.
- Mensagens do decoder e logs embutidos, com hashes para verificar alterações desde a coleta.
- Opções completas, versões/hashes do motor e FFprobe, runtime, Windows e arquitetura.
- Histórico de análises e eventos da sessão.

O SHA-256 é calculado antes da identificação e decodificação, lendo o arquivo inteiro. Ele identifica os bytes analisados;
não detecta corrupção sozinho. O arquivo permanece aberto somente para leitura durante a análise.
Ao preparar a referência do corruptor, preserve também a identidade dos arquivos resultantes para relacionar as mesmas amostras.
É útil manter controles íntegros na rodada para verificar falsos alertas.

## Limites da evidência

`LastDecodedTimestamp` é contexto aproximado: stdout e stderr chegam por pipes independentes.
Não é o instante exato do erro nem um offset em bytes. Sem timestamp, a localização fica desconhecida.
O índice da trilha identifica o contexto do teste; o demuxer pode emitir mensagens sobre outras trilhas.
O intervalo entre primeiro/último frame não prova continuidade perfeita.

São preservadas até 200 mensagens estruturadas de 2.048 caracteres por processo, com contador total separado.
Logs têm limite de conteúdo e sinalização de truncamento. Log ausente/inacessível fica marcado.
`matches_capture` indica se o hash do log confere; `null` indica assinatura anterior indisponível.
Pico de memória `null` significa indisponível.

`Alert` inclui avisos ou retorno não zero, inclusive na identificação; não prova corrupção.
`Clear` significa ausência de alertas no escopo decodificado. Trechos não amostrados e trilhas desmarcadas ficam fora desse escopo.
Modo rápido, limites, falhas de execução e análises inconclusivas não são tratados como conteúdo íntegro.
**Concluído** é um estado da fila, não uma garantia de qualidade visual ou acústica.

## Formato e compatibilidade

Schema atual: `nexovideo.evidence/2`. A política do resultado automático continua `alerts-v1`.
`reference_comparison: "not_performed"` explicita que nenhuma lista externa foi comparada.
Não são exportadas matriz de confusão, precisão ou avaliação manual ativa.
O formato da lista do corruptor e as regras de comparação serão definidos quando a referência estiver disponível.

Os bancos anteriores continuam disponíveis. Avaliações antigas não são apagadas; quando existem, o JSON as preserva em
`legacy_review_history`, apenas como histórico, sem usá-las como referência. Bancos novos não criam a tabela de avaliações.
Os JSONs antigos já exportados não são alterados.

A exportação usa snapshot consistente do SQLite, inclui os logs e recusa sobrescrever arquivos existentes.
O arquivo JSON não inclui os vídeos. Logs e históricos ainda não têm retenção global automática.
Na CLI, o relatório gerado após a análise usa o mesmo schema e coletor.
