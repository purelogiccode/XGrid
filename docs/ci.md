# Continuous integration

The CI and quality-gate documentation now lives in
[Development → Quality gates](development/quality-gates.md).

That page covers:

- the local build/test/format gates;
- the GitHub Actions workflows;
- the native core build;
- the packaging gate.

The short version:

```powershell
dotnet build XGrid.sln -c Release --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```
