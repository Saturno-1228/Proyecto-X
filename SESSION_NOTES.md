# Registro de Sesión: Living Companions Valley (Proyecto X)

Este documento contiene el historial detallado de nuestra sesión de desarrollo (y las sesiones previas), las decisiones arquitectónicas tomadas, los errores detectados del proyecto anterior y las soluciones definitivas que implementamos.

## 🚀 Resumen del Progreso

Logramos migrar exitosamente el mod desde una arquitectura acoplada y saturada (el antiguo proyecto que incluía dependencias pesadas como SQLite y Vosk local) a una arquitectura limpia, modular y altamente eficiente en `Proyecto X`. La interfaz gráfica pasó de ser un prototipo a un clon "Pixel-Perfect" de la experiencia nativa de Stardew Valley.

### Fases Completadas:
1. **Fase 1: Motor de Contexto (Topic Router)**
2. **Fase 2: Optimización de IA (Venice API)**
3. **Fase 3: Cerebro Orgánico (Memoria)**
4. **Fase 4: Integración UI, Emociones y Límites de Contexto**
5. **Fase 5: Motor de Conocimiento Dinámico (Topic-Centric KRS)**
6. **Fase 6: Estabilidad, Filtros de Entidad y Actuación Dinámica (Keyframes)**

---

## 🧠 Decisiones y Soluciones Clave (Histórico Completo)

### 1. Arquitectura Dual-Model (Velocidad vs Razonamiento)
**Problema:** Usar un modelo potente (como GLM-5) para hablar en tiempo real con el NPC generaba demasiada latencia, rompiendo la inmersión en Stardew Valley.
**Solución Detallada:** - **Canal de Charla (`minimax-m25` u otros ultra rápidos):** Implementamos el modelo rápido con el parámetro `reasoning: { enabled: false }` para obtener respuestas instántaneas en la UI del juego.
- **Canal de Sueño/Consolidación (`zai-org-glm-5`):** Reservamos el modelo pesado para ejecutarse *en segundo plano* cuando la charla termina. Este modelo analiza el chat, deduce *insights* del jugador y extrae recuerdos estructurados mediante un estricto `<thinking_protocol>`. Se aumentaron los tokens y tiempos de espera (`Timeouts` a 120s) para asegurar que procese todo.

### 2. El Cerebro Orgánico (Curva de Ebbinghaus y El Limbo)
**Problema:** Si el NPC recuerda todo para siempre, el prompt (contexto) se satura, excediendo el límite de tokens y elevando los costos de la API por las nubes.
**Solución Detallada:**
- Creamos una red de memoria basada en la curva del olvido. 
- Cada día (al dispararse el evento `DayStarted`), las memorias episódicas pierden `-0.1` de fuerza (tardan 10 días en olvidarse).
- Si llegan a `< 0.2`, no se borran, pasan al **Limbo** (`ForgottenMemories`). No gastan tokens en charlas diarias, pero el `TopicRouter` puede escanearlas si el jugador reclama que el NPC ha olvidado algo, permitiéndole al NPC pedir disculpas y resucitar la memoria a fuerza `1.0`.

### 3. Persistencia Nativa y Cero Bases de Datos
**Problema:** Depender de LiteDB o SQLite generaba conflictos de dependencias en .NET 6 y problemas de permisos de escritura en diferentes Sistemas Operativos.
**Solución Detallada:** Abandonamos las bases de datos externas por completo. Ahora todo el `NpcMemoryNetwork` se inyecta directamente dentro del archivo de guardado de la granja del jugador utilizando el API nativa de SMAPI: `_helper.Data.WriteSaveData`. Si el jugador borra la granja, se borra la memoria. Es totalmente Plug-and-Play.

### 4. Congelación Temporal del NPC (`freezeMotion`)
**Problema:** Usar `npc.movementPause` o cambiar la velocidad a 0 corrompía la rutina (*Schedule*) del NPC a largo plazo. Si se congelaban mientras estaban sentados o bebiendo café, sus *sprites* se buggeaban.
**Solución Detallada:**
- Comprobamos `npc.CurrentDialogue.Count == 0` antes de interrumpir para no pisar eventos del juego base.
- Usamos el sistema de Reflexión de SMAPI para inyectar directamente la variable protegida del motor del juego: `_helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(true)`.

