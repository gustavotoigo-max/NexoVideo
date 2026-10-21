# Pesquisa técnica — NexoVideo

Pesquisa em 21/10/2026. Escopo: viabilidade, escolha técnica e experimentos sintéticos; nenhuma beta aprovada.

**Recomendação: C# com .NET 10 LTS, WPF e SQLite, utilizando FFprobe/FFmpeg em subprocessos.**
É a combinação mais alinhada ao alvo Windows, controle de processos e persistência sequencial.
A recomendação é uma avaliação técnica; os resultados de mídia estão separados em [Experimentos](EXPERIMENTOS.md).

## 1. Comparação de stacks — DS-01

| Opção | Integração com Windows | Interface e distribuição | Avaliação para este produto |
|---|---|---|---|
| **C# / .NET 10 / WPF** | P/Invoke para APIs Win32, processos e arquivos na mesma base | Templates XAML, binding, publicação com runtime e Inno Setup | Recomendada: atende ao alvo único e facilita controlar o ciclo de vida dos reparadores |
| Dart / Flutter | Processos em Dart; APIs específicas exigem FFI ou plugin/C++ | Interface customizável; build Windows exige toolchain C++ | Alternativa se compartilhar UI com outras plataformas passar a ser requisito |
| Kotlin / Compose Desktop | Processos na JVM; APIs Windows exigem ponte nativa | JVM e módulos empacotados com jlink/jpackage | Viável, mas adiciona uma camada para as integrações Win32 deste projeto |

