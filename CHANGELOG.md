# Changelog

All notable changes to the Cuvara DOTS package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`Samples~/HybridViews` sample ("Hybrid Views"), declared in `package.json`.** A `MonoBehaviour`
  bootstrap that installs the view layer into the default world, a `PrimitiveViewAssetProvider`
  implementing `IViewAssetProvider` over `GameObject.CreatePrimitive` and a `Stack<GameObject>` pool
  — so the sample runs on a bare install with only the four pinned dependencies and proves the
  standalone claim — orbiting entities that make the transform sync visible, a mid-run despawn that
  exercises the recycle path, and a narrated chunk warm/release in which two chunks share a key: the
  shared key survives the first release and is torn down only on the second. A cold key is left
  deferred on purpose so the "invisible for a few frames" behaviour can be seen rather than read
  about. Sample `README.md` states which optional packages are *not* needed and why.
- **Sample-revealed hazard, documented in the sample README:** releasing a chunk whose entities are
  still alive destroys those views through `IViewAssetProvider.Release` while the
  `EntityViewRegistry` keeps their handles and the entities keep an `EntityViewLink` that will never
  resolve or respawn. The package has no chunk→entity ownership model, so callers must despawn the
  entities before releasing the chunk.
- Sample updated for the 0.4.0 transform change: entities created from code now add `LocalToWorld`
  explicitly (`TransformSystemGroup` writes into it but does not add it — baking would, runtime
  creation does not), and `OrbitMotionSystem` names its group instead of relying on the default.

## [0.4.0] - 2026-08-14

### Added

- **The package's full system group tree**, all positions fixed now so a consumer's `[UpdateAfter]`
  resolves today and does not change meaning as systems land:
  `NetcodeSystemGroup` → `ProvisioningSystemGroup` in `InitializationSystemGroup`;
  `GameplaySystemGroup` (`UpdateBefore(TransformSystemGroup)`) containing `MovementSystemGroup`,
  `LifecycleSystemGroup` and `DotsEndSimulationCommandBufferSystem` (`OrderLast`);
  `ViewSystemGroup` in `PresentationSystemGroup`. Groups other than the view group are empty in this
  version.
- `DotsEndSimulationCommandBufferSystem` — the package's own ECB, played back at the end of
  `GameplaySystemGroup` and therefore *before* `TransformSystemGroup` and long before presentation.
  Unity's `EndSimulationEntityCommandBufferSystem` plays back after the transform systems, which
  would leave a window in which a view could be synced against an entity that died this frame.
- `DotsViewBootstrap.InstallSystems(World)`, called by `Install`.

### Changed

- **Breaking: the view systems moved into `ViewSystemGroup` and became `internal`.** The group tree
  is the ordering contract; a public system name is an accidental API promise. `InternalsVisibleTo`
  grants access to the two test assemblies only.
- **Breaking: transform sync reads `LocalToWorld`, not `LocalTransform`.** `LocalTransform` is
  relative to the parent, so a parented entity's view was placed at local coordinates — correct for
  root entities and wrong for everything else. `LocalToWorld` is what `TransformSystemGroup` computed
  earlier in the same frame and is right in both cases. Uniform scale is recovered as the length of
  the matrix's first basis vector; non-uniform and sheared transforms are not represented.
- **Breaking: every package system and group is `[DisableAutoCreation]`** and created explicitly by
  `DotsViewBootstrap`. Unity's default bootstrap creates every non-disabled system in *every* world,
  so a multi-world setup would have had two view groups driving one `EntityViewRegistry` and
  double-spawning every entity.
- Spawn now runs before despawn (was despawn-first in 0.2.0), matching the agreed tree.
- Group names dropped their `Cuvara` prefix — `Cuvara.DOTS.*` already scopes them. `Dots` is kept on
  the ECB system precisely so it cannot be mistaken for Unity's.

### Resolved

- **The open question about optional asmdef references is answered: Unity drops a name-based
  reference to an assembly that does not exist.** Verified locally rather than assumed — four
  installed `UniT.*.DI` asmdefs in the consuming project reference `"Zenject"`, `com.svermeulen.extenject`
  is not in the project, and it compiles. Those assemblies are not even excluded by `defineConstraints`,
  so this covers the stronger case. `Cuvara.DOTS.GameFoundation`'s references to `UniT.*` / `UniTask` /
  `VContainer` and `Cuvara.DOTS.VContainer`'s reference to `Cuvara.DOTS.GameLogic` are therefore safe
  when those packages are absent, and the GUID-reference fallback is not needed.

### Accepted limitations

