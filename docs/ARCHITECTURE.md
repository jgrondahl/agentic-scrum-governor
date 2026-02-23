# System Architecture

## Overview

The Agentic SCRUM Governor implements **Clean Architecture** with three core layers:

```
┌─────────────────────────────────────┐
│  Flows (Orchestration)              │  CLI entry points, routing,
│  - IntakeFlow                       │  precondition validation,
│  - RefineFlow                       │  decision logging
│  - RefineTechFlow                   │
│  - DeliverFlow                      │
└────────────┬────────────────────────┘
             │ calls
┌────────────▼────────────────────────┐
│  UseCases (Business Logic)          │  Deterministic operations,
│  - IntakeUseCase                    │  no environment coupling,
│  - RefineUseCase                    │  no I/O,
│  - RefineTechUseCase                │  returns typed results
│  - DeliverUseCase                   │
└────────────┬────────────────────────┘
             │ uses
┌────────────▼────────────────────────┐
│  Stores (Data Abstraction)          │  Filesystem operations,
│  ├─ IBacklogStore                   │  process execution,
│  ├─ IRunArtifactStore               │  all behind interfaces
│  ├─ IDecisionStore                  │
│  ├─ IEpicStore                      │
│  ├─ IWorkspaceStore                 │
│  ├─ IProcessRunner                  │
│  └─ IAppDeployer                    │
└─────────────────────────────────────┘
```

## Layer Responsibilities

### Flow (Orchestration)
**Responsibility:** Route, validate preconditions, handle exceptions, log decisions

**Does NOT:**
- Access filesystem directly (use stores)
- Perform business logic (use UseCases)
- Output to console (done by Program.cs)

**Does:**
- Validate repo layout
- Check preconditions (fail-fast)
- Create paths and runIds
- Resolve environment variables
- Map exceptions to exit codes
- Log decisions to decision-log.md

**Example:** `DeliverFlow.cs`
```csharp
public FlowExitCode Execute(string workdir, int itemId, bool verbose, bool approve)
{
    // 1. Validate
    RepoChecks.ValidateLayout(workdir);  // Fail-fast
    
    // 2. Preconditions
    if (item.Status != "ready_for_dev")
        throw new InvalidOperationException(...);
    
    // 3. Resolve
    var appId = _epicStore.ResolveAppId(workdir, item.EpicId);
    
    // 4. Create context
    var runId = GenerateRunId();
    var approver = Environment.GetEnvironmentVariable("GOVERNOR_APPROVER") ?? "local";
    
    // 5. Delegate to UseCase
    var response = _useCase.Process(request);
    
    // 6. Log decision (if approved)
    if (approve && response.Success)
        _decisionStore.LogDecision(workdir, $"{utc:O} | deliver approved | ...");
    
    return FlowExitCode.Success;
}
```

### UseCase (Business Logic)
**Responsibility:** Deterministic computation, artifact generation

**Does NOT:**
- Read environment variables
- Access filesystem directly
- Output to console
- Create paths or runIds (Flow does this)

**Does:**
- Load data via stores (abstract)
- Perform business operations
- Call stores for writes (via abstraction)
- Return typed results

**Key Property:** Zero environment coupling - all context via typed request

**Example:** `DeliverUseCase.cs`
```csharp
public DeliverResponse Process(DeliverRequest request)
{
    // Load via store abstraction
    var backlog = _backlogStore.Load(request.BacklogPath);
    
    // Compute
    var validation = RunValidation(request.WorkspaceAppRoot, request.RunDir);
    var preview = ComputePatchPreview(request.AppId, ...);
    
    // Write via store abstraction
    _runArtifactStore.WriteJson(request.RunDir, "validation.json", validation);
    
    // Return typed result
    return new DeliverResponse
    {
        Success = validation.Passed,
        Result = new DeliverResult { ... }
    };
}
```

### Store (Data Abstraction)
**Responsibility:** All I/O operations behind interfaces