WPF é exclusivo de Windows e oferece estilos/templates. O .NET 10 é LTS, com suporte até novembro de 2028,
condicionado à atualização dos patches. Flutter suporta Windows; Compose Desktop usa JVM.
Fontes: [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/),
[suporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core),
[Flutter Windows](https://docs.flutter.dev/platform-integration/windows/building),
[distribuição Compose](https://kotlinlang.org/docs/multiplatform/compose-native-distribution.html).

WPF é preferível a introduzir WinUI 3 neste caso pela disponibilidade dos recursos XAML locais e pelo
escopo de tabelas, filas e configurações. Isso não constitui benchmark entre frameworks.
Não foram medidas RAM, inicialização ou tamanho de instalador das três stacks.

**Ambiente observado:** SDK .NET 10.0.401 instalado; Python 3.14 disponível; FFmpeg fora do PATH antes dos experimentos.
`NexoDesk New/src/NexoDesk.Ui/NexoDesk.Ui.csproj` já utiliza `net10.0-windows` e `UseWPF`.
O projeto vizinho também tem componentes WinForms; não foi presumido que todo o NexoDesk seja WPF.

## 2. Ferramentas e limites — AN, RC, FM

| Ferramenta | Uso proposto | Situação da pesquisa |
|---|---|---|
| FFprobe | Contêiner, streams, metadados e erros de identificação | Executado em controles íntegros e danificados |
| FFmpeg | Decodificação, amostragem, remux e extração | Executado; código zero não significou integridade |
| Untrunc, fork anthwlock | Reconstrução de MP4 com referência compatível | Executado em MP4 sem `moov`; candidato exigiu ressalvas temporais |
| MKVToolNix | Inspeção JSON e remux de Matroska | Documentado; não executado nesta rodada |
| GPAC/MP4Box | Inspeção/manipulação ISO BMFF | Documentado; não executado, manter experimental |
| MP4Recover | Referência de estratégias/orquestração | Lido; não integrar como dependência do desktop |

**Contêiner** organiza trilhas e índices; **codec** codifica o conteúdo da trilha.
**Remux** troca/recria o contêiner copiando os pacotes, sem recodificá-los.
`moov` guarda metadados/índices relevantes do MP4; `mdat` armazena dados de mídia.
Reconstruir índices não recria bytes de imagens ou áudio que deixaram de existir.

O Untrunc pede um arquivo íntegro semelhante, idealmente da mesma câmera. Seu README também alerta
para incompatibilidades com estruturas internas de versões novas do FFmpeg ao compilar.
Usar executáveis separados evita compartilhar bibliotecas entre os dois processos, mas não comprova qualidade de reparo.
Fonte: [Untrunc](https://github.com/anthwlock/untrunc).

MKVToolNix fornece identificação e manipulação Matroska; saída 1 do mkvmerge significa avisos,
e 2 significa erro. Adaptadores precisam interpretar a convenção de cada ferramenta.
Fontes: [documentação](https://mkvtoolnix.download/docs.html),
[mkvmerge](https://mkvtoolnix.download/doc/mkvmerge.html).

MP4Box é ferramenta de empacotamento/manipulação; não há evidência aqui de reconstrução universal de arquivos.
MP4Recover usa Docker e várias estratégias, inclusive recodificação; sua licença MIT não substitui as licenças
das ferramentas chamadas. Fontes: [MP4Box](https://wiki.gpac.io/MP4Box/MP4Box/),
[MP4Recover](https://github.com/ActiveTK/MP4Recover).

## 3. Análise e evidência — AN-01 a AN-08

### Rápido

Consultar formato/streams em JSON e registrar erro, retorno do processo e versão da ferramenta.
Identificação pode examinar parte do conteúdo, mas não demonstra que todas as trilhas decodificam.
Limitar tempo, saída e parâmetros de sondagem; timeout deve produzir resultado inconclusivo.
Fonte: [FFprobe](https://ffmpeg.org/ffprobe.html).

### Intermediário

Decodificar realmente cada stream escolhido em janelas distribuídas. Proposta de algoritmo:

1. Para N amostras e duração conhecida, distribuir entre início e `max(0, duração - janela)`.
2. No modo MB, estimar quantidade/posições por tamanho e duração; identificar a estimativa na UI.
   Um índice de pacotes pode melhorar o mapeamento, mas sua obtenção também custa leitura.
3. Unir janelas sobrepostas; limitar quantidade, tempo e buffers. Não usar offsets arbitrários de bytes no decoder.
4. Ler o início diretamente, sem seek desnecessário; buscar os demais pontos quando possível.
5. Registrar tempos solicitados e timestamps originais efetivamente decodificados por trilha, com sua base temporal.
6. Seek falho, duração desconhecida, janelas vazias ou limites atingidos: parcial/inconclusivo, oferecer análise completa.

FFprobe documenta seek impreciso em `read_intervals`. O protótipo de pesquisa testa janelas,
mas **ainda não mede a cobertura efetiva na linha temporal original**. PTS normalizado da saída não basta.
Fonte: [FFprobe, read_intervals](https://ffmpeg.org/ffprobe.html#Main-options).

### Completo

Decodificar as trilhas selecionadas até o encerramento, mantendo diagnóstico por stream e intervalos observados.
As execuções desta pesquisa foram separadas por trilha para isolar erros, sempre sequenciais.
Na aplicação, avaliar uma passagem única com telemetria por stream para reduzir releituras; não declarar
essa instrumentação resolvida apenas por usar `-map` ou `-progress`.

Retorno zero, contagem de frames, duração e mensagens são evidências diferentes.
`-xerror` aborta no primeiro erro e não é padrão adequado para mapear todos os defeitos.
`-max_alloc` limita uma alocação individual, não a RAM total do processo.
Fonte: [FFmpeg](https://ffmpeg.org/ffmpeg.html).

Decoder pode ocultar/conceal erros; nem saída sem avisos prova ausência de defeitos visuais ou acústicos.
Manter flags de detecção registradas; não converter aviso genérico automaticamente em corrupção de conteúdo.
Fonte: [opções dos codecs](https://ffmpeg.org/ffmpeg-codecs.html).

Estados de diagnóstico propostos, independentes do estado da fila:

- Estrutura identificada; trilha declarada; decodificação observada.
- Amostragem sem erro observado; decodificação completa sem erro observado.
- Falha estrutural; falha de decode; parcial/inconclusivo; formato não homologado.
- Ausência de áudio observada; áudio esperado somente quando houver evidência da expectativa.

## 4. Arquitetura recomendada — PF-06 a PF-12

Fluxo obrigatório: **um vídeo → análise → processamento do resultado → commit SQLite → liberação → próximo**.
Não permitir que uma segunda janela ou reinício dispare outro worker concorrente sobre a mesma fila.
Usar exclusão mútua entre instâncias e reivindicação transacional de item; recuperação consulta o banco.

| Camada | Responsabilidade |
|---|---|
| UI WPF | Exibir janela paginada de dados, configurações, progresso e cancelamento |
| Coordenador de fila | Uma tarefa ativa; pausa entre arquivos; retomada e checkpoints |
| Analisadores/adaptadores | Construir argumentos e interpretar respostas por ferramenta |
| Supervisor Windows | Timeouts, saída limitada, término da árvore, limites e métricas de processos |
| Repositório SQLite | Inventário, tentativas, trilhas, testes, erros e candidatos de reparo |
| Operações de arquivos | Destino separado, colisões, prévia e execução autorizada |

**Pausa proposta:** concluir o arquivo atual e não iniciar o seguinte. Pausa imediata, se desejada, interrompe
o arquivo e o devolve a pendente; não prometer congelamento transparente do decoder.
Ao retomar, `em_análise` residual vira pendente/interrompido. Não interpretar interrupção como sucesso.
Configuração de concorrência permite threads internas do codec; não múltiplos vídeos, pois AN-08/PF-06 prevalecem.

### Persistência

SQLite em disco local saudável, fora da origem e fora de compartilhamento de rede.
Proposta: `jobs`, `files`, `attempts`, `streams`, `tests`, `errors`, `repair_candidates` e `operations`.
Usar índices por job/estado/ordem e paginação por chave; anexar resultados incrementalmente.
Persistir assinatura de tamanho/data/identidade e parâmetros/versão da análise para invalidar cache;
sem hash completo, alterações de conteúdo que preservem metadados continuam sendo uma limitação.

Um escritor, transações curtas, `foreign_keys=ON`, timeout de banco, política de checkpoint e `synchronous=FULL`
como ponto de partida conservador. WAL pode permitir leitura da UI enquanto o worker grava;
não funciona como solução geral para banco em rede. Backup consistente precisa incluir o estado WAL
por API de backup ou procedimento coordenado, não apenas copiar o `.db` aberto.
Exigir SQLite nativo com correção do WAL-reset, por exemplo 3.51.3+; conferir a versão em execução.
Fonte: [SQLite WAL](https://sqlite.org/wal.html).

`Microsoft.Data.Sqlite` executa suas operações async de forma síncrona. Conexões e comandos devem
ficar em worker dedicado; colocar `await` no thread da UI não resolve.
Fonte: [limitações async](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

Falha ao persistir: parar fila, manter original intacto e registrar falha de infraestrutura em log separado limitado.
Não manter a transação aberta durante decodificação. Resultado e estado concluído devem ser gravados juntos.

### RAM e processos

Não ler vídeos inteiros; drenar stdout/stderr simultaneamente e processar incrementalmente.
Logs com limite/rotação; pequenas filas de mensagens e progresso com frequência limitada.
WPF precisa de paginação de dados além de virtualização de controles: esta não impede carregar uma coleção enorme.
Fonte: [virtualização WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls).

Agrupar subprocessos em Windows Job Objects com término ao fechar o job. Evitar a corrida entre criar
processo e associá-lo ao job; considerar criação suspensa, associação e retomada.
Esperar toda a árvore encerrar, drenar saídas e fechar handles antes de liberar o item.
Limites de memória devem tratar falha de alocação como interrupção/inconclusivo; ainda precisam de medição.
Fonte: [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

Os experimentos curtos não comprovam teto de RAM, cancelamento da árvore ou estabilidade em lotes grandes.
Não propor GC forçado por arquivo como substituto de gerenciamento de referências/handles.

## 5. Recuperação e proteção — RC, FL, PERF

1. Classificar a falha; preservar origem/proveniência. Sidecar não equivale a backup.
2. Selecionar estratégia admissível pela [matriz](MATRIZ.md), sem tentativas cegas de todas as ferramentas.
3. Executar em área de trabalho independente; para ferramentas que escrevem junto da entrada, fornecer cópia.
4. Reanalisar o candidato; comparar trilhas, conteúdo decodificável, continuidade, duração e A/V.
5. Disponibilizar candidato e limitações. Operar na origem somente com o fluxo explícito exigido pelo PRD.

A retirada de avisos no candidato não é evidência suficiente de melhora. Priming de AAC (amostras iniciais
do codificador) e edit lists podem mudar o início temporal mesmo com conteúdo preservado.
Enxertos e reconstrução de headers/timecodes permanecem indisponíveis até testes específicos.

Perfis propostos: cópia saudável permite sondagem/amostragem; mídia instável prioriza clone e leitura linear.
Não usar duração ou endereço lógico para alegar ordem física real dos dados no disco.
Excluir saída da enumeração; não seguir junctions por padrão; proteger nomes, espaço e arquivos em uso.

Executáveis devem vir de local controlado, com argumentos estruturados e caminhos absolutos.
Para análise local, restringir protocolos do FFmpeg e não aceitar playlists/remotos automaticamente;
isso ainda não restringe todos os acessos locais referenciados por um contêiner.
Fonte: [protocolos FFmpeg](https://www.ffmpeg.org/ffmpeg-protocols.html).

## 6. Licenças e distribuição — RC-10, DS

| Componente | Situação verificada | Consequência para o projeto |
|---|---|---|
| FFmpeg | Licença varia com configuração; build de pesquisa contém `--enable-gpl --enable-version3` | Não tratar esse binário como LGPL; inventariar fontes/licenças do build antes de redistribuir |
| Untrunc | Repositório identificado como GPL-2.0; binário testado incorpora FFmpeg 3.3.4 | Auditar versão, fontes, dependências e licença efetiva do pacote |
| MKVToolNix | Mantenedor esclareceu GPL-2.0-only | Não presumir GPL-2.0-or-later nem compatibilidade de incorporação ao mesmo programa GPLv3 |
| GPAC | COPYING inclui LGPL 2.1; dependências opcionais também importam | Auditar o pacote selecionado, não apenas o nome do projeto |
| MP4Recover | Código de orquestração MIT | Ferramentas executadas conservam suas próprias condições |
| Inno Setup | Site oficial solicita compra para uso comercial | Rever termos aplicáveis quando o modelo de distribuição for definido |

Fontes: [FFmpeg legal](https://ffmpeg.org/legal.html),
[licença Untrunc](https://github.com/anthwlock/untrunc),
[esclarecimento do mantenedor MKVToolNix](https://help.mkvtoolnix.download/t/question-about-license/1597),
[GPAC COPYING](https://github.com/gpac/gpac/blob/master/COPYING),
[MP4Recover](https://github.com/ActiveTK/MP4Recover), [Inno Setup](https://jrsoftware.org/isinfo.php).
Executar via subprocesso não elimina as obrigações de redistribuição nem resolve automaticamente a classificação
jurídica da integração. O modelo de licença do NexoVideo continua indefinido.

O GitHub informou que NexoVideo é público. Consulta pública de releases pode dispensar token.
Canal estável e canal beta precisam de regras distintas: o endpoint “latest” não seleciona prereleases.
Tratar 404/sem releases, rede ausente e limites de API, com cache e consulta manual.
Fonte: [API de releases GitHub](https://docs.github.com/en/rest/releases/releases).

Hash verifica correspondência do arquivo, mas um hash fornecido pelo mesmo canal não prova sozinho a autenticidade.
Proposta: instalador assinado e manifesto de atualização assinado com chave pública incorporada; verificar ambos
antes de executar instalador separado. Ainda não implementado nem testado.

Não gerar release neste marco. Confirmar política de versão formal antes do pipeline final.
DS-07 pede elevação contínua, condicionada no PRD à necessidade; manter a decisão pendente, sem mudar o requisito.
A inspeção/decodificação de arquivos acessíveis não demonstra necessidade de administrador.

## 7. Base visual local — UI-07/UI-08

Arquivos inspecionados, somente leitura:

- `C:\Projetos\NexoDesk New\src\NexoDesk.Ui\Themes\Brand.xaml`
- `C:\Projetos\NexoDesk New\src\NexoDesk.Ui\Themes\Light.xaml`
- `C:\Projetos\NexoDesk New\src\NexoDesk.Ui\Themes\Dark.xaml`
- `C:\Projetos\NexoDesk New\src\NexoDesk.Ui\ThemeManager.cs`

| Token | Claro | Escuro |
|---|---|---|
| Azul-marinho de marca | `#0B1733` | `#0B1733` |
| Azul de ação | `#2563EB` | `#3B82F6` |
| Ciano de destaque | `#22D3EE` | `#22D3EE` |
| Fundo | `#F4F6FA` | `#0A1222` |
| Superfície | `#FFFFFF` | `#111B2E` |
| Texto principal | `#0F172A` | `#E6ECF5` |

Fontes locais: Segoe UI Variable Text / Segoe UI; monoespaçada Cascadia Mono / Consolas.
Usar essa paleta na próxima etapa, sem roxo. O kit Figma permanece referência de composição;
não foi copiado nem inspecionado visualmente nesta pesquisa. Acesso e direitos de adaptação estão pendentes.

## 8. Resultado e gate para a primeira beta

Pesquisa documental concluída e experimento inicial executado. **Gate de homologação ainda não aprovado.**
Há evidência de viabilidade do núcleo e evidência direta de que saída zero, remux e candidato reproduzível
não bastam para declarar integridade ou reparo validado.

Antes de habilitar suporte em beta:

- Corpus por contêiner/codec/falha, incluindo referências de câmeras diferentes e falhas reais autorizadas.
- Registrar número de tentativas, êxitos pelo critério definido e perdas; não extrapolar taxa sintética.
- Validar fim/início, múltiplas trilhas, VBR, GOPs, timestamps, fragmentos e arquivos muito grandes.
- Medir memória, vazamento de handles, cancelamento, retomada/falha de banco e fila única entre instâncias.
- Validar destino, colisões, junctions, permissões, falta de espaço e preservação dos originais.
- Fechar licenças, versões, distribuição, assinatura e empacotamento conforme decisões do produto.

O próximo marco previsto no PRD é arquitetura detalhada e UI, usando esta pesquisa como base.
