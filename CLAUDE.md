# OpenRA Developer Reference

Open-source C#/.NET RTS engine reimplementing Command & Conquer: Red Alert, Tiberian Dawn, and Dune 2000.

## Build & Run

```bash
make                              # Build all projects
make TARGETPLATFORM=unix-generic  # Build using system native libraries
make test                         # Lint-check YAML rules for all official mods
make check                        # Code style analysis on engine and mod DLLs
dotnet build OpenRA.sln           # Direct .NET build
dotnet test OpenRA.Test           # Run unit tests
```

Launch commands:
```bash
./launch-game.sh                          # Launch the game
./launch-dedicated.sh                     # Launch dedicated server (default mod: ra)
Mod="cnc" ./launch-dedicated.sh           # Launch with a specific mod
dotnet run --project OpenRA.Server        # Run server directly
```

## Solution Structure

| Project | Purpose |
|---------|---------|
| `OpenRA.Game/` | Core engine: game loop, actor system, networking, scripting, map, coordinate types |
| `OpenRA.Mods.Common/` | Shared mod code: traits, activities, scripting bindings, bot modules |
| `OpenRA.Mods.Cnc/` | Red Alert / Tiberian Dawn specific code |
| `OpenRA.Mods.D2k/` | Dune 2000 specific code |
| `OpenRA.Server/` | Dedicated server (`Program.cs`) |
| `OpenRA.Platforms.Default/` | Platform abstraction (rendering, sound, input) |
| `OpenRA.Utility/` | Command-line utilities (asset import, lint) |
| `OpenRA.Launcher/` | Game launcher |
| `OpenRA.Test/` | Unit tests (NUnit) |
| `mods/` | Mod data: `ra/`, `cnc/`, `d2k/`, `ts/` (rules, maps, assets, scripts) |

## Architecture

### Trait System (ECS Pattern)

The engine uses a trait-based composition model on actors. Every behavior is a pair:

- **TraitInfo** (config/data class): Defined in MiniYAML rules, deserialized via `FieldLoader`. Must implement `TraitInfo` and override `Create()`.
- **Trait** (runtime logic class): Instantiated per-actor. Implements behavior interfaces.

```csharp
// Definition (config)
public class HealthInfo : TraitInfo
{
    public readonly int HP = 0;
    public override object Create(ActorInitializer init) { return new Health(init, this); }
}

// Runtime logic
public class Health : IHealth, ITick, ISync { ... }
```

Key trait interfaces (defined in `OpenRA.Game/Traits/TraitsInterfaces.cs` and `OpenRA.Mods.Common/TraitsInterfaces.cs`):

| Interface | Purpose |
|-----------|---------|
| `ITick` | Called every game tick |
| `INotifyCreated` | Called after all traits are initialized |
| `INotifyAddedToWorld` / `INotifyRemovedFromWorld` | World membership changes |
| `IResolveOrder` | Handle incoming orders |
| `IIssueOrder` | Generate orders from player input |
| `IRender` | Visual rendering |
| `IHealth` | HP, damage state, kill |
| `IOccupySpace` | Cell/position occupation |
| `IDisabledTrait` | Conditional enable/disable |
| `IBot` | Bot player interface |
| `IBotTick` / `IBotEnabled` / `IBotRespondToAttack` | Bot module hooks |

Trait querying on actors:
```csharp
actor.Trait<Health>()                    // Get single trait (throws if missing)
actor.TraitOrDefault<Health>()           // Get single trait (returns null if missing)
actor.TraitsImplementing<ITick>()        // Get all traits implementing interface
actor.Info.HasTraitInfo<MobileInfo>()    // Check trait existence on ActorInfo
world.ActorsHavingTrait<Mobile>()        // Query all actors with a trait
```

### Core Types

| File | Class | Role |
|------|-------|------|
| `OpenRA.Game/Game.cs` | `Game` | Static entry point, main loop, mod loading |
| `OpenRA.Game/World.cs` | `World` | Container for actors, map, players, tick dispatch |
| `OpenRA.Game/Actor.cs` | `Actor` | Entity with traits, owner, activity queue |
| `OpenRA.Game/Player.cs` | `Player` | Player state: `PlayerActor`, faction, shroud, win state |
| `OpenRA.Game/ModData.cs` | `ModData` | Mod rules, sequences, manifest |
| `OpenRA.Game/Map/Map.cs` | `Map` | Terrain, cell grid, rules |

