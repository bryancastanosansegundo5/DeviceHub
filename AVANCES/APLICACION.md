# Avances de Nexo Device Hub

Fecha: 16 de octubre de 2026.

## Programa y entorno

- Programa: **Nexo Device Hub**, aplicación de escritorio WPF sobre .NET 10.
- Objetivo de esta etapa: continuar el soporte del Logitech G PRO Wireless y estudiar la integración segura de las funciones de Onboard Memory Manager (OMM).
- Trabajo en el worktree aislado `C:\Users\bcastano\.codex\worktrees\logitech-rea-ui\DeviceHub`, iniciado desde `main` en `f261cdf`. La integración final de esta iteración irá a `main`; el checkout original se mantiene libre durante el desarrollo.

## Archivos tocados

La huella acumulada es `(líneas añadidas + eliminadas frente a d621167) / líneas del archivo en d621167`, redondeada a una decimal. Sirve para localizar cambios; no mide el porcentaje de funcionalidad. Los archivos creados durante esta etapa figuran como 100 %.

| Archivo | Huella | Trabajo realizado |
|---|---:|---|
| `AGENTS.md` | **6,2 %** | Registra la consulta manual HID++ del C539 y sus límites. |
| `Adapters/DeviceAdapterRegistry.cs` | **34,2 %** | Muestra PRO WIRELESS con identidad reconocida y estado prudente de la consulta OMM. |
| `MainWindow.xaml` | **11,1 %** | Presenta DPI/polling como perfil local e incorpora el botón manual HID++ solo para PRO WIRELESS. |
| `MainWindow.xaml.cs` | **0,8 %** | Conecta el botón de lectura HID++ con el ViewModel. |
| `Services/DeviceLocalProfileLibrary.cs` | **26,8 %** | Valida cuatro etapas de 100 a 25.600 DPI y polling de 125/250/500/1000 Hz para perfiles Logitech. |
| `Services/HidDeviceEnumerator.cs` | **22,1 %** | Reconoce C539 por nombre PnP y lee descriptor HID sin enviar reportes. |
| `Services/LogitechHidppCapabilityReader.cs` | **100 %** | Lee manualmente versión, features consultadas por OMM y tipo HID++ bruto en ranuras 1–6. |
| `Models/HardwareDevice.cs` | **31,0 %** | Añade metadatos de las colecciones HID al inventario tipado del dispositivo. |
| `ViewModels/DeviceCardViewModel.cs` | **14,5 %** | Adapta la ficha al ratón exacto y actualiza el estado HID++ observado. |
| `ViewModels/MainViewModel.cs` | **38,6 %** | Ejecuta la consulta manual, conserva resultados entre búsquedas y evita solaparla con el refresco. |
| `checks/NexoHub.Checks/Program.cs` | **93,9 %** | Añade checks del framing HID++, filtro de versión, eco ping, GetDeviceType y descripción del descriptor. |
| `docs/device-inventory-analysis.md` | **106,9 %** | Registra el filtro OMM extraído, el enrutamiento de ranuras, el byte de tipo y el alcance del lector. |
| `README.md` | **3,5 %** | Corrige el modelo Logitech mostrado y aclara que DPI/polling todavía se guardan solo en Nexo. |
| `dist/LEEME.md` | **18,2 %** | Corrige el nombre del ratón y el estado local de la función en las notas del paquete. |
| `AVANCES/APLICACION.md` | **100 %** | Mantiene este registro de cambios, evidencias y trabajo pendiente. |

## Estado funcional actual

La ficha del Logitech G PRO Wireless permite editar cuatro niveles DPI y polling y guardar esos valores en un JSON del PC. Se añadió una consulta HID++ manual para leer versión, features relevantes para OMM y el byte en bruto de `GetDeviceType`. Una consulta de solo lectura al C539 respondió en la ranura 1 con HID++ 4.2 y las features `0x0003`, `0x0005`, `0x8100` y al menos una entre `0x2201`/`0x2202`; `GetDeviceType` devolvió `0x03`. La especificación publicada enumera Mouse como tipo HID++ `3`; OMM usa internamente `BaseDeviceType.MOUSE=2` y no se ha probado que la DLL los traduzca de esa manera. No se confirma aceptación OMM. No se aplican DPI/polling ni se escriben perfiles de memoria integrada.

