# Data Maintenance

## 数据库位置

- 默认 SQLite 路径：`%LOCALAPPDATA%\Orderly\orderly.db`
- Windows 实际示例：`C:\Users\<你的用户名>\AppData\Local\Orderly\orderly.db`
- 不要手动删除数据库文件，避免误删真实用户数据。

## QA 数据命令

- `--qa-mode`：启动应用前自动确保 QA 数据存在，并进入 QA 演示模式。
- `--seed-qa-data`：执行 QA Seeder，按幂等方式补齐 `[P1.3_QA]` 数据。
- `--clear-qa-data`：仅删除带 `[P1.3_QA]` 标记的 QA 数据，不删除普通用户数据。
- `--reset-qa-data`：先清理 `[P1.3_QA]` 数据，再重新写入稳定 QA 数据。
- `--qa-data-status`：输出当前 `[P1.3_QA]` 数据数量，不修改数据库。

## 安全说明

- QA 数据统一使用 `[P1.3_QA]` 标记。
- `clear` 只删除命中 `[P1.3_QA]` 标记的数据。
- 普通用户数据不在 QA 清理范围内。
- Demo 数据 `[DEMO]` 不属于 QA 清理范围。
- 如果发现非 QA 历史记录或订单仍然引用 QA 父记录，清理会保留这些被引用的 QA 父记录，避免影响非 QA 数据。

## 常用命令示例

```powershell
Orderly.App.exe --qa-data-status
Orderly.App.exe --reset-qa-data
Orderly.App.exe --clear-qa-data
```
