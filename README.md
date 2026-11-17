# Oficina — Orders Service (Fase 4)

Projeto independente preparado para o futuro repositório `pos-tech-fiap-software-architecture-os-service`.

## Estado atual

Domínio extraído com transições e testes unitários; API de infraestrutura com health check e identificação; Dockerfile independente. Os endpoints de negócio, persistência, autenticação, mensageria, infraestrutura cloud e integração externa ainda serão implementados. Este projeto ainda não substitui a aplicação da Fase 3.

Banco planejado: PostgreSQL exclusivo para clientes, veículos, OS e estado da Saga. Nenhum serviço poderá ler o banco de outro.

## Executar

```bash
dotnet run --project src/Orders.csproj --urls http://localhost:8081
dotnet test tests/Tests.csproj --collect:"XPlat Code Coverage"
docker build -t oficina-os:local .
docker run --rm -p 8081:8080 oficina-os:local
```

Endpoints atuais: `GET /health` e `GET /api/info`. Não há dados de negócio persistidos nem credenciais necessárias nesta etapa.

## Próximas etapas

Implementar adaptador de banco próprio, contratos versionados de eventos com inbox/outbox, APIs protegidas, provisionamento Terraform, manifests Kubernetes, pipeline independente com cobertura mínima de 80% e SonarQube, e teste BDD do fluxo distribuído. A Saga será orquestrada no OS Service; compensações financeiras exigem confirmação do estorno antes do cancelamento final.

