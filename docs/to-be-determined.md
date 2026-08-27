# Decisoes Pendentes - Odd Oddities

> Documento vivo para decisoes marcadas como "pendente", "em aberto", "definir depois" ou puladas/adidas durante o processo de discovery e arquitetura.

---

## Decisoes Pendentes

### PEND-001 - Formato final da imagem do post

- **Estagio de origem**: Stage 2 - Requisitos Funcionais.
- **Descricao**: Definir se as imagens serao `1:1`, `4:5` ou configuravel por postagem.
- **Contexto**: A decisao foi adiada por nao impactar diretamente o pipeline. ImageSharp pode produzir os dois formatos sem alteracao estrutural.
- **Status**: Open
- **Como resolver**: Alterar parametro `ImageAspectRatio` em `SystemSettings` quando necessario.

### PEND-002 - Comando administrativo para execucao manual

- **Estagio de origem**: Stage 2 - Requisitos Funcionais.
- **Descricao**: Comando futuro para executar o pipeline manualmente fora do horario.
- **Contexto**: Adiado na POC. Hoje toda execucao passa pelo scheduler.
- **Status**: Open
- **Como resolver**: Criar um comando CLI no Worker, exposto via `--run-now` em uma evolucao futura.

### PEND-003 - Capacidade exata da VPS no painel Contabo

- **Estagio de origem**: Stage 3 - Requisitos Nao Funcionais.
- **Descricao**: Confirmar o plano exato contratado (RAM, disco, banda) no painel da Contabo.
- **Contexto**: Foi informado que a VPS "e a mais barata" e tem outro projeto Docker rodando, totalizando 40 GB livres. Foi decidido reservar 20 GB para o MinIO.
- **Status**: Open
- **Como resolver**: Confirmar com `free -h` e `df -h` via SSH na VPS e atualizar `architecture.md`.

### PEND-004 - Estrategia de recuperacao operacional apos perda do historico

- **Estagio de origem**: Stage 3 - Requisitos Nao Funcionais.
- **Descricao**: Como reagir caso a VPS falhe completamente e o historico (PostgreSQL, MinIO) seja perdido.
- **Contexto**: Foi decidido aceitar perda total. O procedimento de reinstalacao deve ser documentado no runbook operacional.
- **Status**: Open
- **Como resolver**: Gerar runbook antes do go-live com passos para recriar a VPS, restaurar seeds e configurar o pipeline novamente.

### PEND-005 - Modelo de imagem alternativo para fallback

- **Estagio de origem**: Stage 7 - Integracoes Externas.
- **Descricao**: Escolher um modelo de imagem alternativo caso `meta/muse-image` fique indisponivel por periodo prolongado.
- **Contexto**: Recomenda-se escolher um segundo modelo para fallback automatico. Opcoes conhecidas na data da POC: `google/gemini-2.5-flash-image` (USD 0.30/1M output), `openai/gpt-image-1-mini`, `recraft/recraft-v4-styles`.
- **Status**: Open
- **Como resolver**: Avaliar custo/qualidade e adicionar `IMAGE_FALLBACK_MODEL_ID` em `SystemSettings`.

### PEND-006 - Politica de upgrade da VPS

- **Estagio de origem**: Stage 16 - Escalabilidade.
- **Descricao**: Definir sinais que justifiquem migrar para um plano maior na Contabo.
- **Contexto**: Decidiu-se manter "sem upgrade definido" e agir apenas em caso de sinais concretos. Limites nao foram quantificados.
- **Status**: Open
- **Como resolver**: Definir limiares (CPU > 70% sustentado, RAM > 80%, disco > 85%) quando a observabilidade indicar necessidade.

### PEND-007 - Layout final da marca d'agua

- **Estagio de origem**: Stage 2 - Requisitos Funcionais.
- **Descricao**: Decidir posicao, fonte e tamanho exato da marca d'agua "Odd Oddities".
- **Contexto**: Foi decidido que a marca sera discreta, no canto inferior direito. Os detalhes graficos serao definidos durante a implementacao do ImageSharp.
- **Status**: Open
- **Como resolver**: Ajustar `WatermarkSettings` em `SystemSettings` (posicao, fonte, tamanho, opacidade).

### PEND-008 - Runbook operacional

- **Estagio de origem**: Stage 19 - Documentacao.
- **Descricao**: Criar `docs/runbook.md` com procedimentos de deploy, rollback, troubleshooting e renovacao manual do token Meta.
- **Contexto**: Pendencia para antes do go-live. Nao foi gerada na POC inicial.
- **Status**: Open
- **Como resolver**: Gerar o arquivo `docs/runbook.md` antes do primeiro deploy real.

### PEND-009 - Politica de backups

- **Estagio de origem**: Stage 3 - Requisitos Nao Funcionais.
- **Descricao**: Decidir formalmente se backups serao adicionados em uma evolucao futura.
- **Contexto**: Foi decidido aceitar perda total nesta POC. Backups nao foram incluidos por custo e complexidade.
- **Status**: Open
- **Como resolver**: Avaliar viabilidade de snapshot do volume Docker ou export do PostgreSQL para o proprio MinIO em uma evolucao futura.

---

## Estagios Pulados (Adiados)

Nenhum estagio foi pulado. Todos os 23 estagios do processo foram concluidos ou estao refletidos nas pendencias acima.

---

## Itens Resolvidos

Itens resolvidos serao movidos para esta secao conforme decisoes forem tomadas.

- (nenhum ate o momento)
