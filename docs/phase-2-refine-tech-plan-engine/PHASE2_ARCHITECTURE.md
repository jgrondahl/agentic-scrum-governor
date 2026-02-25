# Phase 2 Architecture Decisions

## Overview
This document records architectural decisions, trade-offs, and reasoning for the Phase 2 implementation.

---

## Decision 1: Typed Models Over Anonymous Objects

**Decision**: All request/response/artifact data uses sealed records or classes.

**Rationale**:
- Enables compile-time safety (no string-based property access)
- Self-documenting code (clear schema without documentation)
- IDE support (autocomplete, refactoring, navigation)
- JSON serialization via `System.Text.Json` (explicit mapping via `[JsonPropertyName]`)

**Implementation**:
- `ImplementationPlan`, `PatchPreviewData`, `PatchFileChange` are sealed classes
- Models located in `Application/Models/`
- DI-abstracted via interfaces in `Application/Stores/`

**Rejected Alternative**:
- Dynamic JSON objects: Loses type safety and IDE support
- Magic strings: Fragile to refactoring

---

## Decision 2: Separate Plan & Backlog Artifacts

**Decision**: Implementation plan is separate from backlog estimate artifact.

**Rationale**:
- **Backlog Estimate** (`BacklogEstimate`): Cost/complexity metadata (story points, risks, etc.)
  - Embedded in backlog.yaml
  - Lightweight, domain-oriented

- **Implementation Plan** (`ImplementationPlan`): Technical execution blueprint
  - Persisted to state/plans/ (not in backlog)
  - Comprehensive: build plan, run plan, validation checks, project layout
  - References tracked via `implementation_plan_ref` (string path)

**Benefits**:
- Separation of concerns (estimation ≠ implementation)
- Backlog stays lightweight (one estimate field)
- Plans can be independently reviewed/versioned
- Phase 3 can consume plan without touching backlog estimate

---

## Decision 3: Patch Preview Computed On-Demand

**Decision**: Patch preview is computed by service, not pre-computed.

**Rationale**:
- File contents may change on disk between runs
- Computation is O(files changed), not expensive
- Allows flexibility in diff algorithm (future: enhanced hashing, binary diffs)
- Service abstraction allows testing without real files

**Implementation**:
- `IPatchPreviewService.ComputePatchPreview()` takes candidate plan path
- Compares against approved plan (if exists)
- Returns typed `PatchPreviewData` with file changes

**Alternative Rejected**:
- Pre-computed patches: Would require tracking all file state, expensive at scale

---

## Decision 4: Atomic Writes via Temp File + Move

**Decision**: Both plan persistence and backlog updates use temp file + atomic move.

**Rationale**:
- **Crash safety**: Write to temp, then atomic move to destination
- **No partial writes**: If process crashes mid-write, destination is unchanged
- **Idempotent**: Multiple runs of same approval produce same final state

**Implementation**:
```csharp
var tmp = $"{path}.{Guid.NewGuid()}.tmp";
File.WriteAllText(tmp, content);
if (File.Exists(path)) File.Delete(path);
File.Move(tmp, path);  // Atomic on Windows/Linux
```

**Limitation**: Not multi-process safe (but CLI is single-process)

---

## Decision 5: Decision Log Append-Only

**Decision**: Decision log is append-only, never modified or overwritten.

**Rationale**:
- **Immutable audit trail**: History cannot be altered
- **Simple semantics**: Append = success, no append = failure
- **Replay-safe**: Log can be replayed to reconstruct state

**Implementation**:
- Flow appends decision ONLY after usecase returns success
- Format: `TIMESTAMP | refine-tech approved | item=X | run=Y | by=ACTOR`
- File handle opened in append-only mode

**Key Constraint**:
- No rollback: Once approved, decision is permanent
- Correction: Create new item if mistake detected

---

## Decision 6: Preconditions Validated Before Artifacts

**Decision**: Fail fast on preconditions (epic_id, epic resolution) before writing any run artifacts.

**Rationale**:
- **Early feedback**: User knows if request is invalid immediately
- **Clean failure**: No partial artifacts left behind
- **Clear error message**: summary.md explains what was missing

**Preconditions Checked**:
1. Item exists in backlog
2. epic_id is present and non-empty
3. Epic registry (state/epics.yaml) exists
4. Epic is registered in registry

**Implementation**:
```csharp
// BEFORE artifact generation
if (string.IsNullOrWhiteSpace(item.EpicId))
    throw InvalidOperationException("epic_id required");

var appId = _epicStore.ResolveAppId(workdir, item.EpicId);  // May throw

// AFTER validations, generate artifacts
var plan = BuildImplementationPlan(...);
_runArtifactStore.WriteJson(...);
```

---

## Decision 7: MVP Plan Generation (Simplified)

**Decision**: Phase 2 MVP generates placeholder plan with minimal real content.

**Rationale**:
- **Scope control**: Full LLM-assisted plan generation deferred to future
- **Foundation**: Establishes contract and persistence patterns
- **Manual completion**: Architects fill in real details in run artifacts