- **`OrderFirst` is not used where a sibling's `UpdateAfter` already encodes the same order.**
  Entities sorts `OrderFirst` members into a separate batch and then drops ordering relations between
  that batch and normal members, with only a warning — so the explicit relation is the one that holds.
  Ordering is unchanged from the agreed tree; only the mechanism differs. `OrderLast` is kept on the
  ECB system, which has no sibling relation to conflict with.
- **No `FixedStepSimulationSystemGroup`.** The server is authoritative and the client integrates
  nothing; a fixed 60 Hz group would re-time server-paced data for no determinism gain. It changes
  when prediction lands, and the rate will derive from the server tick rate.
- **Nothing has been compiled.** The `DotsEndSimulationCommandBufferSystem` singleton boilerplate is
  the highest-risk item — it is unsafe code against an API whose exact shape varies by Entities version.

## [0.3.0] - 2026-08-14

### Added

- **Optional `Shared.GameLogic` seam.** `ISimulationModel` plus the value types it speaks in
  (`SimEntity`, `SimBounds`, `SimConstants`, `SimMoveResult`) live in `Cuvara.DOTS.Runtime` with no
  define guards at all. The package owns the abstraction; `Shared.GameLogic` is one implementation
  behind it and never the interface. Consumer code is byte-identical whether or not
  `com.rpgmmo.shared-gamelogic` is installed.
- **`Cuvara.DOTS.GameLogic`** — optional assembly holding `SharedGameLogicSimulation`, which
  delegates movement to `MovementSystem.TryMove` and combat to `CombatLogic.CalculateDamage` /
  `InRange`, and the `Vec2`↔`float2` / `SimBounds`→`MapBounds` conversions. All conversion lives
  here because it can only live here: `Shared.GameLogic.asmdef` is `noEngineReferences: true` and
  can never learn what a `float2` is. Gated by `versionDefines` + `defineConstraints` on
  `com.rpgmmo.shared-gamelogic`, so it is not compiled at all when the package is absent.
- **`PassiveSimulationModel`** — the absent-dependency path. It applies authoritative state and
  predicts nothing, reporting `IsAuthoritative == false` and `SimMoveResult.Unavailable`.
- `RegisterSimulationModel()` in `Cuvara.DOTS.VContainer`, holding the **single `#if`** that decides
  between the two implementations, driven by the asmdef `versionDefine` rather than a hand-set
  Player Settings define — the `GDK_VCONTAINER` pattern from `com.gdk.core`.
- Tests in a new `Cuvara.DOTS.Tests.GameLogic` assembly, itself constrained on
  `CUVARA_SHARED_GAMELOGIC` so it only compiles when the dependency is present: field-by-field
  parity of `SimConstants` against `GameConstants`, and the shared `movement.json` golden vectors
  replayed **through the seam** and compared bit-for-bit.

### Changed

- `package.json` bumped to 0.3.0. Pinned Unity dependencies unchanged — `com.rpgmmo.shared-gamelogic`
  is an optional integration, not a dependency.

### Accepted limitations

- **`PassiveSimulationModel` refuses rather than approximates.** It returns 0 damage, `false` for
  `InRange` and an unchanged position. It does not re-derive the server's movement rule: that rule
  is not "position += direction * speed * dt" — `MovementSystem.Integrate` splits the multiply into
  separate float locals to deny an FMA contraction, and `Vec2.SqrMagnitude` casts every intermediate
  because C# permits higher-precision evaluation and .NET's RyuJIT and Unity's Mono JIT choose
  differently. A re-implementation would be one ULP wrong and drift silently. Callers must check
  `IsAuthoritative`.
- **`SimConstants.Unavailable` is all zeros**, not plausible defaults. With the shared package absent
  there is no source of truth, and inventing one is the literal-copy trap in disguise.
- **`SimEntity` carries no identity.** `EntityState.Id` / `.Type` are `string` and therefore unusable
  in Burst or an `IComponentData`; identity stays ECS-side as a `FixedString64Bytes`. `SnapshotMerger`
  (netcode owns it) and `ValidationLogic` (managed delegate over string keys, server-shaped) are
  deliberately not exposed.
- **The DI assembly is still named `Cuvara.DOTS.VContainer`**, not `Cuvara.DOTS.DI`, since it already
  shipped in 0.2.0 — its root namespace is `Cuvara.DOTS.DI` and the registration lives there.
- **Nothing here has been compiled.** No Unity Editor was available.

## [0.2.0] - 2026-08-14

### Added

- **Chunk-aware view provisioning.** `IViewAssetProvider` is the one seam the view layer has onto
  asset loading and pooling; `ChunkViewProvisioner` warms and releases whole key sets on behalf of a
  spatial chunk or region, reference-counting keys so two chunks sharing a prefab cannot unload it
  from under each other. A key counts once per chunk regardless of how many times the chunk lists
  it, releasing an unknown or already-released chunk is a no-op, and re-warming an existing chunk
  diffs — a key in both the old and new set never transiently reaches zero.
