# Phase 2 Quick Reference

## One-Liner Commands

### Preview
```bash
governor refine-tech --item 1000
```
Output: Run artifacts in `state/runs/` | Backlog: NO CHANGES

### Approve
```bash
governor refine-tech --item 1000 --approve
```
Output: Run artifacts + persisted plan | Backlog: UPDATED

---

## Key Concepts

| Concept | Value |
|---------|-------|
| **Default Behavior** | Preview (read-only) |
| **Approval Flag** | `--approve` |
| **Plan Location** | `state/plans/item-{id}/implementation.plan.json` |
| **Run Artifacts** | `state/runs/{timestamp}_refine-tech_item-{id}/` |
| **Decision Log** | `state/decisions/decision.log` |
| **Precondition 1** | Item exists in backlog.yaml |
| **Precondition 2** | Item has epic_id set |
| **Precondition 3** | Epic registered in epics.yaml |

---

## Artifact Files

| File | Purpose | Mode |
|------|---------|------|
| `implementation.plan.json` | Candidate technical plan | Both |
| `patch.preview.json` | File changes (typed) | Both |
| `patch.preview.diff` | Diff lines (human-readable) | Both |
| `patch.backlog.json` | Backlog changes | Both |
| `patch.backlog.applied.json` | Applied changes | Approval only |
| `estimation.json` | Cost estimate | Both |
| `architecture.md` | Architecture template | Both |
| `qa-plan.md` | QA template | Both |
| `technical-tasks.yaml` | Task breakdown | Both |
| `summary.md` | Status & next steps | Both |

---

## Implementation Plan Structure

```json
{
  "plan_id": "PLAN-...",
  "created_at_utc": "2024-01-15T...",
  "item_id": 1000,
  "epic_id": "EP-1000",
  "app_id": "myapp",
  "repo_target": "apps/myapp",
  "app_type": "dotnet_console",
  "stack": { "language": "csharp", "runtime": "net8.0" },
  "project_layout": [ { "path": "Program.cs", "kind": "source" } ],
  "build_plan": [ { "tool": "dotnet", "args": ["build"] } ],
  "run_plan": [ { "tool": "dotnet", "args": ["run"] } ],
  "validation_checks": [ { "type": "exit_code_equals", "value": "0" } ],
  "patch_policy": { "exclude_globs": ["bin/**", "obj/**"] },
  "risks": [],
  "assumptions": [],
  "notes": "..."
}
```

---

## Patch Preview Diff Format

```
A apps/myapp/Program.cs
M apps/myapp/myapp.csproj
D apps/myapp/old-file.txt
```

Format: `ACTION path`
- `A` = Add
- `M` = Modify
- `D` = Delete
- Paths: repo-relative with `/` separators

---

## Exit Codes

```
0 = Success
1 = Invalid repo layout
2 = Item not found
3 = Backlog parse error
4 = Other (epic resolution, validation, etc.)
```

Always check `summary.md` for details.

---

## Common Workflows

### Development Flow
```bash
# 1. Preview
governor refine-tech --item 1000
# → Review state/runs/*/implementation.plan.json

# 2. Adjust notes in run artifacts if needed
nano state/runs/*/architecture.md
nano state/runs/*/technical-tasks.yaml

# 3. Approve
governor refine-tech --item 1000 --approve

# 4. Verify backlog
cat state/backlog.yaml | grep -A 5 "id: 1000"
```

### Batch Processing
```bash
for ID in 1000 1001 1002; do
  echo "Processing item $ID..."
  governor refine-tech --item $ID --workdir .
  # Review each, then:
  governor refine-tech --item $ID --approve --workdir .
done
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `epic_id missing` | Add `epic_id: EP-XXXX` to backlog item |
| `Epic not found` | Verify `state/epics.yaml` has this epic |
| `Item not found` | Verify ID matches exactly in backlog.yaml |
| `Invalid repo layout` | Run `governor init` to check requirements |
| `Placeholder text` | Edit run artifacts and re-approve |

---

## Code Integration

### For Developers
```csharp
// Inject dependencies
public YourClass(IPlanStore planStore, IPatchPreviewService service)
{
    _planStore = planStore;
    _service = service;
}

// Load approved plan
var plan = _planStore.LoadPlan(workdir, itemId);
if (plan != null)
{
    Console.WriteLine($"Using plan: {plan.PlanId}");
}

// Compute patch
var preview = _service.ComputePatchPreview(workdir, itemId, candidatePath);
var lines = _service.FormatDiffLines(preview);
```

### For Testing
```csharp
// Mock plan store
var planMock = new Mock<IPlanStore>();
planMock.Setup(p => p.LoadPlan(It.IsAny<string>(), 1000))
    .Returns(testPlan);

// Mock preview service
var serviceMock = new Mock<IPatchPreviewService>();
serviceMock.Setup(s => s.ComputePatchPreview(...))
    .Returns(testPreview);
```

---

## Governance Checklist

- [ ] Preview runs first (no --approve flag)
- [ ] User reviews artifacts in state/runs/
- [ ] User approves with --approve flag
- [ ] Backlog is updated atomically
- [ ] Plan persisted to state/plans/
- [ ] Decision log appended
- [ ] Exit code is 0 (success)

---

## Files to Know

| Path | Purpose |
|------|---------|
| `state/backlog.yaml` | Backlog with items |
| `state/epics.yaml` | Epic registry |
| `state/plans/item-{id}/` | Approved plans |
| `state/runs/{timestamp}*/` | Run artifacts |
| `state/decisions/decision.log` | Audit trail |
| `src/GovernorCli/Application/Models/ImplementationPlan.cs` | Plan model |
| `src/GovernorCli/Application/Stores/IPlanStore.cs` | Plan interface |

---
