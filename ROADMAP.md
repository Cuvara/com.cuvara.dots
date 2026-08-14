# Roadmap

Planned scope for `com.cuvara.dots`. Each entry states what it contains and why it belongs
here; the out-of-scope section states what does not and why. This file is a plan, not a
promise of dates.

## Scope

The package provides **hybrid** building blocks: simulation runs in ECS, visuals are
GameObject/MonoBehaviour. It does not render entities. Consumers wire it up through
**VContainer** DI, in the same style as the rest of the stack.

Two rules constrain everything below.

- **Standalone install.** The package must resolve and compile against its four pinned
  dependencies alone — `com.unity.entities`, `com.unity.burst`, `com.unity.collections`,
  `com.unity.mathematics`. Anything needing more lives in a separate assembly, gated by
  `versionDefines` and `defineConstraints`, and is absent rather than broken when its
  dependency is.
- **Dependency direction.** `com.cuvara.dots` may depend on `com.cuvara.netcode`. The
  reverse is forbidden, in every release. Netcode's `IEntityView` stays three methods.

## v0.2.0 — hybrid core

Asset loading first, then the entity/view link, then the data that configures both.

| Feature | Status | Contents |
|---|---|---|
| ViewProvisioning | **in progress** | Chunk-aware async acquisition of view prefabs over GameFoundation's `IAssetsManager` and `IObjectPoolManager`, refcounted, with prewarm and release. |
| EntityViewLink + transform sync | **in progress** | Entity↔GameObject link component and side table, spawn/despawn lifecycle, `LocalTransform` → `Transform` sync each frame. |
| ViewConfig + data setup | planned | ScriptableObject authoring (asset key, pool size, scale, offsets) converted to `IComponentData`, with a blob table for many-per-archetype and named archetype definitions. Runtime authoring, not subscene baking — consumers spawn from server snapshots at runtime. |
| Simulation components and systems | planned | `Lifetime`, `Health`, `MoveToward`, `SpinSpeed`, `MoveData` with their `ISystem` counterparts. Decoupled from the demo singletons and from a hardcoded command-buffer system before they land here. |

No new loader, cache or pool is written. GameFoundation already owns both, the consuming
project already registers them, and a second pool would contend with the first over the
same prefabs.

## v0.3.0 — optional shared simulation, and the netcode adapter

The server's simulation library, `Shared.GameLogic`, is *optional*. The package must be
fully usable without it.

- **The package owns the abstraction.** `ISimulationModel` plus the value types it speaks
  (`SimEntity`, `SimBounds`, `SimConstants`, `float2`) live in `Cuvara.DOTS.Runtime` and
  always compile. `Shared.GameLogic` is one implementation of that interface, never the
  interface itself, so consumer code never names one of its types.
- **Present.** `Cuvara.DOTS.GameLogic` delegates to `MovementSystem.TryMove` and
  `CombatLogic`, converting `Vec2` ↔ `float2` and reading `GameConstants` into
  `SimConstants` at construction. Constants are read, never copied as literals. Entity ids
  stay `FixedString64Bytes` on the ECS side — `EntityState.Id`/`Type` are `string` and
  cannot cross into Burst.
- **Absent.** `PassiveSimulationModel` applies authoritative state as received and reports
  `IsAuthoritative => false`. Consumer code compiles and runs unchanged; views spawn, sync
  and despawn identically. What is lost is exactly: client-side prediction and its rewind
  anchor, bit-exact agreement with the server integrator, and shared combat constants. The
  package deliberately does not reimplement the movement rule — a second copy of it is the
  divergence the shared-logic boundary exists to prevent, and a prediction that is silently
  one ULP wrong is worse than no prediction.
