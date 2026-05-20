# P4 End-to-End Acceptance Precheck Prompt

Use the following prompt as the full task input for a dedicated GPT-5.3 Codex thread.

```text
Follow the local AGENTS.md instructions. Work as an implementation-focused senior engineer. Inspect the repository first. This is a review/precheck task only. Do not make code changes. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Be explicit about what is testable now versus what still lacks confidence.

Task:
Perform an end-to-end acceptance precheck for the local-account security project before formal validation.

Primary goal:
Determine which acceptance scenarios from docs/P4_LOCAL_ACCOUNT_SECURITY_DESIGN.md are already testable and likely to pass, which are unverified, and which are still blocked by implementation gaps or review uncertainty.

Scope:
- Desktop local-account security only.
- Focus on user-visible and system-visible acceptance behavior, not just code presence.

Acceptance areas to assess:
- first-run Owner creation
- Recovery Key generation and acknowledgement
- cold-start login with username + master password
- rejection of wrong master password
- per-account database isolation
- Owner creates Member
- Member cannot create accounts
- Owner disables Member
- Member password reset
- Member PIN reset
- Owner Recovery Key password reset
- sleep/resume PIN unlock
- manual lock
- logout back to login
- encrypted sensitive fields still readable/editable/searchable after login
- backup/restore consistency for launcher identity data plus account business data

What to inspect:
- docs/P4_LOCAL_ACCOUNT_SECURITY_DESIGN.md
- src/Orderly.App/App.xaml.cs
- src/Orderly.App/Views/LoginView*
- src/Orderly.App/Views/PinUnlockView*
- src/Orderly.App/Views/MainWindow*
- src/Orderly.App/ViewModels/LoginViewModel.cs
- src/Orderly.App/ViewModels/MainViewModel*.cs
- src/Orderly.Data/Services/*
- src/Orderly.Data/Repositories/*
- src/Orderly.Data/Sqlite/*
- src/Orderly.Core/Models/*
- src/Orderly.Core/Services/*
- src/Orderly.Core/Repositories/*
- any QA/build scripts that help establish current validation coverage

Required method:
1. Read the design doc acceptance sections first.
2. Build an acceptance checklist from the design.
3. For each acceptance scenario, classify it as:
   - Likely implemented and testable now
   - Implemented but risky / needs directed validation
   - Partially implemented
   - Not yet ready for acceptance
4. For each item, provide exact code evidence.
5. Distinguish:
   - static confidence from code inspection
   - actual executed verification
6. If build or existing QA commands can be run safely, run the smallest relevant set and include the real results.

Deliverables:
- A Markdown acceptance precheck report.
- A scenario-by-scenario checklist table with:
  - scenario
  - current status
  - evidence
  - confidence
  - what still needs to be validated manually or with targeted testing

Output requirements:
- Start with findings and blockers.
- Keep the report acceptance-oriented, not architecture-oriented.
- Explicitly identify any scenario that looks implemented in code but still lacks enough evidence for acceptance.
- End with a practical “what to validate next” sequence.

Suggested final report structure:
1. Findings
2. Acceptance scenario checklist
3. Scenarios that are likely ready
4. Scenarios that need focused validation
5. Scenarios blocked by code or review uncertainty
6. Verification / commands run
7. Recommended manual / scripted validation order
```
