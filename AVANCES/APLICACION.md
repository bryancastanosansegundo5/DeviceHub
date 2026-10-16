# Avances de Nexo Device Hub

Fecha: 16 de octubre de 2026.

## Programa y entorno

- Programa: **Nexo Device Hub**, aplicación de escritorio WPF sobre .NET 10.
- Objetivo de esta etapa: continuar el soporte del Logitech G PRO Wireless y estudiar la integración segura de las funciones de Onboard Memory Manager (OMM).
- Trabajo en el worktree aislado `C:\Users\bcastano\.codex\worktrees\logitech-rea-ui\DeviceHub`, actualizado con `origin/main` en `788ee52` antes de esta continuación (se creó originalmente desde `f261cdf`). La integración final de esta iteración irá a `main`; el checkout original se mantiene libre durante el desarrollo.

## Archivos tocados

La huella acumulada es `(líneas añadidas + eliminadas frente a d621167) / líneas del archivo en d621167`, redondeada a una decimal. Sirve para localizar cambios; no mide el porcentaje de funcionalidad. Los archivos creados durante esta etapa figuran como 100 %.

| Archivo | Huella | Trabajo realizado |
|---|---:|---|
| `AGENTS.md` | **6,2 %** | Registra la consulta manual HID++ del C539 y sus límites. |
| `Adapters/DeviceAdapterRegistry.cs` | **34,2 %** | Muestra PRO WIRELESS con identidad reconocida y estado prudente de la consulta OMM. |
| `MainWindow.xaml` | **17,3 %** | Presenta etapas DPI seleccionables y un deslizador de perfil local, además de polling y consulta manual HID++ para PRO WIRELESS. |
| `MainWindow.xaml.cs` | **0,8 %** | Conecta el botón de lectura HID++ con el ViewModel. |
| `Models/DeviceLocalProfile.cs` | **4,2 %** | Conserva la etapa DPI local seleccionada. |
| `Services/DeviceLocalProfileLibrary.cs` | **29,3 %** | Valida cuatro etapas de 100 a 25.600 DPI, la etapa activa y polling de 125/250/500/1000 Hz para perfiles Logitech. |
| `Services/HidDeviceEnumerator.cs` | **22,1 %** | Reconoce C539 por nombre PnP y lee descriptor HID sin enviar reportes. |
| `Services/LogitechHidppCapabilityReader.cs` | **100 %** | Lee manualmente versión, features OMM, tipo HID++ bruto, DPI actual si 0x2201 está anunciado y polling actual si 0x8060 está anunciado en ranuras de ratón 1–6. |
| `Models/HardwareDevice.cs` | **31,0 %** | Añade metadatos de las colecciones HID al inventario tipado del dispositivo. |
| `ViewModels/DeviceCardViewModel.cs` | **76,4 %** | Adapta la ficha C539, cambia de etapa DPI local y actualiza propiedades para la interfaz. |
| `ViewModels/MainViewModel.cs` | **43,9 %** | Ejecuta la consulta manual, conserva resultados entre búsquedas y muestra features, DPI y polling actual leídos. |
| `checks/NexoHub.Checks/Program.cs` | **160,5 %** | Comprueba framing HID++, filtro de versión/tipo, eco ping, features DPI y lectura de sensor/polling. |
| `docs/device-inventory-analysis.md` | **237,9 %** | Registra límites REA de G HUB, filtro OMM, wrappers DPI/perfiles/polling y los resultados de búsqueda selectiva. |
| `README.md` | **3,5 %** | Corrige el modelo Logitech mostrado y aclara que DPI/polling todavía se guardan solo en Nexo. |
| `dist/LEEME.md` | **18,2 %** | Corrige el nombre del ratón y el estado local de la función en las notas del paquete. |
| `AVANCES/APLICACION.md` | **100 %** | Mantiene este registro de cambios, evidencias y trabajo pendiente. |

## Estado funcional actual

