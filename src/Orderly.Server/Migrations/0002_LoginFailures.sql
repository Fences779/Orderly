-- Persist every failed cloud login attempt, including unknown usernames that cannot be tied to a workspace audit row.

CREATE TABLE IF NOT EXISTS "CloudLoginFailures" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NULL REFERENCES "CloudWorkspaces"("Id"),
    "UserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "Username" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "ClientRequestId" TEXT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "OccurredAt" TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_login_failures_workspace ON "CloudLoginFailures"("WorkspaceId", "OccurredAt" DESC);
CREATE INDEX IF NOT EXISTS ix_login_failures_username ON "CloudLoginFailures"("Username", "OccurredAt" DESC);

