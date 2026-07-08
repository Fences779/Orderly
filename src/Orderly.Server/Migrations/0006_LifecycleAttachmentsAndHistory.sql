-- Cloud Sync v1 data lifecycle, attachment metadata, and entity version history.

CREATE TABLE IF NOT EXISTS "CloudEntityVersions" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "EntityType" TEXT NOT NULL,
    "EntityId" UUID NOT NULL,
    "Revision" BIGINT NOT NULL,
    "Action" TEXT NOT NULL,
    "PayloadJson" JSONB NOT NULL,
    "CreatedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    CONSTRAINT ux_cloud_entity_versions_revision UNIQUE ("WorkspaceId", "EntityType", "EntityId", "Revision", "Action")
);
CREATE INDEX IF NOT EXISTS ix_cloud_entity_versions_entity ON "CloudEntityVersions"("WorkspaceId", "EntityType", "EntityId", "Revision" DESC);

CREATE TABLE IF NOT EXISTS "CloudAttachments" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "EntityType" TEXT NOT NULL,
    "EntityId" UUID NOT NULL,
    "FileName" TEXT NOT NULL,
    "ContentType" TEXT NOT NULL,
    "SizeBytes" BIGINT NOT NULL,
    "Sha256" TEXT NOT NULL,
    "BlobKey" TEXT NOT NULL,
    "Version" BIGINT NOT NULL DEFAULT 1,
    "CreatedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "ArchivedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "ArchivedAt" TIMESTAMPTZ NULL,
    "ArchiveReason" TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_cloud_attachments_entity ON "CloudAttachments"("WorkspaceId", "EntityType", "EntityId", "CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS ix_cloud_attachments_workspace_active ON "CloudAttachments"("WorkspaceId", "ArchivedAt") WHERE "ArchivedAt" IS NULL;