**La etapa Logitech no está terminada.** La consulta HID++ real confirma transporte y features en la ranura 1, pero no el filtro completo de compatibilidad OMM. No se han probado escrituras, lectura de vuelta de ajustes ni persistencia tras reconexión.

## REA y evidencia

### Identidad del ratón

- La captura que proporcionó el usuario muestra **PRO WIRELESS**. Windows presenta el nodo HID conectado bajo `VID_046D&PID_C539` y nombre PnP `PRO WIRELESS`.
- Logitech asigna el receptor C539 al G PRO Wireless. C547 no es una firma de Superlight: Logitech también lo usa para G502 X PLUS. Nexo dejó de inferir el modelo desde C547.
- Identidad con confianza alta por la coincidencia de nombre y VID/PID; falta validar el transporte de configuración del dispositivo emparejado.

### G HUB

- REA inspeccionó `C:\Program Files\LGHUB\resources\app.asar` (SHA-256 `5bcb683032410c4fc35462822751fcb1429d4da7cebfa57cda688d9468401875`), inventario de 269 entradas sin contradicciones de integridad.
- La UI de DPI actualiza una tabla de niveles y el estado de perfil con `activeDpi`, `defaultDpi` y `advancedDpiTable`; la tasa de informe tiene acción/ruta propia y se vincula al perfil cuando el modo a bordo está activo.
- El bundle no revela los informes HID ni vincula ese flujo genérico al receptor C539. El análisis semántico de `app.min.js` agotó el límite de memoria, así que la cobertura sigue siendo parcial.
- Evidencia de búsqueda/extracción: `ev_ae8f0ca6b269529dbf5eb1e5d063387919199d717735d9512ef99cadb98a5977`; búsqueda posterior del bundle `ev_441f81b6fce0407f27c82f3b94b94289bdc0f4169ab9bd463949c1129d11d1a5`.

### OMM

- El ejecutable indicado por el usuario (`OnboardMemoryManager_2.7.4428.exe`, SHA-256 `4dfa89c46df2da173ecf12021b6f82be56c5d69f8d30182346bd753eb3bed5c1`) y su DLL `logi_nethidppio.dll` se inspeccionaron estáticamente; no se ejecutó OMM.
- OMM consulta `Feature2201` para cantidad de sensores y valores/lista DPI; `FeatureBase` asigna `0x2201`. Las firmas CIL indican `GetSensorCount(): Task<int>`, `GetSensorDPIList(int): Task<DPIRanges>`, `GetSensorDPI(int): Task<DPIResolution>` y `SetSensorDPI(int,int,int): Task`.
- `HIDMouse.IsCompatible` exige dispositivo conectado, HID++ 2.0, tipo `MOUSE=2`, features `0x0003`, `0x0005`, `0x8100` y al menos una entre `0x2201` y `0x2202`. La DLL define ranuras hijas `DEVICE1..DEVICE6=1..6` y receptor `RECEIVER=255`; REA no determina qué ranura está ocupada ahora.
- La lectura real del servicio Nexo contestó en `DEVICE1` con HID++ 4.2, los cuatro requisitos de feature y tipo bruto `0x03`. El borrador HID++ asigna `Mouse=3` a la respuesta de GetDeviceType; OMM filtra con un enum interno `MOUSE=2`. La DLL obtiene ese valor en una frontera nativa y no se conoce la conversión, así que Nexo muestra el byte en bruto y no afirma que el filtro completo esté satisfecho.
- En el código del gestor no aparece llamada a `SetSensorDPI` en los 9.535 call edges analizados. Esto no es evidencia de que el ratón no admita DPI por ese feature.
- `Feature8100` (`0x8100`) expone lectura/escritura de datos y directorios de perfiles, modo a bordo y selección de perfil. El perfil interno tiene componentes DPI v1/v6 y polling cableado/inalámbrico. Son nombres/firmas de metadatos; no describen los bytes de informes ni el orden de llamadas del protocolo.
- La DLL mixta C++/CLI contiene 802 implementaciones nativas y 68 imports declarados. REA identificó nombres de métodos y RVA, pero no pudo desensamblar esos cuerpos: Ghidra no está configurado y no hay proveedor nativo compatible en este host.
- Las cadenas `Pro Wireless` del OMM no prueban que esta unidad C539 sea compatible. Logitech especifica que solo algunos ratones aparecen en OMM; queda pendiente confirmar la detección de esta unidad.
- Evidencias: OMM y grafo CIL `ev_e3200c405bcc971dd811da9fec039bf9003c28587d41f1c05c09a3a7e4b7b4cf`; filtro `ev_6f76d717ce3d93b89f230657fd820ec62d929dae965e88d899691fb8f3edb0ed`; IDs de features `ev_09f86482fcf49d6ab649fd649ab44084ab18b34da2b9529a01d2f5045d71c5fa`; constantes de tipo/ranura `ev_1fc688c4e9cf03fa829870ee35849b089db1e1d3bd4b6dbb321adbe086c4bafa`; firmas `ev_daf9fcaa1926bef94b23168ef0dc9438d52a788e5e30023906f25dcd6a360878`; asignación de IDs `ev_f4356fea2a0694f66a3979bfc8046b79373e0199941b62d52fc6508d875a300d`; vistas de tipos `ev_b3987584e8b33eef383c6010b61ae5b6b8d7b9a0e1c74e610122f2acfbdef86b` y `ev_436803b7a5c01363ce2818b71220ecacc7c68963f53734a4fff58e451f704b9b`; contornos nativo/managed `ev_8d5b71daf6aa208ff9974e6bd58eb077c47488dba6f86bfeb432c15d026de101`.

