# QuantumVault – Scripts & UML report

Date: 30 October 2026
Branch: `MonoBuild`

## 1. What was delivered

| File | What it is |
|---|---|
| `Docs/Scripts.zip` | All 60 C# scripts from `Assets/Scripts`, with the folder structure kept (`.cs` files only, no `.meta` files). |
| `Docs/UML/ClassDiagram.drawio` | The new UML class diagram. Opens and edits in draw.io / diagrams.net (desktop, web or the VS Code extension). |
| `Docs/UML/ClassDiagram.png` | The same diagram as an image (7770 × 4848 px), for documents and slides. |
| `Docs/Report.md` | This report. |

Nothing was committed to git. The `Docs` folder sits next to `Assets`, so Unity does not import it.

## 2. How the new UML follows the original design

The new diagram keeps the look of the original: dark background, white class boxes with name / attributes / methods, hollow diamonds for "has", filled diamonds for "owns", and arrows for references.

It also keeps the original structure. **Game** is at the top. **Player** sits under it with its parts. **Tools** hang under the player. **Zone** holds Zone 1–3 and the Level. **Title Screen**, **Story** and **End Screen** are scenes, and the audio, statistics and interactable groups are still there.

Every box from the original now points to the real scripts that do that job:

| Original box | Now in the code |
|---|---|
| Game (StartGame, PauseGame, ExitGame, LoadZone, CompleteGame) | `Game «build»` node → `SceneTransition` (static `Load / Reload / Quit`), `SceneTransitionFade`, `TransitionSettings`, `SceneTransitionButton`, `SceneTransitionTrigger`, `PauseMenu` |
| Title Screen | Scene `MainMenu` → `SceneTransitionButton` (→ StartScene) |
| Story / Dialogue | Scene `StartScene` → `ActivateVoiceInstance` (voicelines) |
| Player (Move, interact, switchtool) | Prefab `Louise 1` → `Movement`, `PlayerInteractor`, `Inventory` |
| Camera | `PlayerCamera` |
| Minimap | `ToggleHUD` (H = HUD, M = map) |
| CharacterModel | `PlayerAnimatorDriver` (+ `CharacterSFX`, `PlaySfxOnStateEnter`) |
| Tools | `Inventory` + `itemType` (Flashlight, Entanglement, WaveParticle; keys 1–3) |
| Flashlight | `PlayerFlashlight`, `FlashlightPickup`, `MirrorBounceLight` |
| Entangler | `Entanglement`, `EntanglementGroup`, `EntanglementPose` |
| PhotonEmitter | `PhotonEmitter`, `SlitGate`, `WhichPathDetector`, `InterferenceWall`, `InterferenceDoorWall`, `InterferenceDoor`, `InterferencePattern`, `Band` |
| GameStatistics | `Timer`, `ZoneTimeManager`, `ResetRunTimes` (and `GUIstats` as a debug overlay) |
| EndScreen | Scene `Eindscherm` → `EndScreenTimes` + `SceneTransitionButton` (→ MainMenu) |
| Zone / Zone 1–3 | Scenes `Z1L1` (observation), `Z2L1` (entanglement), `Z3L1` (wave–particle duality) and `CreditScene` |
| Level | `CheckpointManager`, `Checkpoint`, `ResetVolume`, `ResetTargets` |
| AudioSystem | `CharacterSFX`, `PlaySfxOnStateEnter`, `MusicPauseMenu`, `ActivateVoiceInstance` |
| InteractableObject | `IInteractable` (+ `IPickup`, used by `PlayerInteractor`) |
| PhysicalObject | `ObjectPhysics` (+ `HoldOrientation`, carried by `GrabInteract`) |
| MultistateObject | `IResettable` (state saved and restored on a checkpoint reset) |

Some scripts were not in the original design. They got their own groups: **Puzzle objects** (PortalGate, PressurePlate, PropSocket, PropBarrier, WeightDrawbridge, ObservedVanish, MirrorSurface, MirrorReflection, GlassPane), **Dev tools**, **Editor tools** and **Legacy**.

### Reading the diagram

- **Solid box:** a C# script. **Dashed box:** a Unity scene, prefab or the build (not a script).
- **Header line above a name:** says what kind of type it is. Examples: `«interface»`, `«static»`, `«struct»`, `«enumeration»`, `implements IResettable`.
- **Member markers:** `+` public, `-` private Inspector field, `~` internal. Underlined members are static.
- A few long cross-links are written inside the methods instead of drawn as lines, to keep the diagram readable. For example: `ResetToCheckpoint() → CheckpointManager`, `IsSeen() (PlayerCamera view)`, and DevConsole's commands.

## 3. Game flow (build order)

`MainMenu` → `StartScene` → `Z1L1` → `Z2L1` → `Z3L1` → `CreditScene` → `Eindscherm` → back to `MainMenu`

| From | To | Done by |
|---|---|---|
| MainMenu | StartScene | `SceneTransitionButton` |
| StartScene | Z1L1 | `SceneTransitionTrigger` |
| Z1L1 | Z2L1 | `PortalGate` |
| Z2L1 | Z3L1 | `PortalGate` |
| Z3L1 | CreditScene | `PortalGate` |
| CreditScene | Eindscherm | `PortalGate` |
| Eindscherm | MainMenu | `SceneTransitionButton` |

