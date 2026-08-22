# Publicação NuGet

| Campo | Valor |
| --- | --- |
| **PackageId** | `McpKubernetes` |
| **Tipo** | .NET Global Tool (`DotnetTool`) |
| **Comando CLI** | `mcp-kubernetes` |
| **Target** | `net8.0` |

## Consumir

```powershell
dotnet tool install --global McpKubernetes
```

Versão específica:

```powershell
dotnet tool install --global McpKubernetes --version 0.1.0
```

## Publicar (mantenedor)

```powershell
dotnet pack dotnet/McpKubernetes/McpKubernetes.csproj -c Release -o dotnet/nupkg
dotnet nuget push dotnet/nupkg/McpKubernetes.0.1.0.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json
```

Ou crie a tag `v0.1.0` — o workflow `.github/workflows/publish-nuget.yml` publica via Trusted Publisher (`matneves`).

Na primeira publicação do PackageId, cadastre o repositório em nuget.org → Trusted Publishing (mesmo fluxo do `McpSqlServer`).

## Versionamento

Atualize `<Version>` em `dotnet/McpKubernetes/McpKubernetes.csproj` e crie a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```