Key `World` members:
```csharp
world.Actors                        // IEnumerable<Actor> - all actors
world.WorldTick                     // int - current game tick
world.WorldActor                    // Actor - world-level traits container
world.Map                           // Map reference
world.Players                       // Player[]
world.LocalPlayer                   // Player (null on server/replays)
world.IssueOrder(order)             // Send an order
world.SetPauseState(bool)           // Pause/unpause the game loop
world.ActorsHavingTrait<T>()        // Query actors by trait
world.IsGameOver                    // bool
```

Key `Actor` members:
```csharp
actor.ActorID                       // uint
actor.Info                          // ActorInfo (type name, trait infos)
actor.Owner                         // Player
actor.World                         // World reference
actor.IsInWorld                     // bool
actor.CurrentActivity               // Activity (current task)
actor.Disposed                      // bool
actor.Trait<T>() / .TraitOrDefault<T>() / .TraitsImplementing<T>()
actor.QueueActivity(activity)       // Enqueue an Activity
actor.CancelActivity()              // Cancel current activity
```

Key `Player` members:
```csharp
player.PlayerActor                  // Actor holding player-level traits
player.PlayerName / .InternalName
player.Faction                      // FactionInfo
player.IsBot / .BotType
player.WinState                     // Undefined / Won / Lost
player.Shroud                       // Fog of war
player.PlayerActor.Trait<PlayerResources>()  // Access resources
```

### Coordinate System

| Type | Description | Usage |
|------|-------------|-------|
| `CPos` | Cell position (integer grid) | Map cells, building placement |
| `WPos` | World position (1024 units per cell) | Precise positions, projectiles |
| `MPos` | Map position (internal UV coords) | Internal map representation |
| `WDist` | World distance | Ranges, radii |
| `WRot` | World rotation | Facing, angles |
| `WVec` | World vector | Offsets, velocities |
| `CVec` | Cell vector | Cell offsets |
| `WAngle` | Angle (0-1023 range) | Facing directions |

### Activities

Activities are coroutine-like objects for async actor behavior. Defined in `OpenRA.Game/Activities/Activity.cs`, implementations in `OpenRA.Mods.Common/Activities/`.

States: `Queued` -> `Active` -> `Done` (or `Canceling` -> `Done`)

```csharp
public class Move : Activity
{
    protected override void OnFirstRun(Actor self) { ... }  // Init (not constructor!)
    public override bool Tick(Actor self) { ... }            // Return true when done
    protected override void OnLastRun(Actor self) { ... }    // Cleanup
}
```

Common activities: `Move`, `Attack`, `FindAndDeliverResources`, `Hunt`, `Enter`, `Turn`, `Wait`, `Transform`, `Sell`, `CaptureActor`, `Parachute`.

Rules:
- Do NOT evaluate dynamic state in the constructor; use `OnFirstRun()`.
- Return `true` from `Tick()` at least once.
- Do not reuse started activity instances.
- Call `activity.Cancel()`, not `actor.CancelActivity()`.

### Orders and Networking

Deterministic lock-step networking. All game state changes flow through orders:

| File | Role |
|------|------|
| `OpenRA.Game/Network/Order.cs` | `Order` class - serializable command |
| `OpenRA.Game/Network/OrderManager.cs` | Dispatches orders to the network |
| `OpenRA.Game/Network/Connection.cs` | Network connection abstraction |

Order flow: Player input -> `IIssueOrder` -> `Order` -> network -> `IResolveOrder` on all clients.

```csharp
// Creating and issuing an order
var order = new Order("Move", actor, Target.FromCell(world, cell), false);
world.IssueOrder(order);

// Bot issuing orders
bot.QueueOrder(new Order("AttackMove", unit, Target.FromCell(world, cell), false));
```

### MiniYAML Rules

