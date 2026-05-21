# 📚 Guías de Implementación - Living Companions Valley

Este archivo contiene el historial de guías técnicas y manuales de usuario para los diferentes sistemas complejos implementados en el mod. Sirve como referencia centralizada para entender cómo configurar y expandir cada característica.

---

## 1. Guía de Soporte para Retratos HD (Alta Resolución)

Hemos implementado un motor de auto-escalado matemático impulsado por **Harmony** dentro del mod. Esto permite que el juego nativo de Stardew Valley y nuestro sistema de IA escalen correctamente cualquier retrato gigante a la resolución correcta de la caja de diálogo sin cortarlo.

### 🛠️ Cómo crear e integrar los Retratos HD

Para que funcione correctamente, debes preparar las imágenes en el formato estándar de Stardew Valley (un *Spritesheet* de 2 columnas) pero en tamaño original.

#### A. Formato de la Imagen
Debes crear una grilla (spritesheet) que contenga las diferentes emociones del personaje.
- **Ancho total:** Debe ser exactamente el doble del ancho de un frame individual. (Ej. Si cada retrato HD mide `512x512`, el ancho total del archivo debe ser `1024` píxeles).
- **Alto total:** Dependerá de cuántas emociones quieras incluir. (Ej. Si son 3 filas de emociones, el alto será `1536` píxeles).

**Orden de Emociones (Estándar SV):**
1. Neutral (Arriba, Izquierda)
2. Feliz (Arriba, Derecha)
3. Triste (Medio, Izquierda)
4. Único (Medio, Derecha)
5. Enojado (Abajo, Izquierda)
6. Sonrojado (Abajo, Derecha)

#### B. Ubicación del Archivo
Guarda la imagen resultante en formato PNG en la siguiente ruta exacta dentro de tu mod:
`assets/Portraits/{NombreDelNpc}_LCV.png`
*(Por ejemplo: `assets/Portraits/Marnie_LCV.png`)*

#### C. Carga Automática
¡Eso es todo! `ModEntry.cs` interceptará automáticamente cuando el juego pida el retrato de ese NPC e inyectará tu archivo `_LCV.png`. Nuestro parche de Harmony se encargará del resto de las matemáticas de escalado en todo el juego (preservando la inmersión por completo).

---

### ⚙️ Configuración y Compatibilidad (Ej. Portraiture)

> **⚠️ ADVERTENCIA DE COMPATIBILIDAD:**
> Si en el futuro decides usar un mod dedicado exclusivamente a cargar retratos HD (como **Portraiture** o **HD Portraits**), nuestro parche interno de Harmony podría causar conflictos al intentar escalar las imágenes que el otro mod ya está manejando.

Para prevenir esto, existe un interruptor de seguridad. Si el jugador prefiere usar `Portraiture`, simplemente debe ir al archivo `config.json` de nuestro mod y cambiar esta línea:
```json
"EnableBuiltInHdPortraits": false
```
Al apagarlo, nuestro mod dejará de parchear el juego base y permitirá que los otros mods hagan su trabajo. Mientras tanto, nuestra interfaz de IA (`AiDialogueMenu`) seguirá siendo inteligente y se adaptará dinámicamente al tamaño que le entreguen, sin importar quién cargue la imagen.

---

## 2. Guía de Soporte para la Consciencia Social y Escucha a Escondidas (Eavesdropping)

Hemos refinado el motor de IA para que tenga un entendimiento hiper-realista del espacio a su alrededor y una memoria ligada intrínsecamente a su identidad.

### 👥 El Radar de Testigos (Eavesdropping Engine)
Cuando interactúas con un NPC, el motor ahora escanea un radio de 8 casillas a tu alrededor. 
Si hay otros personajes cerca (ej. Hablas con Marnie en el bar y Shane está al lado):
1. La IA activa sabrá que Shane está ahí y podría mencionarlo (*"Baja la voz, Shane está bebiendo ahí al lado"*).
2. Cuando la conversación termine, **Shane también procesará la conversación en segundo plano**.
3. **Filtro Anti-Basura:** Para evitar que el archivo de guardado pese gigabytes, a los testigos se les aplica un filtro estricto: *Solo formarán recuerdos si escucharon un chisme, si hablaron de ellos o si se reveló un secreto*. La charla trivial (clima, saludos) será ignorada por su subconsciente y su memoria quedará en blanco.

### 🧠 Decaimiento Cognitivo Personalizado
La curva del olvido (el proceso mediante el cual los NPCs olvidan eventos diarios) ya no es igual para todos. Ahora responde matemáticamente a sus archivos `Lore.xml`.

#### A. Multiplicador de Intereses (El Filtro de Pasión)
Debes añadir la etiqueta `<intereses>` al final del bloque `<Identidad>` en el XML de tu personaje. 
Ejemplo en `Marnie.xml`:
```xml
<intereses> animales, vacas, gallinas, alcalde lewis, granja, campo, heno </intereses>
```
**Efecto**: Si un NPC memoriza algo que coincide con estas palabras, su velocidad de olvido se **divide a la mitad** (nunca lo olvidará). Si memoriza algo que no le interesa de un jugador con el que no tiene amistad, su velocidad de olvido se **duplica** (lo olvidará en un par de días).

#### B. Multiplicador de Edad (Hiper-realismo cerebral)
El motor ahora lee la etiqueta `<edad>`. Si detecta las palabras clave `mayor` o `anciano` (Ej. Evelyn o George):
- Olvidarán eventos episódicos y triviales un 50% más rápido que los jóvenes.
- Retendrán "Hechos Aprendidos" (LearnedFacts) 3 veces más fuerte que los jóvenes, representando cómo la memoria a corto plazo falla en la vejez, pero el aprendizaje de base y los prejuicios son casi imborrables.

---
*(Aquí se añadirán futuras guías de implementación como el sistema de economía o integración GMCM)*
