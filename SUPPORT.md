# Support & Community Help

Welcome to the **AutoCAD MCP** (`autocad-mcp`) support hub! Here you will find official resources for getting assistance, reporting issues, and collaborating with CAD developers and engineers.

---

## 🧭 Where to Start?

Before opening an issue or discussion, check these resources:

1. **[Getting Started Guide](docs/GETTING_STARTED.md):** 5-minute setup guide to build, load the Gateway in AutoCAD, and connect your AI client.
2. **[Troubleshooting Guide](docs/TROUBLESHOOTING.md):** Solutions for common issues (`NETLOAD` assembly blocking, port collisions, auth token mismatches, and modal dialog freezes).
3. **[MCP Tools Reference Manual](docs/TOOLS_REFERENCE.md):** Comprehensive technical specification of all 43 MCP tools.
4. **[System Architecture](docs/ARCHITECTURE.md):** Detailed guide to the decoupled two-tier architecture, UI thread concurrency, and security model.

---

## 💬 Community Channels

### 1. General Questions & Discussions (GitHub Discussions)
For configuration questions or workflow ideas:
- Use **GitHub Discussions**:
  - **Q&A:** Configuration questions and general assistance.
  - **Ideas:** Feature suggestions, workflow concepts, and proposals.
  - **Show and Tell:** Share scripts, drawings, or layouts generated with the project.

### 2. Bug Reports (GitHub Issues)
If you found an issue or unexpected behavior:
- Open a **[Bug Report](.github/ISSUE_TEMPLATE/bug_report.md)**.
- Include your AutoCAD version, .NET SDK version, operating system, and reproduction steps.

### 3. Feature Requests
To propose a new MCP tool or Civil 3D capability:
- Open a **[Feature Request](.github/ISSUE_TEMPLATE/feature_request.md)**.

---

## 🧪 Quick Diagnostic Verification

To verify Gateway connectivity and active token configuration:
```powershell
$headers = @{ "X-Gateway-Token" = "luiccian-autocad-token-2027" }
Invoke-RestMethod -Uri "http://localhost:5055/context/info" -Headers $headers
```
This confirms:
- The Gateway is listening on `http://localhost:5055/`.
- The `X-Gateway-Token` is recognized.
- AutoCAD is responding with active drawing metadata.
