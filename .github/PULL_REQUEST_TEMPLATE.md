## Summary
Concise description of what this PR accomplishes and why it is needed.

Closes # (issue number, if applicable)

---

## Type of Change
- [ ] `fix`: Bug fix
- [ ] `feat`: New feature or MCP tool
- [ ] `docs`: Documentation updates or additions
- [ ] `refactor`: Code refactoring without behavior changes
- [ ] `perf`: Performance improvement in Gateway or serialization
- [ ] `ci`: GitHub Actions or automation script changes

---

## Checklist
- [ ] Code builds cleanly in Release configuration: `dotnet build AutoCAD_Integration.slnx -c Release` (0 errors).
- [ ] Follows Single Responsibility Principle (SRP) and `#nullable enable` context.
- [ ] Public classes and methods are documented with XML comments (`/// <summary>`).
- [ ] **Strict `stdout` silence in MCP:** No `Console.WriteLine` calls in `AutoCAD.McpServer` (to prevent JSON-RPC framing corruption). Diagnostic logging uses `Console.Error` or `ILogger`.
- [ ] Native AutoCAD plugin uses non-modal command prefixes (`_.-`) to prevent blocking UI dialogs.
- [ ] Metric units are preserved (`INSUNITS = 4`).

---

## Testing Done
Describe how changes were tested (AutoCAD 2027 environment, MCP client, automated tests).
