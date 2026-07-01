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

脚本会自动安装 `0.1.1`、启动应用并注入本地 `0.1.2` 更新源。人工只需在应用内创建测试账号、创建一条数据、设置头像、修改两项设置、点击检查更新并确认更新，然后按提示回到脚本继续。

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

失败时报告包含失败步骤、原始异常、修复建议、安装目录、数据目录、脚本 SHA256、发布产物 SHA256、本地更新源、快捷方式和卸载注册表字段清单。
