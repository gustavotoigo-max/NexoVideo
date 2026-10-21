## 3. Módulo FORMATS — matriz inicial de formatos
A lista seguinte corresponde a **extensões e famílias candidatas**; cobertura precisa ser validada por contêiner e codec, em três colunas independentes: detectar, decodificar, reparar.

| Grupo | Extensões candidatas |
|---|---|
| Uso geral | .mp4, .mov, .m4v, .avi, .mkv, .wmv |
| Câmeras/filmadoras | .mts, .m2ts, .mod, .tod, .mxf, .dv |
| Transporte/web | .ts, .flv, .f4v, .webm, .ogv |
| Legados/DVD/mobile | .mpg, .mpeg, .vob, .3gp, .3g2, .asf, .divx |
| Profissionais/especiais (pesquisa/validação específica) | .r3d, .braw, .crm, .cin, .dav |

- **FM-01:** caixas de seleção antes do scan, por extensão/família; botão marcar/desmarcar todos; lembrar última seleção.
- **FM-02:** permitir múltiplos formatos no mesmo scan; subpastas recursivas.
- **FM-03:** considerar H.264/AVC, H.265/HEVC, MPEG-2, MPEG-4 Part 2, VP8/VP9, AV1, ProRes, DNxHD/HR, AAC, PCM, MP3, AC-3 etc. como codecs a mapear, **não** como garantia de suporte universal.
- **FM-04:** extensões RAW/proprietárias podem exigir SDK/licença ou não permitir reparo open source. Declarar explicitamente níveis de suporte.

