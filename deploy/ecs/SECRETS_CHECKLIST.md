# ECS Secret Checklist

本清单只说明生产需要准备什么，不记录真实值。真实值只能写入 ECS 的 `/opt/orderly/env/orderly.prod.env` 或云厂商密钥管理服务。

## 必填

| 项 | 环境变量 | 要求 |
| --- | --- | --- |
| ASP.NET 环境 | `ASPNETCORE_ENVIRONMENT` | 必须是 `Production` |
| 公网入口 | `ORDERLY_PUBLIC_URL` | T10 配置真实域名后填写 HTTPS URL |
| 允许来源 | `ORDERLY_ALLOWED_ORIGINS` | 只填真实前端/客户端来源，不允许 `*` |
| Postgres 库名 | `POSTGRES_DB` / `ORDERLY_POSTGRES_DB` | 两者保持一致 |
| Postgres 用户 | `POSTGRES_USER` / `ORDERLY_POSTGRES_USER` | 两者保持一致 |
| Postgres 密码 | `POSTGRES_PASSWORD` / `ORDERLY_POSTGRES_PASSWORD` | 强随机值，不提交 |
| JWT 签名密钥 | `ORDERLY_JWT_SIGNING_KEY` | 至少 32 字节，建议 64 字节以上随机值 |
| 一次性初始化令牌 | `ORDERLY_BOOTSTRAP_ADMIN_TOKEN` | 只用于首次初始化管理员，完成后清空并重启 |
| 一次性管理员密码 | `ORDERLY_BOOTSTRAP_ADMIN_PASSWORD` | 至少 12 位强密码，完成后由管理员立刻改密 |
| OSS Endpoint | `ORDERLY_OSS_ENDPOINT` | 生产对象存储 endpoint |
| OSS Bucket | `ORDERLY_OSS_BUCKET` | 生产 bucket，不公开读 |
| OSS Access Key | `ORDERLY_OSS_ACCESS_KEY_ID` / `ORDERLY_OSS_ACCESS_KEY_SECRET` | 最小权限，只允许所需 bucket/prefix |
| 备份加密密钥 | `ORDERLY_BACKUP_ENCRYPTION_KEY` | 预留给外部备份加密流程，不由当前 API 进程消费 |

## Refresh Token

当前 Cloud Sync v1 服务端没有独立的 `ORDERLY_REFRESH_TOKEN_SECRET`。Refresh Token 是服务端生成的 64 字节随机值，数据库只保存 SHA-256 哈希，并支持轮换、撤销和重放检测。生产保护重点是：

- 不泄露 PostgreSQL 数据库和备份。
- JWT 签名密钥必须强随机。
- 备份文件必须加密或使用 OSS 服务端加密。
- 账号停用、改密、设备撤销后必须确认 refresh token 已失效。

## 必须确认

- `.env.prod.example` 不能直接用于生产。
- 真实 secret 不进 git。
- 不把 secret 写入 runbook、截图、聊天记录或 issue。
- Bootstrap secret 用完后清空。
- OSS bucket 禁止公开读，附件下载只能走 API 鉴权路径。
- 数据库端口不开放公网安全组。
