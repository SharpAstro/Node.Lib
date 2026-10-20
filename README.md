# Node.Lib

The hosting pieces every per-user local node in SharpAstro shares, lifted out of tianwen and pilot (phase 6 of pilot v2's
design). Two packages:

| Assembly | What | Depends on |
| --- | --- | --- |
| `Node.Lib` | `NodeSocketLocation` (where an application's per-user socket and data folder are; `Resolve`, `TryGetNamed`, the `sun_path` limit, the lock path, the socket-connected HTTP handler, a listening probe) and `NodeLock` (one node per socket, cleared and bound only by its holder) | nothing |
| `Node.Lib.Hosting` | `NodeSocketHosting`: Kestrel on the socket the lock admits it to, owner-only on Unix | ASP.NET Core |

A client takes `Node.Lib` alone, so it never carries ASP.NET.

## Who uses it

- **pilot**: `Pilot.Contracts.NodeSocket` is pilot's names (`pilot2`, `PILOT_NODE_SOCKET`) over a
  `NodeSocketLocation`; the node takes the lock and hosting from here; `NodeHolder` says how a refused node asks the
  holder who it is.
- **tianwen** (to move): `TianWen.Hosting.Contracts.Api.NodeSocket`, `TianWen.Hosting.NodeLock` and
  `TianWen.Hosting.Extensions.NodeSocketHosting` are the originals these were lifted from. Its socket lives under its own
  data root, which `NodeSocketLocation`'s `socketFolder` takes (`() => TianWenDataRoot.Directory.FullName`), and its
  clients ask `TryGetNamed`, which is here too.

## Next

- pilot and tianwen take it through a `UseLocalNodeLib` switch (the org's sibling-source pattern).
- Then the rest of the shared table in pilot v2's design: local-only mode, seeing-is-free / commands-need-control
  (pilot's LAN gate over LAN.Lib's grants), the event broadcaster with a bounded queue per client, jobs, the
  notifications ring.

## Build and test

```bash
dotnet build
dotnet test src/Node.Lib.Tests
```
