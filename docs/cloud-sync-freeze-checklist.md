# Cloud Sync v1 Freeze Checklist

进入本地一体化测试前必须满足：

- build 通过。
- test 通过或明确列出非代码原因。
- DbUp migration 可应用到空 PostgreSQL。
- 不存在固定默认云端管理员密码。
- 非 Development 环境缺少强 JWT key 时必须启动失败。
- Snapshot token 不可被客户端篡改。
- 写入契约支持 `Version / BaseVersion / ChangedFields / IdempotencyKey`。
- 审计字段完整。
- 普通用户不能永久删除。
- 附件、历史版本和敏感正文访问必须由服务端二次校验并审计。
