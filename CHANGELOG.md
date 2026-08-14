# Changelog

All notable changes to the Cuvara DOTS package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
