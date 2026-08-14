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
   `LocalTransform` in `SimulationSystemGroup`; `TransformSystemGroup` bakes that into
   `LocalToWorld`, which the package's sync system copies onto the
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
2. Open `Scenes/HybridViewsSample.unity` from the imported folder
   (`Assets/Samples/Cuvara DOTS/<version>/Hybrid Views/Scenes/`).
3. Press Play and read the Console. Nothing else to set up — the scene already has a camera aimed
   at the origin, a directional light, and a **Hybrid Views Sample** GameObject carrying the
   bootstrap with its three view definitions filled in.
4. The whole timeline is ~28 s at the default 4 s step. Select the **Hybrid Views Sample** object
   and lower `Step Seconds` to move faster, raise it to read the Console along the way;
   `Warm Count Per Key` changes how many instances each chunk asks to have ready.

The scene deliberately contains **no render-pipeline-specific components** — no
`UniversalAdditionalCameraData`, no volume, no URP asset reference — so it opens under URP,
HDRP or the built-in pipeline without a missing-script warning. URP attaches its own camera data at
runtime. If your project's URP setup renders it dark, that is the project's lighting settings, not
the sample.

Prefer to build the scene yourself? Add an empty GameObject to any scene, add the
`HybridViewsSample` component, and make sure there is a camera looking at the origin from roughly
`(0, 9, -16)` plus a directional light. The component fills in its own default view definitions.

Nothing is authored in a subscene and no baking is involved: the entities are created from script
in `Start`, into `World.DefaultGameObjectInjectionWorld`.

## What you do NOT need

The sample references only `Cuvara.DOTS.Runtime` plus `Unity.Entities`, `Unity.Burst`,
`Unity.Collections` and `Unity.Mathematics` — the package's four pinned dependencies. Specifically
**not** required:

| Not needed | Why it might look like it is |
|---|---|
| `Cuvara.DOTS.DI` | The bootstrap is a plain `MonoBehaviour`; `DotsViewBootstrap.Install` takes a `World`, not a container. |
| `Cuvara.DOTS.GameFoundation`, UniT, UniTask | `PrimitiveViewAssetProvider` is a complete provider in ~200 lines. `IViewAssetProvider` is `Task`-based, not UniTask-based, precisely so this is possible. |
| Addressables | Views are primitives (or a serialized prefab reference). |
| `Unity.Entities.Graphics` / Hybrid Renderer | Views are ordinary GameObjects rendered by the built-in path. That is the point of a *hybrid* view — no ECS renderer is involved. |
| A subscene, a baker, or an authoring component | Entities are created at runtime from script. |

If you find yourself adding one of these to make the sample compile, that is a bug in the package's
standalone claim, not a missing dependency.

## Caveats, honestly

- **The scene is hand-authored YAML.** It was written without a Unity Editor, from the field layout
  of real Unity 6 scenes, and deliberately kept minimal (camera, light, one GameObject). If Unity
  re-serializes it on first open with extra default fields, that is expected and harmless.
- **This sample has never been compiled or run.** It was written without a Unity Editor available.
  Treat the first import as the first test.
- **`PrimitiveViewAssetProvider` is not a pool you should ship.** It is a second pool over its own
  prefabs, which is exactly what `IViewAssetProvider`'s docs tell you not to build in a real
  project — adapt the pool you already have instead. It exists here to prove the package needs
  nothing else.
- **Releasing a chunk cascades into its views.** Step 5 releases `chunk.alpha` with its cube
  entities still alive: their views are recycled through the ordinary despawn path, their
  `EntityViewLink`s are cleared, the assets are released, and the entities survive with no view —
  a `ChunkCascadeReleased` message reports how many went. Step 6 does it the other way round,
  destroying the entities first, so nothing is left to cascade. Both end with the assets released.
  The cascade only reaches keys the chunk is the last referencer of; pass an `EntityViewCascade` as
  the provisioner's `IViewCascadeSink` or none of this protection exists.
- **All timings are synchronous here.** The provider's `PrewarmAsync` completes in the same frame,
  so the "wait for the warm task" branch in `Update` never actually waits. With a real loader it
  would, and entities would sit view-less for longer than a couple of frames.
- **No performance claim.** Ten views is not a benchmark.
