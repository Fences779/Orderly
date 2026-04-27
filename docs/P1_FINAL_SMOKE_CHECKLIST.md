# P1 Final Smoke Checklist

## 1. 启动检查

- `dotnet build Orderly.sln -c Debug`
- 启动应用
- 离线模式 / `--qa-mode`
- 主窗口打开

## 2. 核心功能检查

- 新增客户
- 创建订单
- 搜索
- 筛选
- 今日 / 逾期 / 明日跟进
- 客户状态切换
- 订单状态切换
- FollowUp 完成 / 延期 / 取消
- Deal 阶段推进
- 新增备注
- 快捷备注模板
- 改价
- ActivityLog
- 重启持久化

## 3. 视觉检查

- 主工作台
- 客户/订单 Tab
- 话术库 Tab
- 设置 Tab
- 弹窗
- 125% 缩放后置到 Final Visual QA

## 4. 不在 P1 范围内

- AI
- OCR
- 自动回复
- 云同步
- 多端同步
