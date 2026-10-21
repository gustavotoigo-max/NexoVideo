# NexoVideo 0.1.0-alpha.1 — usar em outra máquina

Versão inicial para testes em **Windows 10 22H2 ou Windows 11, x64**. Testada no ambiente de desenvolvimento;
a homologação dos demais computadores e do corpus real continua pendente. ARM64 nativo e Windows 32 bits não estão incluídos.

## Instalação recomendada

1. Baixe `NexoVideo-0.1.0-alpha.1-win-x64-Setup.exe` no [release oficial](https://github.com/gustavotoigo-max/NexoVideo/releases/tag/v0.1.0-alpha.1).
2. Execute o instalador. A instalação é por usuário e não exige administrador. O runtime .NET 10.0.13 já está incluído.
3. Abra o NexoVideo. Em **Configurações**, clique em **Baixar FFmpeg verificado** e aguarde cerca de 115 MB de download.
   O pacote é obtido diretamente do fornecedor Gyan, com versão 9.0.2 e SHA-256 fixos. As licenças e os documentos originais são preservados.
4. Selecione a pasta de cópias dos vídeos e inicie uma análise. Para a avaliação inicial, use **Completo**, áudio e vídeo.
5. Em **Relatórios**, registre sua avaliação manual e exporte o JSON completo. Veja `VALIDACAO_MANUAL.md` incluído no pacote.

O aplicativo e o instalador ainda não têm assinatura de código. Windows/SmartScreen pode exibir aviso de editor desconhecido;
confirme a origem e o SHA-256 antes de executar, respeitando a política do computador. Não é necessário desativar o antivírus.

## Alternativa portátil

Baixe `NexoVideo-0.1.0-alpha.1-win-x64-portable.zip`, extraia **a pasta inteira** e abra `NexoVideo.exe`.
Mantenha todas as DLLs e subpastas. O pacote inclui o runtime e não precisa instalar o .NET separadamente.
“Portátil” significa execução sem instalador; banco, configurações e ferramentas continuam no perfil do usuário, não junto do executável.

## Preparar uma máquina offline

Em um computador com internet, obtenha o ZIP original:
[FFmpeg 9.0.2 essentials, fornecedor Gyan](https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip).
Transfira esse ZIP e o pacote NexoVideo para a outra máquina. Em Configurações, use **Importar ZIP do FFmpeg**.
A importação confere tamanho e SHA-256 antes de extrair. Também é possível indicar executáveis existentes manualmente.

SHA-256 do ZIP original do FFmpeg:
`60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`.

Após a preparação, a análise funciona offline. Não há upload automático dos vídeos, logs ou avaliações.
O Untrunc é opcional: não acompanha este release; configure um executável obtido separadamente se quiser testar recuperação com referência.

## Dados, integridade e limites

- Dados: `%LOCALAPPDATA%\NexoVideo`. Ferramentas: subpasta `tools`. A desinstalação preserva esses dados.
- O banco local não é sincronizado entre computadores. Exporte o JSON para reunir as evidências de cada rodada.
- `SHA256SUMS.txt` no release permite conferir os downloads com `Get-FileHash -Algorithm SHA256 -LiteralPath CAMINHO`.
  Hashes detectam alteração; não substituem uma assinatura digital independente.
- `build-manifest.json` dentro do pacote registra versão, commit, runtime e hashes dos arquivos publicados.
- Atualização automática ainda indisponível: baixe os próximos pacotes no GitHub.
- Recuperação permanece experimental e cria candidatos separados; “concluído” não garante integridade visual ou sonora.
- Licenças dos componentes acompanham o pacote. Os ícones desta distribuição são vetores próprios; os PNGs exportados do kit Figma não são incorporados.
