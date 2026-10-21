# Experimentos técnicos e reprodução

**Executados somente em mídia sintética criada nesta pasta, em sequência.** Nenhum acervo real foi utilizado.
Os scripts são instrumentos de pesquisa, não implementação do motor ou da aceitação da beta.

## 1. Ambiente e proveniência

- Windows x64; Python 3.14; versão detalhada em [evidencias.json](evidencias.json).
- FFmpeg/FFprobe 9.0.2 essentials, Gyan, com configuração GPL/version3.
- Untrunc `v1-9d86ec9-dirty`, com FFmpeg interno 3.3.4/Lavc57.89.100.
- URLs, tamanhos e SHA-256 dos pacotes em [ferramentas.json](ferramentas.json).
- Hashes dos executáveis usados e configuração do FFmpeg em `evidencias.json`.
- Downloads HTTPS com checksums conferidos; isso registra integridade/proveniência, não uma auditoria de segurança dos binários.

O projeto FFmpeg fornece fontes e indica fornecedores externos para Windows:
[downloads oficiais](https://ffmpeg.org/download.html), [builds Gyan](https://www.gyan.dev/ffmpeg/builds/).
Untrunc veio do [release do repositório](https://github.com/anthwlock/untrunc/releases/tag/latest).
O nome `latest` do Untrunc é mutável: reproduzir o mesmo experimento exige conferir o hash registrado.
Nenhum pacote foi instalado globalmente ou incluído em instalador do NexoVideo.

## 2. Corpus e método

Doze controles de oito segundos, vídeo 160×96 a 10 fps, som sintético quando aplicável:

| Arquivo | Vídeo | Áudio |
|---|---|---|
| av.mp4 | H.264 | AAC |
| camera.mov | ProRes proxy | PCM s16le |
| av.avi | MPEG-4 Part 2 | MP3 |
| av.mkv | H.264 | AAC |
| av.ts | MPEG-2 | MP2 |
| av.webm | VP9 | Opus |
| av.mpg | MPEG-2 | MP2 |
| av.flv | FLV1 | MP3 |
| av.wmv | WMV2 | WMAV2 |
| hevc.mp4 | HEVC | AAC |
| video_only.mp4 | H.264 | Ausente legitimamente |
| reference.mp4 | H.264, imagem com matiz alterada | AAC |

O arquivo de referência tem conteúdo visual diferente e parâmetros de codificação semelhantes ao MP4 alvo.
Não é referência de câmera física. `moov` foi retirado do alvo, mantendo `mdat`, para isolar a perda de índice.
O pequeno leitor de boxes do script serve **apenas para fabricar essa falha**, não é parser do produto.

Derivações: MP4 com 2.048 bytes do payload zerados; TS cortado em dois terços dos bytes;
AppleDouble sintético de 4 KiB; MP4 copiado com extensão `.bin`.
Remux e reconstrução sempre geraram candidatos em novos nomes.

Cada controle passou por probe JSON, decodificação por trilha e amostras de um segundo em 0, 3,5 e 7 segundos.
Foram usados hashes do conteúdo decodificado por frame/bloco para controles de comparação.
Hash de arquivo é usado para verificar preservação dos originais durante os experimentos.
O script espera o processo encerrar antes da próxima análise.

Não há teste de intervalo em MB, cobertura temporal original, recurso gráfico de reprodução ou qualidade perceptual.
O áudio constante simplifica a comparação e **não comprova sincronia A/V** em conteúdo real.

## 3. Resultados

A primeira execução registrou **152 comandos e 50 verificações: 48 confirmadas e duas divergências**.
Essas verificações incluem geração e preservação de arquivos, não 50 casos independentes de compatibilidade.
As divergências foram mantidas na evidência, investigadas em cinco comandos adicionais e explicadas abaixo.

| Caso | Observação | Implicação |
|---|---|---|
| Controles íntegros | Probe e decode completo funcionaram nos nove tipos de extensão executados | Viabilidade no corpus pequeno; não comprova todas as variantes |
| FLV íntegro com `-ss 0` | Processo retornou zero, mas as duas janelas iniciais ficaram vazias | Medir conteúdo decodificado, não somente retorno |
| FLV sem seek no início | Teste direcionado gerou 10 frames de vídeo e 39 blocos de áudio | Ler início diretamente; manter tratamento de seek falho |
| MP4 somente vídeo | Uma trilha de vídeo observada, sem áudio | Ausência de áudio legítima não é corrupção |
| MP4 renomeado `.bin` | Contêiner e duas trilhas identificados | Identificação funcionou sem extensão reconhecida |
| AppleDouble sintético | Probe falhou; nenhuma trilha | Não usar como fonte de vídeo |
| MP4 sem moov | Probe e remux falharam | Remux não basta para essa falha estrutural |
| Payload adulterado | Probe retornou zero; decode também retornou zero, com diagnósticos e menos unidades | Retorno zero não comprova integridade |
| Remux do payload adulterado | Conteúdo decodificado continuou danificado | Mudança de contêiner não recuperou os bytes afetados |
| TS truncado | 53 frames, enquanto o controle tinha 80 | Recuperação parcial do material sobrevivente |
| Remux desse TS | Manteve os 53 frames e retirou avisos observados no decode | Reduzir avisos não demonstra recuperar duração ausente |
| Remux do MP4 íntegro | Hashes do conteúdo decodificado por trilha coincidiram | Controle positivo de cópia sem recodificação |
| Untrunc sem moov | Criou candidato com duas trilhas e retorno zero | Requer comparação detalhada e classificação experimental |

### Investigação do Untrunc

A comparação inicial contou 79 frames no candidato e 80 no controle. Ao usar `-fps_mode passthrough`
para evitar ajuste automático de cadência na saída de comparação, foram obtidos **80 frames em ambos,
com hashes iguais e na mesma ordem**. Assim, a contagem inicial isolada não comprovava perda de imagem.

O áudio do candidato tinha 770.048 bytes PCM mono s16le, contra 768.000 do controle.
Após ignorar apenas na comparação os 2.048 bytes iniciais, equivalentes a 1.024 amostras,
o restante coincidiu byte a byte. Isso é consistente com priming AAC/edit list não reproduzida da mesma forma;
nenhum corte foi aplicado ao candidato.

A saída de comparação do vídeo ainda registrou **DTS não monotônico**. DTS é timestamp de decodificação;
a coincidência de conteúdo não garante continuidade temporal e sincronia. O Untrunc também informou warnings
ocultos em sua execução. Resultado: candidato estrutural útil neste controle, sem aprovação como reparo validado.

Detalhes em [comparacao_detalhada.json](comparacao_detalhada.json). O experimento não autoriza substituir originais.

## 4. Comandos representativos

Exemplos para PowerShell com executáveis disponíveis. Os scripts gerados registram cada argumento real.

```powershell
ffprobe.exe -v error -show_error -show_format -show_streams -of json "C:\teste\entrada.mp4"
ffmpeg.exe -hide_banner -nostdin -v warning -threads 1 -i "C:\teste\entrada.mp4" -map 0:0 -f null NUL
ffmpeg.exe -hide_banner -nostdin -v warning -threads 1 -ss 3.5 -i "C:\teste\entrada.mp4" -map 0:0 -t 1 -f null NUL
ffmpeg.exe -hide_banner -nostdin -v warning -n -i "C:\teste\entrada.mp4" -map "0:v?" -map "0:a?" -c copy "C:\teste\candidato.mkv"
untrunc.exe "C:\teste\referencia.mp4" "C:\teste\copia_sem_moov.mp4"
```

O índice `0:0` é exemplo e deve vir do probe, não ser fixado como vídeo em todos os arquivos.
O Untrunc escreve candidato junto da entrada; fornecer somente cópia em área de trabalho.
Não acrescentar `-y` ou flags de descarte de pacotes como tentativa genérica de consertar.
Os exemplos não incluem supervisor, timeout ou confirmação de operações; não são o motor da aplicação.

## 5. Reproduzir os experimentos

Requisitos: Python 3.11+ e os executáveis das versões registradas, com hashes conferidos.
Dependências do sistema Python não são necessárias. Os binários já estão extraídos em `.research/tools/`
neste ambiente; não fazem parte dos arquivos destinados ao Git.

Executar a partir de `C:\Projetos\NexoVideo` e usar uma pasta de trabalho nova:

```powershell
$taskFfmpeg = (Get-ChildItem -LiteralPath '.research\tools\ffmpeg' -Recurse -Filter ffmpeg.exe -File | Select-Object -First 1).FullName
$taskFfprobe = Join-Path (Split-Path -Parent $taskFfmpeg) 'ffprobe.exe'
python scripts\validar_pesquisa.py --ffmpeg $taskFfmpeg --ffprobe $taskFfprobe --untrunc '.research\tools\untrunc\untrunc_x64\untrunc.exe' --work '.research\run-02'
python scripts\comparar_controle_sintetico.py --work '.research\run-02'
```

O primeiro script retorna código 1 se alguma hipótese falhar; nesta rodada isso ocorreu nas duas verificações
explicadas acima. O segundo script investiga as diferenças e registra resultados, sem converter o candidato em
reparo aprovado. Não interpretar seu retorno zero como homologação de recuperação.
O executor inicial mata o processo direto em timeout; ele não implementa o Windows Job Object requerido pelo produto.

Conferir o relatório `evidencias.json`, a comparação e os logs da nova pasta. Reproduções podem variar com build,
codec, plataforma e versão: os checksums permitem identificar o que foi efetivamente executado.
Os arquivos JSON em `docs/pesquisa/` preservam a rodada original; não os sobrescrever automaticamente.

## 6. Não validado nesta pesquisa

- Arquivos reais de câmeras, HD/4K/8K, longa duração, grandes volumes, VBR e múltiplas trilhas.
- MKVToolNix e GPAC executados, AV1/DNxHD/RAW proprietários e SDKs.
- Referência incompatível e famílias de câmeras com seus parâmetros específicos.
- Truncamento de MP4 que também perca payload, GOP incompleto isolado e fMP4 danificado.
- Recuperação de fragmentos físicos fora de ordem e enxertos entre fontes.
- SQLite do aplicativo, retomada após crash, paginação, teto de RAM e ausência de vazamento.
- Cancelamento de árvore de processos, timeouts do produto e tratamento de falta de espaço/permissões.
- Instalador, assinatura, atualização e regressão completa exigida pelo PRD.

Para habilitar uma estratégia na beta, complementar com esses casos aplicáveis e registrar taxa de sucesso
por formato/codec/falha. A pesquisa documental está entregue; o gate amplo de homologação continua pendente.
