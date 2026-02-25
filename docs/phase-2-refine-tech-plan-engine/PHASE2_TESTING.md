# Phase 2 Testing & Verification Guide

## Unit Tests

### PlanStoreTests (3 tests)
- **SavePlan_CreatesDirectoriesAndPersistsJson**: Verify atomic persistence
- **LoadPlan_ReturnsNullWhenNotFound**: Verify graceful missing file handling
- **GetPlanPath_ReturnsCorrectPath**: Verify path construction

```bash
dotnet test --filter "PlanStoreTests"
```

### PatchPreviewServiceTests (3 tests)
- **ComputePatchPreview_WithNewPlan_ReturnsChanges**: Verify patch computation
- **FormatDiffLines_ProducesCorrectFormat**: Verify diff output format
- **FormatDiffLines_NormalizesWindowsPaths**: Verify cross-platform path handling

```bash
dotnet test --filter "PatchPreviewServiceTests"
```

### RefineTechUseCaseTests (updated)
- Updated to mock: `IEpicStore`, `IPlanStore`, `IPatchPreviewService`
- Verifies implementation plan generation
- Verifies patch preview typing

### RefineTechFlowTests (updated)
- Updated to mock new dependencies
- Verifies flow orchestration (validation → usecase → decision logging)
- Verifies exit code mapping

---

## Manual Testing

### Setup: Create Test Repository

```bash
mkdir -p /tmp/test-repo/state/plans /tmp/test-repo/state/runs /tmp/test-repo/apps
cd /tmp/test-repo

# Create team-board.md
cat > state/team-board.md << 'EOF'
# Team Board
Team members and roles...
EOF

# Create epics.yaml
cat > state/epics.yaml << 'EOF'
epics:
  - id: EP-TEST-001
    app_id: test-app
EOF

# Create backlog.yaml
cat > state/backlog.yaml << 'EOF'
backlog:
  - id: 1000
    title: Build Test App
    status: candidate
    priority: 1
    size: M
    owner: DevTeam
    story: "As a developer, I want a working test application"
    acceptance_criteria:
      - "Builds with dotnet build"
      - "Runs without errors"
    non_goals: []
    dependencies: []
    risks: []
    epic_id: EP-TEST-001
EOF

# Create required prompts structure
mkdir -p prompts/personas prompts/flows
touch prompts/personas/architect.md
touch prompts/flows/refine-tech.md

# Create src directory (satisfies layout validation)
mkdir -p src
```

---

### Test 1: Preview Mode (No Mutations)

```bash
cd /tmp/test-repo

# Run preview
governor refine-tech --item 1000 --workdir .

# Expected output:
# ✓ Preview written (no backlog changes)
# See state/runs/ for artifacts

# Verify backlog is unchanged
cat state/backlog.yaml | grep implementation_plan_ref
# (should be empty/not present)

# Verify run artifacts exist
ls -la state/runs/*/
# Should have: implementation.plan.json, patch.preview.json, patch.preview.diff, 
#              estimation.json, architecture.md, qa-plan.md, technical-tasks.yaml, summary.md

# Verify patch preview format
cat state/runs/*/patch.preview.diff
# Should output like:
# A apps/test-app/Program.cs
# A apps/test-app/test-app.csproj
# ...
```

---

### Test 2: Approval Mode (With Mutations)

```bash
cd /tmp/test-repo

# Run with approval
governor refine-tech --item 1000 --approve --workdir .

# Expected output:
# ✓ Approved. Backlog updated and decision logged

# Verify backlog is updated
cat state/backlog.yaml | grep -A 2 "id: 1000"
# Should show:
#   status: ready_for_dev
#   estimate: {...}
#   implementation_plan_ref: state/plans/item-1000/implementation.plan.json

# Verify plan persisted
cat state/plans/item-1000/implementation.plan.json | jq '.plan_id'
# Should output: "PLAN-..."

# Verify decision log appended
tail -1 state/decisions/decision.log
# Should show: "YYYY-MM-DD... | refine-tech approved | item=1000 | run=... | by=..."
```

---

### Test 3: Failure: Missing Epic

```bash
cd /tmp/test-repo

# Remove epic_id from backlog
cat > state/backlog.yaml << 'EOF'
backlog:
  - id: 1000
    title: Build Test App
    status: candidate
    priority: 1
    size: M
    owner: DevTeam
    story: "As a developer..."
    acceptance_criteria: []
    non_goals: []
    dependencies: []
    risks: []
EOF

# Run refine-tech
governor refine-tech --item 1000 --workdir .

# Expected output:
# ✗ FAIL
# Item requires epic_id for technical refinement

# Verify backlog is unchanged
# Verify summary.md explains error

# Fix: Add epic_id back
cat > state/backlog.yaml << 'EOF'
backlog:
  - id: 1000
    title: Build Test App
    status: candidate
    priority: 1
    size: M
    owner: DevTeam
    story: "As a developer..."
    acceptance_criteria: []
    non_goals: []
    dependencies: []
    risks: []
    epic_id: EP-TEST-001
EOF
```

