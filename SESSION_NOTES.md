# Registro de Sesión: Living Companions Valley (Proyecto X)

Este documento contiene el historial detallado de nuestra sesión de desarrollo (y las sesiones previas), las decisiones arquitectónicas tomadas, los errores detectados del proyecto anterior y las soluciones definitivas que implementamos.

## 🚀 Resumen del Progreso

Logramos migrar exitosamente el mod desde una arquitectura acoplada y saturada (el antiguo proyecto que incluía dependencias pesadas como SQLite y Vosk local) a una arquitectura limpia, modular y altamente eficiente en `Proyecto X`. La interfaz gráfica pasó de ser un prototipo a un clon "Pixel-Perfect" de la experiencia nativa de Stardew Valley.

### Fases Completadas:
1. **Fase 1: Motor de Contexto (Topic Router)**
2. **Fase 2: Optimización de IA (Venice API)**
3. **Fase 3: Cerebro Orgánico (Memoria)**
4. **Fase 4: Integración UI, Emociones y Límites de Contexto**

---

## 🧠 Decisiones y Soluciones Clave (Histórico Completo)

### 1. Arquitectura Dual-Model (Velocidad vs Razonamiento)
**Problema:** Usar un modelo potente (como GLM-5) para hablar en tiempo real con el NPC generaba demasiada latencia, rompiendo la inmersión en Stardew Valley.
**Solución Detallada:** 
- **Canal de Charla (`minimax-m25` u otros ultra rápidos):** Implementamos el modelo rápido con el parámetro `reasoning: { enabled: false }` para obtener respuestas instántaneas en la UI del juego.
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
**Solución Detallada:** 
- Implementamos una clase `ModConfig.cs`. Al ejecutar el mod, SMAPI crea automáticamente un archivo `config.json` en la carpeta del mod. El usuario solo debe abrir ese archivo y pegar su llave de Venice.

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

---

## 🛠️ Protocolos de Resolución de Errores (Troubleshooting)

| Error Detectado | Razón Técnica | Cómo Solucionarlo |
| :--- | :--- | :--- |
| **CS1061: NPC no contiene una definición para CurrentEmotion** | En SV 1.6 las emociones se guardan en el objeto `Dialogue`, no en `NPC`. | Se corrigió usando nuestra propia variable `this.CurrentEmotion` en `AiDialogueMenu.cs`. No intentes alterar propiedades internas de SV para emociones. |
| **La IA deja de responder o devuelve vacío (Silencio Repentino)** | Bloqueo por filtros de seguridad NSFW/Extorsión de la API de Venice o Fallo HTTP. | Revisar logs. Si la consola muestra una respuesta vacía exitosa (200), el mod inyecta `"..."`. Si hay error 400+, revisar la API Key. |
| **Las palabras de la IA se cortan abruptamente** | El `MaxTokens` fue superado. | Aumentar el límite del `ChatModel` de 500 a 800 en `VeniceApiService.cs`. |
| **El Portrait Box se superpone con el Chat Box Expansivo** | Redimensionamiento extraño de la ventana. | Recalibrar `_chatBoxWidth = this.width - 296 - 16` en `CalculateLayout()`. |
| **Fallo en Caché de Venice** | Alterar el orden del System Prompt. | No mover la etiqueta `<Identidad>` hacia abajo. Lo estático debe ir siempre primero. |

---

## 🎯 Mejoras a Futuro (Hoja de Ruta)

### Sistemas que requieren refinamiento (Ya implementados pero crudos):
1. **Flujo de Rendimiento de Memoria:** Monitorear el tamaño del archivo JSON del jugador. Si las `ForgottenMemories` crecen descontroladamente tras el Año 5 in-game, se deberá implementar "Muerte Neuronal" definitiva tras bajar a fuerza `-0.8`.
2. **Scroll del Chat Dinámico:** Integrar una "Barra de Desplazamiento Visual" (Scrollbar) a la derecha de la caja para dar mejor feedback visual cuando hay texto oculto.
3. **Parseo de Emociones Extremo:** Refinar la Expresión Regular para tolerar espacios en blanco por si la IA es rebelde y no pone el número al inicio estricto.

### Nuevas Funcionalidades Estructurales Planificadas:
1. **Reactivación Inmersiva por Voz:**
   - Portar el código previo de Whisper/Vosk al nuevo sistema `InteractionManager` como método de entrada secundario que reemplace/complemente el uso del teclado.
2. **Sistema de Integración Económica Compleja (Tool Calling):**
   - La IA tendrá privilegios sobre el mundo usando Tool Calling nativo de LLMs.
   - **Ventas Directas:** Comprarle 20 semillas a Pierre descontando el oro del jugador instantáneamente mediante chat.
   - **Mecánica de Regateo (Persuasión):** Convencer al NPC de bajar precios basado en `FriendshipHearts` y argumentos en el chat.
   - **Economía de Deudas Narrativas:** Si se compran cosas a crédito, GLM-5 creará memorias financieras (`FinancialAnchor`). El NPC recordará la deuda y cambiará su actitud al cobrar.
3. **Generación de Archivo ConfigUI Nativo:** 
   - Migrar de pedir la API Key manualmente por Bloc de Notas a integrar la UI de **Generic Mod Config Menu** en la pantalla principal de Stardew Valley.
