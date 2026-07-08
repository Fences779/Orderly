-- Cloud Sync v1 user application, invitation, and device approval gate.

BEGIN;

ALTER TABLE "CloudRefreshTokens"
    ADD COLUMN IF NOT EXISTS "DeviceId" TEXT NULL;

ALTER TABLE "CloudWorkspaceMembers"
    ADD COLUMN IF NOT EXISTS "Id" UUID NULL;

CREATE TABLE IF NOT EXISTS "CloudInvitations" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "Code" TEXT NOT NULL UNIQUE,
    "CloudRole" TEXT NOT NULL,
    "BusinessLabel" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "MaxUses" INTEGER NOT NULL DEFAULT 1,
    "UsedCount" INTEGER NOT NULL DEFAULT 0,
    "ExpiresAt" TIMESTAMPTZ NULL,
    "CreatedByUserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "DisabledByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "DisabledAt" TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_cloud_invitations_workspace ON "CloudInvitations"("WorkspaceId", "CreatedAt" DESC);

CREATE TABLE IF NOT EXISTS "CloudUserApplications" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NULL REFERENCES "CloudWorkspaces"("Id"),
    "InvitationId" UUID NULL REFERENCES "CloudInvitations"("Id"),
    "InviteCode" TEXT NOT NULL,
    "Username" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "RequestedDeviceId" TEXT NOT NULL,
    "RequestedDeviceName" TEXT NOT NULL,
    "RequestedAt" TIMESTAMPTZ NOT NULL,
    "ReviewedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "ReviewedAt" TIMESTAMPTZ NULL,
    "ReviewReason" TEXT NULL,
    "CreatedUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "ClientRequestId" TEXT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_cloud_applications_workspace_status ON "CloudUserApplications"("WorkspaceId", "Status", "RequestedAt" DESC);
CREATE INDEX IF NOT EXISTS ix_cloud_applications_username ON "CloudUserApplications"("Username", "RequestedAt" DESC);

CREATE TABLE IF NOT EXISTS "CloudDevices" (
    "Id" UUID PRIMARY KEY,
    "WorkspaceId" UUID NOT NULL REFERENCES "CloudWorkspaces"("Id"),
    "UserId" UUID NOT NULL REFERENCES "CloudUsers"("Id"),
    "DeviceId" TEXT NOT NULL,
    "DeviceName" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "FirstSeenAt" TIMESTAMPTZ NOT NULL,
    "LastSeenAt" TIMESTAMPTZ NULL,
    "ApprovedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "ApprovedAt" TIMESTAMPTZ NULL,
    "RevokedByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "RevokedAt" TIMESTAMPTZ NULL,
    "DisabledByUserId" UUID NULL REFERENCES "CloudUsers"("Id"),
    "DisabledAt" TIMESTAMPTZ NULL,
    "LastIpAddress" TEXT NULL,
    "LastUserAgent" TEXT NULL,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "UpdatedAt" TIMESTAMPTZ NOT NULL,
    CONSTRAINT ux_cloud_devices_user_device UNIQUE ("WorkspaceId", "UserId", "DeviceId")
);
CREATE INDEX IF NOT EXISTS ix_cloud_devices_workspace_status ON "CloudDevices"("WorkspaceId", "Status", "UpdatedAt" DESC);
CREATE INDEX IF NOT EXISTS ix_cloud_devices_user ON "CloudDevices"("UserId", "UpdatedAt" DESC);

COMMIT;
