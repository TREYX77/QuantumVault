# Wave Duality — Level Setup Guide (made by claudy waudy)

How to add the double-slit mechanic to a level.

A **Photon Emitter** fires a laser at a **Slit Gate**. Every **Interference Wall** linked to that gate opens
doorways where its interference pattern is bright.

The **player is the observer**:
- **Not looking at the slits:** the wall shows the wave pattern, with several doorways (5 on an 18 m wall).
- **Looking at the slits:** the pattern collapses to **2 doorways in the middle**.

Where the player looks decides which way they can go.

---

## The parts

| Piece | Where | What it does |
|---|---|---|
| Player | `Assets/Prefabs/Player/Player-WaveDuality.prefab` | The player, set up as the observer: its **Which Path Detector** watches through its camera. |
| Photon Emitter | `Assets/Prefabs/WaveDuality/Photon Emitter.prefab` | A laser on a carryable box. It lights a gate only if its straight beam actually hits the gate. |
| Slit Gate | `Assets/Prefabs/WaveDuality/Slit Gate.prefab` | The two slits. It holds the physics (slit spacing, wavelength) and the lit/observed state. |
| Interference Wall | `Assets/Prefabs/WaveDuality/Interference Wall.prefab` | The doorway wall. You can also add the component to any box-shaped wall. |
| Scripts | `Assets/Scripts/WaveDuality/` | `PhotonEmitter`, `SlitGate`, `InterferenceWall`, `WhichPathDetector`, `InterferencePattern` (the maths). |
| Materials | `Assets/Materials/WaveDuality/` | `WallSolid`, `SlitPanel`, `PatternDisplay`, `EmitterLens` |

The prefab is named `Player-WaveDuality` because a file name can't contain a `/`.

## Controls

| Key | Action |
|---|---|
| **Q** | Laser on/off, for **every** emitter in the level, from anywhere |
| **F** | Grab / release (the emitter can be carried) |
| **R** (hold) | Rotate the carried object |
| **F3** | Debug overlay, only if the scene has the `SceneManager` object with `GUIstats` |

---

## Setup, step by step

This example builds one room with the floor top at **y = 0**, so the numbers can be copied as they are.

```
Top-down view (+Z is up the page)

  z +12        [ EXIT ]
                  │ lane
  z  +6   ████ ▒▒ █|▒▒▒|█ ▒▒ ████   ← Interference Wall, 18 m, as wide as the room
  z   0          [gate]             ← Slit Gate, facing +Z
  z  -4          [emit]             ← Photon Emitter, aimed at the gate
  z  -8          (you)              ← Player
```

### 1. Create the scene
Choose one:
- File → New Scene, then add a Directional Light and a Global Volume, **or**
- duplicate an existing level (Ctrl+D on the scene in the Project window) and delete what you don't need.

### 2. Add the player
Drag in `Player-WaveDuality` at **(0, 1, −8)**. Its pivot is the middle of the 2 m capsule, so y = 1 stands it on
the floor.

Remove any other player or camera tagged `MainCamera` from the scene.

**Using a different player (for example from another branch)?** Add the observer to it instead:
1. Add Component → Wave Duality → **Which Path Detector**.
2. Drag the player's camera into **View Camera**.
3. Untick **Show Sight Line**.

Without this, looking at the slits does nothing.

### 3. Build the room
Floor: a Cube at **(0, −0.25, 0)**, scale **(18, 0.5, 30)**.

The doorway wall must span **the whole room**, or the player can walk around it.

### 4. Add the Photon Emitter
Drag in `Photon Emitter` at **(0, 0.6, −4)**, rotation **(0, 0, 0)**. It fires along its blue Z arrow.
- Select it: a **green line** shows the laser path, with a sphere where it hits.
- **Start On:** tick it if the laser should fire as soon as the level loads.
- **Toggle Key:** Q toggles every emitter at once. For an emitter that shouldn't respond to Q, set it to **None**.
  Scripts can still switch it through `IsOn`.

### 5. Add the Slit Gate
Drag in `Slit Gate` at **(0, 1.5, 0)**, facing **+Z** (toward the wall).
- It always shows as a faint **yellow box** in the Scene view. Its panels are only built in Play mode.
- Select the emitter again and check that the **green line ends on the yellow box**. The gate is 1.8 m tall around
  its pivot, so the laser's height must fall inside that range.
