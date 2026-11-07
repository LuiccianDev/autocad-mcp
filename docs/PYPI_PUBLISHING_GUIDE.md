# PyPI Publishing & Automation Guide (`luiccian-autocad-mcp`)

Este documento detalla el procedimiento oficial, seguro y automatizado para publicar y distribuir el paquete Python **`luiccian-autocad-mcp`** en **[PyPI.org](https://pypi.org)** mediante **Astral `uv`**, **GitHub Actions Trusted Publishing (OIDC)** y **Atestaciones de Procedencia Criptográfica (PEP 740)**.

---

## 1. Arquitectura de Seguridad (Zero-Token OIDC)

En lugar de almacenar secretos permanentes o contraseñas en GitHub (`PYPI_API_TOKEN`), utilizamos **Trusted Publishing**:
- GitHub Actions solicita a PyPI un token de autenticación de corta duración generado al vuelo mediante OIDC (`id-token: write`).
- Se genera una atestación digital con `actions/attest-build-provenance` que certifica públicamente en PyPI que el paquete fue generado exactamente por este repositorio y commit.

---

## 2. Configuración Única Inicial (Pre-requisitos)

### A. Configurar el "Pending Publisher" en PyPI (Solo 1 vez)
1. Inicia sesión en **[PyPI.org](https://pypi.org)** (asegúrate de tener 2FA activo en tu cuenta).
2. Ve a: **[https://pypi.org/manage/account/publishing/](https://pypi.org/manage/account/publishing/)**.
3. En la sección **"Add a publisher"**, selecciona **GitHub**:
   - **PyPI Project Name:** `luiccian-autocad-mcp`
   - **Owner:** `LuiccianDev`
   - **Repository name:** `autocad-mcp`  *(el repositorio en GitHub se mantiene tal como está)*
   - **Workflow name:** `publish-pypi.yml`
   - **Environment name:** `pypi`
4. Haz clic en **"Add"**.
5. PyPI dejará el proyecto reservado como **Pending Publisher** esperando la primera subida.

---

### B. Crear el Entorno `pypi` en GitHub (Solo 1 vez)
1. Entra a tu repositorio: `https://github.com/LuiccianDev/autocad-mcp`.
2. Ve a **Settings** $\rightarrow$ **Environments** $\rightarrow$ Haz clic en **New environment**.
3. Escribe el nombre: `pypi` y haz clic en **Configure environment**.
4. *(Opcional)*: Puedes marcar la casilla **"Required reviewers"** si deseas que GitHub te pida confirmación antes de disparar la subida a PyPI.

---

## 3. ¿Cuándo Corre la Acción?

El flujo `.github/workflows/publish-pypi.yml` está programado para ejecutarse en dos situaciones:

1. **Automático (Al Publicar una Release en GitHub):**
   - Siempre que redactes y publiques una **GitHub Release** (o asignes un tag de versión, ej. `v2.3.0`), la acción se dispara sola.
2. **Manual (A través de `workflow_dispatch`):**
   - En cualquier momento puedes ir a la pestaña **Actions** en GitHub, seleccionar **"Publish to PyPI"**, elegir el entorno (`pypi` o `testpypi`) y hacer clic en **"Run workflow"**.

---

## 4. Paso a Paso: ¿Cómo Correrlo para Publicar una Nueva Versión?

### Método Recomendado: Vía GitHub Release
1. En tu código local, verifica que la versión en `AutoCAD.McpPython/pyproject.toml` coincida con la que deseas lanzar:
   ```toml
   [project]
   name = "autocad-mcp"
   version = "2.3.0"
   ```
2. Realiza el commit y sube los cambios:
   ```powershell
   git add AutoCAD.McpPython/pyproject.toml
   git commit -m "chore(release): bump version to 2.3.0"
   git push origin main
   ```
3. En GitHub, ve a **Releases** $\rightarrow$ **Draft a new release**:
   - **Tag version:** `v2.3.0` (crear nuevo tag al publicar).
   - **Release title:** `Release v2.3.0`
   - Haz clic en **Publish release**.
4. **¡Listo!** El workflow `publish-pypi.yml` se activará automáticamente:
   - Compilará la librería con `uv build`.
   - Firmará la atestación de procedencia con `actions/attest-build-provenance`.
   - Publicará el paquete en PyPI mediante OIDC.

---

### Método Alternativo: Disparo Manual desde GitHub Actions
1. Ve a: `https://github.com/LuiccianDev/autocad-mcp/actions/workflows/publish-pypi.yml`.
2. Haz clic en **Run workflow**.
3. Selecciona la rama (`main`) y el entorno (`pypi`).
4. Haz clic en el botón verde **Run workflow**.

---

## 5. Verificación de la Publicación

Una vez que el workflow termine en verde:
1. Tu paquete estará visible en:  
   👉 **[https://pypi.org/project/luiccian-autocad-mcp/](https://pypi.org/project/luiccian-autocad-mcp/)**
2. Cualquier usuario en el mundo podrá ejecutarlo directamente sin instalar nada:
   ```bash
   uvx luiccian-autocad-mcp
   ```
   O instalarlo con pip:
   ```bash
   pip install luiccian-autocad-mcp
   ```
