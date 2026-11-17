-- V001: immutable initial storage contract.
-- State, inbox IDs, history and pending outbox share one atomic document.
CREATE TABLE IF NOT EXISTS workflow_state (
    id uuid PRIMARY KEY,
    version bigint NOT NULL,
    document jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS workflow_pending
    ON workflow_state ((jsonb_array_length(document->'outbox')))
    WHERE jsonb_array_length(document->'outbox') > 0;
