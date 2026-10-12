# Technology Validation Spike

**Status:** Not yet executed. This checklist prevents assumptions from being mistaken for verified compatibility.

## Questions to resolve

- [ ] Confirm supported Windows versions and whether .NET 10 LTS is the baseline, including deployment/runtime strategy.
- [ ] Build a minimal WPF prototype on Windows; compare Avalonia only if cross-platform desktop is in scope.
- [ ] Verify Persian fonts, RTL layout, keyboard navigation, scaling, high-DPI behavior and accessibility.
- [ ] Test receipt printer integration with a 58 mm thermal printer, configurable paper widths and print-preview/failure behavior.
- [ ] Test USB barcode scanner input and common keyboard-wedge behavior.
- [ ] Validate EF Core/database provider compatibility, migrations, transaction semantics, SQLite concurrency and PostgreSQL migration path.
- [ ] Verify installer, upgrade, rollback, clean uninstall and data-preservation behavior.
- [ ] Establish Windows CI and a repeatable local build command.
- [ ] Benchmark startup, sales lookup, invoice posting and reports against representative datasets.

## Required evidence

Record OS version, SDK/runtime versions, package versions, hardware model/driver, exact commands, results and unresolved issues. A Linux CI build is useful but does not validate WPF rendering or physical printer behavior.

## Provisional direction

C#/.NET is the leading platform direction. WPF is the Windows-first candidate; SQLite is the single-workstation candidate and PostgreSQL is the central multi-client candidate. Do not treat these as final until the relevant checks are complete.
