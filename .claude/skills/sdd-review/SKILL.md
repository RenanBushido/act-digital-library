name: sdd-review
description: "Revisar uma change do OpenSpec em um dos dois momentos do ciclo: a especificação antes do /opsx:apply, ou o código implementado antes do /opsx:archive. Termina com veredito explícito."
---

# OpenSpec Review

## Objetivo

Revisar uma change do OpenSpec em um dos dois momentos de controle do ciclo, reduzindo retrabalho e garantindo que o código entregue seja rastreável até uma decisão escrita.

/opsx:propose → [REVISÃO PRÉ-APPLY: especificação] → /opsx:apply → [REVISÃO PRÉ-ARCHIVE: implementação] → /opsx:archive

## Qual revisão fazer

Detecte pelo estado da change antes de começar. Não pergunte se der para inferir.

| Estado observado | Revisão |
|---|---|
| `tasks.md` sem tarefas concluídas, sem código correspondente no diff | **Pré-apply** — a especificação |
| `tasks.md` com tarefas marcadas, código já implementado | **Pré-archive** — a implementação |
| Ambíguo | Pergunte ao usuário qual das duas ele quer |

Comandos úteis para orientar a revisão: `openspec list`, `openspec status --change <nome>`, `openspec show <nome> --json`, `openspec validate <nome> --strict`, e `git diff --stat` na revisão pré-archive para delimitar o que o apply tocou.

---

# REVISÃO PRÉ-APPLY — a especificação

Momento: depois do `propose`, antes de existir qualquer código.

Pergunta que ela responde: *a especificação descreve a solução certa, de forma completa e sem ambiguidade?*

Erro encontrado aqui custa uma edição de markdown. O mesmo erro encontrado depois custa um apply inteiro.

Ler: `proposal.md`, `design.md`, `tasks.md`, `specs/` da change, `CLAUDE.md` e as specs principais em `openspec/specs/` que a change altera.

## Consistência

Validar se todos os artefatos descrevem a mesma solução. Identificar:

- Requisitos conflitantes entre artefatos
- Funcionalidade descrita em apenas um artefato
- Tarefa sem requisito associado (escopo que vazou)
- Requisito sem tarefa correspondente (implementação que não vai acontecer)
- Delta `MODIFIED` ou `REMOVED` que não corresponde a nada na spec principal

## Qualidade dos cenários

Cada requisito precisa de cenários `GIVEN/WHEN/THEN` que virem teste sem tradução. Identificar:

- Cenário vago demais para virar assert ("o sistema deve funcionar corretamente")
- Ausência do caso negativo — a rejeição, o limite, o estado inválido
- Ausência do caso concorrente ou idempotente, quando o requisito envolve escrita
- Comportamento observável misturado com detalhe de implementação

## Escopo

Identificar escopo excessivo para uma única change, funcionalidades que deveriam virar changes separadas, e dependências entre elas. Sugerir quebras em capacidades menores quando o delta ficar grande demais para revisão linha a linha.

## Arquitetura

O `CLAUDE.md` do projeto é a autoridade — inclusive quando ele rejeita um padrão consagrado. Não recomende camadas, mediator ou abstração de repositório se o `CLAUDE.md` optou por não tê-los. Divergir da decisão declarada só é achado quando a decisão está sendo **violada**, nunca quando ela apenas contraria uma preferência. Só na ausência total de `CLAUDE.md`, use S.O.L.I.D. e também se possível o Domain Driven Design como referência.

Identificar:

- Violações do padrão que o projeto declarou
- Estrutura de pastas inconsistente com o resto do projeto
- Componentes grandes demais
- Acoplamento desnecessário
- Riscos para manutenção futura

## Implementabilidade

Avaliar se a change pode ser implementada com as informações existentes. Identificar dependências, serviços, entidades, casos de uso e fluxos ausentes. Verificar se os pontos de teste unitário estão previstos.

## Banco de dados

Identificar entidades incompletas, relacionamentos ausentes, dados necessários não previstos, e problemas potenciais de persistência (índices, unicidade, tipos temporais, invariantes que deveriam virar constraint no banco em vez de só validação em código).

---

# REVISÃO PRÉ-ARCHIVE — a implementação

Momento: depois do `apply`, antes de fundir o delta na spec principal.

Pergunta que ela responde: *o código faz o que a spec diz, só o que ela diz, e com a qualidade que o projeto exige?*

