# P2 Closeout Summary

日期：2026-04-29
状态：P2 完成，closeout 验证通过

## P2 完成范围

- P2.0：助手底座、数据模型、仓储/服务合同、本地 stub、SQLite 与 QA 扩展。
- P2.1：手工录入沟通记录，按客户/订单回读最近消息。
- P2.2：AI 建议本地闭环，支持生成、接受、拒绝，并写 `ActivityLog`。
- P2.3：回复草稿流，支持准备草稿、标记已发送、拒绝草稿。
- P2.4：AI provider 架构，支持 `local stub`、缺配置 fallback、缺 key fallback、失败 fallback。
- P2.5：OCR 结果落 `OcrResults`，支持本地 fallback 文本并转沟通记录。
- P2.6：人工复制发送辅助，支持复制草稿后手工确认已发送。
- P2.7：本地 JSON 备份导出、校验、最近备份状态留痕。
- P2.8：空库/QA-only 受控恢复，非空生产库拒绝。
- P2.9：恢复预览、风险确认门控、恢复前摘要确认层。

## 每阶段对应 commit

- P2.0：`17ee4bf` `feat: p2.0 assistant foundation`
- P2.1：`6976bd1` `feat: p2.1 message entry`
- P2.2：`4983f50` `feat: p2.2 ai suggestion flow`
- P2.3：`fca2e15` `feat: p2.3 auto reply draft flow`
- P2.4：`6cf8ff0` `feat: p2.4 ai provider fallback`
- P2.5：`0ad86b4` `feat: p2.5 ocr message entry`
- P2.6：`548e9e1` `feat: p2.6 manual send flow`
- P2.7：`2b3012c` `feat: p2.7 local backup boundary`
- P2.8：`6654dd5` `feat: p2.8 controlled restore`
- P2.9：`d0f8b11` `feat: p2.9 restore preview gate`

## 当前可用产品链路

1. 手工录入沟通记录。
2. OCR 结果转沟通记录。
3. AI 生成建议。
4. 准备回复草稿。
5. 复制草稿。
6. 标记已发送。
7. `ActivityLog` 留痕。
8. 本地备份导出。
9. 备份校验。
10. 恢复预览。
11. 空库 / QA-only 受控恢复。
12. 非空生产库恢复拒绝。
13. P1 smoke 未受影响。

## 明确未做内容

- 未接真实 OCR 引擎。
- 未把 AI 能力作为生产级真实输出承诺。
- 未接真实平台发送，不自动发送，不控制微信/闲鱼窗口。
- 未接云同步、多设备同步、冲突合并。
- 未开放非空生产库覆盖恢复。
- 未进入 P3 范围。

## QA 命令和结果

已执行命令：

```powershell
dotnet build Orderly.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p1-smoke.ps1
powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-full-regression.ps1
```

结果：

- `dotnet build Orderly.sln -c Debug`：PASS，`0 warning / 0 error`
- `run-p1-smoke.ps1`：PASS
- `run-p2-full-regression.ps1`：PASS

`run-p2-full-regression.ps1` 内部顺序执行并全部 PASS：

- `run-p2-1-message-smoke.ps1`
- `run-p2-2-ai-suggestion-smoke.ps1`
- `run-p2-3-auto-reply-smoke.ps1`
- `run-p2-4-ai-provider-smoke.ps1`
- `run-p2-5-ocr-smoke.ps1`
- `run-p2-6-manual-send-smoke.ps1`
- `run-p2-7-backup-smoke.ps1`
- `run-p2-8-restore-smoke.ps1`
- `run-p2-9-restore-preview-smoke.ps1`

说明：

- 默认 closeout 回归不依赖公网。
- 默认 closeout 回归不调用真实 AI API。
- `run-p2-5-deepseek-live-smoke.ps1` 保留为可选联机验证，不纳入默认 closeout 回归。

## 已知限制

- 仍以本地 stub / fallback 为主，AI 与 OCR 不是生产级真实能力。
- 回复发送仍是人工复制发送辅助，不含外部平台回执。
- 恢复能力仍严格限制在空库或 QA-only 目标库。
- 当前自动化以服务层、QA 命令和最小 UIA 为主，不覆盖视觉验收和大范围交互回归。

## P3 建议方向

1. 补强真实 provider 接入后的配置、超时、限流和可观测性。
2. 在不破坏手工发送边界的前提下，完善草稿管理与发送后跟进协同。
3. 继续扩展恢复/导入审计、异常恢复说明和更细粒度的数据比对工具。