## 4. All scripts by group

**Player** (`Assets/Prefabs/Player/Louise 1.prefab`)
- `Movement` – walking, jumping (Simple / Source mode), fly no-clip, teleport.
- `PlayerCamera` – mouse look; owns yaw and pitch.
- `PlayerInteractor` – finds the `IInteractable` under the crosshair, shows the prompt, calls `Interact`.
- `GrabInteract` – picks up, carries, rotates and throws `ObjectPhysics` props.
- `Inventory` – selects the tool with keys 1–3.
- `PlayerAnimatorDriver` – feeds movement to the Animator and turns the head toward the view.
- `CharacterSFX` / `PlaySfxOnStateEnter` – footsteps, run-start and jump sounds.
- `WhichPathDetector` – the player's eyes as a which-path detector for slit gates.
- `ToggleHUD` – hides the HUD and the map.

**Tools**
- `PlayerFlashlight` – the held flashlight (built at runtime).
- `FlashlightPickup` – the pickup that gives it.
- `MirrorBounceLight` – reflects the beam off mirrors.
- `Entanglement` / `EntanglementGroup` / `EntanglementPose` – linked props that move together.
- `PhotonEmitter`, `SlitGate`, `InterferencePattern`, `Band`, `InterferenceWall`, `InterferenceDoorWall`, `InterferenceDoor` – the double-slit puzzle.

**Interaction / physics**
- `IInteractable`, `IPickup` – the interaction interfaces.
- `ObjectPhysics`, `HoldOrientation` – grabbable props and how they are held.

**Level / checkpoints**
- `CheckpointManager` – saves level state when a checkpoint is reached; restores it on reset.
- `Checkpoint`, `ResetVolume`, `ResetTargets`.
- `IResettable` – implemented by PortalGate, PressurePlate, PropSocket, ObservedVanish, PhotonEmitter, InterferenceDoorWall and InterferenceDoor.

**Puzzle objects**
- `PortalGate` – opens when its pressure plates are held; loads the next scene.
- `PressurePlate`, `PropSocket`, `PropBarrier`, `WeightDrawbridge`.
- `ObservedVanish` – vanishes while it is being looked at.
- `MirrorSurface`, `MirrorReflection`, `GlassPane`.

**Scene flow / UI**
- `SceneTransition`, `SceneTransitionFade`, `TransitionSettings`, `SceneTransitionButton`, `SceneTransitionTrigger`.
- `PauseMenu`, `MusicPauseMenu`.

**Story / audio**
- `ActivateVoiceInstance` – one voiceline per trigger; a new one stops the one already playing.

**Statistics**
- `Timer`, `ZoneTimeManager`, `EndScreenTimes`, `ResetRunTimes`.

**Dev tools**
- `DevConsole` + `DevCheats` – backquote console and cheats.
- `GUIstats` – F3 speed overlay.

**Editor tools** (`Editor` folders, not in builds)
- `CheckpointTools`, `PuzzleSetupTools`, `RopeBridgeWalkwayBaker`.

**Legacy / unused in the build**
- Only in `TestLevel 1`: `Portal`, `PortalRequirement`, `BoxPressurePlate`.
- In no scene or prefab: `PortalSceneTransition`, `ItemPickup`.
- Not used anywhere: `ItemGrabbable`.
- `ItemSO` is only on item prefabs that no scene uses.
- `InterferenceWall` is only in a prefab that no build scene uses.

## 5. Things found while reading the scripts

1. **The end screen will always show `--:--.--` for all three zones.**
   - `ZoneTimeManager.SaveCurrentTime` is only called by `PortalSceneTransition`, and that script is in no scene.
   - The zones change scene through `PortalGate` and `SceneTransitionTrigger`, and neither of them saves a time.
   - `ResetRunTimes` sits in StartScene, Z1L1, Z2L1, Z3L1 and CreditScene. It deletes every saved time each time one of those scenes loads.
   - A possible fix:
     - Call `ZoneTimeManager.SaveCurrentTime("Z1L1")` (etc.) from the zone exits (`PortalGate` / `SceneTransitionTrigger`).
     - Keep `ResetRunTimes` only in `MainMenu` or `StartScene`.
   - This has not been changed.
2. **`ItemSO` has `[CreateAssetMenu]` but derives from `MonoBehaviour`.** Unity ignores that attribute unless the class is a `ScriptableObject`, so the "Items" create-menu entry does nothing. Not changed.
3. **`Inventory.cs` starts with an unused `using NUnit.Framework;`.** It is safe to remove. Unused NUnit imports in runtime scripts are a common cause of player-build errors (not verified for this project).
4. **The scenes use a different grab key than the code.** The scenes bind grab to **F**, while `GrabInteract` defaults to **E** in code. For that reason the diagram shows no default value for the grab key.

## 6. Editing or re-exporting the diagram

- Open `Docs/UML/ClassDiagram.drawio` in draw.io (desktop app or app.diagrams.net).
- Every class is a normal draw.io UML class shape, so attributes and methods can be edited in place.
- Lines keep their connections when you move boxes.
- To export: **File → Export as → PNG / SVG / PDF**. Keep the dark background, or turn on "Transparent background" for light documents.
