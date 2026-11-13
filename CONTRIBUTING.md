# Development Workflow

## Before implementation

1. Read the product vision, architecture, roadmap, module boundaries and security requirements.
2. Confirm the current branch and inspect existing changes before editing.
3. Keep changes scoped to one coherent milestone.
4. Record consequential architecture changes in `docs/DECISIONS.md`.

## Code expectations

- Prefer clear names and small, cohesive types.
- Keep domain logic independent from UI and database providers.
- Use decimal/fixed precision for money and explicit currency.
- Use UTC for event instants and explicit business time zones for schedules.
- Never silently mutate posted financial history; use reversal/adjustment workflows.
- Every externally retriable write should have an idempotency strategy.
- Validate inputs at boundaries and return actionable, non-sensitive errors.
- Avoid cross-module table writes and hidden global state.
- Keep UI strings localizable; do not hard-code user-facing text in business logic.
- Do not commit secrets, customer data, local database files, generated installers or build output.

## Tests and validation

- Add tests with behavior changes, especially for financial invariants and failure paths.
- Run formatter/analyzers, unit and integration tests, and a clean build.
- Report exactly what ran, its environment, and any tests not run.
- Never claim a build, installer, printer or UI test passed unless it was actually executed.
- Windows-specific UI, printing and hardware tests must be verified on Windows with the relevant devices.

### Incremental C# formatting

- Pull requests run an incremental whitespace-formatting check for changed `.cs` files with `dotnet format whitespace MG.BusinessPlatform.sln --verify-no-changes`. This gate does not currently reformat the entire solution or replace analyzer/static-analysis checks.
- To run the same changed-file check locally from a feature branch with `origin/main` available:

```powershell
$changedFiles = @(git diff --name-only --diff-filter=ACMR origin/main...HEAD -- '*.cs')
if ($LASTEXITCODE -ne 0) { throw "Could not determine changed C# files." }
if ($changedFiles.Count -gt 0) {
    dotnet format whitespace MG.BusinessPlatform.sln --verify-no-changes --include $changedFiles
    if ($LASTEXITCODE -ne 0) { throw "Changed C# files do not meet whitespace formatting rules." }
}
```

- The initial full-solution baseline reported 306 existing whitespace/line-ending diagnostics across 34 C# files. Keep that legacy debt visible and fix it in focused changes; do not mass-format unrelated files in a feature PR.

## Commits

Use concise imperative commit messages. Prefer small, reviewable commits and update documentation when behavior or assumptions change.
