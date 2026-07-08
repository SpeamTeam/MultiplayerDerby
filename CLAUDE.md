# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

MultiplayerDerby is a Unity 6 (`6000.3.19f1`) multiplayer car-derby combat game: players ram each other's cars, deal impulse-based damage, and score points for damage/kills/survival. Networking is built on **Unity Netcode for GameObjects (NGO) 2.13**, rendering on **URP 17**, camera on **Cinemachine 3**, and controls on the new **Input System**.

There is no README with project-specific content (the repo's `Assets/Readme.asset` is the stock Unity template readme) and no `.cursorrules`/Copilot instructions exist.

## Working with this repo

This is a Unity project, not a package/app with a CLI build pipeline — there are no npm/make/gradle scripts. All day-to-day work happens by opening the project in the **Unity Editor matching `ProjectSettings/ProjectVersion.txt`** (currently `6000.3.19f1`).

- **Editing scripts**: edit the `.cs` files directly (Rider/VS Code both fine); Unity recompiles on focus.
- **Running the game**: press Play in the Editor from `MenuScene` (host/connect flow) or `WorldScene` (arena) directly.
- **Testing multiplayer locally**: the project includes **ParrelSync** (`Assets/ParrelSync`, `Assets/Plugins/ParrelSync`) specifically so you can clone the project (Editor menu it adds) and run a second Editor instance side-by-side as a client, without building.
- **Automated tests**: `com.unity.test-framework` is in `Packages/manifest.json` but there are **no EditMode/PlayMode test assemblies in the repo**. `Assets/Scripts/SampleTests/PlayerSampleScript.cs` is *not* a unit test — it's a leftover sample `NetworkBehaviour` player controller explicitly marked "for testing purposes only, report if found in production." If you add real tests, they need their own test assembly definition wired up via the Unity Test Runner window.
- Only one assembly definition exists in the project (`Assets/ParrelSync/projectCloner.asmdef`); everything else under `Assets/Scripts` compiles into the default `Assembly-CSharp`.

## Architecture

### Two parallel, non-interoperating car systems — know which one you're touching

The codebase currently contains **two separate, independently-implemented car controllers**. They are not variants of each other — don't assume a fix in one applies to the other.

1. **`Assets/Scripts/AI/*`** (`CarController`, `CarCollision`, `CarNavMeshAgent`, `NavMeshTest`) — a self-contained, non-networked car: its own HP/damage/death, its own mesh-deformation-on-impact system, and NavMesh-based autonomous driving (`CarNavMeshAgent` drives `CarController` via `SetInputs`). This is the AI/bot car stack and does not touch `CarHealth`, `PlayerScore`, or NGO at all.
2. **`Assets/Scripts/MainPhysics/*` + `Assets/Scripts/Input/PlayerCarController.cs`** — the actual player-facing derby car, and the one being actively evolved toward server-authoritative multiplayer. Composed of:
   - `PlayerCarController` — `WheelCollider`-based driving, boost (Shift), handbrake/drift (Ctrl), reads `CarControls.inputactions`.
   - `CarHealth` — pure HP state machine (`ApplyDamage`/`Heal`/`Die`/`ResetState`, spawn invulnerability window). Deliberately dumb; doesn't decide *when* damage happens.
   - `CarCollisionDetector` — the damage model: uses `collision.impulse` (mass-aware, unlike raw relative velocity), applies a frontal-hit-angle bonus and an aggressor bonus (whoever is closing in on the target hits harder), per-target cooldown, and filters out near-vertical "ground bump" contacts so curbs/jumps don't register as hits.
   - `CarStabilizer` — anti-roll torque plus auto-flip-upright if a car stays flipped too long; fires `OnFlippedTooLong`.
   - `DriverEjection` — two-phase ragdoll ejection (`Seated → Launched → FullyEjected`); only `FullyEjected` (driver physically exits the cabin trigger volume) counts as a loss and kills the car (`killCarOnEject`).
   - `CarAgent` (the only `NetworkBehaviour` in this stack) — ties the above together: subscribes to `CarHealth` events, awards kill/damage score via `PlayerScore`, disables control on death, and drives respawn through `SpawnManager`.

   **Extensive Russian-language doc comments throughout this stack describe an in-progress migration plan to server authority** (e.g., `CarHealth.currentHealth` → `NetworkVariable<float>`, wrap `ApplyDamage`/scoring/respawn in `IsServer` checks). As of now, **only `CarAgent` is network-aware — `CarHealth`, `CarCollisionDetector`, `CarStabilizer`, `DriverEjection`, `PlayerScore`, and `ScoreTickManager` all run as plain client-side logic with no server authority yet.** When adding networking to any of these, check those comments first — they encode the intended approach.

### Networking bootstrap

- `NetworkHandler` (singleton on the `NetworkManager`/`UnityTransport` object, `DontDestroyOnLoad`) is the entry point: `MakeHost()` starts NGO hosting and loads `WorldScene`; `MakeClient(ip, port)` sets transport connection data and starts a client. On the host, once `WorldScene` finishes loading it spawns a `NetworkProvider` (currently an empty `NetworkBehaviour` stub prefab at `Assets/Prefabs/Network/NetworkProviderObject.prefab`) — the intended place for future server-side session/game-state logic.
- `MenuManager` (in `MenuScene`) is the UI glue: Host button → `NetworkHandler.MakeHost()`; Connect button parses an `ip:port` string (defaults to `127.0.0.1:6767`) → `NetworkHandler.MakeClient()`.
- `PlayerSpawnManager` (`NetworkBehaviour`) spawns the player prefab (from `GameManager.Instance.Config.playerPrefab`) for each connecting client, server-side only. **Note:** it always spawns at `Vector3.zero` (`// TODO: Change spawn position`) — it is not yet wired to `SpawnManager`'s spawn-point picking.
- `SpawnManager` (non-networked, `Assets/SpawnManager.cs`) is the more developed spawn/respawn logic: Fisher-Yates shuffle over `spawnPoints` for unbiased pick, with occupancy check via `Physics.CheckSphere`. `CarAgent.Respawn()` calls into it. It is *not yet* used by the networked initial-spawn path (`PlayerSpawnManager`) and is itself flagged as needing to become server-authoritative (`NetworkObject.Spawn()` after `Instantiate`).
- `SpawnPointsScript` is a separate, older/likely-superseded singleton holder of spawn point transforms (also marked `TODO: change to NetworkBehaviour`) — check whether a given scene actually wires it up before assuming it's live.

### Game bootstrap & config

- `GameManager` (singleton, *not* `DontDestroyOnLoad`) holds a `GameConfig` ScriptableObject (`Assets/Scripts/gameconfig.asset`) with `playerPrefab`, `enemyPrefab`, `cameraPrefab`, `worldSceneName`. On `Start()` it instantiates `cameraPrefab`.
- Scenes (`Assets/Scenes/`): `MenuScene` (host/connect UI), `WorldScene` (the arena — hardcoded by name in both `NetworkHandler` and `GameConfig`), `SampleScene` (stock Unity template scene, not gameplay), `LEHA TEST` (an ad-hoc dev test scene).

### Camera

Cinemachine 3, using `CinemachineOrbitalFollow`. `CameraFollow.InitializeCamera()` (called from `CarAgent.Start()` only when `IsOwner`) points the free-look camera at the local player's car via `CinemachineFind.Instance`. `OrbitalCameraControl` manually drives the orbital axes: rotation only while LMB is held, scroll-wheel zoom with clamped/smoothed radius — deliberately *not* using `CinemachineInputAxisController`, which would rotate continuously from any mouse movement.

### Scoring

- `PlayerScore` (per-car): damage-dealt points, a kill bonus, and a survival tick whose per-second value grows with a coefficient the longer the car survives without dying (capped).
- `ScoreTickManager`: lazily-created singleton running **one** coroutine for all cars (ticks every `tickInterval`, default 1s) instead of a per-car `Update()` — an explicit perf choice documented in the file. Also flagged for becoming server-only once network scoring lands.

### Input

- `CarControls.inputactions` is the real action asset used by `PlayerCarController` (the live player car).
- `InputSystem_Actions.inputactions` and `SampleInputActions.inputactions` are Unity-template/sample leftovers, used only by the sample `PlayerController` in `Assets/Scripts/SampleTests` — not part of the live gameplay path.

### Debug/tuning-only tools (not gameplay features)

- `CrashTestDummy` — a scripted Rigidbody projectile for firing repeatable, known-speed/angle test collisions at a car, used to tune `CarCollisionDetector`'s damage constants without needing a second player.
- `DebugHud` — an `OnGUI` overlay (HP/score/driver-state/speed per watched car), explicitly meant to be removed once real UI exists.

### Third-party assets & prefabs

- Vehicle/prop art lives under `Assets/Assets/*` (Awb Free Low Poly Vehicles, Stylized Vehicles Pack Free, ZIL130 Military Truck, Unity Technologies template assets).
- Gameplay prefabs live under `Assets/Prefabs/PlayerPrefabs/` (`Player.prefab`, `PlayerCar.prefab`, `TCK.prefab`) and `Assets/Prefabs/Network/` (`NetworkProviderObject.prefab`); `Assets/Prefabs/CameraObject.prefab` is the camera rig instantiated by `GameManager`.
