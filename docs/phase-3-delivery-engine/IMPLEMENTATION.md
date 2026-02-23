# Phase 3: Deliver Engine - Implementation

## Overview

The **Deliver Engine** is a complete, deterministic, approval-gated mechanism for transforming backlog items marked `ready_for_dev` into deployable applications.

**Key Achievement:** Clean Architecture + Governance + Determinism = Credible Delivery

### Execution Flow

```
┌──────────────────────────────────────┐
│  governor deliver --item 1           │
└──────────────────┬───────────────────┘
                   │
        ┌──────────▼──────────┐
        │ Preconditions       │
        │ ✓ Status=ready_dev  │
        │ ✓ Estimate present  │
        │ ✓ Epic ID resolvable│
        └──────────┬──────────┘
                   │
        ┌──────────▼──────────────────┐
        │ Generate Candidate          │
        │ (fixture template MVP)      │
        └──────────┬──────────────────┘
                   │
        ┌──────────▼──────────────────┐
        │ Validate                    │
        │ dotnet build → exit 0?      │
        │ dotnet run   → exit 0?      │
        └──────────┬──────────────────┘
                   │
        ┌──────────▼──────────────────┐
        │ Write Artifacts             │
        │ (plan, validation, preview) │
        └──────────┬──────────────────┘
                   │
                   ├─ NO --approve
                   │  └─→ Exit 0 (Success)
                   │
                   └─ YES --approve
                      └─→ Validation passed?
                         ├─ NO  → Exit 9 (ValidationFailed)
                         └─ YES
                            ├─ Deploy to /apps/
                            ├─ Write patch.json
                            ├─ Append decision log
                            └─→ Exit 0 (Success)
```

## Architecture Layers

### DeliverFlow (Orchestration)
**File:** `src/GovernorCli/Flows/DeliverFlow.cs`

**Responsibility:**
- Validate repo layout
- Enforce preconditions (status, estimate, epic_id)
- Resolve epic_id → app_id via IEpicStore
- Generate runId and paths
- Create workspace via IWorkspaceStore
- Map exceptions to exit codes
- Log decisions on approval

**Does NOT:**
- Generate implementations
- Run build/test commands
- Access filesystem directly (use stores)

### DeliverUseCase (Business Logic)
**File:** `src/GovernorCli/Application/UseCases/DeliverUseCase.cs`

**Responsibility:**
- Load backlog via IBacklogStore
- Generate implementation plan
- Generate candidate (via FixtureDotNetTemplateGenerator)
- Run validation (build + run via IProcessRunner)
- Compute patch preview
- Deploy via IAppDeployer (if approved)
- Return typed DeliverResponse

**Does NOT:**
- Read environment variables
- Access filesystem directly (use stores)
- Output to console
- Create paths (Flow does this)

**Key Property:** Deterministic, zero environment coupling

### Store Interfaces (4)
**Location:** `src/GovernorCli/Application/Stores/`

1. **IEpicStore** - Resolve epic_id → app_id from state/epics.yaml
2. **IWorkspaceStore** - Reset and create workspace
3. **IProcessRunner** - Execute commands, capture output
4. **IAppDeployer** - Copy workspace to /apps/

### Store Implementations (4)
**Location:** `src/GovernorCli/Infrastructure/Stores/`

1. **EpicStore** - Simple YAML parser
2. **WorkspaceStore** - Workspace lifecycle
3. **ProcessRunner** - cmd.exe shell integration
4. **AppDeployer** - Recursive file copy with audit

## Typed Models

All artifacts are strongly typed (no anonymous objects).

**Location:** `src/GovernorCli/Application/Models/Deliver/DeliverModels.cs`

