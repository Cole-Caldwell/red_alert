# Red Alert

**A first-person multiplayer social deduction game built in S&box.**

Red Alert is an Among Us-inspired game set aboard the space station **NEXUS-7**, where crew members (Citizens) must complete tasks and identify the hidden threat (Anomaly) before it's too late. Built from scratch as a solo project using S&box's Scene system, C# scripting, and Razor UI framework.

---

## Gameplay Overview

Players are assigned one of two roles at the start of each round:

- **Citizens** - Complete tasks around the station, monitor security cameras, report dead bodies, and vote to eject the suspect during emergency meetings.
- **Anomaly** - Blend in with the crew, sabotage the station, and eliminate Citizens without getting caught.

The game follows a loop of task completion, kills, body reports, emergency meetings, and voting - all in first person with full multiplayer networking.

---

## Features

### Core Game Systems
- **Role Assignment** - Random Anomaly/Citizen assignment with role reveal UI at game start
- **Kill System** - Proximity-based kills with ragdoll death physics and dead body spawning
- **Body Reporting** - Discover and report dead bodies to trigger emergency meetings
- **Emergency Button** - Call emergency meetings from a dedicated station
- **Voting System** - Discussion phase, voting phase, vote tallying, and player ejection
- **Win Conditions** - Anomaly wins when they match Citizen count; Citizens win by ejecting all Anomalies or completing all tasks
- **Game Over Screen** - Displays winners, reveals all player roles, and returns to lobby

### Task System (6 Unique Minigames)
- **Progress Bar** - Hold interact to fill a progress bar
- **Button Sequence** - Press buttons in the correct order
- **Slider Match** - Adjust sliders to match target values
- **Collect Samples** - Click moving targets (DNA collection themed)
- **Memory Match** - Classic card-matching memory game (Redacted Files themed)
- **Wire Connect** - Match colored wires to restore power grid connections

### Anomaly Abilities
- **Purge (Blind)** - Blacks out all Citizen screens for 10 seconds, giving the Anomaly a tactical window to move or kill. 120-second cooldown, unlimited uses. Effect ends immediately on emergency meeting or game over.
- **Ability Progression UI** - Shop-style interface for ability management

### Multiplayer & Networking
- **Lobby System** - Ready terminal with player status display and configurable game countdown
- **Networked Game State** - Full state synchronization across all clients (lobby, in-game, voting, game over)
- **RPC Architecture** - Host-authoritative networking using `[Rpc.Broadcast]` and `[Rpc.Owner]` patterns
- **Game Settings Station** - In-game configurable settings (player count, timers, cooldowns)

### UI & Polish
- **Chat System** - In-game text chat with text-to-speech support, disabled during gameplay, re-enabled during meetings
- **Security Cameras** - Camera monitoring station with multi-camera switching
- **Ghost/Spectator Mode** - Dead players become invisible spectators who can observe but not interact
- **Player Nametags** - Floating nametags above players
- **Leaderboard** - Tab-triggered PlayerBoard and persistent global leaderboard via S&box Services
- **Game Info Panel** - Tabbed UI with game description, how to play guide, coming soon features, and credits
- **Ready Status Panel** - Real-time display of which players are readied up in the lobby
- **Role Reveal Splash** - Animated role reveal screen at game start
- **Meeting Result Splash** - Displays voting outcome with dramatic reveal
- **Death Overlay** - Visual feedback when killed
- **Countdown UI** - Lobby countdown timer visible to all players

### Environment
- **Sliding Doors** - Proximity-triggered automatic doors with networked sound effects
- **Task Stations** - Interactive stations placed throughout the map
- **Spawn System** - Separate lobby spawns and game area spawns with teleportation between phases

---

## Tech Stack

| Technology | Usage |
|---|---|
| **S&box** | Game engine (Source 2 based) |
| **C#** | Game logic, networking, component systems |
| **Razor** | UI panels and HUD elements |
| **SCSS** | UI styling |
| **Hammer Editor** | Level design |

---

## Project Structure

```
red-alert/
├── code/                   # All game source code
│   ├── GameManager.cs          # Core game state machine & round logic
│   ├── PlayerController.cs     # Player movement, interaction, abilities
│   ├── ReadyTerminal.cs        # Lobby ready-up system
│   ├── TaskManager.cs          # Task assignment & tracking
│   ├── TaskStation.cs          # Individual task interaction points
│   ├── TaskData.cs             # Task type definitions
│   ├── ChatSystem.cs           # Multiplayer chat with TTS
│   ├── VotingUI.razor          # Emergency meeting & voting interface
│   ├── GameInfoPanel.razor     # Tabbed game info screen
│   ├── PlayerBoard.razor       # In-game leaderboard
│   ├── GlobalLeaderboard.razor # Persistent cross-session leaderboard
│   ├── TaskListUI.razor        # Player task list HUD
│   ├── ChatPanel.razor         # Chat UI
│   ├── BlindOverlayUI.razor    # Anomaly purge blind effect
│   ├── DeathOverlayUI.razor    # Death screen
│   ├── GhostIndicator.razor    # Spectator mode indicator
│   ├── RoleRevealUI.cs         # Role assignment reveal
│   ├── SecurityCamera.cs       # Camera system
│   ├── SlidingDoor.cs          # Auto doors
│   └── ...                     # Additional UI and bridge files
├── Assets/
│   ├── Prefabs/            # Reusable game objects (dead body, player, etc.)
│   ├── UI/                 # UI asset files
│   ├── materials/default/  # Textures and materials
│   ├── models/             # 3D models
│   ├── scenes/             # S&box scene files
│   └── sounds/             # Sound effects
├── Editor/
│   ├── Assembly.cs         # Editor assembly definition
│   └── MyEditorMenu.cs     # Custom editor tools
├── ProjectSettings/
│   ├── Collision.config    # Collision layer configuration
│   ├── Input.config        # Input bindings
│   ├── Networking.config   # Network settings
│   └── Physics.config      # Physics settings
├── .editorconfig           # Code style settings
├── .gitignore
└── red_alert.sbproj        # S&box project file
```

---

## Getting Started

### Prerequisites
- [S&box](https://sbox.game) installed via Steam (free developer preview)
- S&box Editor to create/edit games
- S&box official to play

---

## Development Notes

This project was built as a learning exercise in game development, starting with zero C# and engine experience. Key technical patterns established during development:

- **Razor UI in S&box** requires bridge classes to access game code from UI components
- **RPC networking**: `[Rpc.Broadcast]` must pass parameters explicitly; nested broadcasts cause exponential duplication
- **Async patterns**: `async void` with `await GameTask.DelaySeconds()` keeps execution on the main thread
- **Leaderboards**: Use `Sandbox.Services.Leaderboards.GetFromStat()` with `await board.Refresh()`
- **S&box Razor limitations**: No inline `<span>` elements, limited CSS property support, type resolution requires bridge pattern

---

## Author

Built as a solo indie project. All game systems (networking, UI, task minigames, role mechanics, and multiplayer synchronization) were implemented from scratch.