- Leave the slit settings alone unless you mean to change them. They affect **every wall** linked to this gate.

### 6. Add the Interference Wall
Choose one:
- Drag in the `Interference Wall` prefab, **or**
- turn an existing wall into one: select any cube wall and use Add Component → Wave Duality → **Interference Wall**.
  In Play mode it hides its own cube and builds the doorway wall in the same footprint, with the same material.

Then set it up:
- **Position:** **(0, 2, 6)**. The wall is 4 m tall, so y = 2 puts its bottom on the floor.
- **Width:** set it with **Scale X** (see the table below).
- **Gate:** drag the Slit Gate from the Hierarchy into the wall's **Gate** field. The gate can be anywhere on the map,
  and one gate can feed several walls.
- **Pattern Distance:** keep **7**.

### 7. Check the Scene-view preview
Select the wall. The Scene view's **Gizmos** toggle must be on. You'll see:
- a **yellow line** to its gate
- **cyan boxes** for the doorways when the player is **not** looking at the slits
- **orange boxes** for the doorways when the player **is** looking at them
- a label such as `Slit Gate · 7 m, 5 doorways, 2 when observed`

It updates as you edit, without pressing Play. Make sure every doorway sits over floor.

### 8. Play-test
1. Press Play, then **Q**.
2. Look at the slits: the wall switches to the 2 middle doorways, and the strip above the wall shows two bands.
3. Look away: the wave doorways come back.
4. Walk through each doorway to check you fit.

### 9. Add the scene to the build
File → Build Profiles → Scene List → **Add Open Scenes**, so menus and scene transitions can load it.

### More puzzles in one level
Repeat steps 4–6 for each room. **Always** drag each wall's own gate into its Gate field. An empty field uses the
nearest gate facing the wall, which could be the neighbouring room's.

---

## Designing the puzzle

**The observation rule.** The player observes whenever any part of a slit is on screen with nothing solid in
between, **at any distance**. The collapsed state holds for 0.35 s after the slits leave the screen.

**Doorways at Pattern Distance 7** (default gate settings):

| Wall width (Scale X) | Not looking | Looking at the slits |
|---|---|---|
| 6 m | 1 | 2 |
| 8–12 m | 3 | 2 |
| 14 m or more | 5 | 2 |

