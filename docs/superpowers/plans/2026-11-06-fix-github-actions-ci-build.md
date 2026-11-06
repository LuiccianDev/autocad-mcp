# Plan de Corrección del Workflow de GitHub Actions y Referencias de AutoCAD en CI

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolver el error de compilación en GitHub Actions (`37 Error(s) CS0246: Could not locate assembly acdbmgd / CivilDocument`) haciendo que `AutoCAD.Gateway.csproj` soporte resolución condicional de ensamblados en la nube mediante paquetes NuGet oficiales (`AutoCAD.NET` y `Speckle.Civil3D.API`), y ampliar `.github/workflows/ci.yml` para validar también el servidor Python con `pytest`.

**Architecture:** Configurar referencias condicionales en `AutoCAD.Gateway.csproj` (si `C:\Program Files\Autodesk\AutoCAD 2027\` existe, usar DLLs locales; si no existe, usar paquetes NuGet de referencia con `ExcludeAssets="runtime"`). Actualizar el flujo de CI en `.github/workflows/ci.yml` para compilar ambos proyectos .NET y ejecutar la suite de pruebas de Python con `uv`.

**Tech Stack:** GitHub Actions, .NET 10, MSBuild, NuGet (`AutoCAD.NET`, `Speckle.Civil3D.API`), Python 3.10+, uv, pytest.

**Spec:** Diagnóstico de los logs de GitHub Actions en `c:\Users\DAVID\Downloads\logs_114633607941` que evidenciaron fallo en `5_Build Solution (Release Configuration).txt`.

---

## 1. Diagnóstico del Error en los Logs de GitHub Actions

Analizando el archivo de log `5_Build Solution (Release Configuration).txt`:
```text
C:\Program Files\dotnet\sdk\10.0.403\Microsoft.Common.CurrentVersion.targets(2453,5): warning MSB3245: Could not resolve this reference. Could not locate the assembly "acdbmgd". Check to make sure the assembly exists on disk.
...
error CS0246: The type or namespace name 'ExtensionApplicationAttribute' could not be found
error CS0246: The type or namespace name 'Document' could not be found
error CS0246: The type or namespace name 'CivilDocument' could not be found
5 Warning(s)
37 Error(s)
##[error]Process completed with exit code 1.
```

### Causa Raíz
1. **Rutas Absolutas Hardcodeadas:** `AutoCAD.Gateway.csproj` busca los ensamblados en `C:\Program Files\Autodesk\AutoCAD 2027\...`.
2. **Entorno de GitHub Actions:** Las máquinas virtuales `windows-latest` de GitHub son runners limpios que **no tienen AutoCAD 2027 ni Civil 3D instalados**, por lo que MSBuild no encuentra esas DLLs y falla con 37 errores de tipos no encontrados.
3. **Omisión de Pruebas Python:** El workflow actual tampoco ejecuta los tests del servidor MCP de Python (`AutoCAD.McpPython`), perdiendo validación continua.

---

## Global Constraints

- La compilación local en máquinas con AutoCAD 2027 instalado debe continuar usando las DLLs nativas de AutoCAD 2027 para garantizar máxima fidelidad.
- En GitHub Actions (donde AutoCAD no está instalado), los paquetes NuGet de referencia deben tener `ExcludeAssets="runtime"` para que los binarios generados no copien DLLs protegidas por derechos de autor de Autodesk.
- No alterar la arquitectura de dos capas ni los nombres de los proyectos ya estandarizados (`AutoCAD.Gateway`, `AutoCAD.McpServer`, `AutoCAD.McpPython`).

---

## Review Focus

1. **Resolución en Runner CI:** Confirmar que `AutoCAD.Gateway.csproj` restaure y compile limpiamente cuando la carpeta `C:\Program Files\Autodesk\AutoCAD 2027` no exista.
2. **Compatibilidad Local:** Verificar que `dotnet build` local continúe funcionando con `0 Errores` en modo `Release`.
3. **Flujo de Trabajo CI Multilenguaje:** Asegurar que `.github/workflows/ci.yml` ejecute tanto la compilación .NET como los tests de Python con `uv`.

---

## Task Structure

### Task 1: Configurar Referencias Condicionales en `AutoCAD.Gateway.csproj`

**Files:**
- Modificar: `AutoCAD.Gateway/AutoCAD.Gateway.csproj`

- [ ] **Paso 1.1:** Añadir condición `Exists(...)` en el bloque de referencias locales:
  ```xml
  <ItemGroup Condition="Exists('C:\Program Files\Autodesk\AutoCAD 2027\acdbmgd.dll')">
    <Reference Include="acdbmgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2027\acdbmgd.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="acmgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2027\acmgd.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="accoremgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2027\accoremgd.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="AeccDbMgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2027\C3D\AeccDbMgd.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="AecBaseMgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2027\ACA\AecBaseMgd.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  ```
- [ ] **Paso 1.2:** Añadir bloque alternativo para CI/entornos sin AutoCAD instalado (`Condition="!Exists('C:\Program Files\Autodesk\AutoCAD 2027\acdbmgd.dll')"`):
  ```xml
  <ItemGroup Condition="!Exists('C:\Program Files\Autodesk\AutoCAD 2027\acdbmgd.dll')">
    <PackageReference Include="AutoCAD.NET" Version="26.0.0" ExcludeAssets="runtime" />
    <PackageReference Include="Speckle.Civil3D.API" Version="2027.0.1" ExcludeAssets="runtime" />
  </ItemGroup>
  ```

---

### Task 2: Actualizar el Workflow de GitHub Actions (`ci.yml`)

**Files:**
- Modificar: `.github/workflows/ci.yml`

- [ ] **Paso 2.1:** Configurar jobs o pasos integrados para:
  1. Setup .NET 10 SDK (`actions/setup-dotnet@v4`).
  2. Setup Python y UV (`astral-sh/setup-uv@v5`).
  3. `dotnet restore AutoCAD_Integration.slnx`
  4. `dotnet build AutoCAD_Integration.slnx -c Release --no-restore`
  5. `uv run --directory AutoCAD.McpPython pytest`
- [ ] **Paso 2.2:** Asegurar que los branches monitorizados incluyan `main` y `master`.

---

### Task 3: Verificación Local y Simulación de CI

- [ ] **Paso 3.1:** Compilar la solución localmente con `.\scripts\build.ps1` y confirmar 0 errores.
- [ ] **Paso 3.2:** Ejecutar las pruebas de Python con `uv run --directory AutoCAD.McpPython pytest` y confirmar 9 tests aprobados.

---

### Task 4: Commit de Corrección para GitHub

- [ ] **Paso 4.1:** Ejecutar `git add AutoCAD.Gateway/AutoCAD.Gateway.csproj .github/workflows/ci.yml`.
- [ ] **Paso 4.2:** Realizar commit semántico:
  `fix(ci): add fallback nuget references for autocad api and enable python tests in ci`
- [ ] **Paso 4.3:** Indicar al usuario cómo subir el commit a GitHub (`git push origin master`) para que el GitHub Actions vuelva a correr y pase a color verde.
