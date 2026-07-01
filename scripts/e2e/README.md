# Orderly Windows Local E2E

本目录用于验证 Velopack Windows 安装、应用内更新和卸载保留数据链路。

## 当前账号自检

在开发账号下只允许运行无副作用自检：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\e2e\Run-OrderlyLocalE2E.ps1 -ValidateOnly -ExpectedTestUserName "your-temporary-test-user"
```

该模式不会安装、启动、卸载或删除 Orderly，也不会读写受保护开发账号的 `%LOCALAPPDATA%\Orderly*`。

## 测试账号安装与更新验收

仅在你专门创建的临时测试账号下运行，并显式传入用户名参数：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\e2e\Run-OrderlyLocalE2E.ps1 -ExpectedTestUserName "your-temporary-test-user"
```

脚本会清理测试账号下的旧 `%LOCALAPPDATA%\Orderly` 安装目录，从 `https://github.com/Fences779/Orderly` 的 GitHub Release 下载 `0.1.2` 安装包，安装后直接启动应用。人工只需确认“关于 Orderly”显示 `0.1.2`，更新源显示 `GitHub Releases（https://github.com/Fences779/Orderly）`，再点击检查更新并创建最小测试数据。

## 测试账号卸载保留数据验收

仅在同一个临时测试账号下运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\e2e\Run-OrderlyLocalE2E.ps1 -UninstallVerify -ExpectedTestUserName "your-temporary-test-user"
```

脚本会执行卸载，验证程序目录、开始菜单项和卸载注册表项已消失，并验证 `%LOCALAPPDATA%\OrderlyData` 仍保留。

## 报告

每次运行都会写入：

```text
%USERPROFILE%\Desktop\Orderly-E2E-Report.txt
```

失败时报告包含失败步骤、原始异常、修复建议、安装目录、数据目录、脚本 SHA256、发布产物 SHA256、GitHub Release 更新源、快捷方式和卸载注册表字段清单。