## Comprobaciones realizadas

- `dotnet build .\NexoHub.csproj -c Release /m:1 /p:UseSharedCompilation=false`: correcto, 0 errores y 0 advertencias.
- `dotnet run --project .\checks\NexoHub.Checks\NexoHub.Checks.csproj -c Release`: correcto; terminó con 7 grupos. El inventario de esta ejecución encontró PRO WIRELESS y AK820, 6 endpoints de audio y no encontró Yeti ni ASTRO conectados.
- `git diff --check`: correcto.
- Harness local de solo lectura contra el C539: correcto; obtuvo ranura 1, HID++ 4.2, features registradas arriba y tipo bruto `0x03`.
- La consulta de GetDeviceType se añadió al mismo flujo manual; muestra el byte sin traducirlo al enum de OMM.
- No se abrió G HUB ni OMM, ni se probaron escrituras o persistencia.

## Siguiente trabajo pendiente

1. Determinar si la DLL convierte el `Mouse=3` del protocolo al `BaseDeviceType.MOUSE=2` interno de OMM y si ese resultado hace que el C539 pase el filtro real.
2. Recuperar los cuerpos nativos C++/CLI de OMM si se necesita reconstruir sus reportes; el análisis actual no revela bytes ni secuencias.
3. Implementar lectura/escritura DPI, polling y perfiles a bordo solo tras confirmar comandos, acuses, lectura posterior y persistencia después de reconectar.
4. Recalcular esta tabla al registrar la próxima iteración.

## Punto de continuación · 16 de octubre de 2026

- `Services/LogitechHidppCapabilityReader.cs` valida las respuestas largas IRoot, el SWID, el eco ping y las versiones HID++ 2.0 admitidas; los checks cubren el framing y el rechazo de respuestas incorrectas.
- La consulta se integró en la ficha exacta C539 y es manual. El harness obtuvo respuesta en ranura 1, HID++ 4.2, las features consultadas y tipo bruto `0x03`; no hay escrituras de ajustes ni perfil.
- El worktree parte de `main` `f261cdf`; la compilación y los 7 grupos de self-check pasan. La tabla de huellas refleja cambios acumulados desde `d621167`.
- El acceso directo local `Nexo Device Hub.lnk` sigue en la raíz del checkout principal y está excluido de Git.
## Fuentes oficiales

[HID++ 2.0 de Logitech](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst), [especificaciones del G PRO Wireless](https://support.logi.com/hc/en-sg/articles/360023463233-Logitech-G-PRO-Wireless-Gaming-Mouse-Technical-Specifications), [producto G PRO Wireless](https://www.logitechg.com/en-gb/products/gaming-mice/pro-wireless-mouse.html), [notas de G HUB](https://support.logi.com/hc/en-gb/articles/360053659674-Logitech-G-HUB), [criterio oficial de compatibilidad OMM](https://support.logi.com/hc/en-hk/articles/360057433714-How-to-enable-On-Board-Memory-Mode-to-use-Onboard-Memory-Manager).
