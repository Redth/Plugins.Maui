# Plugins.Maui

Independent plugins, libraries, and NuGet packages for the .NET MAUI ecosystem.
This is a monorepo, not a single product: projects can have different dependencies,
platform requirements, and release schedules.

## Layout

Each project owns its source, samples, tests, solution, and dependency versions:

```text
ProjectName/
  src/
  samples/
  tests/
  ProjectName.slnx
Platform/
  Linux.SecureStorage/
    src/
    samples/
    tests/
    Linux.SecureStorage.slnx
```

Folder names omit package prefixes such as `Plugin.`, `Plugin.Maui.`, `Platform.`,
and `Platform.Maui.`. Related projects can live under a grouping folder such as
`Platform/`; the group does not need its own solution. For example, the package
`Platform.Maui.Linux.SecureStorage` lives in `Platform/Linux.SecureStorage/`.

Put every source, sample, and test project in that project's `.slnx`. Keep package
versions in a project-local `Directory.Packages.props`, not a repo-wide dependency
catalog. Root build settings provide shared defaults without imposing a target
framework or package version on unrelated projects. Package restore uses nuget.org
explicitly rather than inheriting developers' machine-specific feeds; projects
that need another feed should configure and map it deliberately.

## Projects

| Project | Package | Description |
|---------|---------|-------------|
| [Linux SecureStorage](Platform/Linux.SecureStorage/) | `Platform.Maui.Linux.SecureStorage` | MAUI `ISecureStorage` backed exclusively by Linux Secret Service / libsecret |

## Development

Install a stable .NET 10 SDK. Individual projects document any native prerequisites.
No MAUI workload is needed to build the Linux SecureStorage solution.

```sh
dotnet build Platform/Linux.SecureStorage/Linux.SecureStorage.slnx
dotnet test Platform/Linux.SecureStorage/Linux.SecureStorage.slnx
dotnet pack Platform/Linux.SecureStorage/src/Platform.Maui.Linux.SecureStorage/Platform.Maui.Linux.SecureStorage.csproj -c Release -o artifacts/packages
```
