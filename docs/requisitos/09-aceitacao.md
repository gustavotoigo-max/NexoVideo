## 9. Testes de aceitação da primeira versão
1. Analisar pasta recursiva com diferentes extensões selecionadas e ignorar outras; lembrar seleção ao reabrir.
2. Em arquivo íntegro com A/V, os três modos detectam streams e reportam a cobertura efetiva; silêncio não equivale a ausência de áudio.
3. Em arquivo sem áudio legítimo, não reportar corrupção automaticamente.
4. Em vídeo truncado/corrompido, distinguir erro de estrutura, seek falho, decodificação falha e incompletude da amostragem.
5. Testar recuperação de MP4/MOV truncado com vídeo de referência adequado usando ferramenta open source validada; candidato sempre separado, novo diagnóstico e relatório comparativo.
6. Testar sidecar `._arquivo.mp4` de 4 KB e backup real; não tratar AppleDouble como vídeo; não sobrescrever o original sem confirmação.
7. Testar cancelamento, falta de espaço, diretório de saída dentro da origem, arquivos somente leitura, caminhos longos, falha de subprocesso; nenhum original deve sofrer alteração por simples análise.
8. Testar relatório agregado, ETA e logs coerentes; classificar resultado inconclusivo quando cobertura for insuficiente.
9. Validar build Inno Setup, novo release de teste, aviso de atualização, download verificado, término de subprocessos e inicialização do instalador.
10. Testar em acervo clonado, nunca diretamente em mídia crítica única.
11. Executar lotes grandes confirmando **apenas um vídeo ativo por vez**, fila pausável/retomável, estados persistidos no `.db`, consultas paginadas e liberação de memória após cada arquivo. Simular encerramento abrupto, reinício, falha de banco e vídeos grandes, verificando que a RAM não cresce indefinidamente.

