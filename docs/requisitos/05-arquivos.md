## 5. Módulo FILES — estruturas, ações e proteção
- **FL-01:** seletor de pasta de origem, processamento recursivo e painel de resultados.
- **FL-02:** três opções de destino/ação após análise: (a) operar/restaurar na hierarquia de origem, (b) apagar arquivos selecionados da hierarquia original após confirmação, (c) salvar reparados em outra pasta mantendo toda hierarquia relativa.
- **FL-03:** nenhuma exclusão automática após análise; ações em lote sempre exibir contagem, destino, riscos e prévia.
- **FL-04:** modo padrão recomendado = saída separada e originais intocados.
- **FL-05:** prevenção de colisões de nomes, caminhos longos, ACL/permissões, arquivos em uso, discos sem espaço, links simbólicos/junctions e loops de diretório.
- **FL-06:** não copiar automaticamente conteúdos de `._`, hidden/system como vídeo. Não apagar auxiliares ao término.
- **FL-07:** logs auditáveis de tentativa, origem, destino, hash opcional, resultado e consentimento de operação.
- **FL-08:** se pasta de saída estiver dentro da origem, excluí-la da enumeração para evitar recursão.

