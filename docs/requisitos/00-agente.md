## 0. Como o agente de IA deve trabalhar
- Ler este PRD uma vez no início e consultar **somente os módulos relevantes** em alterações posteriores. Quando a implementação começar, criar `docs/INDEX.md` curto e extrair os módulos deste documento para arquivos independentes, sem duplicação.
- Manter requisitos identificados por código; registrar decisões técnicas e alterações apenas nos documentos afetados. Não reescrever todo o projeto em cada tarefa.
- Agrupar mudanças relacionadas; planejar alteração mínima, implementar, executar testes direcionados e documentar resultados resumidos.
- Não explorar todo o repositório, logs enormes ou saídas completas de ferramentas quando busca pontual bastar. Evitar builds e análises repetidas sem mudança efetiva.
- Usar APIs, bibliotecas e ferramentas consolidadas antes de desenvolver parsers próprios. Não alegar suporte a formato ou reparo sem teste específico.
- Releases GitHub e instaladores Inno Setup devem ser produzidos **para cada versão formal de teste**, não a cada alteração de código ou execução de teste unitário; confirmar essa interpretação antes do pipeline final.
- Trabalho por marcos: pesquisa e matriz de compatibilidade → arquitetura e UI → mecanismo de análise → recuperação → gestão de arquivos → testes de regressão → empacotamento e atualização.
- Nunca solicitar ao agente autonomia para excluir ou sobrescrever acervos reais durante testes. Usar cópias sintéticas/duplicadas.

