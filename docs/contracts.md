# Contratos de mensagens v1

Formato JSON em contracts/message-v1.schema.json. Eventos transportam referências e snapshots necessários ao destinatário; nenhum consumidor faz join/acesso ao banco do produtor.

| Origem / operação | Mensagem | Destino | Efeito |
|---|---|---|---|
| OS: abertura | CreateJob | Execution | Diagnosing |
| Execution: diagnóstico | DiagnosisCompleted | OS | Solicita orçamento |
| OS | GenerateBudget | Billing | WaitingApproval |
| Billing | BudgetGenerated | OS | WaitingApproval |
| Billing: aprovação | BudgetApproved | OS | WaitingPayment |
| Billing: pagamento verificado | PaymentConfirmed | OS | ReadyForExecution |
| OS | AuthorizeRepair | Execution | ReadyForRepair |
| Execution | RepairQueued | OS | Confirma fila liberada |
| Execution: Employee /start | RepairStarted | OS | InExecution |
| Execution: /finish | RepairFinished | OS | Finished |
| Billing: rejeição | BudgetRejected | OS | Inicia compensação |
| OS: cancelamento/timeout | CancelJob | Execution | Canceled ou falha operacional |
| Execution | JobCanceled | OS | Solicita cancelamento financeiro |
| OS | CancelBudget | Billing | Canceled ou RefundPending |
| Billing | RefundPayment | Billing | Estorno idempotente |
| Billing | BudgetCanceled | OS | Canceled após as duas confirmações |
| Execution: trabalho físico iniciado | CompensationFailed | OS | ManualIntervention |

id é estável em reenvios de outbox e é a chave de deduplicação. InboxFingerprints impede reutilização do mesmo id com outro tipo/destino/payload, no escopo da OS. Repetições não dependem de correlationId ou traceparent, que podem mudar em um retry. Registros da versão anterior sem fingerprint continuam sendo reconhecidos pela inbox.

correlationId identifica a cadeia; causationId referencia o comando/evento causador; orderId funciona como sagaId; traceParent propaga W3C Trace Context. A versão 1 aceita causationId opcional por compatibilidade.

SQS é de entrega pelo menos uma vez. Eventos tardios de avanço não reiniciam uma Saga em compensação/cancelada. Workflows só avançam a partir de estados previstos. Mensagens para o serviço errado são rejeitadas. Mensagens incompatíveis/falhas repetidas seguem a política de redrive/DLQ do ambiente.

Autorizar reparo não inicia trabalho físico: só Employee pode executar /api/jobs/{id}/start após ReadyForRepair. Isso deixa a compensação financeira previsível enquanto o trabalho está na fila.
