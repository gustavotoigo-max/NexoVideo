# Coleta e comparação das avaliações

O coletor está integrado à análise e à tela **Relatórios**. Não envia dados para serviços externos.
O JSON exportado contém as evidências e as avaliações, permitindo examiná-lo sem acesso ao banco ou aos logs locais.
Os vídeos não são incluídos no relatório.

## Executar uma rodada

1. Prepare uma pasta com cópias de vídeos íntegros e danificados. Preserve formatos/codecs originais e mantenha o conjunto fixo até terminar.
2. Para a primeira referência, selecione **Completo**, áudio e vídeo. Execute até encerrar a fila.
3. Em **Relatórios**, dê dois cliques no arquivo para abrir o original no player padrão do Windows.
   O duplo clique também funciona na lista de **Análise** e leva à avaliação do mesmo arquivo.
   Como alternativa, selecione e use **Abrir selecionado no player padrão**.
   A abertura é registrada como solicitação; não comprova reprodução, duração assistida ou resultado.
   Registre o avaliador, player/versão, problema observado e qualidade percebida. A lista omite o diagnóstico automático para reduzir influência.
4. Nas notas, informe trilha, minuto/segundo e sintoma: bloco visual, tela preta, áudio ausente, ruído, travamento ou perda de sincronia.
   Registre também se a ausência de áudio é uma característica conhecida da gravação.
5. Marque a reprodução completa somente se realmente conferiu o arquivo inteiro e as trilhas presentes.
   Se o player não iniciar a reprodução, escolha **Problema observado**, marque **Não consegui iniciar a reprodução** e registre o sintoma; não marque reprodução inteira.
   Uma inspeção parcial pode ser salva, mas não entra na matriz. Use **Inconclusivo** se não conseguir julgar.
6. Clique em **Salvar avaliação e conferir SHA-256**. O aplicativo relê o arquivo para confirmar os mesmos bytes da análise.
   Se mudou, faça uma nova análise. Arquivos removidos precisam voltar ao caminho original para essa verificação.
   Com **Selecionar o próximo arquivo após salvar** marcado, a seleção avança inclusive para a próxima página.
   O próximo vídeo só abre com um novo duplo clique; veredito, confirmações e notas não são copiados.
   Desmarque essa opção para continuar no arquivo salvo. Avaliador e player são mantidos para facilitar a rodada.
7. Ao terminar a amostra, clique em **Calcular comparação** e **Exportar relatório completo JSON**.
   O JSON é o arquivo que deve acompanhar a avaliação posterior. Preserve-o com a identificação da rodada.

**Problema observado** é a referência usada na comparação. **Qualidade percebida** (satisfatória, parcial, insatisfatória)
fica separada: um vídeo pode ter um pequeno defeito e ainda ser satisfatório para você.
Não preencha problemas com base no alerta do aplicativo. Avalie o mesmo conjunto inteiro, incluindo os arquivos sem alertas.

## Evidências por arquivo

- Identidade: SHA-256 do conteúdo, tamanho, data de modificação e identificador exclusivo da análise (`AnalysisId`).
- Reprodução da análise: opções completas, modo, versão do motor e FFprobe, hashes do motor/FFprobe, Windows, runtime e arquitetura.
- Trilhas: índices, tipos, codecs, duração, resolução/canais e início declarado.
- Cada teste: trilha selecionada, intervalo solicitado, início/fim observado, frames/blocos de áudio, amostras de áudio, retorno e avisos.
- Cada subprocesso: executável, argumentos, UTC inicial, tempo decorrido, pico de memória disponível, timeout, cancelamento e truncamentos.
- Diagnósticos: mensagem, tempo decorrido dentro do processo e último timestamp de mídia decodificado disponível.
- Persistência: estado da fila, nota de falha, eventos da sessão, análises anteriores desta entrega e histórico das revisões.
- Portabilidade: conteúdo e hashes dos logs dentro do JSON. Logs ausentes/inacessíveis ficam marcados; `matches_capture` detecta alteração desde a coleta (null quando a assinatura anterior não existe).

**Localização:** intervalos solicitados e observados são medições do teste. `LastDecodedTimestamp` é somente contexto aproximado:
stdout e stderr chegam por pipes independentes. Não equivale ao instante exato do erro nem a um offset em bytes.
Se não há timestamp, a localização fica desconhecida. O demuxer pode emitir erros de outra trilha mesmo durante uma execução
com trilha selecionada; esse índice identifica o contexto do teste, sem atribuição automática da mensagem à trilha.
No modo completo, não é inventada uma janela exata de falha quando a ferramenta não a fornece.