An 18 m wall gives these exact positions (metres from the wall's centre):
- **Not looking:** [−7.69…−4.94] [−3.39…−1.62] [−0.78…0.78] [1.62…3.39] [4.94…7.69]
- **Looking:** [−2.33…−0.35] [0.35…2.33]

At 7 m no doorway is open wide enough in both states for the 1 m player, so the choice of where to look always
matters.

**Example puzzle (matches the sketch above).** Put two short divider walls behind the wall, running along Z, at
**x = −1.2** and **x = +1.2**: scale (0.2, 4, 6), position z = 9.
- **Not looking:** the dividers meet solid parts of the wall, so the **centre** doorway leads only into the middle
  lane. Put the exit at the end of it.
- **Looking:** each divider splits one of the 2 middle doorways. The part that opens into the middle lane is only
  0.75 m wide, too narrow for the 1 m player, so these doorways only lead into the side lanes.

The player has to cross **without** looking back at the slits.

**Ideas:**
- **Where the gate stands is the main design tool.** The wall's layout never depends on where the gate is, only on
  Pattern Distance. So place the gate to control *when the player sees it*:
  - beside the route: hard not to observe
  - behind the wall: observing becomes a deliberate choice (walk backwards looking at it)
- **Blocking the view:** anything with a collider between the player and the slits blocks observation, including a
  crate the player carries.
- **Blocking the laser:** anything in the beam cuts it off, the player included. With no light, the wall reseals after
  0.5 s. The emitter can be grabbed and re-aimed.
- **Per-wall tuning:** tune **Pattern Distance** on a wall, not the shared gate, when one room should play differently.

---

## Settings reference

### Interference Wall
| Field | Meaning |
|---|---|
| **Gate** | The Slit Gate it shows the pattern of. Empty: the nearest gate facing the wall. |
| **Pattern Distance** | Fixes the layout as if the gate stood this far away. Higher gives fewer, wider doorways. **0** uses the real distance, so moving the gate changes the doorways. |
| Samples | How finely the pattern is sampled (256). |
| Brightness Threshold | Where the pattern counts as bright enough to open (0.25). Lower gives more, wider gaps. |
| Min Opening Width | Narrower gaps are filled back in (1.4 m; the player is 1 m wide). |
| Solid Material | Optional. Empty: uses the wall's own material. |
| Show Pattern / Pattern Material / Height / Gap | The glowing strip above the wall that shows the pattern. Untick Show Pattern under low ceilings. |

Size comes from the wall's **Box Collider × scale**, so scale it like any other wall.

### Slit Gate (shared by every wall linked to it)
| Field | Meaning |
|---|---|
| Slit Count / Separation / Width / Wavelength | The physics. Defaults: 2 slits, 0.9 m apart, 0.2 m wide, wavelength 0.3. |
| Panel Width / Height / Thickness / Material | The barrier the slits are cut into (2.4 × 1.8 × 0.15). The laser must hit this box. |
| Observation Hold Time | How long "observed" lasts after the slits leave the screen (0.35 s). |
| Illumination Hold Time | How long "lit" lasts after the laser leaves (0.5 s). |
| Gaze Also Collapses | Off. An older, rougher rule; the player's detector already does this. |

### Photon Emitter
| Field | Meaning |
|---|---|
| Muzzle | Where the beam starts. On your own model, add a child aimed down the barrel (blue Z). |
| Toggle Key / Toggle Mode / Start On | Q toggles every emitter. None: only scripts can switch it, through `IsOn`. |
| Range / Beam Mask | How far the laser reaches, and what stops it. |

### Which Path Detector (on the player)
| Field | Meaning |
|---|---|
| **View Camera** | The player's camera. With it set, "observing" means the slits are on screen and not blocked. |
| Blockers | Layers that block the view. To make a see-through window, put it on a layer left out of this mask. |
| Show Sight Line | Off on the player. |

---

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Wall stays solid with the laser on | The laser misses the gate (check the green line), or the Gate field is empty and no gate faces the wall. The console then says "…has no slit gate". |
| Wall is always on the 2 middle doorways | A gate is visible on screen somewhere, even far away. Hide it behind something. |
| A cyan box in the preview doesn't open | It is narrower than Min Opening Width. Raise Pattern Distance or widen the wall. |
| Looking at the slits does nothing | The player has no Which Path Detector, or its View Camera is empty (see step 2). |
| Q also does something else | Another script uses Q. Check every `Key` field in the scene before binding keys. |
| Strip above the wall pokes through the ceiling | Untick Show Pattern, or lower Pattern Gap. |

---

## How it works (for programmers)

**Each frame:**
- `PhotonEmitter` raycasts straight out of its muzzle. If it hits a gate, it calls `SlitGate.Illuminate()`.
- `WhichPathDetector` checks whether the gate's slits are visible to the camera. If so, it calls `SlitGate.Observe()`.
- Both states are time holds, so a one-frame gap doesn't flicker the wall.

**Walls read their gate; the gate never tracks its walls.**
- Each `InterferenceWall` reads `IsIlluminated` / `IsObserved` from its gate every frame.
- It rebuilds only when that state changes, asking the gate for `SamplePattern(...)`.
- One gate can therefore feed any number of walls.

**In Play mode the wall replaces itself:**
- It hides its own renderer and collider.
- It builds slabs and the strip under a `Generated` child whose scale cancels the wall's, so pieces are laid out in
  metres.
- Disabling the component removes `Generated` and restores the original wall.

**The maths lives in `InterferencePattern`:**
- Unobserved: the wave pattern, a single-slit envelope × cos² fringes.
- Observed: the particle pattern, two Gaussian bands.
- Doorway edges are placed symmetrically, so a symmetric pattern always gives mirrored doorways.

## Reference level

`Assets/Scenes/TestLevel 1.unity` has a working rig. Its wall's Pattern Distance is **6.04 on purpose**: it keeps
that room's approved layout, and at 7 m the left doorway would fall off the floor's edge. Use **7** for new walls.
