# Plan de Implementación: Sistema de Memoria Episódica con Fechas

## Objetivo
Dotar a los NPCs de una memoria a largo plazo que no solo almacene qué ocurrió, sino **cuándo** ocurrió. Esto evita la omnisciencia instantánea y permite a la IA calcular cuánto tiempo ha pasado desde un evento (ej. "Me regalaste esto ayer" vs "Hace años que no me visitas").

## User Review Required

> [!IMPORTANT]
> **Persistencia de la Memoria**
> Actualmente, `Hippocampus.cs` guarda memorias temporales en la sesión (ej. intentos de caminata fallidos), pero desaparecen al cerrar el juego. 
> Para que Emily recuerde a su loro de hace 3 años, necesitamos guardar esto permanentemente.
> **Propuesta:** Crear un archivo `longterm_memory_[NPC].json` en la carpeta del mod que almacene una lista de cadenas de texto con estampas de tiempo, y que se cargue cada vez que inicias Stardew Valley.

> [!WARNING]
> **Gestión del Tamaño del Contexto (Límite de Tokens)**
> Si guardamos cada cosita que hace el jugador durante 10 años en el juego, la IA colapsará por límite de tokens.
> **Propuesta:** La IA leerá las últimas 15 memorias más recientes de forma automática. Memorias más antiguas serán inyectadas *solo* si el jugador menciona palabras clave relevantes (Retrieval-Augmented Generation / KRS). ¿Estás de acuerdo con este límite inicial?

## Proposed Changes

### Brain Architecture (Hippocampus)

#### [MODIFY] [Hippocampus.cs](file:///c:/Users/Trabajo/Desktop/Trabajo/Proyectos%20AI/Proyecto%20X/src/Brain/Hippocampus.cs)
- Añadir la inyección automática de fechas en `SavePlayerMemory`: `[Día X de Y, Año Z] Mensaje`.
- Crear la funcionalidad de serialización a disco: `LoadLongTermMemories()` y `SaveLongTermMemories()`.
- Unir memorias pendientes con la memoria a largo plazo.

#### [NEW] [LongTermMemory.cs](file:///c:/Users/Trabajo/Desktop/Trabajo/Proyectos%20AI/Proyecto%20X/src/Models/LongTermMemory.cs)
- Un modelo de datos simple para serializar y deserializar a JSON:
  - `public string Timestamp { get; set; }`
  - `public string Content { get; set; }`
  - `public string Category { get; set; }` (ej. "Relationship", "Event", "Failure")

#### [MODIFY] [Consciousness.cs](file:///c:/Users/Trabajo/Desktop/Trabajo/Proyectos%20AI/Proyecto%20X/src/Brain/Consciousness.cs)
- En el bloque de inyección dinámica, obtener del `Hippocampus` no solo los `_pendingMemories` (cosas que pasaron hace 5 segundos), sino el bloque consolidado de `LongTermMemories` recientes.

## Verification Plan
1. **Prueba de Inyección Manual:** Caminar hacia Emily y desencadenar un fallo de ruta (ej. pedirle que vaya a un lugar bloqueado).
2. **Prueba de Persistencia:** Cerrar el juego, volver a abrirlo, y verificar si Emily recuerda que ayer le pediste ir a un lugar y no pudo.
3. **Prueba Analítica:** Preguntarle a Emily "¿Cuándo fue la última vez que intentaste caminar y te bloquearon?". La IA debería ser capaz de leer la fecha y calcular basándose en el "Estado del Mundo" que se le inyecta al inicio del prompt.
