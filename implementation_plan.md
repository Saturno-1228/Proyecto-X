# Plan de Implementación: Motor de IA de Utilidad (Rockstar-Style AI)

## El Paradigma "Rockstar" (Red Dead Redemption 2 / GTA)
Para lograr un nivel de vida orgánico y no lineal como el de Rockstar, un Árbol de Comportamiento tradicional (Behavior Tree) o un horario fijo (Schedule) es insuficiente porque son rígidos. 

Lo que utilizan estos juegos de alto calibre es una combinación de **Utility AI (IA basada en Utilidad)** acoplada a un **Sistema de Percepción y Necesidades**. En este modelo, el NPC no sigue un guion; en su lugar, evalúa constantemente su entorno y sus necesidades internas, asignando una "puntuación" a cada acción posible. La acción con mayor puntuación gana en tiempo real.

## Arquitectura del "Radiant Utility Engine"

### 1. Sistema Sensorial (Perception)
El NPC ya no está ciego. Cada segundo, un escáner revisa su entorno cercano (ej. un radio de 10 tiles).
* **Detectores:** `PlayerRunningDetector`, `WeatherDetector`, `SocialProximityDetector`, `ObjectDetector` (sillas, fogatas).
* **Memoria Sensorial Corta:** Si detecta lluvia, sabe que está lloviendo. Si el jugador pasa corriendo, lo registra.

### 2. Estado Interno y Necesidades (Internal State)
El NPC tiene un perfil de atributos que fluctúan con el tiempo, emulando necesidades biológicas/psicológicas.
* `Energy` (Baja con el tiempo, aumenta durmiendo/comiendo)
* `SocialNeed` (Aumenta si está solo mucho tiempo)
* `Boredom` (Aumenta si está estático)
* `Stress` (Aumenta si el clima es malo o si hay eventos caóticos)

### 3. Evaluadores de Utilidad (Utility Scorers)
Cada "Acción" que el NPC puede hacer tiene múltiples *Scorers* (Curvas matemáticas de evaluación). Por ejemplo, la acción **"Buscar Refugio"**:
* *Scorer del Clima:* Si llueve, da +100 puntos. Si está soleado, da 0.
* *Scorer de Estrés:* Si está estresado, aumenta la probabilidad de buscar confort (+20).
* **Total:** Si el score pasa de 0 a 120 de golpe porque empezó a llover, esta acción aplasta a cualquier otra y el NPC sale corriendo a un techo.

### 4. Gestor de Acciones Flexibles (Action Controller)
Acciones orgánicas modulares:
* `ActionSeekShelter`: Encuentra un techo o un edificio y camina hacia él.
* `ActionWanderAndLook`: Camina a un punto aleatorio y mira un objeto específico (una flor, un río).
* `ActionSitDown`: Busca una silla cercana y se sienta si está cansado.
* `ActionSocialize`: Si su `SocialNeed` es alta y ve a otro NPC, camina hacia él y reproduce animación de hablar.

## Excepciones Críticas (Safeguards)

> [!WARNING]
> **Festivales y Eventos Cinemáticos**
> Stardew Valley depende de scripts altamente rígidos durante los Festivales (ej. Festival del Huevo, Danza Floral) y Eventos de Corazones. Si la IA de Utilidad toma el control aquí, romperá las cinemáticas.
> **Solución:** El `RadiantEngine` debe tener una regla de exclusión global. Si `Game1.CurrentEvent != null` o `Game1.isFestival()`, el motor de Utilidad se desactiva por completo para todos los NPCs.

## Estructura de Clases (Inyección Dinámica)

```csharp
src/Services/UtilityAI/
├── RadiantAIManager.cs        // El motor principal conectado a UpdateTicked
├── Core/
│   ├── IAction.cs             // Define una acción y su lógica física
│   ├── IScorer.cs             // Devuelve un valor float basado en el contexto
│   ├── SensorySystem.cs       // Escanea el entorno del Stardew
│   └── InternalState.cs       // Variables como Energy, SocialNeed
├── Actions/
│   ├── WanderAction.cs
│   ├── SeekShelterAction.cs
│   ├── FindSeatAction.cs
│   └── InteractWithNPCAction.cs
└── Scorers/
    ├── RainScorer.cs
    ├── EnergyScorer.cs
    └── BoredomScorer.cs
```

## User Review Required

> [!IMPORTANT]
> **Consumo de Rendimiento (CPU)**
> Evaluar docenas de curvas matemáticas y escanear el entorno para 40+ NPCs es pesado. En RDR2, la IA se detiene ("duerme") si el NPC está muy lejos del jugador (Level of Detail AI).
> **Propuesta:** Solo ejecutaremos el motor de Utilidad y el escaneo sensorial para los NPCs que estén **en el mismo mapa (Location)** que el jugador, o en los adyacentes. Los NPCs lejanos usarán los horarios originales de Stardew Valley. ¿Apruebas esta optimización de "IA por Proximidad"?

## Plan de Verificación
1. **Pruebas de Clima:** Poner un NPC (ej. Marnie) al aire libre usando el mod de clima. Iniciar la lluvia. Verificar que su Score de "SeekShelter" se dispare y ella abandone su posición estática para buscar refugio.
2. **Pruebas de Aburrimiento:** Observar a un NPC estático. Tras un tiempo, su `Boredom` debería subir, forzando a la `WanderAction` a dispararse, haciendo que el NPC se mueva, interactúe con el entorno y luego se detenga.
