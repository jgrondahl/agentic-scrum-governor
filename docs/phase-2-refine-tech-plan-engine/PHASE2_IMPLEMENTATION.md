# Phase 2 Refine-Tech Implementation Summary

## Overview
This implementation upgrades the `refine-tech` flow to produce deterministic, machine-readable **implementation plans** with full governance semantics (preview before apply, explicit approval, append-only decision logs).

## Key Changes

### 1. New Models
- **`ImplementationPlan`** (Application/Models/ImplementationPlan.cs)
  - Typed model for technical design artifact
  - Contains: plan_id, stack info, project layout, build/run plans, validation checks, patch policy
  - Deterministic (no GUIDs in generated source, timestamps for metadata only)
  - Persisted to: `state/plans/item-{itemId}/implementation.plan.json`

### 2. New Stores/Services
- **`IPlanStore` & `PlanStore`**
  - Persistence for approved implementation plans
  - Path: `state/plans/item-{itemId}/implementation.plan.json`
  - Atomic writes via temp file + replace

- **`IPatchPreviewService` & `PatchPreviewService`**
  - Computes patch preview (what files would change)
  - Formats diff lines in standard format: `ACTION path` (e.g., `A apps/myapp/Program.cs`)
  - Normalized paths using `/` separators

### 3. Updated Components

#### RefineTechUseCase
- **New constructor parameters**: `IEpicStore`, `IPlanStore`, `IPatchPreviewService`
- **Preconditions** (fail fast):
  - Item exists in backlog
  - `epic_id` is present and non-empty
  - Epic resolves to `app_id` via state/epics.yaml
  
- **Preview mode** (`--approve` = false):
  - Generates candidate implementation.plan.json
  - Computes patch.preview.json with file changes
  - Writes patch.preview.diff (human-readable diff lines)
  - Generates supporting artifacts (estimation.json, architecture.md, qa-plan.md, technical-tasks.yaml)
  - **No mutations** to backlog or state/plans/

- **Approval mode** (`--approve` = true):
  - Validates plan (no placeholder text, required fields present)
  - Persists to `state/plans/item-{itemId}/implementation.plan.json`
  - Updates backlog item:
    - `status` → `ready_for_dev`
    - `implementation_plan_ref` → path to approved plan
    - Embeds estimate
  - Appends decision log entry (via Flow)

#### RefineTechFlow
- Wraps usecase invocation
- Handles exit code mapping and error reporting
- **Decision logging** (append-only):
  - Only appends if both `--approve` flag AND `Process()` returns success
  - Format: `TIMESTAMP | refine-tech approved | item=X | run=Y | by=APPROVER`

#### BacklogModel
- New field: `implementation_plan_ref` (string, nullable)
- Stores reference to approved plan path

#### Program.cs
- Registered new services in DI container:
  - `IPlanStore` → `PlanStore`
  - `IPatchPreviewService` → `PatchPreviewService`

### 4. Test Coverage
- **PlanStoreTests**: Verify save/load/path logic
- **PatchPreviewServiceTests**: Verify diff computation and formatting
- **RefineTechUseCaseTests**: Updated to mock new dependencies
- **RefineTechFlowTests**: Updated to mock new dependencies

## Execution Flow

### Preview (no --approve)
```
governor refine-tech --item 1000
  ↓
RefineTechFlow.Execute()
  ├─ Validate repo layout
  ├─ Resolve epic_id → app_id
  ├─ Call RefineTechUseCase.Process(approve=false)
  │   ├─ Generate candidate implementation.plan.json
  │   ├─ Compute patch.preview.json, patch.preview.diff
  │   ├─ Generate supporting artifacts
  │   └─ Write summary.md
  └─ Return Success
```

**Outputs** in `state/runs/{runId}/`:
- `implementation.plan.json` (candidate)
- `patch.preview.json` (typed)
- `patch.preview.diff` (human-readable)
- `patch.backlog.json` (backlog changes)
- `estimation.json`, `architecture.md`, `qa-plan.md`, `technical-tasks.yaml`
- `run.json`
- `summary.md`

**Backlog**: No changes

---

### Approval (with --approve)
```
governor refine-tech --item 1000 --approve
  ↓
RefineTechFlow.Execute(approve=true)
  ├─ Validate repo layout
  ├─ Resolve epic_id → app_id
  ├─ Call RefineTechUseCase.Process(approve=true)
  │   ├─ Generate candidate plan (as above)
  │   ├─ Validate plan (no placeholders, required fields)
  │   ├─ Persist to state/plans/item-1000/implementation.plan.json
  │   ├─ Update backlog:
  │   │   ├─ status → ready_for_dev
  │   │   ├─ implementation_plan_ref → state/plans/item-1000/implementation.plan.json
  │   │   └─ Estimate embedded
  │   ├─ Write patch.backlog.applied.json
  │   └─ Write summary.md
  └─ LogDecision() to decision log
```

