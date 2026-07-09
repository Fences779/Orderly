# T10 ECS Deployment Runbook

本文件是下一阶段人工部署步骤。T9 不执行这里的任何命令。

## 1. ECS 登录前检查

- T8 Docker Compose 验收 PASS。
- T9 ECS 准备 PASS。
- 当前 git commit、镜像 tag、回滚 tag 已确认。
- 安全组方案已确认：只开放 80/443，SSH 仅限管理来源，PostgreSQL 不开放公网。
- 真实域名、DNS、HTTPS 由人类确认后再做。

## 2. 上传配置

上传到 ECS：

- `docker-compose.prod.yml` -> `/opt/orderly/compose/docker-compose.prod.yml`
- `Caddyfile.example` -> `/opt/orderly/compose/Caddyfile`
- 真实 env -> `/opt/orderly/env/orderly.prod.env`

## 3. 创建目录

```bash
sudo mkdir -p /opt/orderly/compose /opt/orderly/env /opt/orderly/logs/api /opt/orderly/logs/caddy /opt/orderly/backups /opt/orderly/data/postgres /opt/orderly/data/object-storage /opt/orderly/data/caddy /opt/orderly/data/caddy-config
```

## 4. 写入 secret

- 只在 `/opt/orderly/env/orderly.prod.env` 或密钥管理服务中写真实值。
- 替换所有 `__SET_*__`。
- Bootstrap secret 只保留到首个管理员初始化完成。

## 5. 启动 PostgreSQL

```bash
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d postgres
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml ps
```

## 6. 应用 migration

```bash
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d orderly-server
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml logs --tail=200 orderly-server
```

API 启动时执行 DbUp。升级前必须已有备份，且 `ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP=true`。

## 7. 启动 API

如果上一段已启动成功，此处只确认状态：

```bash
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml ps
```

## 8. Health check

在 ECS 本机内网检查：

```bash
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec orderly-server curl -fsS http://localhost:8080/health
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec orderly-server curl -fsS http://localhost:8080/health/db
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec orderly-server curl -fsS http://localhost:8080/health/backups
```

## 9. Smoke test

- Bootstrap 管理员登录。
- 用户申请、审批、员工登录。
- 设备注册、审批、撤销。
- Workspace 权限快照。
- Sync A/B/C。
- 附件上传、下载、未授权拒绝。
- 审计日志可查。
- 普通用户永久删除被拒绝。

## 10. Reverse proxy

确认 API 内网健康后，再启动代理：

```bash
docker compose --profile edge --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d caddy
```

## 11. 域名 / HTTPS

- 人类确认 DNS 已指向 ECS。
- 人类确认安全组开放 80/443。
- 人类确认 Caddy 证书申请成功。
- 未确认前不对真实业务开放。

## 12. 回滚方案

- 保留上一个 API 镜像 tag。
- 保留升级前数据库 dump。
- 若只是不兼容应用镜像，切回旧 tag 并重启 API。
- 若 migration 已变更 schema，先评估是否可向后兼容；不可兼容时按备份恢复流程处理。
- 回滚过程不得删除生产 volume。

## 13. 部署后观察指标

- `/health`
- `/health/db`
- `/health/backups`
- API 容器 restart 次数
- PostgreSQL health
- Caddy 4xx/5xx
- 最近一次备份时间
- 最近一次恢复演练状态
- 登录失败和设备拒绝数量
- 同步冲突、幂等重放、Cursor 补拉异常