La ficha del Logitech G PRO Wireless permite seleccionar una de cuatro etapas, editar su valor con un deslizador y configurar polling; esos cambios aún se guardan como perfil local en el PC. La acción manual HID++ pregunta si la feature pública `0x8060` está anunciada cuando el tipo bruto es mouse `0x03` y, si la anuncia, consulta el intervalo actual; si `0x2201` está disponible, lee DPI actual/default del sensor 0. Ninguna de esas consultas cambia el dispositivo. Una consulta anterior de solo lectura al C539 respondió en la ranura 1 con HID++ 4.2, features `0x0003`, `0x0005`, `0x8100` y al menos una entre `0x2201`/`0x2202`; `GetDeviceType` devolvió `0x03`. OMM usa internamente `BaseDeviceType.MOUSE=2` y no se ha probado que la DLL traduzca el tipo HID++ `0x03` de esa manera. No se confirma aceptación OMM ni se escriben perfiles de memoria integrada.

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
- REA verificó el inventario de 269 entradas del ASAR y los hashes de `app.min.js` (7.369.426 bytes), `main.js` (836.162 bytes), `preload.bundle.js` (11.373 bytes) y `dpi-tester.min.js.map` (354.689 bytes). El mapa de fuentes legible corresponde al overlay de prueba DPI (`DpiTesterOverlay.tsx`), no a la pantalla completa de configuración del ratón.
- Un nuevo análisis aislado de `app.min.js` (768 MB, 90 s y 6.000 nodos) leyó el archivo, pero volvió a agotar memoria antes de parsearlo: cero archivos JS parseados, cero nodos AST y cero módulos. Evidencias: análisis `ev_84a5b33458ee62ac03a20ac307f723b13ea2f60cce393b402b586f91133a6ec0`, vista `ev_bca5675963d86d69324e91790d75264a1b37f1cb9c77afaa3935975b2574ccb1`.
- El análisis conjunto de `main.js` y `preload.bundle.js` sí parseó ambos archivos y observó 8 APIs de `contextBridge`, 29 canales literales, 82 operaciones IPC dinámicas, 58 envíos del renderer y 29 manejadores del proceso principal. No emparejó envíos con manejadores; la gráfica semántica quedó parcial y no se analizaron los 8 archivos restantes. Evidencia: `ev_688043e39d04ca82834daf76bcb6bddd99752834c204b75b573636d45be90d68`.
- En esta continuación, REA aisló `858.min.js` (600.102 bytes): parseó 142.448 nodos AST, pero la gráfica semántica quedó parcial y el chunk no ofrece la pantalla completa de configuración. Evidencias: análisis `ev_4a4b4925695c097d98726df6a71fed36607f4d39f3a1f150b4c787aac33bfb82` y resumen `ev_1d3607d26f3512ed29dc5715c2200e08530a84e53f45f9ef8a2e7e3217c247fa`.
- Un nuevo intento aislado sobre `app.min.js` con 1.024 MB y límite de 30 s leyó el bundle, pero terminó antes del parseo (0 JS, 0 nodos AST, 0 módulos). Evidencia `ev_93444bdb381a1f0b8e70688f0c5d86410e04e76421de86aa589e881bd3872187`. No se elevó el límite porque el intento anterior tampoco logró parsearlo.
- **Paridad de interfaz no alcanzada.** Nexo ya permite elegir etapa y editar su valor con un deslizador, pero ambos cambian solo el perfil local. Siguen pendientes perfiles múltiples, asignación de cambio DPI a botones, restablecimiento de G HUB y aplicación al hardware.

### OMM

