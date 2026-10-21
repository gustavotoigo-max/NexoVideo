# Design — adaptação Fortify360

Referência fornecida pelo usuário: [página Cover](https://www.figma.com/design/fQRvjiFcx7wXJw9932eLLh/?node-id=16-730).
Quadro consultado: [Compliance Engine, 16:1153](https://www.figma.com/design/fQRvjiFcx7wXJw9932eLLh/?node-id=16-1153).

## Aplicação ao NexoVideo

- Menu com 256 px, marca em bloco de 40 px, navegação com ícones e estado ativo; configurações no rodapé.
- Barra superior com 64 px, localização da tela e versão real do aplicativo.
- Fonte Inter, cards de raio 12 px, controles de raio 8 px, espaços e tabela derivados do contexto Figma.
- Tema escuro com tokens existentes NexoDesk: fundo `#0A1222`, superfície `#111B2E`, borda `#24324E`,
  azul `#3B82F6`, marca `#0B1733` e destaque ciano `#22D3EE`. Não foi adotada a paleta roxa da referência.
- Quatro cards representam a fila real, sem copiar indicadores de segurança, usuários, nuvem ou porcentagens fictícias.
- Fluxos de análise, recuperação, configurações e relatórios foram mantidos; adaptação visual, não cópia do produto IAM.
- Janela menor mantém acesso por rolagem, com tabela paginada e virtualizada.

## Recursos locais e correspondência

Ícones exportados pelo Figma, sem URLs temporárias em execução. PNGs são usados pelo WPF e SVGs originais
permanecem junto do manifesto de hashes em `src/NexoVideo.App/Assets/Figma/`.

| Recurso | Nó de origem | Posição no aplicativo | Dimensão lógica |
|---|---|---|---|
| brand | 16:1158 | Marca no topo do menu | 16 × 20 |
| analysis | 16:1178 | Navegação Análise | 20 × 20 |
| recovery | 16:1240 | Navegação Recuperação | 20 × 20 |
| reports | 16:1216 | Navegação Relatórios | 20 × 20 |
| settings-nav | 16:1303 | Navegação Configurações | 20,1 × 20 |
| metric-total | 16:1356 | Card de inventário | 16 × 20 |
| metric-completed | 16:1326 | Card de análises concluídas | 22 × 21 |
| metric-failed | 16:1371 | Card de falhas | 21,35 × 21,225 |
| metric-pending | 16:1341 | Card de pendentes | 18 × 20 |

O ícone de configurações do rodapé da referência estava fora do recorte do quadro e sua exportação veio vazia.
Foi usada a engrenagem visível da barra superior do mesmo design, preservada na dimensão de origem.
Frações de pixel dos ícones são preservadas no WPF apesar do arredondamento da exportação PNG.

Inter 4.1 veio do [projeto oficial](https://github.com/rsms/inter/releases/tag/v4.1), faces Regular, Medium,
SemiBold e Bold, com licença OFL em `Assets/Fonts/OFL.txt`, também copiada ao build. Não há instalação global de fonte.
No pré-release, os gráficos exportados do kit foram substituídos por vetores próprios em `Themes/Icons.xaml`; os PNGs históricos permanecem como referência no repositório, fora dos recursos compilados. O layout e a paleta foram preservados.

## Conferência

- Build WPF sem avisos/erros; motor não foi alterado nesta etapa visual.
- Quatro telas renderizadas, navegação ativa correspondente e modo compacto 1160 × 820 conferidos.
- Auditoria do render verifica fonte realmente carregada do pacote e tamanho/posição de cada imagem local.
- Evidências em `.research/figma/ui-*.png` e `.png.json`; captura do executável em `artifacts/dev-preview.png`.
- Os 15 testes do motor da entrega anterior continuam válidos; essa contagem não representa testes de interação visual.

Backup anterior à adaptação: `.research/before-figma-ui-20261021.zip`.
