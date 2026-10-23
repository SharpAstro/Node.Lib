# CLAUDE.md

`Node.Lib` holds the pieces every per-user local node in SharpAstro shares with its clients; `Node.Lib.Hosting` its
Kestrel side (the socket host and `ListenOnNodeNetwork`, the local-only-aware TCP listen). `Node.Lib` also holds
`DetachedProcess` (namespace `Node.Lib.Processes`), which starts a node so it outlives its client, and so needs `AllowUnsafeBlocks` for its Windows
`CreateProcessW` call. See `README.md` for what is in each and who uses them (pilot, tianwen).

## Build & test

```bash
dotnet build
dotnet test src/Node.Lib.Tests
dotnet test src/Node.Lib.Tests --filter "FullyQualifiedName~NodeLibTests.OneNodeHoldsTheLock"
```

Tests are xunit.v3 with Shouldly. Package versions are central (`Directory.Packages.props`).

## Versioning

`VersionMajorMinor` in the repo-root `Directory.Build.props` is the one place `MAJOR.MINOR` is written; CI reads it
back and appends the run number. Minor for an addition, major for a breaking change. **Merging to `main` publishes**
both packages to nuget.org (the `NUGET_API_KEY` org secret).

## Consumers

pilot and tianwen, and any library change is built against both before it is called done
(`.github/docs/framework-consumers.md` in the org's `.github` repo).
