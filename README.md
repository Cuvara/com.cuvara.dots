# Cuvara DOTS

Shared DOTS/ECS building blocks for Cuvara projects — reusable components, systems, jobs and authoring helpers built on Unity Entities, Burst, Collections and Mathematics.

**Status: scaffold — no runtime code yet.** The package currently ships only its assembly definitions, metadata and placeholder marker types so that the assemblies resolve and the test assemblies compile.

## Layout

| Path | Assembly | Purpose |
|---|---|---|
| `Runtime/` | `Cuvara.DOTS.Runtime` | Shared components, systems and jobs |
| `Editor/` | `Cuvara.DOTS.Editor` | Editor-only tooling and inspectors |
| `Tests/Runtime/` | `Cuvara.DOTS.Tests.Runtime` | Play-mode tests |
| `Tests/Editor/` | `Cuvara.DOTS.Tests.Editor` | Edit-mode tests |

## Installation

### Git URL

Add to your project's `Packages/manifest.json`:

```json
"com.cuvara.dots": "https://github.com/Cuvara/com.cuvara.dots.git#v0.1.0"
```

Or via **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/Cuvara/com.cuvara.dots.git#v0.1.0
```

### Embedded

Clone into your project's `Packages/com.cuvara.dots/` folder for local development.

## Requirements

- Unity 6000.3 or newer

Resolved automatically via `package.json`:

| Package | Version |
|---|---|
| `com.unity.entities` | 1.4.8 |
| `com.unity.burst` | 1.8.30 |
| `com.unity.collections` | 2.6.8 |
| `com.unity.mathematics` | 1.3.2 |

## Conventions

- `com.unity.jobs` is deprecated — it is merged into `com.unity.collections`.
- Use `IJobEntity` or `SystemAPI.Query` instead of the obsolete `Entities.ForEach`.
- Prefer unmanaged `ISystem` over managed `SystemBase`.

## License

MIT — see [LICENSE](LICENSE).
