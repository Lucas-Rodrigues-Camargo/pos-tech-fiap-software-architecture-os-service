# Executar sobre infraestrutura AWS existente

Este é o caminho padrão da entrega. Nenhum comando deste guia exige criar VPC, EKS, RDS, ECR, DynamoDB ou SQS. O Terraform permanece como referência reproduzível e opcional; o workflow de provisionamento só aceita execução manual com confirmação explícita e INFRA_ENABLED=true.

## Contrato do ambiente

| Recurso pré-existente | Requisito da aplicação |
|---|---|
| EKS | Namespace oficina-phase4; deploy role autorizada pelo access entry/RBAC |
| Compute | Nodes suficientes para 2 réplicas por serviço; metrics-server para HPA |
| Identidade AWS | EKS Pod Identity Agent; ServiceAccount oficina-os, oficina-billing e oficina-execution associados às suas roles |
| PostgreSQL OS | Banco exclusivo, usuário com privilégios apenas nesse banco/schema, conectividade dos pods e TLS |
| PostgreSQL Billing | Banco e credencial diferentes do OS; conectividade e TLS |
| DynamoDB Execution | Tabela com partition key string id; role limitada a essa tabela |
| SQS | Uma fila de entrada por serviço, com DLQ e redrive após 5 tentativas |
| ECR | Um repositório por serviço; nomes padrão oficina-os, oficina-billing, oficina-execution ou variable ECR_REPOSITORY |
| Rede | Pods alcançam banco próprio, endpoints AWS e Mercado Pago; bancos não públicos |
| Gateway/TLS | IngressClass nginx existente e secret TLS válido no namespace |
| Datadog | Agent com coleta JSON e receiver OTLP gRPC na porta 4317 dos nodes |

O app usa inbox/outbox em seu próprio banco. Em OS/Billing, a versão atual executa o DDL inicial idempotente de migrations/V001__workflow.sql ao iniciar. O usuário de banco precisa poder criar tabelas e índices apenas no seu schema; não precisa de privilégios sobre os outros bancos. Em Execution, a tabela DynamoDB deve existir antes do deploy.

## Permissões de runtime

Cada role de pod precisa receber/deletar mensagens apenas de sua própria fila, publicar nas filas dos participantes da Saga e consultar atributos da fila. Somente Execution recebe GetItem, PutItem e Scan na sua tabela DynamoDB. O processo não precisa de permissão para criar recursos AWS. Não forneça AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY nos Secrets de produção: use Pod Identity.

A role de deploy é separada: precisa publicar imagens no ECR e aplicar recursos no namespace Kubernetes. Configurar OIDC e acesso ao cluster é responsabilidade do ambiente existente.

## Configuração por repositório

Secrets GitHub:
- AWS_DEPLOY_ROLE_ARN: role OIDC de deploy.
- SERVICE_CONFIG_JSON: objeto JSON de strings seguindo config/environment.example.json.
- SONAR_TOKEN: credencial do quality gate.
- CROSS_REPO_READ_TOKEN: somente para BDD com repositórios privados.

Variables:
- EKS_CLUSTER, AWS_REGION, SONAR_HOST_URL.
- ECR_REPOSITORY se o nome existente for diferente do padrão.
- INGRESS_HOST e TLS_SECRET para publicar o Ingress; sem INGRESS_HOST o pipeline mantém apenas o Service interno.
- DEPLOY_ENABLED=true somente após concluir a configuração.

INFRA_ENABLED deve permanecer ausente/false quando a infraestrutura já existe. TF_STATE_BUCKET e TF_VARS_JSON não são necessários para o deploy da aplicação nesse cenário.

## Validar antes de deploy

Preencha um arquivo de configuração fora do Git e execute:

```bash
python scripts/validate_deployment.py /caminho/config-real.json
```

O validador é local, não acessa AWS e não imprime os segredos. Rejeita placeholders, chave JWT curta, filas iguais/trocadas, endpoints locais, credenciais AWS estáticas, strings multilinha e PostgreSQL sem VerifyFull/CA. O deploy executa essa validação antes da autenticação AWS.

Use a mesma Jwt__Key e issuer da Lambda e do emissor de tokens de funcionários existente. No OS, Internal__Key precisa coincidir com INTERNAL_API_KEY da Lambda. CUSTOMER_API_URL deve terminar em /internal/auth/customers. No Billing, configure credenciais sandbox, webhook secret e URL HTTPS pública; não use o servidor simulado de BDD nesse ambiente.

## Ativação

1. Configurar Secrets/Variables e conferir o contrato de recursos.
2. Abrir PR e deixar build, testes, cobertura, configuração e Terraform validate aprovados.
3. Configurar SonarQube; o deploy habilitado exige o token e o quality gate.
4. Fazer merge na main: imagem recebe tag do commit, migrações iniciais são verificadas pelo app, manifests são aplicados e rollout é aguardado.
5. Conferir /ready, autenticação, um fluxo sandbox e as correlações no Datadog.

O pipeline não executa Terraform apply para fazer deploy da aplicação. A proteção de main, os secrets, a resolução DNS/TLS e a verificação real do ambiente continuam sendo configurações externas; os testes locais não substituem essas evidências.
