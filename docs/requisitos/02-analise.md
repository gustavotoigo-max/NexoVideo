## 2. Módulo ANALYSIS — três modos obrigatórios
- **AN-01 Rápido:** FFprobe/identificação equivalente: contêiner, streams (áudio/vídeo), codec, resolução, canais, duração, taxa de quadros, metadados, mensagens de erro. Não declarar integridade do payload.
- **AN-02 Intermediário:** tentativa real de decodificação de trechos distribuídos ao longo do arquivo e por stream.
  - AN-02a: intervalo configurável em MB como **alvo aproximado de posição no arquivo**, mapeado para posição temporal/índice do contêiner quando possível; nunca supor correspondência exata entre offset de bytes e tempo.
  - AN-02b: alternativa de N amostras igualmente distribuídas, N configurável.
  - AN-02c: duração por amostra em segundos configurável.
  - AN-02d: incluir início e final, quando tecnicamente acessíveis; adaptar keyframes e offsets de seek.
  - AN-02e: registrar posição solicitada vs. efetivamente analisada, streams examinados, erros, cobertura; mídia não seekable → marcar parcial/inconclusivo e permitir análise completa.
  - AN-02f: não alegar que "a cada 20 MB" significa decodificar exatamente blocos físicos de 20 MB; duração real, VBR e GOP variam.
- **AN-03 Completo:** decodificar todas as trilhas selecionadas, contabilizar avisos/erros de demux e decoder, interrupções, frames/pacotes decodificados e tempo analisado. Decodificação sem erro não prova ausência de todos os defeitos visuais ou acústicos.
- **AN-04:** diagnóstico independente por áudio e vídeo; detectar arquivos somente áudio/vídeo e declarar o esperado x observado.
- **AN-05:** análise em lote recursiva, continuar após arquivo inválido; proteção contra travamento, timeouts configuráveis, cancelamento.
- **AN-06:** progressão de análise separada da de reparo, logs por arquivo, idempotência (reexecutar sem alterar original).
- **AN-08:** nunca analisar dois vídeos simultaneamente: uma única tarefa de análise ativa, independentemente do modo escolhido. Manter fila de arquivos com estados pendente, em análise, concluído, falhou, cancelado; oferecer pausar, retomar e cancelar a fila, sem iniciar o próximo item antes do encerramento e liberação dos recursos do atual.
- **AN-07:** reconhecimento de arquivo real por assinatura/contêiner quando possível, não somente extensão.

