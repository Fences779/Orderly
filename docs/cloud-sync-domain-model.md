# Cloud Sync v1 Domain Model Baseline

来源：`docs/Cloud Sync v1.txt`。

冻结边界：

- 云端是最终可信状态源。
- Workspace / Scope 是权限与分发边界。
- 服务端必须校验用户、设备、Workspace、角色、字段级权限、附件权限、版本和幂等。
- 实体至少包含 `EntityId / WorkspaceId / Version / CreatedAt / CreatedBy / UpdatedAt / UpdatedBy / DeletedAt or ArchivedAt`。
- 普通用户不可永久删除数据；永久删除必须满足保留期、云端管理员确认和审计留痕。
