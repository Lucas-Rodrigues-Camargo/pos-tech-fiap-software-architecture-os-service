# Evidências locais — Fase 4

Verificação em 17/11/2026:

- 23 testes .NET aprovados; cobertura mínima de 80% aplicada por scripts/verify_coverage.py.
- Cobertura atual: 94,25% (o relatório gerado no teste é a fonte de verdade).
- Testes de storage: PostgreSQL em container e DynamoDB no LocalStack.
- Imagem oficina-os:phase4 construída com sucesso em Linux/Docker.
- terraform validate aprovado.
- BDD integrado: 2 cenários aprovados, com três hosts HTTP, bancos isolados e SQS/DynamoDB LocalStack.
- Mercado Pago simulado por servidor HTTP nos testes; sandbox externo ainda precisa das credenciais do grupo.

Para reproduzir, siga README e coverage.runsettings. Código gerado pelo OpenAPI em obj é excluído da cobertura; código de negócio e adaptadores permanecem incluídos. A execução dos testes de Execution foi feita em Linux/Docker devido ao bloqueio local da DLL de testes pela política de aplicativos do Windows.

Artefatos completos são publicados pelo workflow CI no GitHub Actions após execução. Estas evidências locais não comprovam deploy AWS, processamento real de pagamentos ou análise SonarQube externa.