**Types:**
- **Interfaces** (Application/Stores/) - Define contracts
- **Implementations** (Infrastructure/Stores/) - Implement I/O

**Examples:**

```csharp
// Interface
public interface IBacklogStore
{
    BacklogFile Load(string filePath);
    void Save(string filePath, BacklogFile backlog);
}

// Implementation
public class BacklogStore : IBacklogStore
{
    public BacklogFile Load(string filePath)
        => BacklogLoader.Load(filePath);  // Delegates to utility
    
    public void Save(string filePath, BacklogFile backlog)
        => BacklogSaver.Save(filePath, backlog);
}
```

## State Machine

### Item Status Lifecycle

```
┌─────────────────────────────────────────────────────┐
│                    candidate                         │
│            (created by intake, waiting refine)      │
└──────────────────┬──────────────────────────────────┘
                   │ governor refine --approve
                   ▼
┌─────────────────────────────────────────────────────┐
│                     ready                            │
│           (business refined, waiting tech review)   │
└──────────────────┬──────────────────────────────────┘
                   │ governor refine-tech --approve
                   ▼
┌─────────────────────────────────────────────────────┐
│                   ready_for_dev                      │
│         (technically ready, waiting delivery)       │
└──────────────────┬──────────────────────────────────┘
                   │ governor deliver --item X --approve
                   ▼
┌─────────────────────────────────────────────────────┐
│                      done                            │
│            (deployed with decision logged)          │
└─────────────────────────────────────────────────────┘
```

## Decision Logging

All approvals recorded immutably in `state/decisions/decision-log.md`:

```
Timestamp | Decision Type | Context
2024-01-15T10:30:00Z | refine approved | item=42 | run=20240115_103000_refine_item-42 | by=alice
2024-01-15T10:35:05Z | refine-tech approved | item=42 | run=20240115_103505_refine-tech_item-42 | by=bob
2024-01-15T11:00:10Z | deliver approved | item=42 | run=20240115_110010_deliver_item-42 | by=alice
```

**Properties:**
- Append-only (no modifications)
- Timestamped (UTC ISO 8601)
- Actor recorded (GOVERNOR_APPROVER env var)
- RunId linked to artifacts
- Immutable audit trail

## Preconditions Pattern

All flows use fail-fast precondition validation:

```csharp
// Before ANY mutation:
1. Validate repo layout
2. Find item
3. Check item exists
4. Check status gate
5. Check required fields (estimate, epic_id)
6. Resolve references (epic_id → app_id)

// If ANY step fails:
- Do NOT create workspace
- Do NOT generate artifacts
- Do NOT modify state
- Return appropriate exit code
```

## Run Artifacts

Each execution produces a run directory with typed artifacts:

```
state/runs/20240115_103005_deliver_item-1/
├── implementation-plan.json    # Plan model
├── validation.json             # ValidationReport model
├── patch.preview.json          # DeliverPatchPreview model
├── patch.json                  # PatchApplied model (if approved)
├── summary.md                  # Human-readable status
├── build.stdout.log            # Command output
├── build.stderr.log            # Error output
├── run.stdout.log
└── run.stderr.log
```

**All JSON artifacts are strongly typed** - no anonymous objects.

## Workspace Isolation

Each app gets an isolated workspace that's reset per run:

```
Run 1: state/workspaces/myapp/ [created]
       state/workspaces/myapp/apps/myapp/ [candidate generated]
       → Delete old workspace for determinism

Run 2: state/workspaces/myapp/ [recreated]
       state/workspaces/myapp/apps/myapp/ [new candidate]
       → Same app_id, fresh state, no cross-run pollution
```

**Benefits:**
- Determinism (no stale state)
- Isolation (per-app)
- Testability (predictable)
- Parallelism (future: run different apps simultaneously)

## Error Handling

Exit codes map to flow outcomes:

