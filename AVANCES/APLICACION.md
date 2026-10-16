# Avances de Nexo Device Hub

Fecha: 16 de octubre de 2026.

## Programa y entorno

- Programa: **Nexo Device Hub**, aplicación de escritorio WPF sobre .NET 10.
- Objetivo de esta etapa: identificar el ratón Logitech exacto, avanzar la ficha DPI/polling, inspeccionar pasivamente los descriptores HID y analizar su posible integración con Logitech G HUB y Onboard Memory Manager (OMM).
- Cambios aislados en `C:\Users\bcastano\.codex\worktrees\logitech-rea-ui\DeviceHub`. El checkout original `C:\Users\bcastano\Desktop\FUENTESBRYAN\DeviceHub` permanece separado.

## Archivos tocados

La huella es `(líneas añadidas + eliminadas) / líneas del archivo base en HEAD`, redondeada a una decimal. Sirve para localizar cambios; no mide el porcentaje de funcionalidad. El archivo nuevo figura como 100 % creado en esta etapa.

| Archivo | Huella | Trabajo realizado |
|---|---:|---|
| `AGENTS.md` | **4,2 %** | Actualiza el inventario Logitech observado y elimina la atribución del receptor C547 a un Superlight no identificado. |
| `Adapters/DeviceAdapterRegistry.cs` | **22,8 %** | Muestra PRO WIRELESS solo con identidad reconocida y añade sus datos pasivos de descriptor HID. |
| `MainWindow.xaml` | **10,3 %** | Mejora la presentación de DPI y polling para el ratón reconocido, con etapas claras y acento visual; conserva el guardado local existente. |
| `Services/DeviceLocalProfileLibrary.cs` | **26,8 %** | Valida cuatro etapas de 100 a 25.600 DPI y polling de 125/250/500/1000 Hz para perfiles Logitech. |
| `Services/HidDeviceEnumerator.cs` | **1,8 %** | Reconoce C539 por nombre PnP y lee página/uso y longitudes del descriptor HID, sin enviar reportes. |
| `Services/LogitechHidppCapabilityReader.cs` | **100 %** | Borrador de consulta manual HID++ de solo lectura para versión y features de OMM; sin integración ni validación aún. |
| `Models/HardwareDevice.cs` | **100 %** | Añade metadatos de las colecciones HID al inventario tipado del dispositivo. |
| `ViewModels/DeviceCardViewModel.cs` | **7,3 %** | Ajusta título y rango DPI de la ficha al modelo Logitech identificado. |
| `checks/NexoHub.Checks/Program.cs` | **22,8 %** | Comprueba la identidad del adaptador, límites DPI, rechazo de polling no admitido y lectura/escritura del perfil local. |
| `docs/device-inventory-analysis.md` | **55,2 %** | Documenta identificación, evidencia REA de G HUB/OMM, fuentes oficiales y límites técnicos. |
| `README.md` | **3,5 %** | Corrige el modelo Logitech mostrado y aclara que DPI/polling todavía se guardan solo en Nexo. |
| `dist/LEEME.md` | **18,2 %** | Corrige el nombre del ratón y el estado local de la función en las notas del paquete. |
| `AVANCES/APLICACION.md` | **100 %** | Crea este registro de implementación y del trabajo pendiente. |

## Estado funcional actual

La ficha del Logitech G PRO Wireless permite editar cuatro niveles DPI y polling, y guardar esos valores en un JSON del PC. Nexo todavía no los lee ni los aplica al ratón, ni cambia perfiles de memoria integrada. La apariencia de la ficha y el perfil local no equivalen a soporte real del hardware.

**La etapa Logitech no está terminada.** No se afirma compatibilidad OMM con la unidad C539, no se ha completado el transporte HID++ y no se ha probado lectura de vuelta o persistencia en el ratón.

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
- En el código del gestor no aparece llamada a `SetSensorDPI` en los 9.535 call edges analizados. Esto no es evidencia de que el ratón no admita DPI por ese feature.
- `Feature8100` (`0x8100`) expone lectura/escritura de datos y directorios de perfiles, modo a bordo y selección de perfil. El perfil interno tiene componentes DPI v1/v6 y polling cableado/inalámbrico. Son nombres/firmas de metadatos; no describen los bytes de informes ni el orden de llamadas del protocolo.
- La DLL mixta C++/CLI contiene 802 implementaciones nativas y 68 imports declarados. REA identificó nombres de métodos y RVA, pero no pudo desensamblar esos cuerpos: Ghidra no está configurado y no hay proveedor nativo compatible en este host.
- Las cadenas `Pro Wireless` del OMM no prueban que esta unidad C539 sea compatible. Logitech especifica que solo algunos ratones aparecen en OMM; queda pendiente confirmar la detección de esta unidad.
- Evidencias: OMM y grafo CIL `ev_e3200c405bcc971dd811da9fec039bf9003c28587d41f1c05c09a3a7e4b7b4cf`; firmas `ev_daf9fcaa1926bef94b23168ef0dc9438d52a788e5e30023906f25dcd6a360878`; asignación de IDs `ev_f4356fea2a0694f66a3979bfc8046b79373e0199941b62d52fc6508d875a300d`; vistas de tipos `ev_b3987584e8b33eef383c6010b61ae5b6b8d7b9a0e1c74e610122f2acfbdef86b` y `ev_436803b7a5c01363ce2818b71220ecacc7c68963f53734a4fff58e451f704b9b`; contornos nativo/managed `ev_8d5b71daf6aa208ff9974e6bd58eb077c47488dba6f86bfeb432c15d026de101`.