**Backlog**: Updated with new fields

**Decision Log**: One line appended (only on success)

---

## Governance Semantics

### Preview Before Apply
- Default behavior: `--approve` not specified → read-only preview
- All artifacts in run folder; backlog untouched
- User can review and then decide

### Explicit Approval
- Must pass `--approve` flag to mutate backlog and persist plan
- Approval must succeed for decision log entry to be appended
- Fail fast on validation errors (e.g., placeholder text in notes)

### Append-Only Decision Log
- Located: `state/decisions/decision.log`
- Format: `TIMESTAMP | refine-tech approved | item=X | run=Y | by=ACTOR`
- Only appended after successful approval and persistence
- Never overwritten or deleted

### Typed Models Only
- All request/response data uses sealed records or classes
- No anonymous objects for preview, plan, patch, or changes
- JSON serialization via `System.Text.Json`

### Fail Fast
- Precondition validation before any artifact generation
- Clear error messages in summary.md
- Non-zero exit code on failure

---

## Implementation Plan Contract (MVP)

**Canonical fields:**
- `plan_id`: Deterministic identifier (e.g., `PLAN-20240115-100000-refine-tech-item-1`)
- `created_at_utc`: ISO-8601 metadata timestamp
- `created_from_run_id`: Link to generating run
- `item_id`: Backlog item
- `epic_id`: Epic from backlog item
- `app_id`: Resolved from epic registry
- `repo_target`: Target directory in repo (e.g., `apps/hello-jeremy`)
- `app_type`: `dotnet_console` (MVP)
- `stack`: Language, runtime, framework
- `project_layout`: List of files/dirs in app
- `build_plan`: List of build steps (tool + args)
- `run_plan`: List of run steps
- `validation_checks`: List of post-execution checks (exit code, stdout, etc.)
- `patch_policy`: Exclude globs (bin/**, obj/**, etc.)
- `risks`, `assumptions`: Lists from backlog
- `notes`: Non-placeholder description

---

## Testing

Run tests:
```bash
dotnet test
```

Key test classes:
- `PlanStoreTests` (3 tests)
- `PatchPreviewServiceTests` (3 tests)
- `RefineTechUseCaseTests` (updated to mock new deps)
- `RefineTechFlowTests` (updated to mock new deps)

---

## Future Enhancements (Out of Scope for Phase 2)

1. **App type detection**: Infer from repo structure or explicitly declare
2. **Full project layout**: Scan repo and populate project_layout
3. **Build/run plan generation**: LLM-assisted or template-based
4. **Validation check generation**: Custom checks per app type
5. **Patch policy customization**: Per-app exclusion rules
6. **Deliver integration**: Phase 3 will require implementation_plan_ref and use it for execution

---

## Files Modified/Created

**Created:**
- `src/GovernorCli/Application/Models/ImplementationPlan.cs`
- `src/GovernorCli/Application/Stores/IPatchPreviewService.cs`
- `src/GovernorCli/Application/Stores/IPlanStore.cs`
- `src/GovernorCli/Infrastructure/Stores/PatchPreviewService.cs`
- `src/GovernorCli/Infrastructure/Stores/PlanStore.cs`
- `tests/GovernorCli.Tests/Infrastructure/Stores/PlanStoreTests.cs`
- `tests/GovernorCli.Tests/Infrastructure/Stores/PatchPreviewServiceTests.cs`

**Modified:**
- `src/GovernorCli/Application/UseCases/RefineTechUseCase.cs` (refactored for Phase 2)
- `src/GovernorCli/State/BacklogModel.cs` (added `implementation_plan_ref`)
- `src/GovernorCli/Program.cs` (registered new services)
- `tests/GovernorCli.Tests/Application/UseCases/RefineTechUseCaseTests.cs` (updated for new deps)
- `tests/GovernorCli.Tests/Flows/RefineTechFlowTests.cs` (updated for new deps)

---

## Verification Checklist

- [x] Build succeeds (`dotnet build`)
- [x] Tests pass (`dotnet test`)
- [x] New models are typed (sealed records/classes)
- [x] Stores use DI (IServiceProvider)
- [x] Decision log appends only on successful approval
- [x] Preview mode does not mutate backlog
- [x] Approval mode persists plan + updates backlog
- [x] Fail fast on missing epic_id, epic resolution failure
- [x] Patch preview format is standard (ACTION path)
- [x] Run artifacts include implementation.plan.json, patch.preview.json, patch.preview.diff
- [x] Summary.md explains next steps (preview mode) or success (approval mode)

---
