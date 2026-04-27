# 部署说明

## 1. 导入工程

用微信开发者工具导入当前目录：

`D:\Dev\Orderly`

小程序根目录为 `miniprogram/`，云函数根目录为 `cloudfunctions/`。

## 2. 初始化云开发环境

1. 打开微信开发者工具。
2. 点击「云开发」。
3. 创建一个云环境。
4. 复制环境 ID。
5. 修改 `miniprogram/config/appConfig.js`：

```js
cloudEnv: '你的环境 ID'
```

如果不填写，代码会使用微信开发者工具当前默认云环境。

## 3. 创建集合

可以先不手工创建集合，`dealInitSeed` 会尝试创建集合并写入 seed。

集合清单：

- `customers`
- `deals`
- `quotes`
- `sku_catalog`
- `followup_tasks`
- `message_templates`
- `captures`
- `activity_logs`

建议根据 `database.indexes.json` 在云开发控制台补索引。

## 4. 部署云函数

在微信开发者工具中，逐个右键以下目录，选择「上传并部署：云端安装依赖」：

- `cloudfunctions/dealInitSeed`
- `cloudfunctions/captureParse`
- `cloudfunctions/captureConfirm`
- `cloudfunctions/customerUpsert`
- `cloudfunctions/dealUpsert`
- `cloudfunctions/dealStageUpdate`
- `cloudfunctions/quoteCreateOrUpdate`
- `cloudfunctions/followupScan`
- `cloudfunctions/statsSummary`
- `cloudfunctions/ocrAdapter`

每个云函数目录都有独立 `package.json`，依赖为 `wx-server-sdk`。

## 5. 灌 seed 数据

方式一：在小程序里操作。

1. 进入首页。
2. 点击「设置」。
3. 点击「初始化演示数据」。

方式二：在云开发控制台直接调用 `dealInitSeed`，参数：

```json
{
  "workspaceId": "default"
}
```

seed 包含：

- 6 个 SKU
- 15 条模板
- 5 个客户
- 10 个 deal
- 3 条报价
- 5 条跟进任务
- 3 条 capture
- activity log 示例

## 6. OCR adapter

默认 `ocrAdapter` 使用 mock provider，会返回一段可解析的示例文本。

如果配置真实 OCR：

1. 在云函数环境变量设置 `OCR_PROVIDER`。
2. 在 `cloudfunctions/ocrAdapter/index.js` 增加对应 provider 调用。
3. 真实 OCR 异常时仍应返回空文本和提示，页面允许用户手动粘贴修正。

当前降级不影响主链路，因为 OCR 页允许用户直接编辑 OCR 文本，再走解析和确认。

## 7. 首页测试入口

编译后进入 `pages/dashboard/dashboard`。

建议先执行：

1. 设置页初始化 seed。
2. 回到工作台手动扫描跟进任务。
3. 按 `docs/manual-test-checklist.md` 验收主链路。
