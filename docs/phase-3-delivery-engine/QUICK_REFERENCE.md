# Phase 3: Quick Reference & Usage Guide

## Getting Started

### 1. Prepare State Files

Create `state/epics.yaml`:
```yaml
epics:
  epic-1:
    app_id: hello-world-app
  epic-2:
    app_id: my-service
```

Update `state/backlog.yaml` with `ready_for_dev` items:
```yaml
backlog:
  - id: 1
    title: "Deliver Hello World"
    status: "ready_for_dev"
    epic_id: "epic-1"
    estimate:
      story_points: 5
```

### 2. Preview
```bash
governor deliver --item 1
# Output: ✓ Validation passed. Preview written.
# Artifacts: state/runs/20240115_103005_deliver_item-1/
```

### 3. Review Artifacts
```bash
cat state/runs/20240115_103005_deliver_item-1/validation.json
cat state/runs/20240115_103005_deliver_item-1/summary.md
```

### 4. Approve & Deploy
```bash
governor deliver --item 1 --approve
# Output: ✓ Approved and deployed.
# Deployed to: /apps/hello-world-app/
# Decision logged to: state/decisions/decision-log.md
```

### 5. Verify
```bash
ls /apps/hello-world-app/
cat state/decisions/decision-log.md
```

## CLI Commands

### Preview Mode
```bash
governor deliver --item <id> [--workdir <path>] [--verbose]
```

**Returns:**
- Exit 0: Validation passed
- Exit 9: Validation failed
- Exit 2,3,4,5: Precondition failed

**Outputs:**
- Workspace: `state/workspaces/{appId}/`
- Artifacts: `state/runs/{runId}_deliver_item-{id}/`
- **NO deployment**

### Approval Mode
```bash
governor deliver --item <id> --approve [--workdir <path>] [--verbose]
```

**Requirements:**
- Validation must pass (else exit 9)

**Results:**
- Deploy: `state/workspaces/{appId}/ → /apps/{appId}/`
- Decision: Append to `state/decisions/decision-log.md`
- Artifacts: Same as preview

## Environment Variables

```bash
# Set approver name
export GOVERNOR_APPROVER=alice
governor deliver --item 1 --approve
# Log entry: by=alice

# Default (if not set)
# Log entry: by=local
```

## Artifact Structure

### Run Directory
```
state/runs/20240115_103005_deliver_item-1/
  implementation-plan.json     # Plan
  validation.json              # Build/run results
  patch.preview.json           # What would deploy
  patch.preview.diff           # File list
  patch.json                   # Applied (approval only)
  summary.md                   # Status
  build.stdout.log             # Build output
  build.stderr.log             # Build errors
  run.stdout.log               # App output
  run.stderr.log               # App errors
```

### Workspace Directory
```
state/workspaces/{appId}/apps/{appId}/
  {generated files}            # Candidate implementation
```

### Deployed Directory
```
/apps/{appId}/
  {deployed files}             # Copy from workspace (approval only)
```

## Troubleshooting

### "Item not found"
```
Error: Backlog item not found: 1
```
**Check:**
```bash
grep "id: 1" state/backlog.yaml
```

### "Item not ready"
```
Error: Item does not meet preconditions (status != ready_for_dev, no estimate, no epic_id).
```
**Fixes:**
```bash
# Fix status
# Fix estimate (run governor refine-tech --item 1)
# Fix epic_id (add to backlog.yaml)
```

### "Epic not found"
```
Error: Epic not found in registry: epic-999
```
**Add to state/epics.yaml:**
```yaml
epics:
  epic-999:
    app_id: myapp
```

### "Validation failed"
```
Error: Validation failed. See state/runs/ for details.
```
**Check:**
```bash
cat state/runs/{runId}/build.stderr.log
cat state/runs/{runId}/run.stderr.log
```

### "Validation failed" but you need to approve anyway
**Can't.** Approval gate requires validation pass.
- Fix errors and re-run preview
- Then approve