- El ejecutable indicado por el usuario (`OnboardMemoryManager_2.7.4428.exe`, SHA-256 `4dfa89c46df2da173ecf12021b6f82be56c5d69f8d30182346bd753eb3bed5c1`) y su DLL `logi_nethidppio.dll` se inspeccionaron estáticamente; no se ejecutó OMM.
- OMM consulta `Feature2201` para cantidad de sensores y valores/lista DPI; `FeatureBase` asigna `0x2201`. Las firmas CIL indican `GetSensorCount(): Task<int>`, `GetSensorDPIList(int): Task<DPIRanges>`, `GetSensorDPI(int): Task<DPIResolution>` y `SetSensorDPI(int,int,int): Task`.
- `HIDMouse.IsCompatible` exige dispositivo conectado, HID++ 2.0, tipo `MOUSE=2`, features `0x0003`, `0x0005`, `0x8100` y al menos una entre `0x2201` y `0x2202`. La DLL define ranuras hijas `DEVICE1..DEVICE6=1..6` y receptor `RECEIVER=255`; REA no determina qué ranura está ocupada ahora.
- La lectura real del servicio Nexo contestó en `DEVICE1` con HID++ 4.2, los cuatro requisitos de feature y tipo bruto `0x03`. El borrador HID++ asigna `Mouse=3` a la respuesta de GetDeviceType; OMM filtra con un enum interno `MOUSE=2`. La DLL obtiene ese valor en una frontera nativa y no se conoce la conversión, así que Nexo muestra el byte en bruto y no afirma que el filtro completo esté satisfecho.
- En el código del gestor no aparece llamada a `SetSensorDPI` en los 9.535 call edges analizados. Esto no es evidencia de que el ratón no admita DPI por ese feature.
- `Feature8100` (`0x8100`) expone lectura/escritura de datos y directorios de perfiles, modo a bordo y selección de perfil. El perfil interno tiene componentes DPI v1/v6 y polling cableado/inalámbrico. Son nombres/firmas de metadatos; no describen los bytes de informes ni el orden de llamadas del protocolo.
- REA focalizó cuatro tipos de `logi_nethidppio.dll`: `Feature2201`/`Feature2202` consultan DPI y exponen setters; `Feature8061` tiene `GetDeviceCapabilities` y `GetActualReportRateList`; `Feature8100` expone lecturas de perfil/modo/resolución y operaciones de escritura. Evidencia `ev_caf795170fa05788a1027379c583c7efb26fc07b045a5ef66c9d4e8cd03361a0`. Sus wrappers llegan a `NativeFeature`; el código nativo que construye informes sigue sin poder recuperarse, así que estos nombres no dan un protocolo ejecutable.
- La inspección general de la DLL encontró 1.732 tipos, 4.662 métodos y 17.165 relaciones de llamada; la cobertura de implementación es parcial por la frontera C++/CLI. Resumen REA `ev_ddf0d93b6e8b38cfc5ab65d9861ed01068ec06dca71a51c546e43f5457b2ec67`.
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
4. Completar el mapa de interfaz del G PRO Wireless en G HUB y recuperar el recorrido renderer → preload → proceso principal → backend; el bundle principal sigue sin parsearse y no hay paridad confirmada.
5. Recalcular esta tabla al registrar la próxima iteración.

## Punto de continuación · 16 de octubre de 2026

- `Services/LogitechHidppCapabilityReader.cs` valida las respuestas largas IRoot, el SWID, el eco ping y las versiones HID++ 2.0 admitidas; los checks cubren el framing y el rechazo de respuestas incorrectas.
- La consulta se integró en la ficha exacta C539 y es manual. El harness obtuvo respuesta en ranura 1, HID++ 4.2, las features consultadas y tipo bruto `0x03`; no hay escrituras de ajustes ni perfil.
- El worktree parte de `main` `f261cdf`; la compilación y los 7 grupos de self-check pasan. La tabla de huellas refleja cambios acumulados desde `d621167`.
- El acceso directo local `Nexo Device Hub.lnk` sigue en la raíz del checkout principal y está excluido de Git.

## Continuación REA · 16 de octubre de 2026

