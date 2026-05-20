# P4 Field Encryption Review Prompt

Use the following prompt as the full task input for a dedicated GPT-5.3 Codex thread.

```text
Follow the local AGENTS.md instructions. Work as an implementation-focused senior engineer. Inspect the repository first. This is a review/audit task only. Do not make code changes. Do not touch miniprogram/, cloudfunctions/, or any WeChat-related integration. Be strict and technical.

Task:
Perform a focused review of the sensitive-field encryption implementation for the local-account security project.

Primary goal:
Determine whether the current encryption design is actually enforced end-to-end, and identify any correctness, migration, session, or data-loss risks.

Scope:
- Only review Phase 5 style work:
  - field encryption service
  - ciphertext column schema
  - plaintext-to-ciphertext migration
  - repository read/write encryption boundaries
  - session DataKey usage
  - backup/restore interaction with encrypted local-account data

Priority review questions:
1. Is real encryption used in the live code path, or are there passthrough/stub paths that can still persist plaintext?
2. Are ciphertext columns added consistently for the intended sensitive fields?
3. Is migration from legacy plaintext to ciphertext safe and idempotent?
4. Do repositories reliably decrypt on read and encrypt on write?
5. Can any code path read or write sensitive fields without a valid session DataKey?
6. Are there risks of partial migration, broken parsing, empty-field corruption, or silently preserved plaintext?
7. Does backup/restore preserve everything required for encrypted local-account data to remain recoverable?

What to inspect:
- docs/P4_LOCAL_ACCOUNT_SECURITY_DESIGN.md
- src/Orderly.Data/Services/FieldEncryptionService.cs
- src/Orderly.Data/Services/PassthroughFieldEncryptionService.cs
- src/Orderly.Data/Services/SensitiveFieldMigrationService.cs
- src/Orderly.Data/Services/SessionContextService.cs
- src/Orderly.Data/Sqlite/DatabaseInitializer.cs
- src/Orderly.Data/Repositories/CustomerRepository.cs
- src/Orderly.Data/Repositories/OrderRepository.cs
- src/Orderly.Data/Repositories/DealRepository.cs
- src/Orderly.Data/Repositories/FollowUpRepository.cs
- src/Orderly.Data/Repositories/CustomerNoteRepository.cs
- src/Orderly.Data/Repositories/ConversationMessageRepository.cs
- src/Orderly.Data/Repositories/AiSuggestionRepository.cs
- src/Orderly.Data/Repositories/OcrResultRepository.cs
- src/Orderly.Data/Repositories/ActivityLogRepository.cs
- src/Orderly.Data/Repositories/PriceAdjustmentRepository.cs
- src/Orderly.Data/Repositories/ReplyTemplateRepository.cs
- src/Orderly.Data/Services/LocalBackupService.cs
- any other directly related local-account encryption code you discover

Review method:
1. Read the design doc’s encryption sections first.
2. Build a sensitive-field coverage checklist from the design.
3. Compare the checklist to:
   - schema changes
   - migration logic
   - repository read/write logic
4. Identify any place where:
   - plaintext may still be stored
   - ciphertext may be unreadable
   - session requirements are unsafe
   - migration can destroy or zero data incorrectly
   - backups cannot restore usable encrypted state
5. Classify findings by severity:
   - Critical
   - High
   - Medium
   - Low

Deliverables:
- A Markdown review report focused on encryption only.
- A coverage table with:
  - entity/table
  - expected sensitive field
  - schema added?
  - migration present?
  - repository encrypts on write?
  - repository decrypts on read?
  - notes/risk
- A list of the top implementation risks.

Output requirements:
- Findings first, with severity and exact file references.
- Do not give a generic summary before the findings.
- Be direct if the implementation looks incomplete or unsafe.
- If possible, run a build and include the result.
- Mention any residual verification gap that cannot be confirmed by static inspection alone.

Suggested final report structure:
1. Findings
2. Encryption coverage matrix
3. Migration safety assessment
4. Session/DataKey safety assessment
5. Backup/restore encryption assessment
6. Verification / commands run
7. Recommended remediation order
```