Hierarchical configuration: engine defaults -> mod rules -> map overrides. Files in `mods/<mod>/rules/`.

```yaml
E1:                          # Actor type name
    Inherits: ^Soldier       # Inheritance with ^ prefix
    Health:
        HP: 50000
    Mobile:
        Speed: 56
    Armament:
        Weapon: M1Carbine
    AutoTarget:
        InitialStance: AttackAnything
```

Red Alert rules files: `infantry.yaml`, `vehicles.yaml`, `structures.yaml`, `ships.yaml`, `aircraft.yaml`, `defaults.yaml`, `player.yaml`, `world.yaml`, `ai.yaml`.

### Bot System

Bot AI uses `ModularBot` (`OpenRA.Mods.Common/Traits/Player/ModularBot.cs`) implementing `IBot`. Behavior is composed of bot modules in `OpenRA.Mods.Common/Traits/BotModules/`:

| Module | Purpose |
|--------|---------|
| `SquadManagerBotModule` | Combat unit grouping and tactics |
| `UnitBuilderBotModule` | Unit production decisions |
| `BaseBuilderBotModule` | Base construction |
| `HarvesterBotModule` | Resource harvesting management |
| `McvManagerBotModule` | MCV deployment |
| `CaptureManagerBotModule` | Capturing enemy buildings |
| `SupportPowerBotModule` | Superweapon usage |
| `BuildingRepairBotModule` | Repairing damaged structures |
| `PowerDownBotManager` | Power management |

Bot modules implement `IBotTick` and are ticked by `ModularBot`. Bots interact with the game only through orders via `IBot.QueueOrder()`.

### Lua Scripting

Lua 5.1 sandbox for mission scripting. Engine binding in `OpenRA.Game/Scripting/ScriptContext.cs`. Mission scripts live in `mods/<mod>/maps/<mission>/`.

Global tables (defined in `OpenRA.Mods.Common/Scripting/Global/`):
`Actor`, `Map`, `Player`, `Trigger`, `Utils`, `Media`, `Reinforcements`, `Camera`, `Beacon`, `Lighting`, `Radar`, `UserInterface`, `DateTime`, `Angle`, `Color`, `CoordinateGlobals`.

Actor/Player property bindings (34 classes in `OpenRA.Mods.Common/Scripting/Properties/`):
`GeneralProperties`, `CombatProperties`, `HealthProperties`, `MobileProperties`, `HarvesterProperties`, `ProductionProperties`, `TransportProperties`, `PlayerProperties`, etc.

### Headless / Dedicated Server

For running without graphics (testing, AI, dedicated servers):
- `launch-dedicated.sh` - Server launch script
- `OpenRA.Platforms.Default/` contains `Null*` implementations (NullRenderer, DummySoundEngine)
- Server binary: `bin/OpenRA.Server.dll`

### RL Integration Points

For reinforcement learning or external AI:
- `IBot` interface + `ModularBot` as reference for bot implementation
- `World.SetPauseState(bool)` to pause/resume the lock-step game loop
- `World.Actors` to enumerate all actors for state observation
- `World.WorldTick` for current simulation step
- `Player.PlayerActor.Trait<PlayerResources>()` for economic state
- `IBot.QueueOrder()` for sending actions
- `World.ActorsHavingTrait<T>()` for filtered actor queries
- `actor.Trait<Health>().HP` / `.IsDead` for unit health
- `actor.Trait<Mobile>()` for movement capabilities
- `World.Map` for terrain and pathfinding data
- `World.IsGameOver` / `Player.WinState` for episode termination

## Code Conventions

- All game-affecting logic must be deterministic (sync-safe). Use `world.SharedRandom`, never `System.Random`.
- Traits must not modify world state in constructors -- use `INotifyCreated.Created()`.
- Bot logic must only act through `IBot.QueueOrder()`, never directly mutating state.
- Performance-sensitive code avoids LINQ and uses `for` loops. `Enum.HasFlag` is avoided (custom `Has*` extensions used instead).
- `[Sync]` attribute marks fields included in sync hash for desync detection.
- `[Desc("...")]` attribute documents YAML-exposed fields.
- License header required on all `.cs` files (GPLv3).
