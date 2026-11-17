# Fase 4 — arquitetura implementada

Fonte: SOAT - Fase 4 - Tech challenge.pdf fornecido pelo usuário. As decisões AWS, .NET, PostgreSQL/DynamoDB, SQS e Saga orquestrada são escolhas do projeto.

## Componentes

```mermaid
flowchart LR
  Cliente --> Auth[Lambda CPF]
  Auth -->|API interna autenticada| OS[OS Service / orquestrador]
  Cliente -->|JWT proprietário| Gateway[Gateway / Ingress]
  Funcionario -->|JWT Employee| Gateway
  Gateway --> OS
  Gateway --> Billing[Billing Service]
  Gateway --> Execution[Execution Service]
  OS --> P1[(PostgreSQL OS)]
  Billing --> P2[(PostgreSQL Billing)]
  Execution --> D[(DynamoDB)]
  OS <--> Q[SQS + DLQ]
  Billing <--> Q
  Execution <--> Q
  Billing <--> MP[Mercado Pago]
  OS --> DD[Datadog: logs, métricas e traces OTLP]
  Billing --> DD
  Execution --> DD
```

Cada serviço tem código, testes, Dockerfile, Terraform, Kubernetes e pipelines próprios. As pastas estão dentro do workspace principal, mas cada uma tem seu próprio Git/remoto; o Git pai as ignora. Não há dependências ProjectReference entre serviços.

- OS: https://github.com/Lucas-Rodrigues-Camargo/pos-tech-fiap-software-architecture-os-service
- Billing: https://github.com/Lucas-Rodrigues-Camargo/pos-tech-fiap-software-architecture-billing-service
- Execution: https://github.com/Lucas-Rodrigues-Camargo/pos-tech-fiap-software-architecture-execution-service

## Sequência

```mermaid
sequenceDiagram
  actor C as Cliente
  participant OS
  participant E as Execution
  participant B as Billing
  participant M as Mercado Pago
  C->>OS: Abrir OS (JWT + Idempotency-Key)
  OS-->>E: CreateJob
  E->>E: Diagnóstico pelo funcionário
  E-->>OS: DiagnosisCompleted
  OS-->>B: GenerateBudget
  B-->>OS: BudgetGenerated
  C->>B: Aprovar orçamento
  B-->>OS: BudgetApproved
  C->>B: Criar checkout
  B->>M: Preferences
  M-->>B: Webhook HMAC
  B->>M: Consultar pagamento
  B-->>OS: PaymentConfirmed
  OS-->>E: StartRepair
  E-->>OS: RepairStarted
  E->>E: Finalizar reparo
  E-->>OS: RepairFinished
```

## Saga e consistência

Estado, inbox, outbox e histórico são persistidos atomicamente por agregado. PostgreSQL usa jsonb e compare-and-swap por versão; DynamoDB usa PutItem condicional. A API só confirma o comando depois da persistência. O dispatcher envia antes de retirar da outbox; falha no save permite duplicata, deduplicada pela inbox.

O cancelamento compensa execução antes de cobrança, evitando estorno de trabalho físico que não pode ser cancelado. Se o trabalho já começou, o estado é ManualIntervention. Billing persiste RefundPending antes de chamar o provedor. RefundPayment retenta com X-Idempotency-Key; falha nunca confirma cancelamento. Após JobCanceled e BudgetCanceled, a OS fica Canceled. Timeouts das etapas anteriores ao reparo iniciam compensação; DLQ retém falhas repetidas.

Limites acadêmicos: inbox/histórico embutidos exigem arquivamento antes de alto volume; a outbox Dynamo usa scan paginado. Itens Dynamo têm limite de tamanho. Cobranças duplicadas diferentes exigem revisão; criar preferências de checkout não garante unicidade da cobrança. Não há migração automática de dados da Fase 3.

## Segurança e observabilidade

JWT valida issuer, audiences, assinatura e validade. CPF do cliente é comparado com o proprietário. A Lambda consulta clientes exclusivamente por API interna autenticada. Não há consulta de banco entre serviços.

Webhook verifica HMAC SHA256, request ID, timestamp e consulta autoritativa no Mercado Pago. Valor, moeda BRL e referência da OS devem coincidir. Tokens sandbox e chaves são configuração externa, nunca código versionado.

Logs JSON propagam correlationId e orderId. OpenTelemetry instrumenta HTTP e processamento de eventos, transportando traceparent no envelope e exportando OTLP ao Datadog Agent. Terraform do cluster foi estendido para receiver OTLP, métricas, dashboard e alerta dos três serviços. Referência: https://docs.datadoghq.com/opentelemetry/setup/otlp_ingest_in_the_agent/

## Testes e evidências

Os testes incluem APIs HTTP in-process, autorização e propriedade, workflow, timeout, retries de estorno, idempotência, PostgreSQL real em container, DynamoDB via LocalStack e consumidores/publicação SQS. O gate de linhas exige 80%; apenas código gerado em obj é excluído.

BDD Gherkin com pytest-bdd executa três hosts separados e bancos/filas reais de teste: sucesso com pagamento e rejeição com compensação. Apenas Mercado Pago é um simulador HTTP local. Há workflow manual Distributed BDD no OS Service, com checkout dos três serviços. Para repositórios privados, configurar CROSS_REPO_READ_TOKEN.

## Implantação

1. Provisionar EKS/VPC/Datadog existentes.
2. Criar backend S3 versionado e configurar roles OIDC de deploy.
3. Em cada serviço, configurar TF_STATE_BUCKET e TF_VARS_JSON; ativar INFRA_ENABLED e executar Provision infrastructure.
4. Preencher SERVICE_CONFIG_JSON com outputs e segredos reais. PostgreSQL usa senha do Secrets Manager e TLS.
5. Configurar EKS_CLUSTER, SONAR_HOST_URL e SONAR_TOKEN; ativar DEPLOY_ENABLED. Merge na main publica imagem e faz rollout.
6. Configurar Ingress/TLS público e Mercado Pago sandbox; validar webhook real.
7. Configurar Lambda CUSTOMER_API_URL, INTERNAL_API_KEY e JWT_KEY coerentes com OS.
8. Ativar proteção main/PR e checks nos três repositórios; convidar avaliadores conforme a entrega.

O deploy fica desativado até configuração explícita. Sonar externo é omitido sem token; deve ser configurado para a entrega. O ambiente cloud e a conta Mercado Pago não foram validados com credenciais reais nesta implementação.

## Itens que dependem do grupo

Credenciais/recursos AWS, segredos Mercado Pago sandbox, SonarQube/Datadog, DNS/TLS, política de branch no GitHub, identificação dos participantes e gravação/publicação do vídeo até 15 minutos. O PDF de arquitetura gerado é documentação técnica; a entrega final precisa dessas evidências reais.

## Decisões

- ADR-004: três bounded contexts reduzem acoplamento entre atendimento, financeiro e produção; preservar domínio existente sem migrar a base automaticamente.
- ADR-005: Saga orquestrada mantém a ordem das compensações explícita e auditável.
- ADR-006: PostgreSQL em OS/Billing pela durabilidade e transações; DynamoDB em execução para estado documental e escala independente.
- ADR-007: SQS, inbox/outbox e versão otimista fornecem entrega pelo menos uma vez sem prometer exactly-once.
