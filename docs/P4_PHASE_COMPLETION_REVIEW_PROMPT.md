# P4 Phase Completion Review Prompt

Use the following prompt as the full task input for a dedicated GPT-5.3 Codex thread.

```text
Follow the local AGENTS.md instructions. Work as an implementation-focused senior engineer. Inspect the repository first. Do not make code changes unless absolutely required for inspection tooling, and do not refactor anything. This task is a review/audit task only. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Be precise and evidence-based.

Task:
Review the current implementation status of the local-account security project against the design spec in docs/P4_LOCAL_ACCOUNT_SECURITY_DESIGN.md.

Primary goal:
Determine, phase by phase, what is actually completed, partially completed, inconsistent, or missing.

Scope:
- Only review the desktop local-account security work.
- Focus on these areas:
  - launcher identity database
  - per-account business database isolation
  - cold-start login
  - first-run Owner bootstrap
  - session context
  - PIN resume unlock
  - manual lock/logout
  - Owner/Member account management
  - Recovery Key flow
  - sensitive-field encryption infrastructure
  - backup/restore changes related to local accounts

Do not:
- Do not modify code.
- Do not fix bugs.
- Do not redesign the architecture.
- Do not spend time on unrelated features such as string narration or WeChat paths, except when they create noise or risk for this feature.

What to inspect:
- docs/P4_LOCAL_ACCOUNT_SECURITY_DESIGN.md
- src/Orderly.App/App.xaml.cs
- src/Orderly.App/ViewModels/LoginViewModel.cs
- src/Orderly.App/ViewModels/MainViewModel*.cs
- src/Orderly.App/Views/LoginView*
- src/Orderly.App/Views/PinUnlockView*
- src/Orderly.Data/Sqlite/*
- src/Orderly.Data/Repositories/*
- src/Orderly.Data/Services/*
- src/Orderly.Core/Models/*
- src/Orderly.Core/Services/*
- src/Orderly.Core/Repositories/*

Required method:
1. Read the design document first.
2. Build a phase matrix for:
   - Phase 1 foundation
   - Phase 2 cold-start auth
   - Phase 3 PIN/session lock
   - Phase 4 account management
   - Phase 5 encryption and backup integration
3. For each phase, classify status as:
   - Completed
   - Mostly completed
   - Partially completed
   - Not implemented
4. For each classification, cite concrete file evidence.
5. Explicitly call out mismatches between design and implementation.
6. Explicitly separate:
   - finished implementation
   - risky implementation
   - placeholder/stub implementation
   - adjacent unrelated changes mixed into the same worktree

Deliverables:
- A structured review report in Markdown.
- One section per phase.
- A final summary table with:
  - phase
  - status
  - confidence
  - key evidence
  - blockers/gaps

Output requirements:
- Findings first, ordered by importance.
- Use file references with exact paths.
- Be concrete, not generic.
- If something appears implemented but risky, say that directly.
- If build verification is possible without changing code, run it and include the result.

Suggested final report structure:
1. Findings
2. Phase-by-phase status
3. Design vs implementation mismatches
4. Adjacent unrelated changes that increase review risk
5. Verification / commands run
6. Recommended next review target
```
