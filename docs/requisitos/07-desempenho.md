## 7. Módulo PERF — mídias sensíveis e performance
- **PF-01:** privilegiar enumeração ordenada e leitura sequencial/contígua em vez de seek aleatório quando possível; uma fila por unidade física e limite de concorrência configurável (padrão conservador).
- **PF-02:** separar I/O de análise e processamento; preferir cache de metadados e evitar repetir leitura completa em testes subsequentes.
- **PF-03:** em HDD com defeito físico, não prometer reduzir desgaste só com algoritmo: priorizar imagem/clone e permitir cancelar diante de erros ou lentidão severa.
- **PF-04:** ordenação por offsets físicos reais depende de informações que o sistema de arquivos pode não fornecer; usar heurísticas e medir impacto, sem alegar controle exato da cabeça.
- **PF-05:** perfis 'conservador para mídia instável' e 'rápido para cópia saudável' como requisitos de configuração sujeitos a validação.

### PF-06 a PF-12 — fila, RAM e persistência de resultados
- **PF-06 (obrigatório):** processamento estritamente sequencial dos vídeos: **ANALISA → PROCESSA RESULTADOS → PERSISTE NO BANCO LOCAL `.db` → LIBERA RECURSOS → PRÓXIMO VÍDEO**. Não executar múltiplos arquivos em paralelo. Paralelismo interno de um codec, se usado, deve respeitar limites de recursos e nunca provocar análise concorrente de outro arquivo.
- **PF-07:** controlador de fila com estados persistidos por arquivo (`pendente`, `em_análise`, `concluído`, `falhou`, `cancelado`), contadores, ordenação previsível e opção de retomada após fechamento inesperado. Ao iniciar novamente, itens marcados `em_análise` devem voltar a `pendente` ou `interrompido`, nunca ser tratados como concluídos.
- **PF-08:** usar banco local leve (preferencialmente **SQLite**, arquivo `.db`) para armazenar inventário, estado da fila, resultados por arquivo e por teste, métricas e erros. Consultas paginadas para tabelas/relatórios; não carregar todo o histórico na RAM.
- **PF-09:** manter em memória somente o registro em execução, pequenos buffers temporários e uma janela limitada de itens para interface. Gravar resultados incrementalmente em transações curtas ao final de cada arquivo e, quando necessário, checkpoints durante análises muito longas. Evitar logs ilimitados em memória.
- **PF-10:** a análise e decodificação devem utilizar RAM de forma **limitada** (buffers/streams, não carregar o vídeo inteiro em memória). Ao concluir, falhar ou cancelar um arquivo, fechar handles/processos, liberar buffers/objetos e descartar referências antes de iniciar o próximo arquivo. Confirmar ausência de crescimento progressivo de memória.
- **PF-11:** separar fila e interface: operações de I/O/banco fora do thread da UI, com atualização de progresso leve e limitada em frequência. Estabelecer limites de consumo de memória e estratégias de tratamento de falha para não travar o aplicativo.
- **PF-12:** ao retomar, consultar o `.db` em vez de revarrer/reprocessar itens concluídos sem necessidade; validar mudanças de tamanho/data/assinatura antes de reutilizar resultados. Se o banco falhar, parar de forma segura sem perder/modificar os vídeos originais.

