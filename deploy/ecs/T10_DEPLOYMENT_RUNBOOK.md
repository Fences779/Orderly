# T10-C Existing Baseline Adoption Runbook

本文件是 T10-C 的人工执行顺序。T10-B 只更新本地仓库和文档，不执行这里的部署命令。

## 0. 架构边界

- 现有 `orderly-postgres`、`orderly-redis`、`compose_postgres_data`、`compose_redis_data` 归旧 baseline 所有。
- 新应用 compose project 使用 `ORDERLY_COMPOSE_PROJECT_NAME=orderly-app`。
- 新应用 compose 只拥有 `orderly-server` 和可选 `caddy`。
- 新应用 compose 通过 external network `compose_default` 访问旧 baseline。
- 新应用 compose 不声明 `postgres` / `redis` service，不声明旧 volume，不设置会冲突的 `container_name`。
- PostgreSQL 和 Redis 不开放宿主机端口，不开放公网。

## 1. 远端只读 preflight

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
whoami
hostname
date -Is
docker ps --filter name=orderly-postgres --filter name=orderly-redis --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"
docker network inspect compose_default --format "network={{.Name}} driver={{.Driver}} labels={{json .Labels}}"
docker volume inspect compose_postgres_data compose_redis_data --format "volume={{.Name}} driver={{.Driver}} mount={{.Mountpoint}} labels={{json .Labels}}"
'
```

停止条件：

- `orderly-postgres` 或 `orderly-redis` 不存在或不是 running。
- `compose_default` 不存在。
- 任一现有 volume 不存在。
- 发现宿主机发布了 `5432` 或 `6379`。

## 2. Docker network 连通性验证

只使用现有容器做 DNS / 内网检查，不创建临时容器：

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker exec orderly-postgres getent hosts orderly-redis
docker exec orderly-redis getent hosts orderly-postgres
docker exec orderly-postgres sh -lc "pg_isready -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\""
docker exec orderly-redis sh -lc "export REDISCLI_AUTH=\"$(cat /run/secrets/redis_password)\"; redis-cli --no-auth-warning PING"
'
```

不得输出 secret 内容。

## 3. 配置文件上传

上传目标：

- `deploy/ecs/docker-compose.prod.yml` -> `/opt/orderly/compose/docker-compose.prod.yml`
- `deploy/ecs/Caddyfile.example` -> `/opt/orderly/compose/Caddyfile`
- 真实 env -> `/opt/orderly/env/orderly.prod.env`

真实 env 必须包含：

- `ORDERLY_COMPOSE_PROJECT_NAME=orderly-app`
- `ORDERLY_APP_UID=1000`
- `ORDERLY_APP_GID=1000`
- `ORDERLY_EXISTING_DOCKER_NETWORK=compose_default`
- `ORDERLY_POSTGRES_HOST=orderly-postgres`
- `ORDERLY_POSTGRES_PORT=5432`
- `ORDERLY_POSTGRES_DB=orderly`
- `ORDERLY_POSTGRES_USER=orderly`
- `ORDERLY_EXISTING_POSTGRES_PASSWORD_FILE=/opt/orderly/env/postgres_password`
- `ORDERLY_POSTGRES_PASSWORD_FILE=/run/secrets/existing_postgres_password`
- `ORDERLY_REDIS_HOST=orderly-redis`
- `ORDERLY_REDIS_PORT=6379`
- `ORDERLY_REDIS_PASSWORD_FILE=/opt/orderly/env/redis_password`

## 4. secret 存在性和权限验证

只输出文件名、权限、属主、大小和哈希，不输出内容：

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
stat -c "%A %U %G %s %n" /opt/orderly/env/postgres_password /opt/orderly/env/redis_password /opt/orderly/env/orderly.prod.env
sha256sum /opt/orderly/env/postgres_password /opt/orderly/env/redis_password /opt/orderly/env/orderly.prod.env
'
```

停止条件：

- `postgres_password` 或 `redis_password` 缺失。
- `orderly.prod.env` 仍有 `__SET_` 占位符。
- `orderly.prod.env` 权限过宽。
- `ORDERLY_ALLOWED_ORIGINS=*`。

## 5. docker compose config

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml config --quiet
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml config --services
'
```

