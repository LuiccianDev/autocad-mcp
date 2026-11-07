# Publish Python Package (`autocad-mcp`) to PyPI.org Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure, validate, automate, and document the official release and distribution of the `autocad-mcp` Python package to PyPI.org via modern packaging standards and GitHub Actions Trusted Publishing (OIDC).

**Architecture:** The Python implementation (`AutoCAD.McpPython`) utilizes Hatchling as build-backend and `uv` for project management. The plan standardizes package metadata and license inclusion, validates wheel/sdist artifacts locally with `twine`, creates a secure GitHub Actions publishing workflow utilizing PyPI Trusted Publishers (OIDC without long-lived tokens), and provides an end-to-end verification runbook.

**Tech Stack:** Python 3.10+, Hatchling, uv, twine, GitHub Actions (`pypa/gh-action-pypi-publish`), PyPI OIDC Trusted Publishing.

**Spec:** Requirement to publish the existing `AutoCAD.McpPython` package (`autocad-mcp` v2.3.0) to `https://pypi.org/project/autocad-mcp/` so AI clients can consume it zero-config via `uvx autocad-mcp`.

## Global Constraints

- **Repository Language:** All code, configuration, comments, and commit messages MUST be in Technical English (per `AGENTS.md`).
- **Package Naming:** PyPI package name is strictly `autocad-mcp` (kebab-case) with import module `autocad_mcp` (snake_case).
- **Tooling:** Build and dependency management must leverage `uv` and `hatchling`.
- **Security:** Publishing must prioritize PyPI Trusted Publishing (OIDC) with `id-token: write` rather than hardcoding static API secrets.
- **No Console Pollution:** STDIO transport integrity must remain pristine (`autocad_mcp` writes only to stderr).

## Review Focus

1. **Wheel / Sdist missing LICENSE or README:** Verify that the built `.tar.gz` and `.whl` contain `LICENSE` and `README.md` at root.
2. **Twine Metadata Validation Failure:** Verify `twine check dist/*` returns `PASSED` with zero warnings or malformed description fields.
3. **CLI Entrypoint Resolution:** Verify `autocad-mcp` console script executes properly when installed from built wheel without module path errors.
4. **PyPI Package Name Collision:** Verify availability or ownership registration of `autocad-mcp` on PyPI before triggering production deployment.
5. **OIDC Token Permission Scope:** Ensure GitHub Actions workflow configures `permissions: id-token: write` and `contents: read` with target environment `pypi`.

---

### Task 1: Package Metadata & License Synchronization

**Files:**
- Create: `AutoCAD.McpPython/LICENSE`
- Modify: `AutoCAD.McpPython/pyproject.toml`
- Test: `AutoCAD.McpPython/tests/test_package_metadata.py`

**Interfaces:**
- Consumes: Root `LICENSE` (Apache 2.0).
- Produces: Normalized `pyproject.toml` with explicit `license-files`, complete classifiers, URLs, and dev dependencies (`twine`, `build`).

- [ ] **Step 1: Write the failing metadata unit test**

Create `AutoCAD.McpPython/tests/test_package_metadata.py`:
```python
from pathlib import Path
import tomllib
import autocad_mcp


def test_package_metadata_consistency():
    pkg_root = Path(__file__).parent.parent
    pyproject_path = pkg_root / "pyproject.toml"
    license_path = pkg_root / "LICENSE"

    assert pyproject_path.exists(), "pyproject.toml must exist"
    assert license_path.exists(), "LICENSE file must exist in package root for wheel inclusion"

    with open(pyproject_path, "rb") as f:
        data = tomllib.load(f)

    project = data["project"]
    assert project["name"] == "autocad-mcp"
    assert project["version"] == autocad_mcp.__version__
    assert project["readme"] == "README.md"
    assert "autocad-mcp" in project["scripts"]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run --directory AutoCAD.McpPython pytest tests/test_package_metadata.py -v`
Expected: FAIL due to missing `AutoCAD.McpPython/LICENSE`.

- [ ] **Step 3: Copy LICENSE and update `pyproject.toml`**

1. Copy root `LICENSE` to `AutoCAD.McpPython/LICENSE`.
2. Update `AutoCAD.McpPython/pyproject.toml` to add `license-files = ["LICENSE"]` under `[project]` or `[build-system]` and add `twine>=5.0.0` to `[project.optional-dependencies] dev`.

- [ ] **Step 4: Run test to verify it passes**