- REA no ha reconstruido G HUB al 100 % ni se ha replicado toda su interfaz. El bundle principal no se parsea dentro de límites razonables; los análisis exitosos son parciales y no revelan el recorrido completo de configuración para este ratón.
- La inspección dirigida de OMM confirma wrappers managed para leer DPI, perfiles y lista de polling; sus llamadas cruzan a código nativo sin cuerpos disponibles. REA tampoco pudo ejecutar `objdump` en Windows (`ERR_REA_WINDOWS_NATIVE`). No se enviaron consultas nuevas al dispositivo ni escrituras HID.
- Se endureció `LogitechHidppSlotStatus.HasRequiredFeatures`: ahora exige respuesta de tipo HID++ además de versión y features, evitando marcar completa una consulta con el tipo ausente. Se añadió un self-check para ese caso; esto no afirma compatibilidad con OMM.
- Validación de esta iteración: `dotnet build .\NexoHub.csproj -c Release /m:1 /p:UseSharedCompilation=false` terminó con 0 errores y 0 advertencias; `dotnet run --project .\checks\NexoHub.Checks\NexoHub.Checks.csproj -c Release /p:UseSharedCompilation=false` pasó los 7 grupos; `git diff --check` correcto.
- Archivos tocados en esta iteración: `Services/LogitechHidppCapabilityReader.cs` (1 línea lógica; propiedad; huella acumulada 100 %), `checks/NexoHub.Checks/Program.cs` (assert; huella acumulada 94,7 %), `docs/device-inventory-analysis.md` (evidencia REA; huella acumulada 134,5 %) y este registro (100 % por ser nuevo desde la base indicada).
- Pendiente: recuperar el código nativo de OMM o documentación específica suficiente para validar framing y respuestas; obtener más evidencia del flujo UI de G HUB y llevar DPI/polling al dispositivo solo con lectura posterior y persistencia demostradas.
## Fuentes oficiales

