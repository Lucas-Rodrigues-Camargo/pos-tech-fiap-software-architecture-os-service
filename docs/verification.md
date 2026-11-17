# Evidências verificadas — os

Gerado por scripts/record_evidence.py a partir do último TRX aprovado e do relatório Cobertura.

| Medida | Resultado |
|---|---:|
| Testes executados | 23 |
| Testes aprovados | 23 |
| Testes falhos | 0 |
| Cobertura de linhas | 94.01% |
| Cobertura de branches | 89.09% |
| Mínimo de linhas exigido no CI | 80% |

Código gerado em obj é excluído; código de negócio, APIs e adaptadores utilizados são incluídos. Classes de bootstrap sem uso foram removidas; os testes exercitam o workflow real.

Os testes de persistência usam somente o banco correspondente ao serviço: PostgreSQL em container ou DynamoDB no LocalStack. APIs, autorização, idempotência, fila e Saga são exercitados conforme a matriz dos requisitos. O BDD integrado possui três cenários (sucesso, rejeição e cancelamento pago com estorno).

Os resultados locais podem ser obtidos em Linux/Docker quando a política de aplicativos do Windows bloquear DLLs de teste. O CI usa Linux. Artefatos TRX/Cobertura/OpenCover são publicados pelo GitHub Actions.

Esses resultados não comprovam deploy AWS, aprovação de SonarQube externo ou operação real de Mercado Pago sandbox. Nenhum recurso AWS é criado pelo script de evidências.
