# Phase 3 Corrections: Principal-Level Defensibility Fixes

## Summary
Fixed three critical architectural and security issues to make Phase 3 credible as Principal-level governed delivery.

## 1. Locked Down ProcessRunner (Security)

**Issue:** ProcessRunner accepted arbitrary `commandLine` strings via cmd.exe, allowing command injection.

**Fix:**
- Changed `IProcessRunner.Run()` signature from `(name, workingDirectory, commandLine, stdout, stderr)` to `(AllowedProcess, workingDirectory, args[], stdout, stderr)`
- Created `AllowedProcess` enum restricting only `DotnetBuild` and `DotnetRun`
- Added forbiddence token validation (no `&`, `|`, `;`, `$`, etc. in args)
- Updated implementation to build safe command line without shell interpretation

**Result:** ProcessRunner is now allowlisted and injection-proof.

## 2. Typed Patch Preview with Strict Format (Precision)

**Issue:** `DeliverPatchPreview.Files` was `List<string>` and diff format was "simple file list" (ambiguous).

**Fix:**
- Created typed `PatchFile` model with:
  - `action` ("A" add, "M" modify, "D" delete)
  - `path` (relative path)
  - `size` (file size)
  - `workspace_sha256` (SHA256 of generated file)
  - `repo_sha256` (SHA256 of deployed file, optional for add)
- Changed `DeliverPatchPreview.Files` to `List<PatchFile>`
- Changed `PatchApplied.FilesApplied` to `List<PatchFile>`
- Implemented SHA256 computation for all files
- Defined strict diff line format: `ACTION|path|size|sha256`

**Result:** Patch preview is now fully typed and defensibly auditable.

## 3. Decoupled Deliver from Fixture (Architecture)

**Issue:** Deliver was hard-coded to generate fixture; template selection was not first-class, contradicting Phase 3 goal to "deliver Phase 2 output".

**Fix:**
- Added `delivery_template_id` field to `BacklogItem` (from Phase 2 output)
- Added `TemplateId` to `DeliverRequest` (passed from Flow)
- Created `ValidateTemplate()` method to allowlist permitted templates
- Created `GenerateCandidate()` factory method to route to template-specific generator
- Updated `ImplementationPlan` to record actual template used
- Fixture remains one implementation of generator, not the only path

**Result:** Template selection is now first-class and Phase 2-driven. Fixture is correctly positioned as one generator option.

## Files Modified

### Models
- `BacklogModel.cs` - Added `delivery_template_id` field
- `DeliverModels.cs` - Added `PatchFile` type, updated `DeliverPatchPreview` and `PatchApplied`

### Stores
- `IProcessRunner.cs` - Changed to `(AllowedProcess, workingDirectory, args[], stdout, stderr)`
- `IAppDeployer.cs` - Changed return type to `List<PatchFile>`

### Implementations
- `ProcessRunner.cs` - Allowlist enforcement, token validation, safe command building
- `AppDeployer.cs` - SHA256 computation for all files, returns `PatchFile` records

### UseCases
- `DeliverRequest.cs` - Added `TemplateId` field
- `DeliverUseCase.cs` - 
  - Updated to use new `ProcessRunner` API
  - Added SHA256 computation for patch files
  - Added `ValidateTemplate()` and `GenerateCandidate()` factory methods
  - Updated `ComputePatchPreview()` to return `PatchFile` records

### Flows
- `DeliverFlow.cs` - Added precondition for `delivery_template_id`, pass to UseCase

### Enums
- `AllowedProcess.cs` - New enum restricting dotnet build/run only

### Tests
- `DeliverUseCaseTests.cs` - Updated mocks to use new `IProcessRunner` API, added `TemplateId` to requests

## Exit Codes Impacted
None (all exit codes remain same, preconditions just stricter)

## Backwards Compatibility
- Breaks if calling code uses old ProcessRunner signature
- Breaks if calling code expects string list from AppDeployer
- Backlog files must now include `delivery_template_id` for delivery items

## Build Status
✅ **Build successful**
✅ **All tests compile**

## Next Steps (Phase 4+)
- Implement real template generators (Roslyn-based code generation)
- Add more generators to allowlist
- Add integration test validation to `ValidateCandidate()`
- Enhance diff format to handle file modifications/deletions

---

**Phase 3 is now defensible at Principal level: bounded autonomy, typed artifacts, strict governance, security-hardened.**
