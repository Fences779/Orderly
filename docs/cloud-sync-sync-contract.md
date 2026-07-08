# Cloud Sync v1 Sync Contract Baseline

来源：`docs/Cloud Sync v1.txt` 第 7、8、13、14 节。

冻结规则：

- 客户端写入必须携带 `BaseVersion / ChangedFields / IdempotencyKey`。
- 同字段并发修改必须拒绝。
- 不同字段并发修改允许服务端字段级合并。
- 增量同步使用服务端可信 Cursor / Sequence，不依赖客户端时间。
- Snapshot token 必须由服务端可信生成，客户端不可篡改。
- SignalR 只通知“有变化”，主数据仍通过 Sync API 拉取。
- 所有写入请求重复发送时只能执行一次，并回放原处理结果。