- **`Cuvara.DOTS.GameFoundation`** — optional adapter implementing `IViewAssetProvider` over the
  GameFoundation / UniT `IAssetsManager` + `IObjectPoolManager` pair. No loader, cache or pool of its
  own: a second pool over the same prefabs would fight the first over recycling.
- **Hybrid entity↔GameObject views.** `EntityViewRequest` → `EntityViewLink` (+ the
  `EntityViewLinkCleanup` cleanup component), a managed `EntityViewRegistry` side-table reached from
  `ISystem` structs through the managed `EntityViewRegistryReference` singleton, and three systems in
  `PresentationSystemGroup`: spawn, despawn and per-frame `LocalTransform` → `Transform` sync.
- **`Cuvara.DOTS.VContainer`** — optional `RegisterDotsViews()` registration extension, mirroring
  `GameFoundationVContainer.RegisterGameFoundation`. The caller supplies the `IViewAssetProvider`.
- **Explicit system group hierarchy.** `CuvaraViewPresentationGroup` (in `PresentationSystemGroup`)
  contains `CuvaraViewLifecycleGroup` (despawn, then spawn) and `CuvaraViewTransformSyncGroup`
  (`UpdateAfter` the lifecycle group). No package system sits in a default group or relies on
  implicit creation order, and consumers order their own systems against the package groups rather
  than against individual systems.
- Tests: reference-count semantics of the chunk provisioner (edit mode), the entity→view
  spawn/despawn/sync lifecycle against an isolated `World` (play mode), and reflection assertions
  on the group attributes (edit mode) — a misplaced `[UpdateInGroup]` never fails a build and shows
  up only as views trailing the simulation by a frame.

### Changed

- `package.json` bumped to 0.2.0. Its four pinned Unity dependencies are unchanged — VContainer,
  UniTask and GameFoundation/UniT are **not** dependencies, only optional integrations.

### Removed

- The `PackageMarker` placeholder in `Runtime/` and both placeholder smoke tests, now that those
  assemblies hold real code. `Editor/PackageMarkerEditor.cs` stays: `Cuvara.DOTS.Editor` still has
  no real code, and an assembly definition over an empty folder produces no assembly.

### Accepted limitations

- **Warm counts only grow.** If chunk A warms 8 instances of a key and chunk B warms 2, releasing A
  leaves 8 warm. Shrinking would destroy pooled instances a live chunk may be about to spawn, which
  is the hitch prewarming exists to avoid. Memory returns when the count reaches zero.
- **The sync split is structural, not a measured win.** The collect half is an `IJobEntity` writing
  blittable samples; the apply half is a flat main-thread loop, because `Transform` cannot be touched
  off the main thread or Bursted. This has not been profiled — at low view counts the scheduling
  overhead may cost more than it saves. The `Complete()` before the drain is a sync point every frame.
- **Cold keys defer rather than load.** An entity whose view prefab has not been warmed stays
  invisible for a few frames instead of forcing a synchronous load. The hitch belongs in the chunk
  prewarm, where it is asynchronous and expected.
- **View handles are never reused.** A recycled id would let a stale `EntityViewLink` address someone
  else's view — a bug that reads as a rendering glitch. Wrap-around at `int.MaxValue` is not defended.
- **Nothing here has been compiled.** No Unity Editor was available; see the 0.2.0 notes in the
  commit message for what the first compile is most likely to catch.

## [0.1.0] - 2026-08-14

### Added

- Initial package scaffold. No runtime gameplay code — assemblies and metadata only.
- `package.json` declaring `com.cuvara.dots` for Unity 6000.3, MIT licensed, depending on
  `com.unity.entities` 1.4.8, `com.unity.burst` 1.8.30, `com.unity.collections` 2.6.8 and
  `com.unity.mathematics` 1.3.2 — the versions already used by the consuming Unity project.
- Four assembly definitions: `Cuvara.DOTS.Runtime`, editor-only `Cuvara.DOTS.Editor`, and the
  Unity Test Framework assemblies `Cuvara.DOTS.Tests.Runtime` (play mode) and
  `Cuvara.DOTS.Tests.Editor` (edit mode), the latter two gated on `UNITY_INCLUDE_TESTS`.
- Placeholder `PackageMarker` types in each assembly folder. Unity produces no assembly for an
  assembly definition whose folder holds no C# file, which would break every reference to it;
  the markers keep the graph resolvable until real code lands.
- Smoke tests in both test assemblies asserting the runtime assembly is referenceable.
- `README.md`, `CHANGELOG.md`, MIT `LICENSE` and a Unity-package `.gitignore`.
