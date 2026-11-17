# Rastreabilidade — Tech Challenge Fase 4

Fonte normativa: PDF SOAT - Fase 4 - Tech challenge.pdf fornecido pelo grupo. Esta matriz descreve evidências verificáveis; não substitui as evidências externas de entrega.

| Requisito | Implementação e evidência | Validação |
|---|---|---|
| 3 microsserviços independentes | Repositórios OS, Billing e Execution; sem ProjectReference entre serviços | Builds e Dockerfiles independentes |
| Banco próprio por serviço | PostgresStore apenas em OS/Billing, DynamoStore apenas em Execution; roles/configurações isoladas | StorageTests no banco escolhido por cada serviço |
| Ao menos SQL e NoSQL | PostgreSQL para OS/Billing; DynamoDB para execução | Containers locais e LocalStack |
| OS: abertura/status/histórico | src/Workflow.cs e endpoints /api/orders | WorkflowTests e API tests |
| Orçamento, aprovação e pagamento | Billing workflow; Checkout Pro, consulta de pagamento e estorno | MercadoPagoTests e testes HTTP com provedor simulado |
| Integração Mercado Pago | src/MercadoPago.cs, webhook HMAC + conferência de OS/valor/BRL | Contratos HTTP testados; sandbox real depende da conta |
| Diagnóstico/fila/reparo | Execution: diagnosis, fila paginada, pagamento libera ReadyForRepair; Employee inicia /start e finaliza /finish | WorkflowTests, CommandsApiTests e BDD |
| REST e mensageria | APIs protegidas e SQS por serviço; contratos/message-v1 | QueueTests; BDD com SQS LocalStack |
| Nenhum acesso a banco alheio | Adaptador próprio e Lambda consultando OS via HTTP | Projetos separados; teste Node da consulta HTTP |
| Saga com rollback/compensação | OS orquestra; Execution cancela antes do Billing; estorno durável e intervenção após trabalho físico | Testes de rejeição, corrida, timeout, evento tardio e falha/retry do estorno |
| Idempotência e concorrência | Inbox com fingerprint; outbox atômica; versão otimista | ProcessorTests e StorageTests |
| Testes unitários em todos | Regras reais do workflow, autorização, adapters e operações | TRX e docs/verification.json gerados pelos resultados |
| BDD completo | Sucesso pago, rejeição e cancelamento pago com estorno | Gherkin em e2e/features/orders.feature |
| Cobertura mínima 80% | coverage.runsettings exclui apenas obj; scripts/verify_coverage.py bloqueia CI | Cobertura/OpenCover publicados no Actions |
| Qualidade no CI | SonarQube com qualitygate.wait; testes e preflight obrigatórios | Sonar externo precisa das credenciais; deploy bloqueado sem elas |
| CI/CD independente | .github/workflows/ci.yml em cada repositório | Build, testes, configuração, Docker, Terraform validate e rollout |
| Kubernetes | Deployment, Service, ServiceAccount, HPA, probes, limites e Ingress/TLS | Manifests preparados; deploy real depende do cluster existente |
| Observabilidade da Fase 3 | JSON/correlationId/causationId/traceparent, OpenTelemetry e Datadog | Código instrumentado; dashboards/alerta definidos no repo do cluster |
| main protegida / PR obrigatório | Branches feature publicadas para revisão; documentação de checks | Proteção e merge precisam da configuração GitHub |
| Documentação arquitetural | docs/architecture.md, aws-existing-infra.md, contratos e PDF no repositório principal | Fonte e PDF versionados |
| Vídeo até 15 minutos / portal | Roteiro de demonstração e checklist de entrega | Participantes, gravação e publicação pelo grupo |

## Escopo das provas

Os testes de integração de persistência usam PostgreSQL real em container e DynamoDB no LocalStack. O BDD usa três hosts/containers independentes, SQS e bancos separados. O Mercado Pago é um servidor HTTP simulado; não é pagamento real nem validação do sandbox externo.

Contagens e cobertura devem ser consultadas em docs/verification.json, geradas por scripts/record_evidence.py a partir do último TRX aprovado e do relatório Cobertura. Não há dependência de prints manuais para esses números.

## Critérios para considerar a entrega operacional

- CI e quality gate aprovados nos commits entregues.
- Proteção de main habilitada e PRs revisados.
- Ambiente AWS existente configurado e rollout verificado.
- Fluxo Mercado Pago sandbox validado com credenciais reais de teste.
- Logs/traces/dashboard demonstrados.
- PDF final com identificação do grupo e links do vídeo/deploy.

A implementação local e a configuração pronta para deploy não devem ser descritas como comprovação de produção ativa.