验收：

- services 只能包含 `orderly-server`；带 `--profile edge` 时才包含 `caddy`。
- 不得包含 `postgres` 或 `redis`。
- 不得声明 `compose_postgres_data` 或 `compose_redis_data`。

## 6. pull / build

如果使用远端镜像：

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml pull orderly-server
docker compose --profile edge --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml pull caddy
'
```

如果改为 ECS 本机构建，必须先单独授权上传源码或构建上下文；默认不使用本机构建。

## 7. 启动应用容器

注意：启动 `orderly-server` 会执行 DbUp migration。没有“允许启动 API 并执行 DbUp”的明确授权时，T10-C 必须停在第 6 步。

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d orderly-server
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml ps
'
```

## 8. healthcheck

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec -T orderly-server curl -fsS http://localhost:8080/health
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec -T orderly-server curl -fsS http://localhost:8080/health/db
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec -T orderly-server curl -fsS http://localhost:8080/health/backups
'
```

## 9. API smoke test

- Bootstrap 管理员登录。
- 用户申请、审批、员工登录。
- 设备注册、审批、撤销。
- Workspace 权限快照。
- Sync A/B/C。
- 附件上传、下载、未授权拒绝。
- 审计日志可查。
- 普通用户永久删除被拒绝。

## 10. SignalR smoke test

- 连接 `/hubs/workspace`。
- 带有效 JWT 连接成功。
- 无效或无权限 JWT 被拒绝。
- 一端同步写入后，另一端收到 workspace 通知。

## 11. 数据库只读连通性验证

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml exec -T orderly-server sh -lc "printenv ORDERLY_POSTGRES_HOST ORDERLY_POSTGRES_DB ORDERLY_POSTGRES_USER"
docker exec orderly-postgres sh -lc "psql -v ON_ERROR_STOP=1 -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -Atc \"select current_database(), current_user;\""
'
```

不得输出 PostgreSQL 密码。

## 12. Redis 连通性验证

当前 API 不消费 Redis。T10-C 只确认旧 Redis baseline 仍健康：

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker exec orderly-redis sh -lc "export REDISCLI_AUTH=\"$(cat /run/secrets/redis_password)\"; redis-cli --no-auth-warning PING"
docker exec orderly-redis sh -lc "export REDISCLI_AUTH=\"$(cat /run/secrets/redis_password)\"; redis-cli --no-auth-warning DBSIZE"
'
```

## 13. 启动 Caddy

只有 API 内网健康、域名和安全组已确认后才执行：

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --profile edge --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml up -d caddy
docker compose --profile edge --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml ps
'
```

## 14. rollback 条件和命令

可直接回滚应用容器的条件：

- 新 API 镜像启动失败。
- `/health`、`/health/db` 或 `/health/backups` 不健康。
- API smoke 或 SignalR smoke 失败。
- Caddy 反代失败。

只停新应用，不碰旧 PostgreSQL/Redis/volume：

```bash
ssh -o BatchMode=yes orderlyops@118.178.237.56 'set -eu
docker compose --profile edge --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml stop caddy orderly-server || true
docker compose --profile edge --env-file /opt/orderly/env/orderly.prod.env -f /opt/orderly/compose/docker-compose.prod.yml rm -f caddy orderly-server || true
docker ps --filter name=orderly-postgres --filter name=orderly-redis --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
docker volume inspect compose_postgres_data compose_redis_data >/dev/null
'
```

如果 DbUp 已经修改 schema，不能只靠停容器回滚数据；必须进入备份恢复流程，由人类确认恢复目标库。任何情况下都禁止 `docker compose down -v`，禁止删除或重建 `compose_postgres_data` / `compose_redis_data`。
