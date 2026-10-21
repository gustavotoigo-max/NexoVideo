# Vídeos para os primeiros testes

Prioridade para identificação e diagnóstico, sem promessa de reparo dos arquivos danificados:

| Formato | Combinação já exercitada em controles sintéticos |
|---|---|
| MP4 | H.264/AAC, HEVC/AAC e H.264 sem áudio |
| MOV | ProRes/PCM |
| AVI | MPEG-4 Part 2/MP3 |
| MKV | H.264/AAC |
| TS e MPG | MPEG-2/MP2 |
| WEBM | VP9/Opus |
| FLV | FLV1/MP3 |
| WMV | WMV2/WMAV2 |

Para MP4/MOV sem índice, preparar também vídeo íntegro da mesma câmera/aplicativo, com codec, resolução,
taxa de quadros e áudio semelhantes. Não alterar extensões nem converter os vídeos para o teste.

Se conhecidos, registrar dispositivo/programa de origem, sintoma, duração aproximada esperada e presença esperada
de áudio. Uma gravação originalmente sem áudio deve ser diferenciada de trilha perdida.

Sugestão de organização, com cópias:

```text
samples/
  MP4/
    corrompidos/
    referencias-integras/
  MOV/
    corrompidos/
    referencias-integras/
  AVI/
  MKV/
  TS/
  WEBM/
  MPG/
  FLV/
  WMV/
```

`samples/` está excluída do Git; nenhum acervo foi preenchido automaticamente. A referência deve ser selecionada
manualmente. Os primeiros candidatos disponíveis usam remux para MKV ou Untrunc experimental com referência.

Rodada seguinte: M4V, MTS/M2TS, MXF, DV, VOB, ASF, 3GP/3G2, OGV e MOD/TOD.
R3D, BRAW, CRM, DAV e CIN precisam de investigação específica. A extensão não garante compatibilidade.

Para comparar os resultados, siga o [procedimento de avaliação manual e coleta](VALIDACAO_MANUAL.md).
Inclua controles íntegros; avaliar apenas arquivos com alerta impede medir falhas não detectadas.
