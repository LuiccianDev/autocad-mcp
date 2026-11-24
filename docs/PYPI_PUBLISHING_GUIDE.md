# PyPI Publishing & Automation Guide (`luiccian-autocad-mcp`)

This document describes the automated publishing workflow for the Python package **`luiccian-autocad-mcp`** on **[PyPI.org](https://pypi.org)** using **Astral `uv`**, **GitHub Actions Trusted Publishing (OIDC)**, and **PEP 740 Provenance Attestations**.

---

## 1. Zero-Token Security Architecture (OIDC)

Instead of storing static API tokens (`PYPI_API_TOKEN`) in GitHub Secrets, publishing uses **Trusted Publishing**:
- GitHub Actions requests a short-lived OIDC token at runtime (`id-token: write`).
- `actions/attest-build-provenance` generates a PEP 740 provenance attestation linking the wheel and sdist directly to the repository commit and workflow run.

---

## 2. One-Time Prerequisites

### A. Configure PyPI Trusted Publisher
1. Sign in to **[PyPI.org](https://pypi.org)** (with 2FA enabled) and open **[Publishing Settings](https://pypi.org/manage/account/publishing/)**.
2. Under **Add a publisher**, select **GitHub**:
   - **PyPI Project Name:** `luiccian-autocad-mcp`
   - **Owner:** `LuiccianDev`
   - **Repository name:** `autocad-mcp`
   - **Workflow name:** `publish-pypi.yml`
   - **Environment name:** `pypi`

### B. Protected `pypi` Environment in GitHub
In `LuiccianDev/autocad-mcp` **Settings -> Environments -> `pypi`**:
- Require manual reviewer approval (`Required reviewers`) before production uploads execute.
- Restrict deployment policy exclusively to `v*` tags (`custom_branch_policies: true` with tag pattern `v*`).

---

## 3. When Publishing Runs

`.github/workflows/publish-pypi.yml` runs in two modes:
1. **Production Release (`release: [published]`):** Automatically triggered when a GitHub Release for a `v*` tag is published (or chained from `release.yml`).
2. **Manual Dispatch (`workflow_dispatch`):** Allows selecting `testpypi` (for dry-run testing) or `pypi` (blocked unless targeting a `v*` tag policy and approved by a required reviewer).

---

## 4. Release Checklist (Synchronized Versions)

1. Synchronize the target version across all four files:
   - `AutoCAD.Gateway/AutoCAD.Gateway.csproj` (`<Version>`)
   - `AutoCAD.McpServer/AutoCAD.McpServer.csproj` (`<Version>`)
   - `AutoCAD.McpPython/pyproject.toml` (`version = "..."`)
   - `CHANGELOG.md` (`## [X.Y.Z] - YYYY-MM-DD`)
2. Merge the release PR into `master` (protected by `protect-master` ruleset checks).
3. Create and push an annotated `vX.Y.Z` tag (protected by `protect-release-tags`) or publish the GitHub Release, then approve the `pypi` environment deployment when prompted.

---

## 5. Post-Publish Verification

Once the workflow completes:
1. Verify the package page at **[https://pypi.org/project/luiccian-autocad-mcp/](https://pypi.org/project/luiccian-autocad-mcp/)**.
2. Test zero-install execution:
   ```powershell
   uvx luiccian-autocad-mcp
   ```
