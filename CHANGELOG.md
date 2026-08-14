# Changelog

All notable changes to the Cuvara DOTS package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
