## 4. Módulo RECOVERY — restauração, arquivos auxiliares e enxertos
- **RC-01:** após a análise, buscar arquivos de backup/sidecar/temporários/ocultos e variantes na mesma pasta (e, se configurado, em locais correlatos), mantendo trilha de proveniência.
- **RC-02:** identificar natureza por cabeçalho, tamanho, formato real, timestamps e relação plausível; `._nome.mp4` normalmente é AppleDouble, **não** contém um segundo vídeo e não deve ser usado como fonte de imagens.
- **RC-03:** comparar original, backups e resultados de reparo com métricas: streams recuperados, duração verificável, volume de mídia decodificada, intervalos íntegros, continuidade temporal, erros, resolução, sincronia A/V e eventuais perdas.
- **RC-04:** tentar, quando tecnicamente apropriado: remux sem recodificação; reconstrução de índice/átomos; recuperação usando vídeo de referência da mesma câmera; extração de streams recuperáveis; remux de trechos; reconstrução de timecodes/headers quando verificável.
- **RC-05:** enxerto entre fontes exige compatibilidade comprovada de codec, parâmetros, cronologia, referências de frames, timestamps e alinhamento de áudio; nunca concatenar bytes arbitrários nem misturar trechos incompatíveis. Registrar regiões substituídas.
- **RC-06:** gerar candidato em pasta temporária segura e comparar antes de disponibilizar. **Substituir original apenas sob aprovação do usuário**, com backup independente, verificação pós-escrita e rollback onde possível.
- **RC-07:** não reencodar por padrão: pode esconder problemas, introduzir perdas e alterar evidência. Recodificação opcional e claramente identificada.
- **RC-08:** suportar seleção de vídeo íntegro de referência (manual e, futuramente, sugestões por metadados/assinaturas de gravação).
- **RC-09:** distinguir `não reparável com técnicas disponíveis`, `recuperação parcial`, `reparo estrutural` e `reparo validado`; comparar sem afirmar superioridade por métricas isoladas.
- **RC-10:** instalar/embutir ferramentas de terceiros somente após avaliação de licenças e redistribuição.

### Pesquisa técnica inicial — ferramentas open source verificadas
| Ferramenta | Uso avaliado para MVP | Limitação / ação necessária |
|---|---|---|
| FFprobe/FFmpeg | identificação, demux, decodificação, remux, extração de streams | não reconstrói automaticamente todo arquivo que perdeu índice; validar codecs e disponibilidade do build |
| Untrunc (fork anthwlock) | recuperação de MP4/MOV/M4V/3GP truncado usando arquivo íntegro semelhante como referência | exige referência adequada e compatibilidade; licença GPL-2.0, verificar obrigações de distribuição |
| MKVToolNix | inspeção, extração e remux de Matroska quando estrutura recuperável | não é garantia de recuperação de payload destruído |
| GPAC/MP4Box | investigar inspeção e manipulação estrutural de ISO BMFF/MP4 | validar comandos, versão, segurança e taxa de sucesso por amostra antes de integrar |
| MP4Recover (ActiveTK) | referência de orquestração de ferramentas de reparo | projeto experimental; não assumir robustez, maturidade ou elegibilidade para integração automática |

Fontes primárias e leitura obrigatória antes da implementação:
- https://ffmpeg.org/ffprobe.html
- https://ffmpeg.org/ffmpeg.html
- https://github.com/anthwlock/untrunc
- https://mkvtoolnix.download/
- https://gpac.io/
- https://github.com/ActiveTK/MP4Recover

**Gate de aprovação da pesquisa:** construir matriz por formato × ferramenta × falha (índice ausente, truncamento, pacote danificado, áudio perdido, GOP incompleto, arquivo fragmentado). Provar funcionamento em arquivos de teste, registrar versão/licença/comando/taxa de sucesso observada. Incluir na primeira beta somente estratégias validadas; nunca divulgar suporte universal de reparação.

