# Platform22 Remediation Plan

Plan to fix the high and medium priority findings from the 2026-08 project review.
Order: CI first (protects all changes), de-duplication before file splits (same code
areas), then security and persistence. Tests are added throughout.

## Phase 0 - CI pipeline (review issue 3)

- Add `.github/workflows/ci.yml`: build + `dotnet test` on push/PR, .NET 10, NuGet cache.
- Done when: workflow runs green on a PR.

## Phase 1 - De-duplication (review issue 4)

Do before file splits; the duplicates live across `OrleansHost` and `Tui`.

- Move Orleans silo/client bootstrap (`OrleansHost/Program.cs`, `TranslinkMapActor.cs`)
  into shared builders in `Platform22.Orleans`.
- One `StationDirectoryCache` record, one `GetPort`/`GetClusterId` helper set, in
  `Platform22.Orleans`.
- Extract the line-overlay logic shared by `TranslinkMapClient.cs` and
  `TranslinkMapActor.cs` into one service.
- Done when: each concept has one definition; all tests pass.

## Phase 2 - Split large files (review issues 5 and 6)

No behavior change; extract cohesive units:

- `TerminalGuiTransitApp.cs` -> menus/pickers, key handling, refresh loop, rendering.
- `SshTransitHost.cs` -> auth callback, shell-command mode, socat process manager.
- `TranslinkGtfsHttpClient.cs` -> static GTFS zip fetch+cache+parse, realtime feed
  fetch, index builder.
- Isolate reflection uses (`MenuBar.OpenMenu`, FxSsh `_timeout`,
  `GetProperty("Id"/"Name")`) into small adapters with fallbacks.

Done when: no file >400 lines; tests pass.

## Phase 3 - SSH authentication (review issue 1)

Depends on Phase 2 (auth code is isolated first).

- Config-driven mode: `PLATFORM22_SSH_AUTH=none|publickey`. Default `publickey`;
  `none` only when the Aspire run-mode env sets it.
- Wire key verification through the Phase 2 auth adapter; mount an authorized-keys
  Secret in `platform22-security.yaml`; AppHost passes the dev bypass only in run mode.
- Unit-test the auth decision class; manual test with a real SSH client.

Done when: anonymous logins refused outside local dev.

## Phase 4 - Grain storage (review issue 2)

Data is a self-repopulating cache, so no state migration is needed.

- Add `Microsoft.Orleans.Persistence.Redis`, register Redis grain storage in silo and
  client paths via the Phase 1 builders.
- Delete `ValkeyGrainState.cs` and its static multiplexer.
- Tests: grain round-trips with `Microsoft.Orleans.TestingHost` against Testcontainers
  Redis, or the memory provider for CI speed.

Done when: `ValkeyGrainState` is deleted; snapshots survive a silo restart.

## Phase 5 - Test coverage (review issue 7)

Cover what earlier phases made testable:

- Poller: fake clock + stub client; abstract the Redis locks behind an interface ->
  test lease/throttle/prewarm logic.
- Map actor: cache JSON versioning and legacy-fallback tests.
- Grains: round-trip tests from Phase 4.
- Shell-command parser of `SshTransitHost`: table tests.

Done when: every previously untested component has at least one test path; full suite
runs in under two minutes.

## Effort estimate

P0-P1 ~half day, P2-P3 ~1-2 days, P4-P5 ~1 day.

## Progress

- [x] Phase 0 - CI pipeline
  - `.github/workflows/ci.yml` builds and tests the solution on push/PR.
- [x] Phase 1 - De-duplication
  - `Platform22.Orleans/OrleansEnvironment.cs`, `Platform22OrleansHosting.cs`,
    `StationDirectoryCache.cs`, `StationDirectoryCacheReader.cs` are now the
    single homes for env settings, silo/client bootstrap, the directory cache
    payload, and its reader. Line dispatch lives only in
    `TranslinkRailLineCatalog`.
- [x] Phase 2 - Split large files
  - `TerminalGuiTransitApp` is partial across core/input/menus/rendering files;
    SSH host, TUI session, and shell are separate classes; the GTFS client is
    split into client/cache, static parsing, realtime parsing, and response
    composition. Largest file is ~320 lines.
- [x] Phase 3 - SSH authentication
  - `SshAuthPolicy`: `PLATFORM22_SSH_AUTH=none|publickey`; unset fails closed.
    The AppHost sets `none` in run mode only. The k8s manifest mounts a
    `platform22-ssh-authorized-keys` Secret and forces publickey mode.
    FxSsh key/password auth arrives through the `UserAuth` event; keys match by
    SHA256 or MD5 fingerprint, from full authorized_keys lines or bare prints.
  - Note: FxSsh 1.4.0 removed `_timeout`; sessions now use the public
    `ConfigureKeepalive`, so that reflection use is gone entirely (as is the
    shell's `GetProperty` lookup, replaced with delegates). The Terminal.Gui
    menu-open reflection remains, isolated in `MenuBarOpener`.
- [x] Phase 4 - Grain storage
  - `Microsoft.Orleans.Persistence.Redis` registered when valkey is configured,
    memory otherwise; grains use `[PersistentState]` with a small
    `JsonGrainState` wrapper (plain string state breaks providers that build
    default instances on read). `ValkeyGrainState` deleted.
- [x] Phase 5 - Test coverage
  - New suites: grain round-trips on an in-process silo, lease-store decisions
    against an in-memory KV store (`TimeProvider`-driven), poller prewarm/poll
    flows with stub feeds, auth-policy matrix, directory-cache reader formats,
    and shell command handling via an injected output sink.

Final state: solution builds warning-free; 52 tests pass (17 + 35) in under a
second.