Run: `uv run --directory AutoCAD.McpPython pytest tests/test_package_metadata.py -v`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AutoCAD.McpPython/LICENSE AutoCAD.McpPython/pyproject.toml AutoCAD.McpPython/tests/test_package_metadata.py
git commit -m "chore(python): synchronize license file and enrich packaging metadata"
```

---

### Task 2: Build Artifacts Generation & Twine Validation

**Files:**
- Create: `scripts/build-python-dist.ps1`
- Test: `AutoCAD.McpPython/tests/test_build_artifacts.py`

**Interfaces:**
- Consumes: `AutoCAD.McpPython/pyproject.toml` and source tree `src/autocad_mcp/`.
- Produces: `AutoCAD.McpPython/dist/*.whl` and `AutoCAD.McpPython/dist/*.tar.gz` validated against PyPI specifications.

- [ ] **Step 1: Write integration test for build artifacts validation**

Create `AutoCAD.McpPython/tests/test_build_artifacts.py`:
```python
import subprocess
from pathlib import Path
import zipfile
import tarfile


def test_build_artifacts_and_twine():
    pkg_root = Path(__file__).parent.parent
    dist_dir = pkg_root / "dist"

    # Build using uv
    build_res = subprocess.run(["uv", "build", "--directory", str(pkg_root)], capture_output=True, text=True)
    assert build_res.returncode == 0, f"uv build failed: {build_res.stderr}"

    wheels = list(dist_dir.glob("*.whl"))
    sdists = list(dist_dir.glob("*.tar.gz"))
    assert len(wheels) >= 1, "At least one wheel artifact must be produced"
    assert len(sdists) >= 1, "At least one sdist artifact must be produced"

    # Check wheel content
    with zipfile.ZipFile(wheels[0], "r") as z:
        names = z.namelist()
        assert any(n.endswith("LICENSE") for n in names), "Wheel must contain LICENSE"
        assert any("autocad_mcp/server.py" in n for n in names), "Wheel must contain server module"

    # Twine check
    twine_res = subprocess.run(
        ["uv", "run", "--directory", str(pkg_root), "twine", "check", f"{dist_dir}/*"],
        capture_output=True,
        text=True,
    )
    assert twine_res.returncode == 0, f"Twine validation failed: {twine_res.stdout}\n{twine_res.stderr}"
    assert "PASSED" in twine_res.stdout
```

- [ ] **Step 2: Run test to verify execution**

Run: `uv run --directory AutoCAD.McpPython pytest tests/test_build_artifacts.py -v`
Expected: PASS with verified `.whl` and `.tar.gz` archives.

- [ ] **Step 3: Create helper script `scripts/build-python-dist.ps1`**

Write standard utility script in `scripts/build-python-dist.ps1`:
```powershell
<#
.SYNOPSIS
    Builds and validates python distribution packages (sdist and wheel) for PyPI.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ProjectDir = Join-Path $PSScriptRoot "..\AutoCAD.McpPython"
$DistDir = Join-Path $ProjectDir "dist"

Write-Host "==> Cleaning old distribution artifacts..." -ForegroundColor Cyan
if (Test-Path $DistDir) {
    Remove-Item -Path $DistDir -Recurse -Force
}

Write-Host "==> Building package with uv..." -ForegroundColor Cyan
uv build --directory $ProjectDir

Write-Host "==> Running twine check on generated artifacts..." -ForegroundColor Cyan
uv run --directory $ProjectDir twine check "$DistDir/*"

Write-Host "==> Package build and validation completed successfully!" -ForegroundColor Green
```

- [ ] **Step 4: Verify manual execution of build script**

Run: `pwsh -File ./scripts/build-python-dist.ps1`
Expected: Returncode 0, outputs `.whl` and `.tar.gz` with "PASSED" twine validation.

- [ ] **Step 5: Commit**

```bash
git add scripts/build-python-dist.ps1 AutoCAD.McpPython/tests/test_build_artifacts.py
git commit -m "ci(python): add automated build and twine verification script"
```

---

### Task 3: Dedicated PyPI Trusted Publishing GitHub Workflow

**Files:**
- Create: `.github/workflows/publish-pypi.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: GitHub release tag `v*` or manual workflow dispatch.
- Produces: Published package on PyPI (`https://pypi.org/project/autocad-mcp/`) using GitHub OIDC.

- [ ] **Step 1: Create `.github/workflows/publish-pypi.yml`**

Create `.github/workflows/publish-pypi.yml`:
```yaml
name: Publish to PyPI

on:
  release:
    types: [published]
  workflow_dispatch:
    inputs:
      target_env:
        description: 'Target repository'
        required: true
        default: 'pypi'
        type: choice
        options:
          - pypi
          - testpypi

jobs:
  pypi-publish:
    name: Build & Upload to PyPI
    runs-on: ubuntu-latest
    permissions:
      id-token: write  # Mandatory for Trusted Publishing (OIDC)
      contents: read

    environment:
      name: ${{ github.event.inputs.target_env || 'pypi' }}
      url: ${{ github.event.inputs.target_env == 'testpypi' && 'https://test.pypi.org/p/autocad-mcp' || 'https://pypi.org/p/autocad-mcp' }}

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Install uv
        uses: astral-sh/setup-uv@v5

      - name: Build sdist and wheel
        run: uv build --directory AutoCAD.McpPython

      - name: Verify package distribution
        run: uv run --directory AutoCAD.McpPython twine check AutoCAD.McpPython/dist/*

      - name: Publish to PyPI / TestPyPI via OIDC
        uses: pypa/gh-action-pypi-publish@release/v1
        with:
          repository-url: ${{ (github.event.inputs.target_env == 'testpypi') && 'https://test.pypi.org/legacy/' || '' }}
          packages-dir: AutoCAD.McpPython/dist/
          skip-existing: true
```

- [ ] **Step 2: Align `.github/workflows/release.yml`**

Update `.github/workflows/release.yml` to remove duplicate local distribution build or keep it strictly for GitHub Release asset attachment, referencing the specialized publish workflow.

- [ ] **Step 3: Validate GitHub workflow YAML syntax**

Verify file schema and structure locally.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/publish-pypi.yml .github/workflows/release.yml
git commit -m "ci: implement GitHub Actions Trusted Publisher workflow for PyPI"
```

---

### Task 4: PyPI Account Configuration & Publisher Setup (Maintainer Runbook)

**Files:**
- Create: `docs/PYPI_PUBLISHING_GUIDE.md`

**Interfaces:**
- Consumes: PyPI.org Trusted Publishing specifications and repository settings (`LuiccianDev/autocad-mcp`).
- Produces: Comprehensive step-by-step documentation for one-time PyPI account setup, OIDC trust configuration, and manual fallback tokens.

- [ ] **Step 1: Write `docs/PYPI_PUBLISHING_GUIDE.md`**

Document the exact instructions:
1. **PyPI Account & 2FA Setup:**
   - Sign up / Log in to [pypi.org](https://pypi.org).
   - Enable 2FA in Account Settings (TOTP / Security Key).
2. **Setting up Trusted Publisher (OIDC - Zero Token Security):**
   - Go to [PyPI Publishing Management](https://pypi.org/manage/account/publishing/).
   - Add a "Pending Publisher" before the first release:
     - **PyPI Project Name:** `autocad-mcp`
     - **Owner:** `LuiccianDev`
     - **Repository name:** `autocad-mcp`
     - **Workflow name:** `publish-pypi.yml`
     - **Environment name:** `pypi`
3. **Setting up GitHub Environment:**
   - In GitHub repository: `Settings` > `Environments` > `New environment` > name: `pypi`.
4. **Triggering Deployment:**
   - Option A: Create a GitHub Release with tag `v2.3.0`.
   - Option B: Execute manual trigger via GitHub Actions tab (`workflow_dispatch`).
5. **Verifying Installation:**
   - Verification command: `uvx autocad-mcp` or `pip install autocad-mcp`.

- [ ] **Step 2: Commit**

```bash
git add docs/PYPI_PUBLISHING_GUIDE.md
git commit -m "docs: add comprehensive PyPI publishing and trusted publisher guide"
```

---

### Task 5: Documentation & Badges Update

**Files:**
- Modify: `README.md`
- Modify: `AutoCAD.McpPython/README.md`

**Interfaces:**
- Consumes: Published PyPI package endpoint (`https://pypi.org/project/autocad-mcp/`).
- Produces: Live PyPI version badge, updated installation instructions via `pip` and `uvx`.

- [ ] **Step 1: Add PyPI badge and installation notes to `README.md`**

Add badge:
```markdown
[![PyPI version](https://img.shields.io/pypi/v/autocad-mcp.svg?logo=pypi&logoColor=FFE873)](https://pypi.org/project/autocad-mcp/)
[![Python versions](https://img.shields.io/pypi/pyversions/autocad-mcp.svg?logo=python&logoColor=white)](https://pypi.org/project/autocad-mcp/)
```
Include explicit installation command:
```bash
pip install autocad-mcp
# or zero-install with uvx:
uvx autocad-mcp
```

- [ ] **Step 2: Update `AutoCAD.McpPython/README.md`**

Ensure `AutoCAD.McpPython/README.md` mentions both local execution and official PyPI distribution channels.

- [ ] **Step 3: Commit**

```bash
git add README.md AutoCAD.McpPython/README.md
git commit -m "docs: add PyPI version badges and pip/uvx distribution instructions"
```

---

## Self-Review

1. **Spec coverage:** The plan covers metadata preparation, packaging validation with twine, GitHub Actions OIDC Trusted Publishing, step-by-step account configuration guide, and end-user verification via `uvx`/`pip`.
2. **Step scan:** Each task defines explicit files, test assertions, build commands, and atomic commits.
3. **Type consistency:** The package name is consistently `autocad-mcp` across `pyproject.toml`, GitHub workflows, documentation, and PyPI URLs.
4. **Review Focus:** Covered: missing license in wheel, twine metadata errors, entrypoint validation, PyPI name reservation, and OIDC token permissions.
5. **Proportion:** Concise, actionable step-by-step implementation guide adhering to `writing-plans` guidelines.