### 5. Archivo de Configuración Dinámico (API Key)
**Problema:** Las credenciales como la `VeniceApiKey` nunca deben estar incrustadas (*hardcodeadas*) en el código por seguridad y distribución.
**Solución Detallada:** - Implementamos una clase `ModConfig.cs`. Al ejecutar el mod, SMAPI crea automáticamente un archivo `config.json` en la carpeta del mod. El usuario solo debe abrir ese archivo y pegar su llave de Venice.

### 6. Reconstrucción "Pixel-Perfect" de la UI y Chat Dinámico
**Problema:** La interfaz híbrida anterior era un prototipo funcional pero estéticamente no igualaba a Stardew Valley. Además, el jugador no podía ver textos largos.
**Solución Detallada:**
- **Inyección del Motor Gráfico Nativo:** Desechamos nuestras matemáticas iniciales y reescribimos `AiDialogueMenu.draw` copiando el código fuente descompilado de ConcernedApe. Usamos `Game1.menuTexture` (recorte `0, 256, 60, 60`), renderizando a profundidades de capa exactas (`0.8f` y `0.88f`) para que todo luzca indistinguible del juego original.
- **Barra de Chat Multilínea (Auto-Scroll):** Mapeamos un "TextBox Invisible" en coordenadas negativas que intercepta todo el input nativamente. Usamos `ParseTextIntoLines` para crear un Word-Wrap matemático, haciendo que la caja del jugador *crezca hacia arriba* (hasta 3 renglones) y active un sistema de scroll interno.

### 7. Dirección Escénica (Sistema de Emociones Dinámico) y Cero Emojis
**Problema:** La IA no expresaba físicamente sus emociones en los retratos y usaba Emojis de texto (`:)`, `🐔`) que rompían la inmersión.
**Solución Detallada:**
- **Reglas Absolutas:** Se inyectaron comandos drásticos en `ContextBuilderService.cs` que prohíben totalmente los Emojis.
- **El Prompt Numérico:** Se le enseñó a la IA a iniciar cada respuesta obligatoriamente con un código entre corchetes (ej. `[3]` para Feliz, `[1]` para Enojado).
- **Intercepción Limpia (Regex):** En `InteractionManager.cs`, interceptamos ese número (ej. `[X]`), se lo quitamos al string, y se lo inyectamos a una variable personalizada (`AiDialogueMenu.CurrentEmotion`). Ese número altera el `Game1.getSourceRectForStandardTileSheet`, cambiando instantáneamente la cara del retrato a la emoción seleccionada.

### 8. Preservación Cognitiva y Estabilidad de API (Context Capping)
**Problema:** Tras una charla larga, el NPC dejaba de responder o las frases se cortaban a la mitad.
**Solución Detallada:**
- **Guillotina de Tokens Removida:** Se aumentó el `MaxTokens` del modelo rápido de chat de 150 a 500 en `VeniceApiService.cs` para evitar truncamientos de oraciones.
- **Torniquete Cognitivo:** Añadimos `.TakeLast(10)` en `InteractionManager.cs` al historial temporal. La API solo recibe los últimos 5 turnos de diálogo, evitando ahogar la ventana de contexto. El recuerdo a largo plazo sigue protegido por el modelo GLM-5 en segundo plano.

### 9. Motor de Conocimiento Dinámico (Arquitectura Hiper-Granular)
**Problema:** Almacenar toda la información del mundo en el System Prompt base saturaba la ventana de contexto y los costos. Mezclar la identidad estática del NPC con lo que opina de todos los demás en un solo archivo XML (`Marnie.xml`) creaba documentos gigantescos, propensos a errores y difíciles de mantener.
**Solución Detallada:**
- **Diseño Hiper-Granular (Carpetas por NPC):** Creamos un *Knowledge Retrieval System (KRS)* donde cada personaje tiene su propio directorio (`Assets/Knowledge/Marnie/`).
- **Archivos Individuales por Relación/Tema:** Dentro de esa carpeta, existen subcarpetas (`Relationships/`, `Domain/`) con archivos ultra específicos y separados (ej. `marnie_shane.xml`, `marnie_tienda.xml`). Esto otorga a los desarrolladores espacio infinito para expandir el "cerebro" y el lore del NPC sin ensuciar su identidad base.
- **Escaneo y Emparejamiento en Tiempo Real:** El `TopicRouterService.cs` carga estos pequeños XML en memoria. Cuando el jugador escribe, el Router busca coincidencias de *Keywords* (ej. "Shane", "sobrino", "cerveza") e inyecta EXCLUSIVAMENTE el archivo correspondiente en la sección de Lore Dinámico del prompt de Minimax. 
- **Consciencia Espacial (Bonus):** Expandimos `EnvironmentState` para leer el objeto que el jugador sostiene en sus manos (`Game1.player.ActiveObject`) y enviarlo al contexto, mejorando drásticamente el realismo.