```csharp
public enum FlowExitCode
{
    Success = 0,                          // Operation completed
    InvalidRepoLayout = 2,                // init --help
    ItemNotFound = 3,                     // Check backlog.yaml
    BacklogParseError = 4,                // Fix YAML syntax
    DefinitionOfReadyGateFailed = 5,      // Check preconditions
    PromptLoadError = 6,                  // Check prompts/ files
    ContractValidationFailed = 7,         // LLM output invalid
    ApplyFailed = 8,                      // Unexpected error
    ValidationFailed = 9                  // Build or run failed
}
```

**Program.cs maps codes to user messages:**
```csharp
case FlowExitCode.DefinitionOfReadyGateFailed:
    AnsiConsole.MarkupLine("[red]Item not ready (status, estimate, epic_id)[/]");
    break;
```

## Patterns & Conventions

### Typed Models
All domain concepts are typed:

```csharp
// Application/Models/Deliver/DeliverModels.cs
public class ImplementationPlan
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; }
    
    [JsonPropertyName("item_id")]
    public int ItemId { get; set; }
    
    [JsonPropertyName("app_id")]
    public string AppId { get; set; }
    // ... more properties
}
```

**No anonymous objects, no `dynamic`, no `object`.**

### Store Pattern
Data operations hidden behind interfaces:

```csharp
// Application/Stores/IBacklogStore.cs
public interface IBacklogStore
{
    BacklogFile Load(string filePath);
    void Save(string filePath, BacklogFile backlog);
}

// Infrastructure/Stores/BacklogStore.cs
public class BacklogStore : IBacklogStore { ... }

// DI: services.AddSingleton<IBacklogStore, BacklogStore>();
```

**Benefits:**
- Testable (mock stores in tests)
- Replaceable (swap implementations)
- Decoupled (Flow/UseCase don't know about BacklogStore)

### Request/Response Pattern
UseCases take typed requests, return typed responses:

```csharp
// Request: all input from Flow
public class DeliverRequest
{
    public int ItemId { get; set; }
    public string AppId { get; set; }
    public string BacklogPath { get; set; }
    // ... more context
}

// Response: typed result
public class DeliverResponse
{
    public bool Success { get; set; }
    public DeliverResult Result { get; set; }
}

// UseCase: stateless, deterministic
var response = useCase.Process(request);
```

## Testing Strategy

### Unit Tests (UseCases)
Mock stores, verify business logic:

```csharp
[Test]
public void Process_WithValidItem_ReturnsTypedResult()
{
    var backlog = new BacklogFile { Backlog = new() { item } };
    _storeMock.Setup(s => s.Load(...)).Returns(backlog);
    
    var result = _useCase.Process(request);
    
    Assert.That(result.Success, Is.True);
    Assert.That(result.Result.Plan.ItemId, Is.EqualTo(1));
}
```

### Integration Tests (Flows)
Create minimal state, run full flow, verify outcomes:

```csharp
// 1. Create state/backlog.yaml
// 2. Call flow.Execute()
// 3. Verify state/runs/ artifacts
// 4. Verify decision log appended
```

### Manual Scenarios
Full end-to-end with real CLI:

```bash
governor init
governor intake --title "Test" --story "..."
governor refine --item 1
governor refine-tech --item 1
governor deliver --item 1
governor deliver --item 1 --approve
```

## Future Extensibility

### Adding a New Flow

1. Create `Flows/NewFlow.cs` (orchestration)
2. Create `Application/UseCases/NewUseCase.cs` (logic)
3. Create request/response models
4. Register in Program.cs DI
5. Add command in Program.cs BuildXCommand()
6. Add tests

### Adding a New Store

1. Create interface `Application/Stores/INewStore.cs`
2. Create implementation `Infrastructure/Stores/NewStore.cs`
3. Implement interface methods
4. Register in DI
5. Inject where needed

### Adding a New Artifact Model

1. Create typed model in `Application/Models/`
2. Add JSON property attributes
3. Return from UseCase
4. Write via IRunArtifactStore
5. Add to tests

---

**For Phase 3 (Deliver Engine) architecture details, see [docs/phase-3-delivery-engine/](../phase-3-delivery-engine/).**
