"""Pruebas unitarias para metadatos del paquete y consistencia con pyproject.toml."""

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