## Comprobaciones realizadas

- `dotnet build .\NexoHub.csproj -c Release /m:1 /p:UseSharedCompilation=false`: correcto, 0 errores y 0 advertencias.
- `dotnet run --project .\checks\NexoHub.Checks\NexoHub.Checks.csproj -c Release`: correcto; inventario encontró PRO WIRELESS y el conjunto de comprobaciones terminó con 6 grupos correctos.
- `git diff --check`: correcto.
- No se abrió G HUB ni OMM, no se enviaron informes al ratón y no se probó persistencia tras desconexión.

## Siguiente trabajo pendiente

1. Resolver o instalar con autorización un proveedor de desensamblado nativo para entender el backend C++/CLI de OMM, sin cargarlo contra el ratón.
2. Verificar por evidencia del dispositivo si C539 expone las features necesarias y si OMM admite este PRO Wireless.
3. Implementar lectura HID++ con identificación del endpoint, tamaños de informe y respuesta validados.
4. Añadir escrituras solamente después de verificar comandos, valores, acuse y lectura posterior. Probar perfiles a bordo después de reconectar antes de declarar persistencia.
5. Completar UI y pruebas del flujo confirmado, y actualizar las huellas de este archivo al cerrar cada iteración.

## Punto exacto de pausa · 16 de octubre de 2026

- El inventario pasivo previo observó en C539 una colección HID `FF00/0002` con informes IN/OUT de 20 bytes y otra `FF00/0001` de 7 bytes. Esto no demuestra qué features responde el ratón emparejado.
- Se empezó `Services/LogitechHidppCapabilityReader.cs`: limita el intento al Logitech G PRO Wireless C539, busca el endpoint HID de 20 bytes y prepara consultas IRoot `GetFeature` y versión de protocolo para ranuras 1–6. No envía comandos DPI ni escribe perfiles.
- El lector aún no se conectó a `MainViewModel`/`MainWindow`, no tiene checks de paquetes/respuestas, no se compiló ni ejecutó desde que se añadió y no se lanzó contra el ratón.
- Las huellas de la tabla son de la iteración anterior y deben recalcularse al retomar. El archivo lector es nuevo y figura como 100 % creado.
- El trabajo sigue en `C:\Users\bcastano\.codex\worktrees\logitech-rea-ui\DeviceHub`, aislado del checkout original.

### Continuación al reanudar

1. Contrastar y corregir el formato de respuesta HID++ con la documentación y el OMM analizado; añadir checks de paquetes/respuestas.
2. Compilar y ejecutar las comprobaciones en el worktree. Después, hacer una consulta manual de solo lectura al endpoint identificado, sin sondeo periódico.
3. Con la evidencia resultante, conectar el estado a la ficha Logitech, actualizar REA/documentación y recalcular las huellas.
4. Mantener DPI/polling y perfiles a bordo sin escritura hasta validar comandos, confirmación y lectura posterior; verificar reconexión antes de declarar persistencia.
## Fuentes oficiales

[HID++ 2.0 de Logitech](https://github.com/Logitech/cpg-docs/blob/master/hidpp20/README.rst), [especificaciones del G PRO Wireless](https://support.logi.com/hc/en-sg/articles/360023463233-Logitech-G-PRO-Wireless-Gaming-Mouse-Technical-Specifications), [producto G PRO Wireless](https://www.logitechg.com/en-gb/products/gaming-mice/pro-wireless-mouse.html), [notas de G HUB](https://support.logi.com/hc/en-gb/articles/360053659674-Logitech-G-HUB), [criterio oficial de compatibilidad OMM](https://support.logi.com/hc/en-hk/articles/360057433714-How-to-enable-On-Board-Memory-Mode-to-use-Onboard-Memory-Manager).
