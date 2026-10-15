# ASTRO Command Center: análisis para el hub de dispositivos

**Fecha:** 15 de octubre de 2026  
**Aplicación examinada:** `C:\Program Files (x86)\Astro Gaming\ASTRO Command Center`  
**Ejecutable:** `ASTRO Command Center.exe`  
**SHA-256:** `991E558129AF98765CBA4F02B10FA5CC4D537E2B6BB4BD3032CA010832EB2DCF`

## Equipo observado en este PC

Windows muestra conectado un Astro A50 con identificador USB `VID_9886`, `PID_0015` y endpoints `Astro A50 Game` / `Astro A50 Voice`. Esto confirma qué dispositivo está conectado durante el análisis; todavía no permite asignarle con seguridad una generación concreta.

## Hallazgos

- El programa es un ejecutable nativo Windows de 32 bits basado en Qt. Importa las API HID `HidD_GetFeature` y `HidD_SetFeature`.
- La aplicación modela productos llamados `AstroA20`, `AstroA50Europa`, `AstroA50Mimas`, `AstroMixAmpPro` y `AstroMixAmpProIapetus`. Los recursos instalados incluyen presets para A20, A50, A50 Mimas y MixAmp Pro TR. La documentación de Logitech enumera compatibilidad del ASTRO Command Center de Windows con A50 Gen 3/4 y MixAmp Pro TR. [Compatibilidad oficial de ASTRO Command Center](https://support.logi.com/hc/en-ca/articles/16411213694487-Astro-Command-Center-for-Windows-10)
- Los archivos `.astroeq` guardan ecualizadores de cinco bandas: ganancia y frecuencia para las bandas 1–5, y ancho de banda para las bandas 2–4.
- Los perfiles `.agp` son archivos INI que pueden agrupar ajustes de headset, micrófono, stream mix y extras del dispositivo. El perfil MixAmp incluido contiene campos como `Noisegate_Value`, `Sidetone_Value`, `Game_Audio_Value` y `Chat_Audio_Value`.
- El código inicializa la lectura de ecualización y micrófono desde el dispositivo. La interfaz incluye el texto “Reverts back to the settings currently saved on the device”. Hay evidencia de recuperación de valores guardados por el dispositivo, aunque no se hizo una prueba de desconexión o apagado para comprobar cuánto persiste cada ajuste.

## Protocolo HID recuperado estáticamente

El ejecutable intercambia reportes HID de 64 bytes. Los mensajes llevan identificador de comando y una secuencia que la aplicación comprueba al validar respuestas. Los siguientes identificadores salen de las rutinas que manejan los ajustes:

| Comando | Hex | Función observada |
| --- | --- | --- |
| 99 | `0x63` | Leer y escribir las ganancias de ecualización; el código lee tres slots y cinco bandas por slot. |
| 100 | `0x64` | Leer y escribir frecuencias y parámetros de ancho de banda del ecualizador. |
| 101 | `0x65` | Leer dos valores de estado de la interfaz del dispositivo. |
| 102 | `0x66` | Leer y escribir el modo de noise gate; se representa como una opción enumerada. |
| 104 | `0x68` | Leer y escribir sidetone; la aplicación limita el valor a `0–100`. |
| 105 | `0x69` | Consultar y cambiar nombres de presets EQ guardados en el dispositivo. |
| 108 | `0x6C` | Seleccionar el EQ activo. |

El análisis estático identifica las operaciones y parte de sus datos, pero aún no establece el formato completo de cada reporte para todos los modelos Astro. Por eso los IDs son una base sólida para el adaptador, mientras que los bytes de cabecera, confirmaciones y variaciones por modelo deben validarse antes de escribirlos desde el hub.

## Consecuencias para el hub

Conviene implementar Astro como un adaptador HID por modelo, en vez de tratar todos los dispositivos Astro como si compartieran el mismo mapa de ajustes. Para el A50, el alcance confirmado en el ejecutable incluye tres slots EQ, cinco bandas, noise gate, sidetone y nombres de presets. El formato `.agp` permite plantear importación/exportación de perfiles; algunos campos de stream mix aparecen en perfiles MixAmp, pero sus comandos HID no se han recuperado todavía.

ASTRO Command Center no muestra evidencia de macros de teclado ni de asignación de macros a botones. Esa función requerirá otra capa del hub y dependerá de que el dispositivo acepte asignaciones o de que el hub las ejecute desde el PC.

Antes de habilitar escrituras desde el hub, falta confirmar la generación exacta del A50 `9886:0015`, probar lectura y escritura con capturas de reportes en ese modelo y verificar el ajuste tras desconectar/reconectar. No se enviaron reportes HID, no se cambiaron ajustes y no se ejecutó el actualizador de firmware. El archivo `assets\forced_fw\mimas_190705151435_tm-35155_tr-43_rm-34843_rr-43.afw` no se analizó ni se aplicó.

## Base reproducible

La copia analizada del ejecutable coincide con el hash del original. Ghidra se usó en modo headless sobre esa copia. Los ficheros temporales de descompilación no forman parte del repositorio; los hallazgos relevantes se resumen en esta nota.
