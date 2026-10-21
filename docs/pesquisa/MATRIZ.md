# Matriz inicial de compatibilidade e falhas

**Não é lista de suporte homologado.** Extensão não identifica, sozinha, contêiner ou codec.
Pesquisa: [relatório](PESQUISA_TECNICA.md). Evidência: [experimentos](EXPERIMENTOS.md).

Legenda: **S** = evidência sintética desta rodada; **D** = capacidade documentada/candidata, não testada aqui;
**P** = pendente de amostra/validação específica; **N** = não há estratégia validada nesta pesquisa.
Uma célula S vale somente para o par contêiner/codec e cenário indicado, nunca para toda extensão.

## 1. Detectar, decodificar e reparar separadamente — FM-01 a FM-04

| Extensão/família candidata do PRD | Detectar | Decodificar | Reparar | Escopo e limite |
|---|---|---|---|---|
| `.mp4` | S, FFprobe | S, H.264/AAC e HEVC/AAC | S, remux controle; Untrunc sem moov com ressalvas | Reconstrução produziu aviso temporal e priming extra; não é reparo validado |
| `.mov` | S | S, ProRes/PCM | D, remux; P, referência | Sem teste de MOV danificado |
| `.m4v` | D, ISO BMFF quando aplicável | D, depende de codec/contêiner real | P, remux/Untrunc | Extensão também pode designar stream elementar; não generalizar MP4 |
| `.avi` | S | S, MPEG-4 Part 2/MP3 | D, remux/recriação de índice legível | Sem teste de AVI sem índice |
| `.mkv` | S | S, H.264/AAC | D, remux/MKVToolNix | MKV foi destino de remux, mas nenhum MKV danificado foi testado |
| `.wmv`, `.asf` | S somente `.wmv` | S somente WMV2/WMAV2 em ASF | D, remux se trilhas legíveis | Outras variantes ASF/WMV pendentes |
| `.ts` | S | S, MPEG-2/MP2 | S, remux parcial após truncamento | Preservou parte existente; não recuperou o final ausente |
| `.mts`, `.m2ts` | D, MPEG-TS/BDAV | P, amostra real | P | Renomear TS não valida M2TS/AVCHD |
| `.mod`, `.tod` | P, variantes de câmera | P | P | Confirmar se MPEG-PS/TS e seu codec; exigir amostra da câmera |
| `.mxf` | D | P, variantes profissionais | P | Mapping de essências, OP e metadados não testado |
| `.dv` | D | D, DV | P | Sem amostra; não prometer remontar segmentos ausentes |
| `.flv` | S | S, FLV1/MP3 | D | `-ss 0` produziu janela vazia; início sem seek funcionou no teste direcionado |
| `.f4v` | D, ISO BMFF | P | P | Não coberto pelo controle FLV1 |
| `.webm` | S | S, VP9/Opus | D, remux quando legível | Sem teste de dano |
| `.ogv` | D, Ogg | P, Theora/Vorbis e variantes | P | Não executado |
| `.mpg` | S | S, MPEG-2/MP2 | D | Sem teste de dano |
| `.mpeg`, `.vob` | D, PS/variante DVD | P | P | DVD e MPEG-1 não validados pelo controle `.mpg` |
| `.3gp`, `.3g2` | D, ISO BMFF | P, codecs móveis | P, Untrunc/remux | AAC/H.264 de MP4 não prova AMR/H.263 ou câmera específica |
| `.divx` | P, possivelmente AVI | P | P | Não identificar por extensão apenas |
| `.r3d` | D, demuxer r3d presente no build | P | N | Presença no inventário não comprova todas as gerações RED |
| `.dav` | D, demuxer dhav presente no build | P | N | DAV varia por fabricante; não generalizar DHAV |
| `.braw`, `.crm` | P | P | N | Sem amostra nem integração SDK validada |
| `.cin` | P | P | N | Extensão ambígua: Cineon pode ser imagem individual; não pressupor vídeo |

