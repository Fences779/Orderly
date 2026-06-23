# Agent Instructions & Constraints

## CRITICAL RULES - DO NOT MODIFY

### -1. Explicit Implementation Keyword Gate
- **Status**: Active and strict.
- **Constraint**: Unless the user explicitly gives a standalone implementation trigger such as `施工`, `修改`, `改代码`, `分析执行`, `implement`, `fix`, or `apply changes`, Codex must stay in discussion mode only.
- **Required Behavior**: Without an explicit implementation trigger, **DO NOT** modify files, edit code, write patches, run change-causing commands, or perform implementation by implication.
- **Default Interpretation**: Requests such as `看看`, `检查`, `分析`, `排查`, `评估`, `方案`, `怎么改`, or similar wording must be treated as discussion-only, even if the likely technical solution is already clear. A standalone `分析执行` is implementation authorization, not discussion-only wording.
- **Escalation Rule**: When the user's intent is ambiguous, resolve it as non-implementation by default and discuss scope, goal, and approach first.

### -1.1 Discussion-First Workflow
- **Status**: Active and strict.
- **Default Mode**: Codex must default to discussion mode for every new user request unless the user explicitly provides an implementation trigger.
- **Required Sequence**:
  1. The user raises a requirement or question.
  2. Codex responds with analysis, answers, and/or a proposed change plan only.
  3. Codex confirms the user's actual intent, scope, and boundaries.
  4. Codex explicitly asks whether it may begin modification.
  5. Codex may start implementation only after the user gives an explicit implementation trigger such as `施工`, `修改`, `改代码`, `分析执行`, `implement`, `fix`, or `apply changes`.
- **Constraint**: Codex must not infer permission to modify code from context, momentum, prior similar tasks, or solution clarity.

### -1.2 Post-Change Validation Workflow
- **Status**: Active by default after every approved code change.
- **Required Behavior After Modification**:
  - Automatically check for compile or build errors using the most relevant local project command.
  - 施工完成之后进行编译正确性审核。
  - 默认先执行“本次修改点定向验收”：拉起最相关本地预览或应用入口，只验证这次刚修改的点、直接影响的交互和对应保存/回显/状态链路；改对则收口，未改对继续修改。
  - `run-p1-smoke.ps1`、`run-commerce-smoke.ps1`、全页面 UIA 点检、业务闭环回归，不再作为每次日常改动后的默认必跑项；仅在本次改动直接命中对应链路、用户明确要求，或进入发布前/专项回归时执行。
  - Automatically launch the most relevant local preview target when a preview entry exists and can be started safely.
  - Return a brief acceptance checklist telling the user what to verify.
- **Failure Handling**: If build or preview cannot run, Codex must state the real blocker and provide the exact command the user should run locally.

### -1.3 Minimal Scope Escalation Rule
- **Status**: Active and strict.
- **Constraint**: Changes must stay within the smallest necessary scope to solve the approved task.
- **Required Behavior**:
  - If the fix appears to require touching additional files, modules, UI surfaces, shared state, services, or adjacent subsystems beyond the initially expected scope, stop and ask the user before expanding.
  - Do not proactively widen the refactor surface just because a broader cleanup may be technically beneficial.
- **Escalation Note**: The user may explicitly approve broader scope or higher reasoning depth before Codex continues.

全程和我交流必须使用简体中文，注意是简体中文，全程简体中文

如果有问题，一个一个问我，然后给出选项让我选择答案，我说的一个一个问我是你问题全部思考完成准备好的时候多个问题排队一起让我选择答案

每一次大的更新完文件，类似版本换代或者说有新功能加入时，必须即时修改README.md中关于文件状态的部分，其他部分不要动

如果是多个任务，每个任务完成的时候复核一遍，确认无误后进行Git commit，但是不要push

发现问题之后和我说你的解决方案，以及准备把架构处理成什么样，不要专业的术词，交付给我你的修复目标和稳定性即可

### 针对 WPF 项目热重载卡死的防范规范
- 任何具备热重载（dotnet watch）或持续监视特性的脚本与工具，必须确保项目配置文件（.csproj）中明确排除了对 `*_wpftmp.csproj` 及 `bin`/`obj` 等动态编译目录的监视，防止编译死循环与异常闪退。
- 在自动化测试或后台静默运行的脚本中，严禁依赖会产生阻断性等待（如 `pause`、`read`、`Press any key to continue`）的命令或批处理文件，避免 AI 代理在验证流程中永久挂起。
