# Turbo Tag

A physics-based, first-person multiplayer party game built in Unity. Players use a variety of movement abilities and combos to hide, chase, and outsmart each other across dynamic environments.

---

## 🎮 Game Overview

Turbo Tag's primary game mode is a twist on **Tag / Hide and Seek**. Players take turns as the Seeker, while others hide or flee. When the Seeker tags a player, that player joins the Seeker's team. The last player caught — the one who hid the longest — wins the round.

The game is built around two core pillars:
- **Physics-based fun** — expressive movement, ability combos, and goofy interactions
- **Deep customization** — nearly everything can be tuned, from ability cooldowns and strength to game mode rules and player stats

---

## 🏃 Movement System

Players have a robust set of core movement abilities available at all times:

- Walking, Sprinting, Crouching, Prone / Sliding
- Wall Running, Climbing, Ledge Grabbing / Shimmying
- Dashing, Swinging, Aerial Control

---

## ⚡ Ability System

At the start of each round, every player selects **4 abilities** across the following slots:

| Slot | Type |
|------|------|
| 1 | Basic Ability |
| 2 | Quick Ability |
| 3 | Throw Ability |
| 4 | Trap Ability |

Abilities can aid navigation, create chaos, or slow down opponents. All ability values (cooldown, strength, duration) are fully configurable.

---

## 🛠️ Tech Stack

| Tool | Purpose |
|------|---------|
| Unity | Game engine |
| C# | Scripting language |
| Photon PUN | Multiplayer networking |
| Git / GitHub | Version control |

---

## 📁 Project Structure

```
Assets/
├── Scripts/
│   ├── Player/         # Player controller, movement, input, animation
│   ├── Abilities/      # Individual ability classes
│   ├── Audio/          # AudioManager and player audio
│   ├── UI/             # Menus, HUD, lobby screens
│   └── Managers/       # Game mode logic, scene management
├── Scenes/             # Loading, Menu, Lobby, PreGame, Levels
├── Prefabs/
├── Audio/
└── Art/
```

---

## 🚧 Current Status

This project is in active early development by a solo developer. Systems currently implemented:

- [x] Core player physics and movement
- [x] Wall running, climbing, ledge grabbing
- [x] Multiplayer networking via Photon PUN
- [x] Scene management (Loading → Menu → Lobby → PreGame → Level)
- [x] Ability slot system (selection and loading)
- [x] Audio manager with scene-aware music and crossfading
- [ ] Sound effects
- [ ] Complete ability roster
- [ ] Game mode logic
- [ ] UI / HUD polish
- [ ] Win condition and scoring

---

## 👤 Developer

**Justin** — Solo developer, CS graduate, first commercial Unity project.