- **The switch.** `Cuvara.DOTS.GameLogic.asmdef` carries
  `versionDefines: com.rpgmmo.shared-gamelogic → CUVARA_SHARED_GAMELOGIC` and
  `defineConstraints: [CUVARA_SHARED_GAMELOGIC]`; the assembly simply is not built when the
  dependency is absent. All conversion lives on this side, because `Shared.GameLogic` is
  `noEngineReferences: true` and can never learn about `float2`. **One `#if` exists in the
  whole package**, in the DI registration file, choosing which implementation to register.
  Consumers call `builder.RegisterCuvaraDots(...)` and their `LifetimeScope` is identical
  either way.
- **Guard.** An edit-mode test, itself constrained to `CUVARA_SHARED_GAMELOGIC`, asserts
  every `SimConstants` field equals its `GameConstants` source and drives the integrator
  through the seam against the shared golden vectors.

Also in this release: the netcode `IEntityView` adapter (a separate assembly, gated on
`com.cuvara.netcode`, arrow pointing one way only), and an ECS → MonoBehaviour event queue
for one-shot request entities, held until a second consumer exists to shape its API.

## v0.4.0 — 2D

Pure ECS cannot render a sprite and cannot simulate a 2D collider: `com.unity.entities.graphics`
is mesh-only and `com.unity.physics` is 3D. Hybrid is therefore *more* necessary in 2D than in
3D, not less. Everything here sits on top of v0.2.0 and changes none of its design.

| Feature | Verdict | Notes |
|---|---|---|
| Tile data in ECS | in the package | Chunked grid in a blob asset, plus queries (lookup, neighbourhood, line-of-sight). This is what pathfinding, AOI and tile-vs-entity tests need — none of which want a `Tilemap` component call per query. Rendering stays on `Tilemap`/`TilemapRenderer` GameObjects. Cost: an editor-side bake from an authored `Tilemap` into the blob, and a chunk size chosen for the consumer's access pattern rather than copied from Unity's internal one. |
| Sprite view pooling | already covered | Not a new feature. A view prefab whose root carries a `SpriteRenderer` flows through ViewProvisioning unchanged; ViewConfig gains sorting-layer and order fields, and a 2D sample demonstrates it. |
| Sorting / draw order | in the package | Sorting layers exist only on the GameObject side, so an ECS sort key (explicit, or derived from world Y) is computed in a system and drained to `SpriteRenderer.sortingOrder` in the same main-thread pass as transform sync. Cheap, and it is the one piece a 2D consumer would otherwise hand-write per project. |
| 2D collision | **not in this package** | There is no DOTS 2D physics. The honest options are 3D `Unity.Physics` constrained to a plane, or `Physics2D` on the GameObject side; both are project decisions, not package ones. The package ships only grid queries over the tile blob, which is a broadphase over static tiles and not a physics engine. It will not pretend otherwise. |

The tilemap and physics2d modules are built-in, so no extra package dependency is introduced
by the parts that are in scope.

## Out of scope

- **Entity rendering wrappers** over Entities.Graphics — `RenderMeshUtility`, `RenderMeshArray`,
  `MaterialMeshInfo`. Visuals are GameObjects; the package never creates a rendered entity.
- **A new asset loader, asset cache, or GameObject pool.** GameFoundation owns these.
- **Wrappers over `SystemAPI` singleton access.** Unity's API is already the abstraction; a
  wrapper adds version coupling and no capability.
- **Scene bootstrap** — cameras, lights, ground planes. Demo scaffolding, and it belongs in a
  sample.
- **Snapshot merge, interpolation, transport, codec, entity-handle interning.**
  `com.cuvara.netcode` owns them, and a second copy of the merge rule is precisely the
  client/server divergence the shared-logic boundary exists to prevent.

## Measurement caveat

Standalone Windows and Linux builds use Mono2x with managed stripping disabled. A green result
there exercises neither IL2CPP nor the stripper, so it cannot validate AOT behaviour, `link.xml`
preservation, or Burst codegen. Any performance or AOT claim in this repository must be backed by
an Android or WebGL build, with stripping raised above the default Minimal for a stripping test.