[HID++ 2.0 de Logitech](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst), [especificaciones del G PRO Wireless](https://support.logi.com/hc/en-sg/articles/360023463233-Logitech-G-PRO-Wireless-Gaming-Mouse-Technical-Specifications), [producto G PRO Wireless](https://www.logitechg.com/en-gb/products/gaming-mice/pro-wireless-mouse.html), [notas de G HUB](https://support.logi.com/hc/en-gb/articles/360053659674-Logitech-G-HUB), [criterio oficial de compatibilidad OMM](https://support.logi.com/hc/en-hk/articles/360057433714-How-to-enable-On-Board-Memory-Mode-to-use-Onboard-Memory-Manager).

## Continuación REA · 16 de octubre de 2026

- REA amplió el seguimiento CIL de OMM: `Feature8061.GetDeviceCapabilities` y `GetActualReportRateList` llaman a funciones nativas `feature_8061.*` y convierten el resultado a `ReportRates` (`ev_ae82221e0fc1fa09c37e9aba187f50b68a76054754640e6db7689b491bd7d085`). La frontera nativa sigue sin revelar framing, bytes ni un mapeo probado entre la clase interna `Feature8061` y el ID HID++.
- Logitech cataloga `0x8060` como Adjustable Report Rate y define el reporte largo de 20 bytes. Nexo hace `GetFeature(0x8060)` solo durante la acción manual y solo para el tipo `0x03`; muestra índice, no anunciada, o sin respuesta/error. El índice cero no es una consulta fallida. No lee ni cambia el polling.
- Archivos tocados en esta continuación: `Services/LogitechHidppCapabilityReader.cs` (**100 %** de huella acumulada), `ViewModels/MainViewModel.cs` (**41,3 %**), `checks/NexoHub.Checks/Program.cs` (**100,9 %**), `docs/device-inventory-analysis.md` (**144,8 %**) y este archivo (**100 %**, creado desde la base). El porcentaje usa la fórmula de la sección «Archivos tocados»; no es cobertura funcional.
- Validación: `dotnet build .\NexoHub.csproj -c Release /m:1 /p:UseSharedCompilation=false` terminó con 0 errores y 0 advertencias; los 7 grupos de `NexoHub.Checks` pasaron; `git diff --check` no encontró errores.
- No se hizo una consulta nueva al hardware ni una escritura HID en esta continuación. La lista de tareas pendientes permanece abierta; en particular, faltan los cuerpos nativos de OMM y el recorrido completo de configuración DPI de G HUB.

## Investigación G HUB · 16 de octubre de 2026

- Una pasada REA acotada a `main.js` y `preload.bundle.js` (512 MiB, 60 s, 6.000 nodos) encontró el envío dinámico `A.p.GL_SOCKET_SEND` y un manejador literal `GL_SOCKET_SEND`. No se probó que ambos canales coincidan ni se trazó la operación posterior. No encontró `dpiTable` ni `updateMouseSettingsWrapper` en esos dos archivos; no se obtuvo la conexión UI → IPC → backend.
- G HUB sigue sin ingeniería inversa completa y no hay paridad total con su interfaz. Evité repetir el parseo del `app.min.js` de 7,37 MB porque los intentos previos agotaron memoria antes de parsear; los resultados obtenidos hasta ahora son parciales.
- Archivos tocados en esta actualización: `docs/device-inventory-analysis.md` (**144,8 %** de huella acumulada) y `AVANCES/APLICACION.md` (**100 %**, archivo creado desde la base). No se modificó código ni se consultó el ratón.
- Al ser solo documentación, no se repitieron build ni checks; `git diff --check` se ejecutó correctamente antes de registrar este avance.

## Continuación REA y diagnóstico DPI · 16 de octubre de 2026

- REA inspeccionó otra vez OMM y su DLL mixta: confirmó los wrappers de `Feature2201`/`Feature2202`, el getter de tipo que cruza a `NativeFeature` y la frontera de 802 implementaciones nativas no recuperadas. El enum de OMM sigue sin poder compararse con el byte `0x03` leído por HID++; no afirmamos compatibilidad completa ni implementamos getters DPI sin framing conocido. Evidencias `ev_4a037698c38d5924fd8262967cb6167056eba7151e5de8a8fada4c268b54a764`, `ev_3bf203c95e98c246c325bbdb3c0bb75d34137ccb5d03ff7af28ba2f1288cc851` y `ev_8d5b71daf6aa208ff9974e6bd58eb077c47488dba6f86bfeb432c15d026de101`.
- En G HUB, REA aisló `dpi-tester.min.js` y su mapa con 192 MiB: el tester lee la etapa DPI activa y la pasa al minijuego, pero no configura el ratón (`ev_f6ad8e08cff0dc7638b8a3d82a73c3a3697285d0f38c51e37e490c23eb8533de`). La extracción íntegra del ASAR no encontró discrepancias (`ev_16a7a37728ee77c51380034ea2dc81ec2ad64c4c236dcd55d2f9563b9bc258f4`). Una búsqueda literal de bajo coste sobre `app.min.js` con hash `d95f649c2eddd640f3295afc1af46dbf0cee0a9a4da9f2faf9675f64e3821e49` encontró la tabla DPI y acciones de polling; la conexión entre esa UI, el canal IPC y los reportes para C539 sigue sin probarse.
- Cambios de esta iteración: el lector mantiene `0x2201` y `0x2202` separados, la ficha enseña ambos estados y el filtro conserva la regla verificada de OMM (cualquiera de los dos). También se actualizan este registro y `docs/device-inventory-analysis.md`.
- Huella acumulada desde `d621167`: `Services/LogitechHidppCapabilityReader.cs` (100 %, archivo nuevo desde la base), `ViewModels/MainViewModel.cs` (41,3 %), `checks/NexoHub.Checks/Program.cs` (102,6 %), `docs/device-inventory-analysis.md` (158,6 %) y `AVANCES/APLICACION.md` (100 %, archivo nuevo desde la base). El porcentaje representa cambios de líneas, no cobertura funcional.
- Validación pendiente en el cierre de esta iteración: build Release, 7 grupos de checks y `git diff --check`. No se consultó el ratón ni se ejecutó G HUB u OMM durante esta continuación.
- La ingeniería inversa de G HUB no está completa y su interfaz no tiene paridad total con Nexo. Los siguientes pasos activos son seguir el guardado de `reportRate`/DPI hacia su backend y documentar qué operaciones de ratón pueden confirmarse en el C539.

## Continuación REA y lectura DPI · 16 de octubre de 2026

- REA y las lecturas acotadas del `app.min.js` con hash `d95f649c2eddd640f3295afc1af46dbf0cee0a9a4da9f2faf9675f64e3821e49` localizan el panel DPI: cada etapa pasa `activeDpi` y `defaultDpi` a `updateMouseSettingsWrapper`. El componente también usa acciones `assignDPI` y `restoreDefaults`. El bundle principal sigue sin poder parsearse completo y el puente del panel a IPC/backend no está probado.
- La vista REA de `main.js` y `preload.bundle.js` encontró 58 envíos de renderer, 29 manejadores y 82 canales dinámicos en dos fuentes; no emparejó envíos con manejadores. La cobertura sigue parcial, con 41.396 incógnitas semánticas y 8 archivos no analizados.
- OMM ofrece getters `Feature2201.GetSensorCount` y `GetSensorDPI`; Nexo los usa ahora solo desde la acción manual HID++ y solo para ranuras de ratón que anuncian `0x2201`. La salida puede incluir DPI actual/default; no se manda ningún setter ni se modifica la memoria del periférico.
- Se añadió el selector de etapa activa y el deslizador DPI local inspirado en la captura. El perfil guarda la etapa activa, y las comprobaciones verifican que solo cambie la etapa seleccionada y que se respeten los límites. Sigue siendo edición local.
- Huella acumulada frente a `d621167`: `MainWindow.xaml` **17,3 %**; `Models/DeviceLocalProfile.cs` **4,2 %**; `Services/DeviceLocalProfileLibrary.cs` **29,3 %**; `Services/LogitechHidppCapabilityReader.cs` **100 %** (archivo nuevo); `ViewModels/DeviceCardViewModel.cs` **76,4 %**; `ViewModels/MainViewModel.cs` **43,9 %**; `checks/NexoHub.Checks/Program.cs` **140,4 %**; `docs/device-inventory-analysis.md` **189,7 %**; este registro **100 %** (nuevo). El porcentaje mide líneas cambiadas, no funciones terminadas.
- Validación: build Release correcto, 0 errores y 0 advertencias; los 7 grupos de `NexoHub.Checks` pasaron. Un harness temporal hizo una sola lectura real: ranura 1, HID++ 4.2, feature `0x2201` en índice `0x0C`, un sensor, 1400 DPI actuales/800 predeterminados. No se abrió G HUB/OMM ni se envió ninguna escritura HID. Faltan compatibilidad OMM confirmada, paridad de UI y cualquier escritura que demuestre lectura posterior y persistencia.

## Continuación REA y lectura de polling · 16 de octubre de 2026

- El seguimiento REA acotado de `app.min.js` localizó `getReportRateList` en el módulo 21835 y solo en la ruta de teclados con `hasPerformanceReportRate()`. Usa `electronNet.send` → IPC `SOCKET_SEND` → socket de `main`; no se atribuye al ratón C539.
- Al guardar `setStandaloneReportRate` (módulo 84135) crea `reportRate:{value}` y despacha `MTX` (`setOnboardProfileConfiguration`). El handler envía un `SET` a `/onboard_profiles/%(id)s/tag_configuration` por `electronNet.send`/IPC/socket. Esto acredita el envío de la petición de configuración desde G HUB, no la escritura física al C539 ni su persistencia. `assignDPI`, `restoreDefaults` y `updateMouseSettingsWrapper` modifican acciones/estado Redux; en esta pasada no se conectaron al mismo handler. REA tampoco logró parsear íntegro el bundle principal.
- Como referencia de protocolo independiente, el código público de libratbag implementa `0x8060` función 1 (`GetReportRate`): envía parámetro 0 y recibe el intervalo actual en milisegundos en el primer parámetro ([código de libratbag](https://github.com/libratbag/libratbag/blob/master/src/hidpp20.c#L1581-L1637)). Logitech confirma el nombre/ID `0x8060` en su catálogo HID++; el detalle de función no procede de una especificación oficial de Logitech.
- Nexo consulta ahora ese getter únicamente durante la acción manual, solo para C539 exacto, tipo HID++ bruto `0x03` y feature `0x8060` anunciada. Valida reporte largo, ranura, índice, función/SWID y un intervalo distinto de cero. No se envía el setter 2 ni se cambia polling/perfil; falta confirmar una respuesta real de polling de esta unidad.
- El análisis de OMM se mantiene separado: su clase administrada `Feature8061` tiene ID `0x8061`, lista de tasas/capacidades y sin getter explícito de tasa actual. No hay evidencia de que sea alias de `0x8060`.
- La ingeniería inversa de G HUB y la paridad de interfaz siguen incompletas; los siguientes pasos son encontrar quién aporta `reportRates` para el C539, revisar el handler de `tag_configuration` y validar la respuesta de polling de esta unidad.

## Continuación REA y contraste con OMM · 16 de octubre de 2026

- La pasada REA selectiva sobre main.js y preload.bundle.js encontró el puente de socket del proceso principal, pero dejó ocho archivos del ASAR fuera del análisis. El resumen tiene 53.188 incógnitas semánticas y cero transmisiones IPC emparejadas; la consulta de la ruta tag_configuration sobre esta selección no encontró semillas, lo que no prueba ausencia en el renderer. Evidencia de análisis principal: ev_2da6bcb8c4ea37f86a4871f49e18455764156c03cc2909c9d88d50198b4327a0.
- En main.js, el ejecutable lghub_agent.exe se resuelve bajo %PROGRAMFILES%\LGHUB y se inicia con spawn en segundo plano. El proceso principal usa ws://localhost:9010 y recibe SOCKET_SEND; esto sitúa el transporte local de G HUB antes del agente.
- La inspección estática del agente ubica las rutas /onboard_profiles/%s/tag_configuration y /onboard_profiles/%s/tag_configuration/all junto con nombres de código HID++ y Feature8100 de memoria integrada. La relación entre la ruta concreta y esas funciones nativas no está probada: Ghidra y IDA no están configurados, Hopper no admite este host y objdump no funcionó. Son cadenas, nombres y rutas, no un call graph ni evidencia de ejecución.
- REA de OMM sí muestra el recorrido administrado del dato de polling: HIDProfile asigna report rate al objeto OnboardProfile; Feature8100.WriteProfileData obtiene el sector nativo y llama a feature_8100.write_profile_data. El setter del dato y la llamada de escritura del perfil son pasos separados. No se demuestra que cualquier cambio de tasa invoque Write, los bytes transmitidos ni la confirmación del dispositivo. Evidencias ev_e3200c405bcc971dd811da9fec039bf9003c28587d41f1c05c09a3a7e4b7b4cf, ev_0f023da87343ba3b12b9de49051fc7adb6dedbfc71859c05a88c9687c0b18c8c y ev_bc14a5453613868d783b411b3acbdc613a69b8943700aa89315b6e2e283174ff.
- El filtro OMM revisado usa comprobaciones genéricas de tipo HID++, versión y features, sin una comprobación C539 observada. La etiqueta Pro Wireless en OMM no acredita compatibilidad VID/PID. No se ha confirmado que el C539 supere el filtro.
- La ingeniería inversa de G HUB y la paridad de interfaz siguen incompletas. Esta iteración fue de análisis y documentación: no se modificó código, no se ejecutaron G HUB u OMM y no se consultó ni escribió el ratón.

## Pausa: REA del guardado a bordo · 16 de octubre de 2026

- **G HUB no está reconstruido al 100 % y Nexo no replica toda su interfaz.** REA estático y selectivo sobre `app.min.js` (SHA-256 `d95f649c2eddd640f3295afc1af46dbf0cee0a9a4da9f2faf9675f64e3821e49`) identificó que la acción `MTX` (`setOnboardProfileConfiguration`) conserva la tarjeta activa y sustituye `reportRate` por `{ value }`; el perfil a bordo activo aporta `profileId`. El helper manda el cuerpo mediante `SET` a `/onboard_profiles/%(id)s/tag_configuration`. Evidencias relacionadas: `ev_3cab6c784e4f3054687b77e29a29ba7044fcfc08a5fc8ba0a90c925b6e58d492`, `ev_653d43d3c84b4002a8e838830c4dca95e0b860145d7ddca2cbb08d0a4a9ad182`, `ev_c607204ae5d7476612553366525d74491c9cbd5d52f75b46acf0b38a3106da2c` y `ev_400bef38266fd6b524b1686145a52743063b7abd1780607c2290b53b4f1b9a9`.
- La interfaz de ese flujo depende de capacidades genéricas (`hasOnboardEditSupport` y `onboardProfiles.supportsOnboardMode`); no apareció una condición explícita para `C539`. No hay evidencia de qué capacidades recibe G HUB para esta unidad ni de que el `SET` persista en el ratón. Los offsets hallados en el bundle (bytes) son: cuerpo 2.285.421; acción 2.388.811; envío del saga 2.826.701; helper `SET` 1.459.372; ruta 1.599.501; capacidades 1.896.668/1.917.318. La búsqueda de ruta en una gráfica parcial no prueba ausencia: `app.min.js` quedó fuera del análisis semántico completo.
- OMM: el guardado de UI llega a `HIDProfile.Write` (`0x06000281`); su estado asíncrono (`<Write>d__176.MoveNext`, `0x0600070e`) toma `_f8100`, el sector de `_profile_directory_entry` y `_profile_data`, y llama a `Feature8100.WriteProfileData`. La inspección directa de `OnboardMemoryManager.Models.HIDProfile` encontró el tipo y sus 110 métodos en metadatos completos (`ev_67e957a8e7d1403d3053dc4f0180a10cd8bc8a633888e91e12b13f54533ea515`). El tramo administrado no mostró una comprobación de compatibilidad antes de la llamada; presupone un perfil ya inicializado.
- En `logi_nethidppio.dll`, `Feature8100.WriteProfileData` obtiene el sector nativo, prepara un vector de bytes y cruza a `feature_8100.write_profile_data` (`ev_bc14a5453613868d783b411b3acbdc613a69b8943700aa89315b6e2e283174ff`). El contenido/formato final de los bytes y la respuesta del hardware siguen desconocidos. La evidencia estática no demuestra por sí misma que cambiar polling invoque el guardado.
- Estado al pausar: el tipo bruto `0x03` del C539 todavía no se ha relacionado de forma demostrada con el enum `MOUSE=2` de OMM; no se ha validado compatibilidad OMM completa, escritura, lectura posterior ni persistencia tras reconexión. No se ejecutaron G HUB/OMM ni se consultó o escribió el ratón.
- Archivos modificados en esta actualización: `AVANCES/APLICACION.md` (**100 % de huella acumulada**, archivo nuevo frente a la base `d621167`; ese porcentaje no mide funcionalidad). No se cambió código ni se ejecutaron build/checks; se revisó el diff y `git diff --check` antes del commit.
