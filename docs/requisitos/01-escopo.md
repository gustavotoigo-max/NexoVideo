## 1. Objetivo, escopo e critérios gerais
Desenvolver uma ferramenta desktop para varrer em massa vídeos obtidos por recuperação de dados, diagnosticar presença/decodificação de áudio e vídeo, apontar defeitos, investigar arquivos auxiliares e executar reparações tecnicamente viáveis. A primeira **versão de teste para aprovação** já deve possuir os três modos de análise e um conjunto validado de técnicas open source de reparação; funcionalidades não comprovadas deverão aparecer como indisponíveis ou experimentais, nunca como recuperação garantida.

**Princípios inegociáveis:**
1. Nenhuma exclusão, substituição ou modificação durante a etapa de análise.
2. Nenhuma operação destrutiva sem confirmação explícita e indicação dos arquivos afetados.
3. Preferir saída nova preservando o original; substituição in-place somente com transação segura, cópia preservada e validação; nunca declarar 'melhor' apenas porque o arquivo abre.
4. Diferenciar resultados: trilha declarada, trilha decodificável, integridade amostrada, integridade decodificada, resultado visual efetivo (não inferível automaticamente em todos os casos).
5. Falha ou ausência de áudio não significa corrupção quando a mídia originalmente não tinha áudio.
6. Operações longas precisam admitir cancelamento e deixar o acervo original consistente.

