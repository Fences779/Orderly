-- Cloud Sync v1 audit envelope fields required by the frozen protocol.

ALTER TABLE IF EXISTS "CloudAuditLogs"
    ADD COLUMN IF NOT EXISTS "DeviceId" TEXT NULL;

ALTER TABLE IF EXISTS "CloudAuditLogs"
    ADD COLUMN IF NOT EXISTS "Result" TEXT NOT NULL DEFAULT 'Succeeded';

ALTER TABLE IF EXISTS "CloudAuditLogs"
    ADD COLUMN IF NOT EXISTS "CorrelationId" TEXT NOT NULL DEFAULT '';

UPDATE "CloudAuditLogs"
SET "Result" = 'Succeeded'
WHERE "Result" IS NULL OR btrim("Result") = '';

UPDATE "CloudAuditLogs"
SET "CorrelationId" = COALESCE(NULLIF("ClientRequestId", ''), "Id"::text)
WHERE "CorrelationId" IS NULL OR btrim("CorrelationId") = '';

CREATE INDEX IF NOT EXISTS ix_audit_correlation ON "CloudAuditLogs"("WorkspaceId", "CorrelationId");
CREATE INDEX IF NOT EXISTS ix_audit_result ON "CloudAuditLogs"("WorkspaceId", "Result", "OccurredAt" DESC);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cloud_applications_client_request
    ON "CloudUserApplications"("WorkspaceId", "ClientRequestId")
    WHERE "ClientRequestId" IS NOT NULL AND btrim("ClientRequestId") <> '';
