# Hybrid Views sample

Entities drive GameObjects. Four ECS entities per key orbit the origin, their views are pooled
primitives, and the Console narrates what the chunk reference counts do while it happens.

## What it shows

1. **Bootstrap.** `HybridViewsSample` builds an `EntityViewRegistry` over an `IViewAssetProvider`
   and calls `DotsViewBootstrap.Install(world, registry)`. That single call is the whole
   integration — after it, an entity gets a view by carrying an `EntityViewRequest`.
2. **A self-contained provider.** `PrimitiveViewAssetProvider` implements `IViewAssetProvider` with
   `GameObject.CreatePrimitive` and a `Stack<GameObject>` pool. It also accepts a serialized prefab
   per key if you would rather look at your own art.
3. **Transform sync.** `OrbitMotionSystem` (a Bursted `ISystem` + `IJobEntity`) writes
   `LocalTransform` in `SimulationSystemGroup`; the package's sync system copies it onto the
   `Transform` later in the same frame, from `PresentationSystemGroup`. The views move and spin
   because of that, and nothing else.
4. **Despawn and recycle.** Half the entities are destroyed mid-run. Their views return to the pool
   through the cleanup-component path, and the next spawn reuses them — the final log line compares
   *acquires* against *instantiations*, and acquires being higher is the pool working.
5. **Chunk warm and release — the loud part.** Two chunks share the `sphere` key:

   | Step | Action | `cube` | `sphere` | `capsule` |
   |------|--------|--------|----------|-----------|
   | 1 | warm `chunk.alpha` = [cube, sphere, **cube**] | 1 | 1 | 0 |
   | 3 | warm `chunk.beta` = [sphere, capsule] | 1 | **2** | 1 |
   | 5 | release `chunk.alpha` | **0 → torn down** | **1 → kept** | 1 |
   | 5b | release `chunk.alpha` again | 0 | 1 | 1 | 
   | 6 | release `chunk.beta` | 0 | **0 → torn down** | **0 → torn down** |

   Three things people get wrong, all visible in the log:
   - `cube` is listed **twice** in chunk.alpha and still counts **once**. The provisioner
     de-duplicates on intake; counting occurrences would leak a reference that never comes back.
   - Releasing chunk.alpha does **not** unload `sphere`, because chunk.beta still lists it. The
     spheres keep rendering across the release.
   - Releasing the same chunk twice is a **no-op**, not a double decrement. Step 5b proves it.

6. **A cold key is deferred, not force-loaded.** Two capsule entities are created in step 2 while
   `capsule` is still cold. They exist and move, but have no view until chunk.beta warms them in
   step 3, when they pop in. That is `EntityViewSpawnSystem` retrying rather than hitching on a
   synchronous load.

## How to run it

1. Import the sample (Package Manager → Cuvara DOTS → Samples → *Hybrid Views* → Import).
2. New empty scene. Add an empty GameObject, add the `HybridViewsSample` component.
3. Make sure the scene has a camera looking at the origin from roughly `(0, 8, -14)` and a
   directional light — the views are lit primitives and an unlit scene looks like nothing spawned.
4. Press Play and read the Console. The whole timeline is ~28 s at the default 4 s step; lower
   `Step Seconds` to move faster, raise it to read along.

Nothing is authored in a subscene and no baking is involved: the entities are created from script
in `Start`, into `World.DefaultGameObjectInjectionWorld`.

## What you do NOT need

The sample references only `Cuvara.DOTS.Runtime` plus `Unity.Entities`, `Unity.Burst`,
`Unity.Collections` and `Unity.Mathematics` — the package's four pinned dependencies. Specifically
**not** required:

| Not needed | Why it might look like it is |
|---|---|
| `Cuvara.DOTS.VContainer` | The bootstrap is a plain `MonoBehaviour`; `DotsViewBootstrap.Install` takes a `World`, not a container. |
| `Cuvara.DOTS.GameFoundation`, UniT, UniTask | `PrimitiveViewAssetProvider` is a complete provider in ~200 lines. `IViewAssetProvider` is `Task`-based, not UniTask-based, precisely so this is possible. |
| Addressables | Views are primitives (or a serialized prefab reference). |
| `Unity.Entities.Graphics` / Hybrid Renderer | Views are ordinary GameObjects rendered by the built-in path. That is the point of a *hybrid* view — no ECS renderer is involved. |
| A subscene, a baker, or an authoring component | Entities are created at runtime from script. |

If you find yourself adding one of these to make the sample compile, that is a bug in the package's
standalone claim, not a missing dependency.

## Caveats, honestly

- **This sample has never been compiled or run.** It was written without a Unity Editor available.
  Treat the first import as the first test.
- **`PrimitiveViewAssetProvider` is not a pool you should ship.** It is a second pool over its own
  prefabs, which is exactly what `IViewAssetProvider`'s docs tell you not to build in a real
  project — adapt the pool you already have instead. It exists here to prove the package needs
  nothing else.
- **Releasing a chunk while its entities are still alive leaves dangling links.** When a key's
  refcount hits zero, the provider destroys live instances of that key, but the `EntityViewRegistry`
  still holds their handles and the entities still carry an `EntityViewLink` that will never
  resolve or respawn. The package has no chunk→entity ownership model, so the sample destroys the
  entities *before* releasing the chunk. If your chunks unload while entities live on, you need to
  despawn or re-request views yourself.
- **All timings are synchronous here.** The provider's `PrewarmAsync` completes in the same frame,
  so the "wait for the warm task" branch in `Update` never actually waits. With a real loader it
  would, and entities would sit view-less for longer than a couple of frames.
- **No performance claim.** Ten views is not a benchmark.