---

### Test 4: Failure: Epic Not Resolvable

```bash
cd /tmp/test-repo

# Update backlog with unknown epic
cat > state/backlog.yaml << 'EOF'
backlog:
  - id: 1000
    title: Build Test App
    status: candidate
    priority: 1
    size: M
    owner: DevTeam
    story: "As a developer..."
    acceptance_criteria: []
    non_goals: []
    dependencies: []
    risks: []
    epic_id: EP-NONEXISTENT
EOF

# Run refine-tech
governor refine-tech --item 1000 --workdir .

# Expected output:
# ✗ FAIL
# Epic resolution failed: Could not resolve epic_id 'EP-NONEXISTENT'

# Verify backlog is unchanged
# Verify summary.md contains error details
```

---

### Test 5: Idempotency (Rerun Approval)

```bash
cd /tmp/test-repo

# First approval (see Test 2)
governor refine-tech --item 1000 --approve --workdir .
FIRST_PLAN=$(cat state/plans/item-1000/implementation.plan.json | jq '.plan_id')
FIRST_TIMESTAMP=$(cat state/plans/item-1000/implementation.plan.json | jq '.created_at_utc')

sleep 1

# Rerun with same item (should replace)
governor refine-tech --item 1000 --approve --workdir .
SECOND_PLAN=$(cat state/plans/item-1000/implementation.plan.json | jq '.plan_id')
SECOND_TIMESTAMP=$(cat state/plans/item-1000/implementation.plan.json | jq '.created_at_utc')

# Verify:
# - Plan ID is same (deterministic)
# - Timestamp changed (new run)
# - Backlog still has implementation_plan_ref
# - Decision log has 2 entries (one per approval)
```

---

## Integration Test: Full Workflow

```bash
#!/bin/bash
set -e

REPO=/tmp/test-repo-full
rm -rf $REPO
mkdir -p $REPO/state/plans $REPO/state/runs $REPO/apps $REPO/src
cd $REPO

# Setup
cat > state/team-board.md << 'EOF'
# Team
- Alice: Architect
- Bob: Developer
EOF

cat > state/epics.yaml << 'EOF'
epics:
  - id: EP-HELLO
    app_id: hello-world
EOF

cat > state/backlog.yaml << 'EOF'
backlog:
  - id: 100
    title: Build Hello World App
    status: candidate
    priority: 1
    size: S
    owner: Alice
    story: "Build a simple CLI that prints Hello World"
    acceptance_criteria:
      - "App builds with dotnet build"
      - "App runs and prints output"
    epic_id: EP-HELLO
  - id: 200
    title: Add Logging
    status: candidate
    priority: 2
    epic_id: EP-HELLO
EOF

mkdir -p prompts/personas prompts/flows
touch prompts/personas/architect.md prompts/flows/refine-tech.md

echo "=== Step 1: Preview Item 100 ==="
governor refine-tech --item 100 --workdir .
echo "✓ Preview generated"

echo "=== Step 2: Approve Item 100 ==="
governor refine-tech --item 100 --approve --workdir .
echo "✓ Approved"

echo "=== Step 3: Verify Backlog Updated ==="
cat state/backlog.yaml | grep -A 5 "id: 100" | grep -E "(status|implementation_plan_ref)"

echo "=== Step 4: Preview Item 200 ==="
governor refine-tech --item 200 --workdir .
echo "✓ Preview generated"

echo "=== Step 5: Approve Item 200 ==="
governor refine-tech --item 200 --approve --workdir .
echo "✓ Approved"

echo "=== Step 6: Check Decision Log ==="
wc -l < state/decisions/decision.log | xargs echo "Decision log entries:"

echo ""
echo "✅ FULL WORKFLOW SUCCESSFUL"
```

Save as `test-full-workflow.sh` and run:
```bash
chmod +x test-full-workflow.sh
./test-full-workflow.sh
```

---

## Verification Checklist

- [ ] Unit tests pass: `dotnet test`
- [ ] Test 1: Preview mode does not mutate backlog
- [ ] Test 2: Approval mode persists plan and updates backlog
- [ ] Test 3: Missing epic_id fails gracefully
- [ ] Test 4: Unknown epic fails with clear message
- [ ] Test 5: Reapproval is idempotent (plan ID same, timestamps updated)
- [ ] Artifacts format is correct (patch.preview.diff uses `ACTION path`)
- [ ] Summary.md provides clear next steps
- [ ] Decision log only appends on successful approval
- [ ] implementation_plan_ref field populated correctly

---