## Exit Codes

| Code | Meaning | Fix |
|------|---------|-----|
| 0 | Success | Check artifacts in state/runs/ |
| 2 | InvalidRepoLayout | Run `governor init` |
| 3 | ItemNotFound | Check backlog.yaml |
| 4 | BacklogParseError | Fix YAML syntax |
| 5 | PreconditionFailed | Fix item (status, estimate, epic_id) |
| 8 | ApplyFailed | Unexpected; check logs |
| 9 | ValidationFailed | Fix build/run errors |

## Common Workflows

### Full Delivery
```bash
# Create item
governor intake --title "New App" --story "..."

# Refine
governor refine --item 1

# Technical review
governor refine-tech --item 1

# Deliver preview
governor deliver --item 1

# Inspect
cat state/runs/*/summary.md

# Deploy
governor deliver --item 1 --approve

# Verify
ls /apps/
cat state/decisions/decision-log.md
```

### Rapid Iteration
```bash
# Generate
governor deliver --item 1

# Check errors
cat state/runs/*/build.stderr.log

# Retry (workspace auto-resets)
governor deliver --item 1

# Approve when ready
governor deliver --item 1 --approve
```

### Batch Delivery
```bash
# Get all ready_for_dev items
grep -n "ready_for_dev" state/backlog.yaml

# Deliver each
for id in 1 2 3; do
  governor deliver --item $id --approve
done
```

## Decision Log

**Location:** `state/decisions/decision-log.md`

**Format:**
```
TIMESTAMP | decision_type | context
```

**Example:**
```
2024-01-15T10:30:05.000Z | deliver approved | item=1 | run=20240115_103005_deliver_item-1 | by=alice
2024-01-15T10:35:10.000Z | deliver approved | item=2 | run=20240115_103510_deliver_item-2 | by=bob
```

**Properties:**
- Append-only (immutable)
- ISO 8601 UTC timestamps
- Actor from GOVERNOR_APPROVER env var
- LinkedRunId to artifacts
- Full audit trail

## Performance Notes

**First run:** 10-15s (includes .NET SDK download)
**Subsequent:** 10-15s (cached SDK)

### Breakdown
- Preconditions: <100ms
- Workspace reset: <1s
- Generate: <100ms
- Build: 5-10s*
- Run: <1s
- Deploy: <1s
- Decision log: <100ms

*First run includes SDK download

## Monitoring

### View all runs
```bash
ls -lt state/runs/ | grep deliver_item
```

### Track decisions
```bash
cat state/decisions/decision-log.md | grep deliver
```

### Compare deployed vs workspace
```bash
diff -r state/workspaces/app-1/apps/app-1/ /apps/app-1/
```

### Check artifact completeness
```bash
ls state/runs/{runId}/
# Should have: implementation-plan.json, validation.json, 
#              patch.preview.json, patch.json (if approved),
#              summary.md, *.log files
```

## Known Limitations

1. **Fixture Generator** - Very minimal (not production-grade)
   - Future: Replace with real template generator

2. **Validation** - Only build + run
   - Future: Add integration tests, security scans

3. **YAML Parser** - Simple custom parser
   - Future: Use proper YAML library for scale

4. **Process Execution** - Windows cmd.exe only
   - Future: Cross-platform support

5. **No Rollback** - Can't undo deployment
   - Workaround: Re-run to re-deploy with different template

## Future Enhancements

- [ ] Real template generator (Roslyn)
- [ ] Integration test validation
- [ ] Security scanning
- [ ] Cross-platform process execution
- [ ] Workspace retention policy
- [ ] Rollback mechanism
- [ ] Multi-app orchestration
- [ ] Transitive dependencies

---

**For technical details, see [IMPLEMENTATION.md](IMPLEMENTATION.md)**
**For API contract, see [API_CONTRACT.md](API_CONTRACT.md)**
**For requirements verification, see [COMPLETION_CHECKLIST.md](COMPLETION_CHECKLIST.md)**