Mensagens estruturadas: primeiras 200 por processo, até 2.048 caracteres cada. O contador continua após esse limite.
Logs: até 1 MiB de texto por processo, com indicação de limite; a exportação também registra truncamento se ocorrer.
Pico de memória `null` significa indisponível, especialmente em interrupções.
Frames e timestamps demonstram observação de conteúdo; o intervalo entre primeiro/último frame não prova continuidade perfeita.

## Interpretação da comparação

Política versionada: `alerts-v1`, schema `nexovideo.validation/1`.

| Resultado | Software | Sua avaliação |
|---|---|---|
| TP | Alerta | Problema observado |
| TN | Sem alerta | Sem problema observado |
| FP | Alerta | Sem problema observado |
| FN | Sem alerta | Problema observado |

Alerta inclui avisos ou retorno não zero do decoder/identificação. **Alerta não prova corrupção**: codecs não suportados,
avisos toleráveis e outros desacordos podem aparecer como falsos positivos, que precisam de análise das evidências.
Uma análise parcial com alerta conhecido pode entrar como positiva; uma parcial sem alerta não entra como negativa.
Timeout, cancelamento, limite de saída, falha de execução, alteração da origem, modo rápido e legado sem identidade ficam fora da matriz.
O estado **Concluído** da fila não significa TN.

Para entrar na matriz, a revisão precisa ser conclusiva, confirmar reprodução completa (ou impossibilidade de abrir, classificada como problema) e corresponder ao SHA-256 e ao `AnalysisId` atuais.
Arquivos com falha/cancelamento também aceitam avaliação quando a identidade foi coletada, mas o resultado automático permanece excluído.
Reanalisar cria um novo identificador: a avaliação anterior permanece no histórico e não é reaproveitada silenciosamente.
Para arquivos analisados antes desta entrega, inicie uma nova sessão; retomar uma fila concluída não adiciona evidências retroativamente.
Falha de estrutura e impossibilidade de abrir o vídeo podem ser avaliadas como problema, com a observação do sintoma nas notas.

- **Precisão dos alertas:** TP / (TP + FP).
- **Sensibilidade:** TP / (TP + FN), parcela dos problemas que geraram alerta.
- **Acurácia:** (TP + TN) / pares comparáveis.
- **Especificidade:** TN / (TN + FP).
- **F1:** 2 × TP / (2 × TP + FP + FN).
- **Cobertura da comparação:** pares comparáveis / total da sessão.

Taxas no JSON variam de 0 a 1; `null` indica denominador zero. São exibidas as contagens e exclusões,
além dos grupos por modo e extensão. As contagens de automático inconclusivo e revisão ausente podem se sobrepor.
Formato agrupado é a extensão; o contêiner e os codecs identificados constam do diagnóstico.
Trechos não amostrados e trilhas desmarcadas ficam fora do escopo automático, mesmo se a revisão humana achar um defeito ali.

Essas métricas medem concordância com a sua referência no conjunto avaliado. Não são garantia geral de qualidade perceptual nem
estimativa confiável da população com poucas amostras. Rodadas sintéticas verificam o coletor; a precisão no acervo real ainda depende
dos seus vídeos e avaliações. Compare modos usando o mesmo conjunto e condições de reprodução.

## Integridade e compatibilidade

Durante a análise, o arquivo e o executável FFprobe ficam abertos somente para leitura, impedindo escrita/exclusão concorrente no Windows.
O hash completo acrescenta uma leitura sequencial do vídeo. Falhas de leitura e cancelamentos não são ocultados como mídia íntegra.
A exportação usa um snapshot consistente do SQLite, grava primeiro em temporário e não substitui um relatório já existente.
O banco existente é atualizado com tabelas adicionais; os resultados anteriores continuam disponíveis.
Históricos e logs ainda não têm retenção global automática: reserve espaço para as rodadas.

Na CLI, o JSON gerado após `analisar` usa o mesmo coletor e schema. A avaliação manual é feita no aplicativo.
Abra-o com o mesmo `--data PASTA` usado na CLI para acessar essa sessão.