### 10. Seguridad, Thread-Safety y Limpieza de Repositorio
**Problema:** Riesgo de filtrar la API Key de Venice y cierres del juego por peticiones asíncronas fallidas.
**Solución Detallada:**
- Creación de un `.gitignore` robusto que excluye `config.json` y binarios.
- Inyección de bloques `try/catch` en el hilo secundario de `ConsolidateMemoriesAsync` (GLM-5) para proteger el hilo principal de Stardew Valley contra Timeouts o caídas del servidor.
- Mejora del Regex a `@"^\s*\[(\d+)\]\s*"` para hacer el sistema a prueba de fallos si la IA devuelve espacios en blanco invisibles.

### 11. Optimización del Caché Nivel Dios ("El Sándwich")
**Problema:** Al cambiar de tema (ej. pasar de hablar de Shane a la Tienda), el `TopicRouter` alteraba el System Prompt. Esto reseteaba la memoria caché de Minimax, cobrando el 100% de los tokens nuevamente.
**Solución Detallada:**
- Reordenamos la inyección del contexto en 3 capas estratégicas: **1. Prompt Estático Puro (XML base)** -> **2. Historial de Chat** -> **3. Contexto Dinámico (Clima, Lore inyectado)**.
- Al poner lo dinámico al final, Minimax logra retener en caché más del 90% de los tokens en cada mensaje, reduciendo los costos de la API dramáticamente incluso al cambiar de tema constantemente.

### 12. Regla de Identidad y Sanitización de Guardado
**Problema:** El juego arrojaba `ArgumentException` al intentar guardar la memoria de caballos o mascotas, ya que sus nombres contenían espacios. Además, la IA intentaba darle consciencia a entidades no válidas.
**Solución Detallada:**
- **Sanitización:** Se inyectó Regex en `MemoryService.cs` para limpiar los nombres antes de guardarlos en SMAPI.
- **Regla de Identidad:** El mod ahora solo procesa interacciones y decaimiento de memoria para NPCs que cuenten explícitamente con un archivo de identidad (`.xml`) en la carpeta `Lore`.

### 13. Actuación Dinámica (Keyframes) y Sincronización Facial
**Problema:** La IA solo mostraba una emoción por mensaje y las expresiones no coincidían con los sprites nativos de Stardew Valley (ej. intentar verse "enojada" cargaba el rostro "feliz").
**Solución Detallada:**
- **Remapeo Nativo:** Corregimos la matriz para que coincida con el motor del juego: `[0] Neutral, [1] Feliz, [2] Triste, [3] Único, [4] Enojado, [5] Sonrojado`.
- **Sistema de Keyframes:** Implementamos una lógica de paginación donde el menú limpia las etiquetas múltiples de emoción en un mismo texto y cambia el rostro del retrato *en tiempo real*, sílaba por sílaba, sincronizándolo con el efecto de máquina de escribir y los clics de avance del usuario.

---

## 🛠️ Protocolos de Resolución de Errores (Troubleshooting)

| Error Detectado | Razón Técnica | Cómo Solucionarlo |
| :--- | :--- | :--- |
| **CS1061: NPC no contiene una definición para CurrentEmotion** | En SV 1.6 las emociones se guardan en el objeto `Dialogue`, no en `NPC`. | Se corrigió usando nuestra propia variable `this.CurrentEmotion` en `AiDialogueMenu.cs`. No intentes alterar propiedades internas de SV para emociones. |
| **La IA deja de responder o devuelve vacío (Silencio Repentino)** | Bloqueo por filtros de seguridad NSFW/Extorsión de la API de Venice o Fallo HTTP. | Revisar logs. Si la consola muestra una respuesta vacía exitosa (200), el mod inyecta `"..."`. Si hay error 400+, revisar la API Key. |
| **Las palabras de la IA se cortan abruptamente** | El `MaxTokens` fue superado. | Aumentar el límite del `ChatModel` de 500 a 800 en `VeniceApiService.cs`. |
| **El Portrait Box se superpone con el Chat Box Expansivo** | Redimensionamiento extraño de la ventana. | Recalibrar `_chatBoxWidth = this.width - 296 - 16` en `CalculateLayout()`. |
| **Fallo en Caché de Venice al cambiar de tema** | El Lore Dinámico rompía el bloque superior del prompt. | Mantener el orden del "Sándwich": Estático -> Historial -> Dinámico. |
| **ArgumentException al iniciar el día** | SMAPI intentaba guardar memoria de NPCs con espacios en su nombre (ej. Mascotas). | El `MemoryService` ahora sanitiza nombres y filtra mediante la "Regla de Identidad". |

