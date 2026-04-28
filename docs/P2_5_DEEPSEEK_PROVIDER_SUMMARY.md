# P2.5 DeepSeek Provider Summary

## 1. 本轮完成

- 新增 `DeepSeekSuggestionProvider`：
  - 文件：`src/Orderly.Data/Services/DeepSeekSuggestionProvider.cs`
  - 使用官方 Base URL：`https://api.deepseek.com`
  - 调用 endpoint：`/chat/completions`
  - 请求体采用 OpenAI-compatible chat completions 格式
  - 默认模型：`deepseek-chat`
  - 支持 `system` / `user` messages
  - 显式设置 `stream = false`
  - 解析 `response.choices[0].message.content`
- 接入现有 Provider 选择工厂：
  - 文件：`src/Orderly.Data/Services/AiSuggestionProviderFactory.cs`
  - 当 `ORDERLY_AI_PROVIDER=deepseek` 时启用 DeepSeek Provider
- 扩展环境变量读取：
  - 文件：`src/Orderly.Data/Services/AiProviderOptions.cs`
  - API Key 仅从 Windows 用户环境变量 `DEEPSEEK_API_KEY` 读取
  - 未配置时给出明确错误，并继续由 `Local Stub` fallback，不让应用崩溃
- 复用现有业务 fallback / 审计链路：
  - 文件：`src/Orderly.Data/Services/LocalAiAssistantService.cs`
  - DeepSeek 成功与 fallback 场景都会保留 `AiSuggestions` / `ActivityLog`
- 补充 UI 标识：
  - `src/Orderly.App/ViewModels/AiSuggestionListItem.cs`
  - `src/Orderly.App/Views/MainWindow.xaml`
- 补充 QA：
  - `tools/qa/run-p2-4-ai-provider-smoke.ps1`
  - 新增 DeepSeek 无 Key 时的 fallback 验证
  - `tools/qa/run-p2-5-deepseek-live-smoke.ps1`
  - 从用户环境变量读取 `DEEPSEEK_API_KEY / ORDERLY_AI_PROVIDER / ORDERLY_AI_MODEL`
  - 用最短输入发起一次真实 DeepSeek 联网请求
  - 仅输出脱敏 Key、响应摘要和本地 JSON 报告，不输出完整密钥

## 2. 配置方式

- Provider 切换：
  - `ORDERLY_AI_PROVIDER=deepseek`
- DeepSeek API Key：
  - `DEEPSEEK_API_KEY`
- 模型：
  - 默认 `deepseek-chat`
  - 如后续要切 `deepseek-reasoner`，可通过 `ORDERLY_AI_MODEL` 覆盖

PowerShell 设置用户环境变量：

```powershell
[Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", "<your-api-key>", "User")
```

临时在当前终端指定 provider：

```powershell
$env:ORDERLY_AI_PROVIDER="deepseek"
dotnet run --project src/Orderly.App/Orderly.App.csproj
```

说明：

- 不要把真实 Key 写进代码、`appsettings`、脚本、文档示例或 git。
- 新设置的用户环境变量通常需要新开终端，当前已打开的 PowerShell 会话不会自动刷新。

## 3. 安全约束

- 不记录认证请求头。
- 不输出完整 API Key。
- 不把真实 Key 写入任何仓库文件。
- 无 Key / 配置错误时只返回明确错误摘要，并交给现有 fallback 兜底。

## 4. 验证建议

- Build：
  - `dotnet build Orderly.sln -c Debug`
- Provider smoke：
  - `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-4-ai-provider-smoke.ps1`
- DeepSeek live smoke：
  - `powershell -ExecutionPolicy Bypass -File .\tools\qa\run-p2-5-deepseek-live-smoke.ps1`
- 如果本机已经配置了 `DEEPSEEK_API_KEY`，可额外测试：

```powershell
$env:ORDERLY_AI_PROVIDER="deepseek"
dotnet run --project src/Orderly.App/Orderly.App.csproj
```

补充说明：

- `run-p2-5-deepseek-live-smoke.ps1` 会优先读取当前进程环境变量；如果当前 shell 未刷新，会回退读取 Windows 用户环境变量并注入到脚本进程。
- live smoke 请求内容使用极短输入 `只回复：DeepSeek OK`，验证条件只要求 `response content` 非空。
