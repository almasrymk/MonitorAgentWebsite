# Change log (knowledge base)

This log records changes to the system or to this knowledge base. It is not the git history.

## 2026-11-03 (Africa/Cairo) — initial analysis

- Created `docs/AI_PROJECT_CONTEXT/` from a source reading of the modular monolith, Angular workspace, deploy files, ADRs, and CI workflow.
- No application source, configuration, or dependency was modified.
- Existing `docs/adr` and `docs/operations` were read and left in place.
- Notable corrections versus older docs: CQRS pipeline includes audit; Angular packages are 22.2.x; Support, Content, and Audit are live modules; domain events are not dispatched.
- Open defect recorded: staff company search assumes every company has an owner.

## Template for later entries

```
## YYYY-MM-DD — short title
- Requirement:
- Code paths:
- Schema change: yes/no
- Tests run and result:
- Knowledge files updated:
```