```csharp
public class ImplementationPlan
{
    public string RunId { get; set; }
    public int ItemId { get; set; }
    public string AppId { get; set; }
    public string TemplateId { get; set; }  // "fixture_dotnet_console_hello"
    public List<string> Actions { get; set; }
}

public class ValidationReport
{
    public bool Passed { get; set; }
    public List<ValidationCommandResult> Commands { get; set; }
}

public class DeliverPatchPreview
{
    public int ItemId { get; set; }
    public string AppId { get; set; }
    public List<string> Files { get; set; }
    public bool ValidationPassed { get; set; }
}

public class PatchApplied
{
    public int ItemId { get; set; }
    public string AppId { get; set; }
    public string RunId { get; set; }
    public List<string> FilesApplied { get; set; }
}

public class DeliverResult
{
    public ImplementationPlan Plan { get; set; }
    public ValidationReport Validation { get; set; }
    public DeliverPatchPreview Preview { get; set; }
    public PatchApplied? PatchApplied { get; set; }  // null unless approved
}
```

## Preconditions (Fail-Fast)

Before ANY generation or deployment:

1. ✅ Repo layout valid (RepoChecks.ValidateLayout)
2. ✅ Item exists
3. ✅ Status == "ready_for_dev"
4. ✅ Estimate present (StoryPoints > 0)
5. ✅ EpicId present and non-empty
6. ✅ EpicId resolvable in state/epics.yaml

**If ANY fail:**
- No workspace created
- No artifacts generated
- Return appropriate exit code
- Flow exits cleanly

## Validation Mechanism

Runs two deterministic commands against candidate:

```
1. dotnet build
   → Capture stdout to build.stdout.log
   → Capture stderr to build.stderr.log
   → Record exit code

2. dotnet run
   → Capture stdout to run.stdout.log
   → Capture stderr to run.stderr.log
   → Record exit code

Validation passed = Both exit 0
```

**Output:** `state/runs/{runId}/validation.json`
```json
{
  "passed": true,
  "commands": [
    {
      "name": "dotnet build",
      "exit_code": 0,
      "stdout_file": "...",
      "stderr_file": "..."
    },
    {
      "name": "dotnet run",
      "exit_code": 0,
      "stdout_file": "...",
      "stderr_file": "..."
    }
  ]
}
```

## Fixture Template Generator

**Purpose:** Minimal deterministic fixture for MVP (NOT production behavior)

**File:** `src/GovernorCli/Application/UseCases/Deliver/FixtureDotNetTemplateGenerator.cs`

**Generated:**
```
{appId}/
  {appId}.csproj
  Program.cs           # "Hello from Deliver fixture!"
  GlobalUsings.cs
```

**TemplateId:** `fixture_dotnet_console_hello`

**Future:** Replace with real template generator (Roslyn-based)

## Approval Gate

Requires successful validation before deployment:

```
if !validation.Passed:
  → Cannot deploy with --approve flag
  → Exit ValidationFailed (9)
  → No state modified

if validation.Passed && --approve:
  → Deploy workspace to /apps/{appId}/
  → Write patch.json (PatchApplied)
  → Append to decision log
  → Exit Success (0)
```

## Artifacts Written

**Location:** `state/runs/{runId}_deliver_item-{itemId}/`

| File | Purpose | Typed Model |
|------|---------|------------|
| `implementation-plan.json` | What will be generated | ImplementationPlan |
| `validation.json` | Build/run results | ValidationReport |
| `patch.preview.json` | What would deploy | DeliverPatchPreview |
| `patch.preview.diff` | Simple file list | Text |
| `patch.json` | Applied patch (approval only) | PatchApplied |
| `summary.md` | Human-readable status | Text |
| `build.stdout.log` | Build output | Text |
| `build.stderr.log` | Build errors | Text |
| `run.stdout.log` | App output | Text |
| `run.stderr.log` | App errors | Text |

## Decision Logging

On successful approval:

```
Appends to: state/decisions/decision-log.md

Format: TIMESTAMP | decision_type | context

Example:
2024-01-15T10:30:05.000Z | deliver approved | item=1 | run=20240115_103005_deliver_item-1 | by=alice
```

**Properties:**
- Append-only (immutable)
- ISO 8601 UTC timestamp
- Actor from GOVERNOR_APPROVER env (default: "local")
- LinkedRunId for artifact traceability
- Never modified or deleted

## Workspace Strategy

