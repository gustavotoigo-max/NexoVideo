# PRD — Analisador e Recuperador de Vídeos
**Versão do documento:** 0.2 — 2026-10-21  
**Estado:** Requisitos consolidados para desenvolvimento / decisões técnicas pendentes  
**Plataforma-alvo:** Windows desktop  
**Regra central:** analisar primeiro, preservar evidências, nunca destruir automaticamente.


Requisitos modularizados no início do desenvolvimento. Cada módulo tem uma única fonte:

- [0. Como o agente de IA deve trabalhar](docs/requisitos/00-agente.md)
- [1. Objetivo, escopo e critérios gerais](docs/requisitos/01-escopo.md)
- [2. Módulo ANALYSIS — três modos obrigatórios](docs/requisitos/02-analise.md)
- [3. Módulo FORMATS — matriz inicial de formatos](docs/requisitos/03-formatos.md)
- [4. Módulo RECOVERY — restauração, arquivos auxiliares e enxertos](docs/requisitos/04-recuperacao.md)
- [5. Módulo FILES — estruturas, ações e proteção](docs/requisitos/05-arquivos.md)
- [6. Módulo UI — operação e configurações](docs/requisitos/06-interface.md)
- [7. Módulo PERF — mídias sensíveis e performance](docs/requisitos/07-desempenho.md)
- [8. Módulo DISTRIBUTION — Windows, releases e atualização](docs/requisitos/08-distribuicao.md)
- [9. Testes de aceitação da primeira versão](docs/requisitos/09-aceitacao.md)
- [10. Pendências técnicas explícitas](docs/requisitos/10-pendencias.md)
- [11. Regra de conclusão](docs/requisitos/11-conclusao.md)

Consulte [o índice de documentação](docs/INDEX.md) e [o estado das decisões](docs/DECISOES.md).
