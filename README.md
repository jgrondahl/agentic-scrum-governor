# Agentic SCRUM Governor

A deterministic, governance-driven SCRUM delivery engine that enforces Clean Architecture boundaries and explicit approval gates throughout the software delivery lifecycle.

## Overview

The Agentic SCRUM Governor provides a structured CLI for transforming raw ideas into delivered software through defined phases:

1. **Intake** - Capture raw ideas as backlog items
2. **Refine** - Business refinement (acceptance criteria, priorities)
3. **Refine-Tech** - Technical readiness review (estimates, architecture)
4. **Deliver** - Generate, validate, and deploy candidate implementations
5. **Done** - Track completed work with decision audit trail

## Architecture

The system is built on **Clean Architecture** principles:

- **Flows** - Orchestration layer (CLI entry points, preconditions, decision logging)
- **UseCases** - Business logic (deterministic, zero environment coupling)
- **Stores** - Data abstraction (filesystem, process execution, decisions)

**Key Guarantee:** No mutations without explicit approval. All decisions logged immutably.

For detailed architecture overview, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Quick Start

### Installation

```bash
dotnet build
dotnet test
```

### CLI Commands

```bash
# Initialize repository
governor init --workdir /path/to/repo

# Create a backlog item from an idea
governor intake --title "Feature X" --story "As a user, I want..."

# Refine business requirements
governor refine --item 1

# Technical readiness review
governor refine-tech --item 1

# Generate and validate candidate implementation
governor deliver --item 1

# Deploy with approval
governor deliver --item 1 --approve
```

## Phases

### Phase 1: Foundation
Repository structure, CLI scaffolding, backlog YAML, state management.

### Phase 2: Refine & Refine-Tech
Business and technical refinement flows with LLM-driven estimation and architecture review.

### Phase 3: Deliver Engine ✅
Complete delivery mechanism with:
- Precondition validation (fail-fast)
- Deterministic workspace isolation
- Build + run validation
- Approval-gated deployment
- Immutable decision logging

See [docs/phase-3-delivery-engine/](docs/phase-3-delivery-engine/) for complete details.

## File Structure

```
src/GovernorCli/
  Application/
    Models/              # Typed artifacts (no anonymous objects)
    Stores/              # Store interfaces
    UseCases/            # Business logic (deterministic)
  Domain/
    Enums/               # FlowExitCode, ItemStatus, etc.
    Exceptions/          # Typed domain exceptions
  Flows/                 # Orchestration layers (CLI entry points)
  Infrastructure/
    Stores/              # Store implementations (I/O)
  Runs/                  # Run artifact writing
  State/                 # Backlog, decision log loading/saving
  Program.cs             # DI container, command routing

tests/GovernorCli.Tests/
  Application/UseCases/  # UseCase unit tests
  Flows/                 # Flow integration tests

docs/
  ARCHITECTURE.md        # System design and patterns
  phase-3-delivery-engine/
    IMPLEMENTATION.md    # Phase 3 technical deep-dive
    API_CONTRACT.md      # Types, contracts, preconditions
    QUICK_REFERENCE.md   # Usage examples, troubleshooting
    COMPLETION_CHECKLIST.md  # Requirements verification
```

## State Structure

```
state/
  backlog.yaml              # Backlog items (source of truth)
  epics.yaml                # Epic → app_id registry
  runs/                      # Run artifacts (one per execution)
    {runId}_[flow]_item-X/
      *.json                # Typed artifacts
      *.log                 # Command output
      summary.md            # Human-readable result
  workspaces/               # Isolated build environments
    {appId}/
      apps/{appId}/         # Candidate implementation
  decisions/
    decision-log.md         # Immutable approval trail
```

## Key Guarantees

### Preconditions (Fail-Fast)
No mutations occur until all preconditions pass:
- Repo layout valid
- Item exists with required fields
- Status gates enforced (e.g., `ready_for_dev` for delivery)
- Epic registry resolvable

### Validation
Candidates validated with deterministic build + run commands:
- All command output captured to audit files
- Exit codes recorded in validation.json
- Validation must pass before approval

### Approval Gate
- Deployment ONLY if validation passed
- Explicit `--approve` flag required
- Decision logged immutably to state/decisions/decision-log.md
- No silent deployments

### Determinism
- Workspaces reset before each run (eliminates stale state)
- All artifacts typed (no anonymous objects)
- Reproducible execution across runs

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | InvalidRepoLayout |
| 3 | ItemNotFound |
| 4 | BacklogParseError |
| 5 | DefinitionOfReadyGateFailed |
| 8 | ApplyFailed |
| 9 | ValidationFailed |

## Environment Variables

| Variable | Purpose | Default |
|----------|---------|---------|
| `GOVERNOR_APPROVER` | Record who approves decisions | "local" |

## Technologies

- **.NET 8** - Runtime
- **System.CommandLine** - CLI framework
- **Spectre.Console** - Rich terminal output
- **NUnit** - Unit testing
- **Moq** - Test mocking
- **YAML** - State format (simple parser, no external deps)

## Design Patterns

- **Clean Architecture** - Separated concerns (Flow/UseCase/Store)
- **Repository Pattern** - Abstracted data access (IBacklogStore, etc.)
- **Factory Pattern** - LanguageModelProviderFactory for LLM selection
- **Strategy Pattern** - Pluggable refinement strategies
- **Command Pattern** - CLI commands as first-class objects

## Testing

```bash
# Run all tests
dotnet test

# Run specific test fixture
dotnet test --filter DeliverUseCaseTests

# Verbose output
dotnet test --verbosity detailed
```

### Test Coverage
- **DeliverUseCase**: Valid, validation fail, approval, missing item
- **DeliverFlow**: Invalid layout, precondition failures
- **RefineTechUseCase**: Estimation, readiness validation
- **RefineTechFlow**: Status transitions, decision logging

## Documentation

- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** - System design, layer responsibilities, patterns
- **[docs/phase-3-delivery-engine/IMPLEMENTATION.md](docs/phase-3-delivery-engine/IMPLEMENTATION.md)** - Deliver engine architecture
- **[docs/phase-3-delivery-engine/API_CONTRACT.md](docs/phase-3-delivery-engine/API_CONTRACT.md)** - Types, contracts, preconditions
- **[docs/phase-3-delivery-engine/QUICK_REFERENCE.md](docs/phase-3-delivery-engine/QUICK_REFERENCE.md)** - Usage guide
- **[docs/phase-3-delivery-engine/COMPLETION_CHECKLIST.md](docs/phase-3-delivery-engine/COMPLETION_CHECKLIST.md)** - Requirements verification

## Contributing

Follow the existing patterns:
- Typed models in `Application/Models/`
- Store interfaces in `Application/Stores/`
- Implementations in `Infrastructure/Stores/`
- Business logic in `Application/UseCases/`
- Orchestration in `Flows/`
- Tests mirror source structure

## License

[Project License]

## Status

- ✅ Phase 1: Foundation
- ✅ Phase 2: Refine & Refine-Tech
- ✅ Phase 3: Deliver Engine (MVP complete)

**Next:** Phase 4 - Real template generator, enhanced validation

---

**For detailed architecture and Phase 3 implementation, see [docs/](docs/) directory.**
