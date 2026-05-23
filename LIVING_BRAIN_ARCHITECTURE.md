# Living Entity Brain Architecture

## The Core Philosophy (The Rockstar Approach)
Instead of a worker looping "Chop Wood -> Walk -> Chop Wood", the worker possesses an internal state, sensory inputs, and reactive behaviors. They are 90% autonomous, reactive to their environment, and feel genuinely alive.

Our AI NPCS have both **Long and Short Term Memory** from the Generative AI (LLM) system. To strictly differentiate systems and avoid confusion, the GOAP brain's perception relies on a **`SensoryCache`** or **`SpatialAwarenessBuffer`**, explicitly avoiding the word "Memory". All GOAP files live cleanly in the `Services.WorkBrain` namespace.

---

## Architectural Blueprint

### A. The Sensory System (Perception)
A lightweight spatial awareness system to scan the farm without lagging the main game thread.
- **Vision:** Periodically scans the `GameLocation` (radius e.g., 15 tiles) categorizing data from `terrainFeatures`, `objects`, `resourceClumps`, and `characters`.
- **SensoryCache:** Stores identified entities (`PerceivedEntity`) briefly to inform the GOAP planner.
- **Environment Awareness:** Detects rain, time of day, and season changes.

### B. Internal State & Needs Engine (Motivations)
An internal state machine managing the worker's personal motivations.
- **Needs:** `Energy`, `Morale`, `Rest`.
- **Dynamic Override:** If `Energy` drops below 20%, the "Need to Rest" utility score overrides the "Desire to Work". The worker pauses their task, walks to a resting spot, and plays an animation (e.g., sitting, drinking water).

### C. The Reaction & Interrupt System (Emergent Behavior)
An Event-Driven Interrupt system allowing immediate but temporary deviations from the current goal.
- If an urgent stimulus occurs (e.g., Slime attacks, player interacts, bomb detonates), the brain pushes an **Interrupt State**.
- The current GOAP Action is safely paused.
- The worker executes the Reaction (Flee, Greet, React).
- Once the stimulus is resolved, the interrupt state is popped, and the worker seamlessly resumes their previous task.

### D. The Player Override (The 10%)
A secure injection channel for direct player commands.
- **Direct Command:** The player forces a priority target (e.g., "Chop this specific tree").
- Overrides the autonomous Utility loop without breaking pathfinding or internal animations, inserting the command at the top of the GOAP stack.

---

## Structural Interfaces

- **`ILivingBrain`**: The central orchestrator running on the `UpdateTicked` cycle asynchronously.
- **`ISensorySystem`**: The environment scanner and `SensoryCache` manager.
- **`IInternalState`**: Tracks stats (Energy, Morale) and determines Need priorities.
- **`IReactionSystem`**: Manages the event-driven interrupt stack.
- **`IGoapPlanner`**: The Goal-Oriented Action Planner that assigns tasks based on sensory data and needs.