---

## 🎯 Mejoras a Futuro (Hoja de Ruta)

### TAREA INMEDIATA: Recreación "Pixel-Perfect" de la UI Nativa
- **Renderizado Fiel:** Reemplazar el `drawTextureBox` genérico de nuestra caja principal por las matemáticas exactas extraídas de `DialogueBox.cs` (Dibujo manual de 9 piezas con offsets).
- **Portrait Plate:** Implementar la "Placa de Retrato" (`Rectangle(583, 411, 115, 97)`).
- **Joya de Amistad Animada:** Inyectar la gema palpitante (o estática si hay 10 corazones) en la interfaz gráfica.

### Sistemas que requieren refinamiento:
1. **Flujo de Rendimiento de Memoria:** Monitorear el tamaño del archivo JSON del jugador. Si las `ForgottenMemories` crecen descontroladamente tras el Año 5 in-game, se deberá implementar "Muerte Neuronal" definitiva tras bajar a fuerza `-0.8`.
2. **Scroll del Chat Dinámico:** Integrar una "Barra de Desplazamiento Visual" (Scrollbar) a la derecha de la caja para dar mejor feedback visual cuando hay texto oculto.
3. **Decaimiento y Asimilación de Memoria Diferenciada (Por Edad, Rasgos y Personalidad):** - El decaimiento actual es un parámetro global temporal y estático para todos. En futuras iteraciones, la tasa de olvido debe calcularse por NPC. 
   - **Edad (Hiper-realismo cognitivo):** Personajes mayores (como George o Evelyn) tendrán una curva de olvido más pronunciada para eventos triviales (`Episodic`), y requerirán de más repetición para solidificar un recuerdo permanente (`LearnedFact`). Sus anclas emocionales (`EmotionalAnchor`) se mantendrán intactas.
   - **Filtro Cognitivo por Personalidad:** Cada personaje interpretará y memorizará un mismo evento de formas radicalmente distintas dependiendo de quién es.

### Nuevas Funcionalidades Estructurales Planificadas:
1. **Reactivación Inmersiva por Voz:**
   - Portar el código previo de Whisper/Vosk al nuevo sistema `InteractionManager` como método de entrada secundario que reemplace/complemente el uso del teclado.
2. **Sistema de Integración Económica Compleja (Tool Calling & Agencia del Mundo):**
   - La IA tendrá privilegios sobre el mundo usando Tool Calling nativo de LLMs.
   - **Ventas Directas:** Comprarle semillas o animales directamente desde el chat.
   - **Acciones Físicas:** Ejecución de comandos como `[ACTION:GiveItem]`, `[ACTION:FollowPlayer]`, `[ACTION:ChangeHearts]`.
   - **Economía de Deudas Narrativas:** Creación de memorias financieras (`FinancialAnchor`).
3. **Generación de Archivo ConfigUI Nativo:** 
   - Migrar de pedir la API Key manualmente por Bloc de Notas a integrar la UI de **Generic Mod Config Menu** en la pantalla principal de Stardew Valley.
4. **Sistema de Chismes y Memoria Colectiva (Gossip Engine):**
   - Compartir memorias entre NPCs. Si le cuentas un secreto a un NPC, este puede filtrarse a un archivo global `TownRumors` y otros NPCs podrían mencionarlo al día siguiente.
5. **Consciencia Espacial Profunda (Deep Spatial Awareness):**
   - Expandir el `ContextBuilderService` para que escanee un radio de casillas alrededor del jugador, detectando monstruos, otros NPCs cercanos, cultivos específicos y el estado financiero/salud del jugador para comentarios ultra-contextuales.
6. **Misiones Generadas Dinámicamente (Dynamic Quests):**
   - La IA puede inyectar misiones reales en el Diario de Misiones de Stardew Valley mediante el diálogo (ej. "Tráeme 50 de madera").