Capacidades documentadas gerais: [FFmpeg, formatos e codecs](https://ffmpeg.org/general.html),
[formatos](https://ffmpeg.org/ffmpeg-formats.html). Para casos especiais, estas fontes não substituem
amostra da variante específica. Inventário exato do build está nos logs referenciados em `evidencias.json`.

Codecs executados: H.264, HEVC, ProRes, MPEG-4 Part 2, MPEG-2, VP9, FLV1, WMV2;
AAC, PCM, MP3, MP2, Opus, WMAV2. AV1, VP8, DNxHD/HR, AC-3, Theora, Vorbis e demais perfis
continuam candidatos não testados, mesmo quando um decoder aparece no executável.

## 2. Formato × ferramenta × falha — gate RC/FM

| Formato / classe de falha | FFprobe/FFmpeg | Untrunc | MKVToolNix | GPAC/MP4Box | MP4Recover | Evidência / decisão |
|---|---|---|---|---|---|---|
| MP4 H.264/AAC, moov ausente | S: sondagem/remux falharam | S: candidato com referência distinta e compatível | N | P | D, orquestração | Conteúdo encontrado; timestamps/priming precisam de tratamento; experimental |
| MOV/M4V/3GP ISO BMFF, índice ausente | D: pode impedir demux | P | N | P | P | Não extrapolar o MP4 sintético |
| AVI, índice ausente | P: leitura/remux conforme estrutura restante | N | P: importação/remux | N | N | Necessário fixture próprio, incluindo OpenDML |
| Matroska, Cues ausentes | D: leitura pode ser sequencial | N | D: remux se estrutura legível | N | N | Sem teste nesta rodada |
| MP4 não fragmentado, truncamento retirou moov final | P: depende do que restou | P: referência e payload restante | N | P | P | Retirar apenas moov não simula truncamento simultâneo do payload |
| MPEG-TS MPEG-2/MP2, truncamento do final | S: remux manteve 53 frames existentes | N | P: importação | P | N | Recuperação parcial; original íntegro tinha 80 frames |
| MP4 H.264/AAC, pacotes adulterados | S: probe e decode retornaram zero, com erros no decode | P | N | P | P | Remux manteve defeitos; não rotular como reparado |
| Outros contêineres, pacotes danificados | P | P somente entradas admitidas | P somente formatos admitidos | P somente formatos admitidos | P | Decoder pode ocultar dano; criar fixture por codec |
| MP4 somente vídeo legítimo | S: uma trilha de vídeo identificada/decodificada | N | N | N | N | Não classificar ausência de áudio como corrupção |
| Trilha de áudio realmente perdida | D: pode extrair o que existir, não inventar áudio | P se dados ainda existirem sem índice | P se dados ainda existirem | P se dados ainda existirem | P | Zero evidência de recuperar áudio fisicamente ausente |
| H.264/HEVC, GOP incompleto | P: tentar decodificar a partir de referência válida | P | N: remux não recria frames | N: empacotar não recria frames | P | Não testado isoladamente; dano ao payload não prova toda classe GOP |
| MP4 fragmentado, segmentos moof/mdat ausentes | D: ler fragmentos sobreviventes quando possível | P, não assumir suporte | N | D: manipular fragmentos legíveis | P | fMP4 não foi testado |
| Arquivo recuperado com fragmentos físicos fora de ordem | N para recuperação genérica | N para recuperação genérica | N | N | N | Requer contexto de recuperação/forense; não confundir com fMP4 |
| AppleDouble `._av.mp4`, 4 KiB | S: rejeitado como mídia | Não usar | Não usar | Não usar | Não usar | Assinatura 0x00051607; classificar como auxiliar |

“P” não autoriza habilitar uma estratégia; significa que falta evidência para decidir sua aplicabilidade.
“N” significa ausência de solução validada, não uma prova matemática de impossibilidade.
Fontes das estratégias: [Untrunc](https://github.com/anthwlock/untrunc),
[mkvmerge](https://mkvtoolnix.download/doc/mkvmerge.html),
[MP4Box](https://wiki.gpac.io/MP4Box/MP4Box/), [MP4Recover](https://github.com/ActiveTK/MP4Recover).

## 3. Taxa observada e critérios

| Experimento de reparo | Tentativas | Resultado bruto | Resultado admissível |
|---|---:|---|---|
| FFmpeg, controle MP4 íntegro → MKV | 1 | 1/1 remux com hashes decodificados iguais | Viabilidade de remux, não reparação de dano |
| FFmpeg, MP4 sem moov | 1 | 0/1 candidato | Estratégia não resolveu esta falha |
| FFmpeg, TS truncado → MKV | 1 | 1/1 candidato | Parcial; 53/80 frames, sem o trecho final |
| FFmpeg, MP4 com payload adulterado → MKV | 1 | 1/1 candidato | Defeito permaneceu; 0/1 reparo do payload |
| Untrunc, MP4 sem moov + referência distinta | 1 | 1/1 candidato | 80 frames iguais em ordem, PCM igual após priming; aviso temporal, 0/1 reparo plenamente validado |

Cada “1/1” descreve **um controle sintético**, sem estimativa de sucesso em acervos reais.
Não calcular taxa universal a partir desta tabela. Um candidato pode conter mídia útil e ainda não satisfazer
sincronia, continuidade ou integralidade exigidas para “reparo validado”.
