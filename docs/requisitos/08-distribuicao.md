## 8. Módulo DISTRIBUTION — Windows, releases e atualização
- **DS-01:** linguagem/stack pendente: C#, Dart ou Kotlin; decisão por interoperabilidade com FFmpeg, UI Windows, empacotamento e manutenção. Não fixar sem estudo.
- **DS-02:** Inno Setup cria instalador da versão formal.
- **DS-03:** cada ciclo formal de teste para aprovação gera tag/release GitHub, binário, changelog e instalador; testes internos/unitários não criam releases por padrão.
- **DS-04:** verificar releases GitHub em abertura e manualmente, com tratamento para rede ausente e rate limits. Definir repositório público/privado e autenticação antes de codificar.
- **DS-05:** barra superior da primeira janela: status de versão; botão à direita 'Verificar atualizações'; se disponível, vira 'Baixar e instalar'.
- **DS-06:** download com integridade/autenticidade verificada (hash e idealmente assinatura), fechar app e subprocessos, iniciar instalador separado; evitar executável de atualização substituindo a si mesmo.
- **DS-07:** solicitação do produto = execução sempre como administrador. Implementar manifesto/UAC se confirmada necessidade; documentar riscos (principalmente varreduras em pastas e execução de ferramentas externas elevadas).
- **DS-08:** encerrar árvore de subprocessos ao fechar/cancelar, preferencialmente Windows Job Objects; não deixar FFmpeg ou reparadores órfãos.
- **DS-09:** configurar versão e rastreabilidade do build; não armazenar token GitHub no instalador.
- **DS-10:** revisão de licenciamento de dependências (FFmpeg varia conforme build; untrunc GPL); não presumir que todas podem ser redistribuídas num produto fechado.

