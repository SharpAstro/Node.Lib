# Node.Lib

The hosting pieces every per-user local node in SharpAstro shares, lifted out of tianwen and pilot (phase 6 of pilot v2's
design). Two packages:

| Assembly | What | Depends on |
| --- | --- | --- |
| `Node.Lib` | `NodeSocketLocation` (where an application's per-user socket and data folder are; `Resolve`, `TryGetNamed`, the `sun_path` limit, the lock path, the socket-connected HTTP handler, a listening probe) and `NodeLock` (one node per socket, cleared and bound only by its holder); `CircularBuffer<T>` (a lock-free ring of the most recent items, with how many were ever added, so a client asks for only what it lacks: a notification feed, a job's last lines); `LocalOnlyMode` (a process binds nothing but loopback and its socket: an app's environment variable or `--local-only`, decided once); `WebSocketEventQueue` (one event-stream client's bounded queue and its one sender: a write never waits, order holds, a stalled send ends and the client is dropped); `DetachedProcess` (in `Node.Lib.Processes`; starts a node so it outlives its client: on Windows it breaks away from the client's job, has no window and its own process group, and says whether it could, `OutlivesClient`) | nothing |
| `Node.Lib.Hosting` | `NodeSocketHosting`: Kestrel on the socket the lock admits it to, owner-only on Unix, and `ListenOnNodeNetwork`, a TCP listen on every address that binds only when the process's `LocalOnlyMode` allows it and says whether it did; `NodeAccessGate`: seeing is free, commands need control (a node's `NodeAccessPolicy` says which route is open, a command or this machine's, and checks the grant), the socket passes untouched | ASP.NET Core |

A client takes `Node.Lib` alone, so it never carries ASP.NET.

## Who uses it

- **pilot**: `Pilot.Contracts.NodeSocket` is pilot's names (`pilot2`, `PILOT_NODE_SOCKET`) over a
  `NodeSocketLocation`; the node takes the lock and hosting from here; `NodeHolder` says how a refused node asks the
  holder who it is; its client starts the node with `DetachedProcess`, and `--lan` listens through `ListenOnNodeNetwork`.
- **tianwen**: `TianWen.Hosting.Contracts.Api.NodeSocket` is its names over a `NodeSocketLocation` under its own data
  root (`() => TianWenDataRoot.Directory.FullName`); its clients ask `TryGetNamed`; the node takes the lock and hosting
  from here.
- Both nodes' `EventHub`s put each client's events on a `WebSocketEventQueue` and keep their own policy: pilot resumes a
  dropped client from the last delta it applied, tianwen's resyncs by polling and keeps its two pools and presence.

## Next

Jobs stay each node's own: tianwen's are in-process work holding a device or a kind (a second start joins or is
refused), pilot's are processes queued by count and free memory. They share the model (a state, a step, an error, start
and end), not code. (The grants'
file store is LAN.Lib's `LanGrantFile`.)

## Build and test

```bash
dotnet build
dotnet test src/Node.Lib.Tests
```