**Isolation:** Each app gets dedicated workspace

```
state/workspaces/
  {appId}/
    apps/
      {appId}/
        [generated files]
```

**Determinism:** Reset before each run

```
Run 1: Delete state/workspaces/app-1/ (if exists)
       Create state/workspaces/app-1/
       Generate candidate
       → No stale state from previous runs

Run 2: Delete state/workspaces/app-1/
       Create state/workspaces/app-1/
       Generate candidate (fresh)
```

**Deployment:** Copy workspace to repo on approval

```
state/workspaces/app-1/apps/app-1/ → /apps/app-1/
                      (candidate)      (deployed)
```

## Exit Codes

| Code | Meaning | Next Step |
|------|---------|-----------|
| 0 | Success | Artifacts in state/runs/; decision logged if approved |
| 2 | InvalidRepoLayout | Run `governor init` |
| 3 | ItemNotFound | Check backlog.yaml |
| 4 | BacklogParseError | Fix YAML syntax |
| 5 | DefinitionOfReadyGateFailed | Check item status, estimate, epic_id |
| 8 | ApplyFailed | Unexpected error; check logs |
| 9 | ValidationFailed | Build/run failed; check state/runs/ logs |

## DI & CLI Registration

**Program.cs:**
```csharp
// DI
services.AddSingleton<IEpicStore, EpicStore>();
services.AddSingleton<IWorkspaceStore, WorkspaceStore>();
services.AddSingleton<IProcessRunner, ProcessRunner>();
services.AddSingleton<IAppDeployer, AppDeployer>();
services.AddSingleton<DeliverUseCase>();
services.AddSingleton<Flows.DeliverFlow>();

// CLI
root.Subcommands.Add(BuildDeliverCommand(provider, workdirOption, verboseOption));
```

## Testing

### Unit Tests
**File:** `tests/GovernorCli.Tests/Application/UseCases/DeliverUseCaseTests.cs`

- ✅ Valid item + passing validation → typed result
- ✅ Failing validation → no deployment
- ✅ Approval + passing validation → deployed + PatchApplied
- ✅ Missing item → ItemNotFoundException

### Integration Tests
**File:** `tests/GovernorCli.Tests/Flows/DeliverFlowTests.cs`

- ✅ Invalid layout → InvalidRepoLayout exit code

### Manual Testing
```bash
# 1. Set up state/epics.yaml and backlog.yaml
# 2. Mark item as ready_for_dev
# 3. Preview
governor deliver --item 1
# 4. Verify artifacts
ls state/runs/*/
# 5. Approve
governor deliver --item 1 --approve
# 6. Verify deployment
ls /apps/
# 7. Check decision log
cat state/decisions/decision-log.md
```

## Known Limitations (MVP)

1. **Fixture Only** - Real template generator in Phase 4
2. **Build + Run Only** - No integration tests, security scans
3. **Simple YAML Parser** - Works for MVP, would use real parser for scale
4. **Windows cmd.exe** - Cross-platform support in Phase 4
5. **No Rollback** - Can re-run to re-deploy

## Performance

| Operation | Time |
|-----------|------|
| Preconditions | <100ms |
| Workspace reset | <1s |
| Generate | <100ms |
| Build (fixture) | 5-10s* |
| Run (fixture) | <1s |
| Deploy | <1s |
| **Total (preview)** | **10-15s** |
| **Total (approve)** | **10-15s** |

*First run includes .NET SDK download

## Future Enhancements

- [ ] Real template generator (Roslyn-based)
- [ ] Integration test validation
- [ ] Security scanning
- [ ] Cross-platform process execution
- [ ] Workspace retention policy
- [ ] Rollback to previous workspace
- [ ] Multi-app delivery orchestration
- [ ] Transitive dependency handling

---

**For API details, see [API_CONTRACT.md](API_CONTRACT.md)**
**For usage guide, see [QUICK_REFERENCE.md](QUICK_REFERENCE.md)**
**For acceptance verification, see [COMPLETION_CHECKLIST.md](COMPLETION_CHECKLIST.md)**
