# Resumen de Depuración: SmartPathfinder — Lo que falló y lo que funcionó

## El Problema Original
Emily (NPC controlado por IA) debía caminar desde la **FarmHouse** hasta el **Coop** en la granja. En su lugar, se quedaba **atascada en el porche** de la casa, caminando hacia un lado sin nunca bajar las escaleras.

---

## Intentos Fallidos (en orden cronológico)

### ❌ Intento 1: "Los escombros bloquean el camino"
**Hipótesis**: La granja tiene ~679 escombros/maleza, y estos estarían bloqueando la ruta entre la FarmHouse y el Coop.

**Por qué falló**: El camino entre la FarmHouse y el Coop estaba completamente limpio. Los escombros estaban en zonas periféricas de la granja, no en la ruta directa. Se verificó visualmente con capturas de pantalla.

**Lección**: No asumir la causa sin verificar los datos. El usuario confirmó que el camino estaba limpio.

---

### ❌ Intento 2: "El `Game1.viewport` está desfasado"
**Hipótesis**: La función `isTilePassable` usaba `Game1.viewport` (la cámara del jugador) como referencia, lo cual podía causar que tiles fuera de la pantalla del jugador se reportaran como no-pasables.

**Cambio aplicado**: Se reemplazó `Game1.viewport` por un viewport sintético que cubre todo el mapa:
```csharp
var viewport = new xTile.Dimensions.Rectangle(0, 0, location.Map.DisplayWidth, location.Map.DisplayHeight);
```

**Resultado**: Parcialmente correcto — era un problema real pero **no era la causa principal** del atasco en el porche. Se mantuvo el fix porque era una mejora legítima.

---

### ❌ Intento 3: "Eliminar NPCBarrier y NoPath sin más"
**Hipótesis**: Las propiedades `NPCBarrier` y `NoPath` en los tiles de las escaleras impedían que Emily bajara del porche. Eliminar estos checks debería resolver el problema.

**Cambio aplicado**: Se eliminaron ambos checks de `IsTileWalkable`.

**Resultado**: Emily **sí pudo bajar las escaleras** ✅, pero tomó una **ruta absurda** hacia los cultivos en vez de ir a la izquierda directamente. Se quedó atascada con la caja de envíos (Shipping Bin). El fix solucionó un problema pero creó otro.

**Lección**: `NPCBarrier` bloqueaba las escaleras, pero quitarlo sin agregar inteligencia de ruta causó que el A* eligiera caminos subóptimos que pasaban por cultivos y edificios.

---

### ❌ Intento 4: "Eliminar también `isCollidingPosition` y el check de Buildings"
**Hipótesis**: El check `isCollidingPosition` causaba falsos positivos (confirmado: marcaba como bloqueado el tile `{55,13}` al lado del Silo). El check de edificios era redundante con `isTilePassable`.

**Cambio aplicado**: Se eliminaron ambos checks.

**Resultado**: Emily ahora podía moverse más libremente, pero **no respetaba los footprints de edificios** como la Shipping Bin, e intentaba caminar a través de ellos. El A* tampoco tenía preferencia por caminos pavimentados vs. cruzar por cultivos del jugador.

**Lección**: Quitar `isCollidingPosition` fue correcto (realmente causaba falsos positivos). Pero quitar Buildings fue excesivo — se necesitaba **restaurar con mejor lógica de puertas**, no eliminar por completo.

---

### ❌ Intento 5: "Restaurar Buildings + Costos de tile, pero sin considerar warp landing"
**Hipótesis**: Restaurar el check de edificios (con excepción para puertas) y agregar un sistema de costos por tile resolvería tanto la ruta torpe como la colisión con la Shipping Bin.

**Cambio aplicado**: 
- Restaurado check de Buildings con excepción de puertas (`door.Y` y `door.Y - 1`)
- Agregado sistema de costos: caminos(1), césped(5), tierra(50), cultivos(200)

**Resultado**: **SmartPathfinder falló completamente** y cayó al pathfinder nativo (que usa la ruta torpe). La razón: Emily aterriza tras el warp en `{60,13}`, que está **dentro del footprint de Farmhouse**. El A* no podía ni iniciar porque el tile de INICIO estaba bloqueado. Además, la excepción de puerta no cubría `door.Y + 1` (el tile de aterrizaje del warp).

**Lección**: Siempre considerar el flujo completo: warp → landing tile → pathfinding. El tile de aterrizaje puede estar técnicamente "dentro" de un edificio.

---

## Solución Final ✅

Se aplicaron **dos fixes** que resolvieron todos los problemas simultáneamente:

### Fix 1: Start tile siempre walkable
```csharp
// CRÍTICO: El tile donde el NPC ya está parado SIEMPRE es caminable.
walkableCache[startTile] = true;
```
Si el NPC ya está físicamente en un tile, ese tile es caminable por definición, sin importar lo que digan los checks estáticos. Esto resuelve **cualquier** caso futuro de warp landing problemático.

### Fix 2: Excepción de puertas ampliada
```csharp
// Permitir: tile de la puerta, tile encima, y tile debajo (warp landing)
if (tile.X == door.X && (tile.Y >= door.Y - 1 && tile.Y <= door.Y + 1))
    continue;
```
Se amplió la excepción para cubrir 3 tiles verticales alrededor de cada puerta de edificio, permitiendo que el A* pueda trazar rutas que pasen por las zonas de entrada/salida.

### Fix 3: Sistema de costos (se mantiene de Intento 5)
| Tipo de tile | Costo | Efecto |
|---|---|---|
| Camino pavimentado (`Flooring`/`Path`) | 1 | Ruta preferida |
| Césped/suelo normal | 5 | Neutral |
| Tierra arada (`HoeDirt`) | 50 | Evitada |
| Cultivos activos | 200 | Prácticamente prohibida |

---

## Archivos Modificados
- [SmartPathfinder.cs](file:///c:/Users/Trabajo/Desktop/Trabajo/Proyectos%20AI/Proyecto%20X/src/Services/SmartPathfinder.cs) — Toda la lógica de pathfinding
- [ModEntry.cs](file:///c:/Users/Trabajo/Desktop/Trabajo/Proyectos%20AI/Proyecto%20X/src/ModEntry.cs) — Inyección del logger

## Commit
`c49b128` — `fix(pathfinder): sistema de costos por tile + fix warp landing en edificios`
