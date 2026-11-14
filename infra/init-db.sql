CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS routing_sops (
    id SERIAL PRIMARY KEY,
    sop_code VARCHAR(32) NOT NULL UNIQUE,
    title TEXT NOT NULL,
    department VARCHAR(64) NOT NULL,
    sla_hours INT NOT NULL,
    content TEXT NOT NULL,
    embedding vector(768)
);

CREATE TABLE IF NOT EXISTS triage_audit_logs (
    id UUID PRIMARY KEY,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    redacted_ticket TEXT NOT NULL,
    pii_entities_scrubbed INT NOT NULL,
    category VARCHAR(64) NOT NULL,
    priority VARCHAR(32) NOT NULL,
    confidence NUMERIC(4,3) NOT NULL,
    routed_to_human_review BOOLEAN NOT NULL,
    matched_sop_code VARCHAR(32),
    rationale TEXT NOT NULL
);
