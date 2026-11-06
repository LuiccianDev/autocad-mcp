# Security Policy for AutoCAD MCP (`autocad-mcp`)

Workstation and CAD environment security and integrity are paramount. Because **AutoCAD AI Gateway** interacts directly with the memory and process space of `acad.exe`, and **AutoCAD.McpServer** / **AutoCAD.McpPython** communicate over HTTP and STDIO, strict security guidelines apply to protect user machines.

---

## Supported Versions

Active security updates and bug fixes are provided for the following versions:

| Component | Version | Support Status |
|---|---|---|
| `AutoCAD.Gateway` | v2.4.x | :white_check_mark: Supported |
| `AutoCAD.McpServer` | v2.4.x | :white_check_mark: Supported |
| `AutoCAD.McpPython` | v2.4.x | :white_check_mark: Supported |
| .NET SDK | .NET 10 | :white_check_mark: Supported |
| Autodesk Platform | AutoCAD / Civil 3D 2027 | :white_check_mark: Supported |

---

## Security Model & Network Guidelines

1. **Local Network Binding (`localhost`):**
   - The HTTP listener within `AutoCAD.Gateway` binds by default strictly to `http://localhost:5055/`.
   - **WARNING:** Never expose port `5055` to public network interfaces or bind to `0.0.0.0` without an encrypted tunnel (TLS/mTLS) and perimeter authorization. Any client with network access to this port could inject commands directly into your active AutoCAD session.

2. **Token-Based Authentication (`X-Gateway-Token`):**
   - All HTTP requests to the Gateway require the header:
     ```http
     X-Gateway-Token: luiccian-autocad-token-2027
     ```
   - **Dynamic Credential Evaluation:** The security token is evaluated dynamically at runtime.
   - **Resolution Precedence:**
     1. Live token set in the AutoCAD session (`AI_SET_TOKEN` or `AI_GENERATE_TOKEN`).
     2. Windows environment variable `AUTOCAD_GATEWAY_TOKEN`.
     3. The `"env"` configuration block in the AI client (`claude_desktop_config.json` or `mcp_config.json`).
     4. Default development fallback (`luiccian-autocad-token-2027`).
   - **Native Management Commands in AutoCAD:**
     - `AI_SHOW_CONFIG`: Prints current server status and masked token in AutoCAD console.
     - `AI_SET_TOKEN`: Updates the active token in memory without restarting AutoCAD.
     - `AI_GENERATE_TOKEN`: Generates a cryptographically random token (`luiccian-xxxxxxxx...`) and applies it immediately in memory and Windows user environment variables.
   - **Synchronization Utility:**
     Run the PowerShell synchronization script:
     ```powershell
     .\scripts\set-gateway-token.ps1 -UpdateExampleConfigs
     ```

3. **Command Injection Defenses:**
   - The Gateway prepends non-modal prefixes `_.-` and cancellation sequences `\x03\x03` to prevent UI thread lockouts.
   - AI models interact primarily through typed MCP tools with schema validation prior to sending instructions to the Gateway.

---

## Reporting a Vulnerability

If you discover a security vulnerability in this project:

1. **DO NOT file a public issue on GitHub.**
2. Submit a report through **GitHub Private Vulnerability Reporting** (*Security* $\rightarrow$ *Advisories* $\rightarrow$ *New draft advisory*).
3. Include:
   - Clear description of the vulnerability.
   - Step-by-step reproduction steps or proof-of-concept (PoC).
   - Potential impact on the workstation or DWG files.
4. Maintainers will acknowledge your report within 48 business hours and coordinate a fix prior to any public disclosure.
