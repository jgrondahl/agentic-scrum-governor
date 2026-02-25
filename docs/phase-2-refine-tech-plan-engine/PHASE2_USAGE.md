# Phase 2 Refine-Tech Usage Guide

## Quick Start

### Step 1: Preview Technical Design
```bash
governor refine-tech --item 1000 --workdir /path/to/repo
```

**Output**: Run artifacts in `state/runs/{timestamp}_refine-tech_item-1000/`
- `implementation.plan.json` - Candidate plan (typed, deterministic)
- `patch.preview.json` - File changes (typed)
- `patch.preview.diff` - Human-readable diff lines
- `estimation.json` - Cost/complexity estimate
- `architecture.md` - Architecture template (fill in)
- `qa-plan.md` - QA template (fill in)
- `technical-tasks.yaml` - Task breakdown (fill in)
- `summary.md` - What happened

**Backlog**: ✅ NO CHANGES

---

### Step 2: Review & Approve
After reviewing the preview (especially architecture.md and technical-tasks.yaml):

```bash
governor refine-tech --item 1000 --approve --workdir /path/to/repo
```

**Output**: 
- Same run artifacts as above
- Plus `patch.backlog.applied.json` (what was applied)

**Backlog Changes**:
- `status` → `ready_for_dev`
- `estimate` → Updated with story points, confidence, risks, etc.
- `implementation_plan_ref` → `state/plans/item-1000/implementation.plan.json`
- `technical_notes_ref` → `runs/{timestamp}_refine-tech_item-1000/`

**Decision Log**:
- Appends: `2024-01-15T10:30:00+00:00 | refine-tech approved | item=1000 | run=20240115_103000_refine-tech_item-1000 | by=local`

---

## Preconditions

The item must satisfy:
1. **Exists in backlog** (exact ID match required)
2. **Has epic_id** set (required for Phase 2)
3. **Epic is resolvable** from `state/epics.yaml`
   ```yaml
   epics:
     - id: EP-1000
       app_id: hello-jeremy
   ```

### Example: Invalid Epic
```bash
$ governor refine-tech --item 1000 --workdir /tmp
✗ FAIL
Epic resolution failed: Could not resolve epic_id 'EP-UNKNOWN' to app_id.
Ensure state/epics.yaml exists and contains this epic.

# Run artifacts written to: state/runs/.../summary.md (explains error)
# Backlog: NO CHANGES
```

---

## Generated Artifacts

### implementation.plan.json
Deterministic plan describing what will be built:

```json
{
  "plan_id": "PLAN-20240115-103000-refine-tech-item-1000",
  "created_at_utc": "2024-01-15T10:30:00.000Z",
  "created_from_run_id": "20240115_103000_refine-tech_item-1000",
  "item_id": 1000,
  "epic_id": "EP-1000",
  "app_id": "hello-jeremy",
  "repo_target": "apps/hello-jeremy",
  "app_type": "dotnet_console",
  "stack": {
    "language": "csharp",
    "runtime": "net8.0",
    "framework": "dotnet"
  },
  "project_layout": [
    { "path": "Program.cs", "kind": "source" },
    { "path": "hello-jeremy.csproj", "kind": "project" },
    { "path": ".gitignore", "kind": "config" },
    { "path": "README.md", "kind": "docs" }
  ],
  "build_plan": [
    {
      "tool": "dotnet",
      "args": ["build", "-c", "Release"],
      "cwd": "."
    }
  ],
  "run_plan": [
    {
      "tool": "dotnet",
      "args": ["run", "-c", "Release", "--no-build"],
      "cwd": "."
    }
  ],
  "validation_checks": [
    {
      "type": "exit_code_equals",
      "value": "0"
    }
  ],
  "patch_policy": {
    "exclude_globs": ["bin/**", "obj/**", ".vs/**", "**/*.user", "**/*.suo"]
  },
  "risks": ["Team unfamiliar with C# async patterns"],
  "assumptions": ["dotnet 8.0 available in deployment env"],
  "notes": "Implementation plan for item 1000 (Build Hello World CLI). Generated from refine-tech run ..."
}
```

### patch.preview.diff
Human-readable file changes:

```
A apps/hello-jeremy/Program.cs
A apps/hello-jeremy/hello-jeremy.csproj
A apps/hello-jeremy/.gitignore
A apps/hello-jeremy/README.md
```

Format: `ACTION path` (A=add, M=modify, D=delete)

---

## Governance Enforcement

### 1. Preview Before Apply ✅
- Default: read-only preview with no backlog mutations
- Explicit flag required: `--approve`
- User can review run artifacts before approval

### 2. Fail Fast ✅
- Missing epic_id → immediate error, no artifacts
- Epic resolution fails → error summary in summary.md
- Plan validation fails (e.g., placeholder text) → error, no persistence

### 3. Append-Only Decision Log ✅
- Entry appended only after successful approval
- Format: `TIMESTAMP | refine-tech approved | item=X | run=Y | by=ACTOR`
- Never overwritten, never conditional

### 4. Typed Models ✅
- All data structures are sealed records or classes
- No anonymous objects
- JSON serialization via `System.Text.Json`

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Invalid repo layout |
| 2 | Item not found |
| 3 | Backlog parse error |
| 4 | Other error (e.g., epic resolution, validation failure) |

Check `summary.md` for details in case of failure.

---

## Integration with Phase 3 (Deliver)

Phase 3 will:
1. **Require** `implementation_plan_ref` to be set
2. **Read** the approved plan from `state/plans/item-{id}/implementation.plan.json`
3. **Use** build_plan, run_plan, and validation_checks to execute delivery
4. **Ignore** legacy `delivery_template_id` field

---

## Troubleshooting

### "epic_id missing"
**Fix**: Add `epic_id` to backlog item:
```yaml
backlog:
  - id: 1000
    title: Build Hello World CLI
    epic_id: EP-1000
    story: As a developer, I want a CLI that greets the world
```

### "Epic not found in registry"
**Fix**: Ensure `state/epics.yaml` contains the epic:
```yaml
epics:
  - id: EP-1000
    app_id: hello-jeremy
```

### "Plan notes cannot contain placeholder text"
**Fix**: Edit `state/runs/{run}/technical-tasks.yaml` to replace placeholder tasks with real ones, then re-run with `--approve`.

---