É aqui que se pega escopo extra, cenário sem teste e regra implementada diferente do combinado.

Comparar o código implementado contra `specs/`, `design.md` e `tasks.md` da change.

## Cobertura

Para **cada requisito** do delta, apontar explicitamente onde ele foi implementado (arquivo e símbolo). Marcar como problema:

- Requisito sem implementação localizável
- Cenário sem teste correspondente
- Tarefa marcada como concluída sem código que a sustente

## Fidelidade

- Regra implementada de forma diferente da que o `design.md` decidiu, sem o design ter sido atualizado
- Comportamento que contradiz um cenário `GIVEN/WHEN/THEN`
- Código fora do escopo da change (refatoração oportunista, feature não pedida) — apontar mesmo quando a mudança for boa; ela pertence a outra change

## Invariantes declarados

Verificação obrigatória, e não pode ser resumida. Abra o `CLAUDE.md`, percorra a seção **Proibições** e a tabela do **Contrato HTTP**, e reporte item por item com veredito: `ok`, `violado` ou `não aplicável a esta change`. Nunca conclua "segue o CLAUDE.md" sem essa enumeração — é onde as violações silenciosas aparecem.

Pontos de atenção, porque falham sem quebrar build nem teste:

- **Cache no caminho de decisão.** Rastreie o handler de criação de empréstimo até o banco. Leitura de cache decidindo disponibilidade é achado de risco Alto.
- **Idempotência fora da transação.** A gravação da resposta idempotente e a criação do empréstimo precisam estar na mesma transação. Duas transações é risco Alto.
- **Auditoria fora da transação do fato**, ou emitida via `ILogger` em vez de tabela.
- **Health check de liveness com dependência.** Qualquer verificação de banco ou cache alcançada pelo endpoint de liveness é violação literal de requisito.
- **Exclusão mútua em processo** — `lock`, `SemaphoreSlim`, dicionário estático, `IMemoryCache`. Inválido em aplicação com múltiplas réplicas.
- **Nomes de métrica divergentes** dos declarados no `CLAUDE.md`, incluindo os valores permitidos de cada tag.
- **Status HTTP ou `type` de Problem Details** fora da tabela do Contrato HTTP.
- **Relógio obtido direto** (`DateTime.UtcNow`, `DateTime.Now`) em vez do abstraído; **`CancellationToken`** não propagado até a última chamada de I/O.
- **`DELETE` físico** onde o projeto exige preservação de histórico.

## Qualidade do código

- Aderência ao `CLAUDE.md` — ele é a autoridade sobre arquitetura, inclusive sobre o que decidiu não usar
- Tratamento de erro consistente com o padrão do projeto
- Ausência de valores fixos que deveriam ser configuração; nenhum segredo no código
- Recursos liberados corretamente; assincronia sem bloqueio
- Testes que realmente falham quando a regra é quebrada — desconfiar de teste que passa por acidente

## Verificação executável

Quando possível, executar e reportar o resultado em vez de opinar: `openspec validate <nome> --strict`, mais o build, os testes e o linter do projeto.

---

# Riscos

Classificar cada problema encontrado como **Alto**, **Médio** ou **Baixo** risco. Apontar ambiguidades, decisões não documentadas e dificuldades previsíveis de implementação ou manutenção.

# Material de apoio

Consultar o MCP Context7 quando estiver disponível, para confrontar a documentação oficial com o código gerado. Se não estiver disponível, dizer isso explicitamente no relatório: a verificação contra documentação oficial não foi feita. Nunca confirmar comportamento de biblioteca de memória.

---

# Resultado esperado

Iniciar informando qual das duas revisões foi aplicada e por quê. Depois:

### Pontos positivos

Aspectos bem definidos ou bem implementados. Ser específico — elogio genérico não ajuda.

### Problemas encontrados

Um item por problema, com arquivo e linha quando aplicável, e a classificação de risco. Ordenar do mais grave para o menos grave.

### Recomendações

Ação concreta para cada problema, na ordem em que deve ser executada.

### Conclusão

Um único veredito, sem hedge:

- **Pronto para Apply** (revisão pré-apply)
- **Pronto para Archive** (revisão pré-archive)
- **Requer ajustes** — seguido da lista mínima de correções que mudam o veredito

Se nada de relevante for encontrado, dizer isso claramente em vez de inventar observações para preencher a seção.