**Content**:
- `app_type`, `stack`, `repo_target`: Inferred from app_id (dotnet_console assumption)
- `project_layout`: Standard .NET structure (Program.cs, .csproj, README.md, .gitignore)
- `build_plan`: Standard `dotnet build -c Release`
- `run_plan`: Standard `dotnet run -c Release --no-build`
- `validation_checks`: Single exit code check
- `patch_policy`: Standard excludes (bin/**, obj/**, .vs/**, *.user, *.suo)
- `notes`, `assumptions`, `risks`: Copied from backlog item

**Future Enhancement**:
- Phase 3+ will add LLM-assisted plan refinement
- Support multiple app types (node, python, java, etc.)
- Custom build/run commands per app

---

## Decision 8: Plan Validation Strategy

**Decision**: Validation happens at persistence time (approval), not generation time.

**Rationale**:
- **Flexible preview**: User can generate artifacts without strict validation
- **Approve-time safety**: Only validated plans are persisted
- **Clear failure point**: User knows exactly when validation fails

**Validations**:
1. plan_id is set and non-empty
2. notes do not contain placeholder text (e.g., "Placeholder:")
3. Required fields present (via sealed class design)

**Rejected Alternative**:
- Preview-time validation: Would reject valid workflows (e.g., user wants to see errors first)

---

## Decision 9: Epic Resolution Decoupling

**Decision**: Epic → AppId resolution is delegated to `IEpicStore`, not hardcoded.

**Rationale**:
- **Testability**: Mock epic store in tests without file I/O
- **Flexibility**: Future implementations can use database, API, etc.
- **SOLID (Dependency Inversion)**: UseCase depends on abstraction, not concrete epics.yaml

**Current Implementation**:
- `EpicStore`: Parses YAML from state/epics.yaml
- Simple format: `epics: - id: EP-X; app_id: myapp`

**Future Enhancement**:
- Database-backed epic store
- Remote API for epic resolution
- Cached epic registry

---

## Decision 10: Backlog Reference via String Path

**Decision**: Backlog stores implementation_plan_ref as string path, not as nested object.

**Rationale**:
- **Loose coupling**: Backlog doesn't embed plan (avoids duplication)
- **Simple reference**: Just a path string, easy to serialize/deserialize
- **Resolution lazy**: Plan loaded on-demand in Phase 3, not at reference time
- **Audit trail**: Path itself documents where plan came from

**Path Format**:
`state/plans/item-{id}/implementation.plan.json`

**Benefit**:
- Backlog stays lightweight (just string reference)
- Plan can be independently versioned/replaced
- Phase 3 can load plan by following reference

---

## Decision 11: Run Artifact Organization

**Decision**: All run artifacts (both preview and approval) stored in single run folder hierarchy.

**Rationale**:
- **Single source of truth**: All decisions/artifacts for one run in one place
- **Timestamped**: Folder name includes timestamp, allows rerunning safely
- **Complete history**: state/runs/ contains full audit trail

**Folder Structure**:
```
state/runs/
├── 20240115_100000_refine-tech_item-1000/
│   ├── implementation.plan.json
│   ├── patch.preview.json
│   ├── patch.preview.diff
│   ├── patch.backlog.json
│   ├── patch.backlog.applied.json (if --approve)
│   ├── estimation.json
│   ├── architecture.md
│   ├── qa-plan.md
│   ├── technical-tasks.yaml
│   ├── run.json
│   └── summary.md
└── 20240115_101500_refine-tech_item-1000/
    └── [second approval, same item]
```

---

## Decision 12: Error Messaging Strategy

**Decision**: Error details written to run artifacts (summary.md), not CLI output.

**Rationale**:
- **Audit trail**: Error messages persist for later review
- **Structured logging**: summary.md is machine-parseable
- **User experience**: Clear file path to detailed explanation

**Implementation**:
```csharp
// On failure
WriteFailureSummary(request, "EpicIdMissing", 
    "Item requires epic_id... Set epic_id and try again");

// User sees:
// ✗ FAIL
// See state/runs/{run}/summary.md for details
```

---

## Trade-offs Made

| Trade-off | Decision | Rationale |
|-----------|----------|-----------|
| MVP vs Full | MVP (simplified plan generation) | Scope control, foundation solid |
| Async I/O vs Sync | Sync (File.WriteAllText) | Simpler, sufficient for MVP |
| Multi-process safety | Not required | CLI is single-process |
| Rollback support | Not included | Deferred to Phase 3+ |
| App type detection | Hardcoded dotnet_console | MVP; infer later |
| Advanced validation | Minimal (plan_id, notes) | Full validation deferred |

---

## Future Enhancements

1. **Enhanced Plan Generation**: LLM-assisted build/run/validation plan
2. **App Type Detection**: Scan project structure to infer stack
3. **Plan Versioning**: Keep history of plan versions per item
4. **Rollback**: Revert to prior approved plan if needed
5. **Custom Validation**: Per-app validation rules
6. **Performance**: Optimize diff computation, caching
7. **UI**: Web dashboard to review and approve plans
8. **Integration**: Slack notifications on approval

---

## SOLID Principles Applied

| Principle | Application |
|-----------|-------------|
| **S**ingle Responsibility | Each class has one job (service, store, flow) |
| **O**pen/Closed | Extensible via interfaces (IPlanStore, IPatchPreviewService) |
| **L**iskov Substitution | Implementations swap freely (PlanStore vs PlanStoreDb) |
| **I**nterface Segregation | Focused interfaces (plan store, preview service separate) |
| **D**ependency Inversion | Depend on abstractions (IXxxStore), not concretions |

---

## Clean Architecture Applied

- **Entities**: BacklogItem, ImplementationPlan (core domain models)
- **UseCases**: RefineTechUseCase (business logic, orchestration)
- **Interfaces**: IBacklogStore, IPlanStore, IPatchPreviewService (boundary abstractions)
- **Frameworks**: Infrastructure/Stores (concrete implementations)
- **Flow**: RefineTechFlow (orchestration, environment coupling)

---

## Conclusion

Phase 2 implementation prioritizes:
1. **Governance** (preview-approve-log pattern)
2. **Type safety** (sealed models, no anonymous objects)
3. **Clean architecture** (SOLID, DI, separation of concerns)
4. **Foundation** (MVP scope, extensible design)

The design is production-ready for MVP scope and well-positioned for future enhancements.
