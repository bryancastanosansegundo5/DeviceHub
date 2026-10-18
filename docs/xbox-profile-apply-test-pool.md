# Pool de pruebas: aplicación del perfil Xbox

## Alcance y límites confirmados

- Nexo guarda los cambios Xbox en perfiles JSON locales; no los escribe ahora al mando.
- El contrato REA completo de `Devices.Abstraction.winmd` no declara perfiles/ranuras Elite. `GamepadButtons` incluye `Paddle1`–`Paddle4` como entradas, pero el remapeo estándar no puede asignarlas. Evidencia REA: `ev_885939e1…`, métodos `ev_ac779996…`, campos `ev_43c7611f…`.
- El mismo WinMD tiene `ProgrammableButtonDataId.Slot1Command1..10` y `ClearProgrammableSystemButtonsForControllerSlot`, pero no identifica esas ranuras como perfiles Elite; sus acciones listadas son de sistema y no incluyen botones/sticks. No usar los métodos `Clear*` para probar remapeos. Evidencia REA: `ev_392882f6…`.
- La API pública de Windows para remapeo estándar exige identidad de paquete y la capacidad restringida `xboxAccessoryManagement`; su efecto es de usuario/sistema y puede afectar a todos los juegos. No prueba escritura en memoria interna del mando ni cubre curvas/deadzones del perfil Elite.
- Ghidra no terminó el análisis estático de `Devices.App.dll` (20 MB): agotó 300 s y Java registró `OutOfMemoryError: Java heap space`. No se infiere comportamiento a partir de esa inspección fallida.
- No se ha consultado ni escrito el mando durante esta investigación.

## Pruebas pendientes de luz verde

Ejecutar solo cuando Bryan diga que se puede probar y FC27 no esté usando el mando. No desconectar, reiniciar ni actualizar firmware. Antes de cualquier escritura, guardar y leer de vuelta el estado que se vaya a cambiar.

| Orden | Prueba | Operación | Criterio |
|---|---|---|---|
| 1 | Identidad y capacidad del proceso | Solo lectura: paquete/identidad, capacidad y API disponible. | Continuar únicamente si el proceso tiene `xboxAccessoryManagement`; si no, parar. |
| 2 | Identidad del mando | Solo lectura: enumerar Gamepad/GIP y cotejar el modelo exacto con Xbox Elite. | No enviar nada a dispositivos que no coincidan. |
| 3 | Estado inicial | Solo lectura: capturar perfil visible en Xbox Accessories, remapeo del usuario y lecturas XInput/Gamepad. | Guardar valores originales para comparar/restaurar. |
| 4 | Remapeo estándar reversible | Cambiar una asignación estándar en una copia de prueba; leerla de vuelta y comprobar su entrada en Gamepad/XInput. | Debe aplicar y leerse igual; restaurar el estado original inmediatamente. No asumir que afecta al mando físico si solo cambia la capa global de Windows. |
| 5 | Perfil Elite y ranura | Contrastar Perfil 1 y la ranura activa sin cambiarla; comprobar si existe una API con lectura de paletas y ranura. | Si no hay API/lectura verificable, no escribir P1–P4 ni afirmar que se aplican. |
| 6 | Campos del editor, uno por uno | Medir asignaciones P1–P4, curvas/radial, inversión/intercambio, zonas muertas, vibración y brillo por separado. | Solo habilitar escritura para campos con setter identificado y lectura de vuelta; registrar el resto como no compatible. |
| 7 | Restauración | Releer y restaurar todos los valores tocados; leer de vuelta. | El estado debe coincidir con la captura inicial. |

La reconexión/reinicio para demostrar persistencia queda fuera del pool actual: Bryan pidió que no se corte la conexión. Sin esa comprobación no se afirmará persistencia a bordo.

## Primera ejecución (solo preflight, 2026-10-17)

- FC27 estaba ejecutándose (PID 37792), así que se detuvo el pool antes de cualquier operación de entrada o escritura.
- PnP identificó `Mando Xbox One Elite` (`VID_045E`, `PID_02E3`) mediante inventario de solo lectura.
- Nexo estaba ejecutándose desde `dist/visual-update-9/NexoHub.exe`; no aparece registrado como paquete MSIX. Xbox Accessories sí está instalado (versión `2506.2505.28001.0`). La prueba de capacidad falla en el build actual: Nexo no tiene identidad/capacidad de paquete.
- No se leyó el estado de entrada del mando, no se enviaron cambios y no se interrumpió la conexión.

## Segunda ejecución (preflight autorizado, 2026-10-17)

- FC27 estaba cerrado. El Elite Series 1 continúa presente como `USB\VID_045E&PID_02E3` (`XboxComposite`); no se desconectó.
- Xbox Accessories estaba abierto (`Devices.App.exe`, paquete `Microsoft.XboxDevices_2506.2505.28001.0_x64__8wekyb3d8bbwe`). Nexo sigue ejecutándose desde `dist/visual-update-9/NexoHub.exe`.
- La consulta `GetPackageFullName` de Nexo devolvió `15700` (`APPMODEL_ERROR_NO_PACKAGE`): Nexo no tiene identidad de paquete y, por tanto, no puede recibir `xboxAccessoryManagement` en su build actual. Se activa el criterio de parada de la prueba 1.
- La prueba 2 confirma el modelo exacto. Las pruebas 3–7 no se ejecutaron por la condición de parada; no se abrió ni guardó un perfil, no se tocó la asignación estándar de Windows y no se enviaron mensajes HID/GIP.
- Resultado: los remapeos de Nexo siguen guardándose solo en el PC. No hay evidencia de un escritor/readback de perfiles para Elite Series 1; la interfaz actual no puede hacer que un cambio de Nexo llegue al mando.
- Hallazgo local de solo lectura: Xbox Accessories conserva seis configuraciones JSON `Version 2.2` en WGS; `configDisambig` asocia `GIP:00007EF4248BED7E` con Perfil 1 en ranura 1 y FIFA sin joys en ranura 2. El archivo de Nexo contiene esas mismas seis configuraciones y tiene Perfil 1 activo, así que la importación local ya funciona en este PC.
- Los logs de Accessories también contienen `ConfigurationSnapshot` de `Troy`/`Slot1`, pero sus asignaciones de palas no coinciden con el JSON local de Perfil 1. Sin identificar el origen exacto de ese snapshot, no se toma como lectura de memoria ni como copia segura para restauración.
- La captura pasiva Frida de 12 s sobre `Devices.App.exe` solo vio cargados `Devices.App.dll` y `Devices.Abstraction.dll`; no se abrió un handle al mando ni se observó una escritura durante esa ventana inactiva. No demuestra cómo se comporta al guardar desde la interfaz.
- Las comprobaciones del proyecto no pudieron ejecutarse en este equipo de trabajo: `dotnet run --project .\checks\NexoHub.Checks\NexoHub.Checks.csproj -c Release` terminó con `No .NET SDKs were found`. No hubo compilación ni cambios de código que validar.

## Tercera ejecución (LB, observación pasiva de Xbox Accessories, 2026-10-17)

- FC27 estaba cerrado y el mando permaneció conectado.
- En la pantalla oficial de Asignación se cambió únicamente LB de Menú a Ver y se restauró a Menú inmediatamente; los demás controles no se tocaron.
- La vista oficial terminó mostrando `Botón superior izquierdo → Menú`. Nexo muestra el mismo valor importado de `Perfil 1`, pero esto solo verifica la copia local de Accessories y el preview de Nexo.
- Una sesión Frida pasiva en `Devices.App.exe` observó cero llamadas `DeviceIoControl` y cero `NtDeviceIoControlFile` durante el cambio/restauración. El handle `0xcc8` de las llamadas `NtWriteFile` se identificó como `\\Device\\NamedPipe\\UIA_PIPE_41324_64fcbcfd`, así que esas escrituras eran al canal de automatización de UI, no al mando.
- Esta observación no cubre otras rutas de comunicación ni constituye una lectura de memoria física. No se demostró persistencia a bordo, no se cortó la conexión y Nexo no envió una escritura HID/GIP.

## Cuarta ejecución (guardado XAC y análisis del IOCTL, 2026-10-17)

- El log de Xbox Accessories confirma que Perfil 1 / Slot 1 terminó con `LeftBumper: Menu` y que WGS registró `ConnectedStorage_WriteData:EndState:Success`. Eso confirma el perfil local de Accessories, no el contenido de la memoria del mando.
- Al salir de la pantalla de asignación, la captura pasiva observó llamadas al IOCTL privado `0x40001ccc` de XboxGIP: una entrada de 90 bytes y otra de 35 bytes. La sesión de Frida se cerró; no se enviaron informes manuales. La captura no obtuvo el estado de finalización asociado a esas llamadas, por lo que no confirma ACK del mando.
- El análisis estático de `xboxgip.sys` identifica `0x40001ccc` como una transacción GIP encolada y asíncrona; no revela que el payload sea un remapeo ni el significado de la respuesta. La documentación pública GIP describe mensajes/ACK de protocolo, pero no documenta este IOCTL privado.
- `IGipRemappingApi` declara getter/setter estándar; `StandardControllerRemapping` y `StandardControllerButtonRemapSettings` no incluyen ranura, perfil ni persistencia. El único método encontrado con ranura explícita, `ClearProgrammableSystemButtonsForControllerSlot`, borra botones programables y no se usó.
- Microsoft especifica que `IProgrammableGamepad` delega el remapeo interno al fabricante. La especificación no define cómo escribir la memoria de este Elite Series 1.
- Resultado: hay evidencia de una petición de Xbox Accessories al driver al guardar, pero no existe todavía un formato de escritura/readback seguro para Nexo. No afirmar que el remapeo se aplicó ni que persiste en el mando.

Referencias oficiales: [MS-GIPUSB: SupportedInterfaces](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gipusb/12feb7ea-3095-4dea-8288-ae66def54ecb), [definición de códigos IOCTL](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/defining-i-o-control-codes), [buffers de IOCTL](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/buffer-descriptions-for-i-o-control-codes).

## Sexta comprobación (slots y datos locales, 2026-10-18)

- La lectura de solo archivos de WGS encontró un GUID de ranura 2 sin archivo Elite asociado. `FIFA sin joys` existe en la copia, pero `configDisambig` no la asigna ahora a esa ranura. Nexo muestra un marcador «Perfil no encontrado» y no la vincula a ese perfil por nombre.
- Nexo resuelve la posición activa comparando el valor LB consultado por XboxGIP con los valores de los dos perfiles. Solo selecciona uno cuando la coincidencia es única; si los dos perfiles comparten LB o falta el perfil de una ranura, deja la posición sin confirmar.
- REA inspeccionó estáticamente `Devices.Abstraction.winmd` del paquete Xbox Accessories 2506. `IGipRemappingApi` expone remapeo estándar con parámetros de usuario y configuración, sin número de ranura; `IGipControllerApi` tampoco declara una propiedad de ranura activa. Esto no descarta mensajes privados del controlador en `Devices.App.dll`, pero el API WinMD no ofrece una lectura directa del switch.
- `dotnet build` y las diez comprobaciones de `NexoHub.Checks` pasan. No se leyó el mando, no se movió el switch y no se escribió ninguna configuración física durante esta comprobación.

## Quinta ejecución (alcance de la API pública y Ghidra, 2026-10-17)

- Microsoft documenta `SetStandardControllerButtonRemapping` como remapeo del mando estándar para un usuario: `previous=false` lo aplica y el mapeo queda persistente en el sistema para ese usuario. La página advierte que afecta a todos los juegos. No lo confundir con la memoria de la ranura Elite; no se llamó al setter.
- La clase Preview requiere `xboxAccessoryManagement`, una capacidad restringida. Nexo sigue sin identidad/capacidad de paquete, y declarar la capacidad no agregaría selector Slot 1 ni probaría persistencia onboard.
- Ghidra no pudo abrir directamente el DLL bajo `WindowsApps` por acceso denegado. Se copió a `%LOCALAPPDATA%\Temp\Nexo-XAC-20261017-Devices.App.dll` y se confirmó el mismo SHA-256 (`18e98caec85441199d39173ba43fce10dd2c175dfb562efe47a01e61fa3ab6fc`); la búsqueda de xrefs a `DeviceIoControl` agotó 300 s sin resultados. No se obtuvo una ruta estática nueva y no se repetirá ese análisis completo.
- No se enviaron escrituras al mando ni se interrumpió su conexión. Nexo todavía guarda este cambio en el perfil local; no se ha verificado el remapeo onboard.

Referencias oficiales: [SetStandardControllerButtonRemapping](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.preview.legacygipgamecontrollerprovider.setstandardcontrollerbuttonremapping?view=winrt-28000), [Windows.Gaming.Input.Preview](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.preview?view=winrt-28000), [capacidades de aplicaciones](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations).

## Sexta ejecución (captura de salida del editor, 2026-10-17)

- Xbox Accessories mostraba `Perfil 1`, `Ranura 1` y `Botón superior izquierdo (LB) → Menú`. Para provocar una edición solo en LB, se puso temporalmente en `Ver` y se restauró inmediatamente a `Menú`; el estado final visible quedó en `Menú`. No se pulsó ningún botón físico ni se desconectó el mando.
- La selección del destino no generó llamadas observadas a `DeviceIoControl` en `Devices.App.exe`. Al volver del editor sí aparecieron tres llamadas al IOCTL `0x40001ccc`, con tamaños de entrada/salida 90/3, 33/3 y 35/59 bytes. `DeviceIoControl` retornó falso con E/S pendiente; dos bloques `OVERLAPPED` observados a los 55–65 ms mostraron estado cero y sus buffers de salida cambiaron. La lectura del tercer bloque no se considera válida porque la memoria pudo reutilizarse.
- Los tamaños, respuestas y correlación con la navegación no permiten etiquetar estas llamadas como escritura de perfil ni como lectura de vuelta de la ranura. No se encontró una confirmación que vincule LB→Menú con la memoria del mando.
- Evidencia Frida del cierre del editor: `ev_a858f54b6e653ffd62e7cb207e8ea44b01c41d767781424a9e832210475a6655`. La sesión se cerró al terminar la captura; Xbox Accessories y el mando siguieron abiertos/conectados.
- Resultado: la app oficial confirma el valor en su editor de Perfil 1/Ranura 1; la persistencia onboard sigue sin verificar. Nexo continúa guardando solo el JSON local y todavía no dispone de un escritor Xbox comprobado.

## Séptima ejecución (finalización asíncrona y correlación LB, 2026-10-17)

- FC27 permaneció cerrado. Xbox Accessories siguió abierto con el Elite conectado; la vista final volvió a mostrar Perfil 1 / Ranura 1 y LB → Menú. No se pulsaron botones físicos ni se desconectó el mando.
- La traza pasiva de `Devices.App.exe` ahora observó la finalización real de las operaciones asíncronas: el IOCTL privado `0x40001ccc` de 90 bytes acabó con estado NT `0`, 3 bytes transferidos y respuesta `01 80 1d`; una petición posterior de 35 bytes acabó con estado `0`, 59 bytes transferidos y respuesta de 59 bytes.
- Durante el cambio temporal exclusivo de LB Menú → Ver, el byte de entrada situado en el offset absoluto 41 cambió de `D2` a `D3`, y la respuesta de 59 bytes a la petición `02 1d 37` también contenía `D3`. Tras restaurar LB → Menú, ambos volvieron a `D2`. Esta correlación confirma que los datos capturados dependen de la asignación LB en el flujo de Xbox Accessories.
- **Límite:** no se ha decodificado formalmente la estructura privada ni identificado la respuesta de 59 bytes como lectura independiente de memoria física. `01 80 1d` solo demuestra una respuesta de 3 bytes al IOCTL, no un commit durable. No se demuestra almacenamiento flash/a bordo ni persistencia tras reconexión.
- Se descargó el script de observación y se cerró la sesión Frida al acabar la captura. No se ejecutó ningún IOCTL desde Nexo y no se tocó la asignación global de Windows.
- Resultado: existe una pista de protocolo más concreta y correlacionada con LB, pero no un formato de escritura/readback suficientemente validado para que Nexo escriba con seguridad. El único cambio temporal se restauró en Xbox Accessories.

## Octava ejecución (handle del driver y restauración, 2026-10-17)

- La sesión pasiva identificó el handle `0xad8` de Xbox Accessories como `\\Device\\XboxGIP_Admin`. La traza muestra una llamada de 90 bytes con LB→Ver (`D3`) y, al restaurar LB→Menú, otra de 90 bytes con `D2`; ambas se enviaron a ese mismo objeto. La pila de llamadas solo llegó a frames sin símbolos útiles de `Devices.App.exe`/WinRT.
- En el editor oficial se verificó antes que LB ya estaba asignado a Menú. Para recoger la llamada se cambió temporalmente únicamente LB a Ver y después se restauró a Menú. El perfil quedó en Perfil 1 / Ranura 1, LB→Menú, visible en Xbox Accessories. No se pulsó el mando ni se desconectó.
- La ruta `\\Device\\XboxGIP_Admin` y el paquete capturado no resuelven el formato: el búfer contiene un wrapper con punteros de proceso y la carga privada; no se ha demostrado cómo crear el handle, construir los campos dinámicos o validar un readback físico desde Nexo.
- Una prueba local sin IOCTL intentó abrir `\\.\\XboxGIP_Admin` con acceso cero desde el contexto normal del usuario y recibió Win32 `ERROR_ACCESS_DENIED (5)`. No se intentó elevar privilegios ni duplicar el handle del proceso de Accessories.
- Se descargó el hook y se cerró Frida. No se enviaron comandos desde Nexo ni se modificó el mapeo global de Windows. Esta evidencia mejora la identificación del transporte, pero no valida un escritor independiente ni persistencia tras reconexión.

## Novena ejecución (validación estática del contrato del driver, 2026-10-17)

- La llamada observada de Xbox Accessories usa `\\Device\\XboxGIP_Admin`, cuyo dispatch `DEVICE_CONTROL` enruta `0x40001ccc` a `FUN_140035b60` en el `xboxgip.sys` instalado (SHA-256 `ea57e827b3b739d89f68cb41460c3466eac0c9d2be128cd9f7eea6654a816db7`).
- El driver exige un búfer de sistema de al menos 32 bytes, valida un token de contexto de 64 bits en offset 20 y un selector en offset 8, lee la longitud en offset 28 y entrega el payload desde offset 32. Encola la operación de forma asíncrona.
- El token/contexto y el selector no se han obtenido por una interfaz pública en Nexo. La decodificación no revela el formato completo de mensajes GIP, si una operación escribe perfil o lo consulta, ni una confirmación de commit durable. La respuesta de 59 bytes observada antes tampoco está probada como lectura independiente del almacenamiento persistente.
- No se escribió al driver desde Nexo. Queda descartada la implementación de un envío directo con un paquete construido solo a partir de la captura: falta contexto válido y contrato de lectura/escritura.

## Décima ejecución (callsite de Windows.Gaming.Input, 2026-10-17)

- En `Windows.Gaming.Input.dll`, el frame capturado cae en `gipdevice.cpp` (`FUN_1800348d8`, RVA `0x34be2`); construye un envoltorio de 32 bytes más payload y llama al helper de `DeviceIoControl` con `0x40001ccc`. El helper usa `OVERLAPPED` y `GetOverlappedResult`, lo que explica que las longitudes capturadas sean resultados completados asíncronamente.
- Las llamadas públicas de `LegacyGipGameControllerProvider` documentadas permiten leer/guardar el remapeo estándar de botones por usuario. Microsoft advierte que el resultado es global para los juegos; esa API no ofrece selector de perfil/ranura Elite ni afirma escribir ese perfil en la memoria del mando. No es equivalente al objetivo de Perfil 1/Ranura 1.
- Aunque el wrapper del transporte privado estático ya se conoce, siguen sin verificarse el contrato del payload para el perfil, una interfaz pública para pedir esa operación de ranura, y el significado persistente de la respuesta. No se usó el setter global ni se enviaron IOCTL desde Nexo.

## Undécima ejecución (capacidad del paquete oficial, 2026-10-17)

- El manifiesto instalado de `Microsoft.XboxDevices_2506.2505.28001.0_x64__8wekyb3d8bbwe` declara `xboxAccessoryManagement`; Nexo se ejecuta sin identidad de paquete y el acceso directo a `\\.\XboxGIP_Admin` le fue denegado.
- Esto explica una diferencia de acceso entre Xbox Accessories y Nexo, pero no basta para habilitar el remapeo onboard: la API pública asociada solo expone el remapeo estándar global por usuario, sin perfil/ranura Elite. No se creó ni instaló un paquete de prueba ni se cambió la configuración de Windows.

## Duodécima ejecución (compatibilidad del formato, 2026-10-17)

- La tabla pública MS-GIPUSB no enumera `0x1D`; el `01 1d 37` observado es un cuerpo dentro del wrapper privado de XboxGIP, y sus tres primeros bytes no bastan para tratarlo como una cabecera GIP estándar de cuatro bytes.

## Decimotercera ejecución (buffer de perfil y acceso desde Nexo, 2026-10-17)

- En la captura de Xbox Accessories, el write de 90 bytes y el query posterior de `02 1d 37` devuelven la misma configuración; el byte de LB es `D3` para Ver y `D2` para Menú. Es evidencia de readback inmediato, no de persistencia después de reconectar.
- El token `0x00007EF4248BED7E` coincide con el serial PnP `7EED8B24F47E` leído como entero little-endian. La adquisición por serial es una inferencia; `IGipControllerApi.GetDeviceId()` aparece en el contrato WinRT, pero no se observó su llamada dinámica.
- Nexo detectó el Elite y derivó ese candidato, pero `CreateFile` de `XboxGIP_Admin` devolvió `ERROR_ACCESS_DENIED` con aperturas síncrona/solapada y acceso cero/lectura/escritura; `QueryDosDevice` confirma que el enlace apunta al objeto esperado. La SDDL estática concede GR/GW a Everyone y el token del probe es de usuario interactivo con integridad media, por lo que la causa no está resuelta. El intento acabó antes del primer IOCTL; no se envió ningún mensaje ni se alteró el mando. La política del entorno rechazó iniciar el probe elevado.
- El código experimental de Nexo solo contempla LB → Menú: lee primero el bloque, modifica un byte, comprueba el ACK y exige igualdad completa en el readback. La ruta no ha pasado aún una prueba con el mando porque Windows negó el acceso. El AK820 sigue sin writer confirmado.
- El esquema publicado por terceros para páginas de perfil `0x4D` corresponde al Elite Series 2 (`PID 0B00`, firmware 5.17). El mando de este equipo es Elite Series 1 (`PID 02E3`), que tiene un formato de entrada distinto. No se trasladan al Series 1 los offsets ni los comandos del Series 2.
- La respuesta de 59 bytes sí es el resultado completado de una petición asíncrona del driver y contiene el mismo cambio D2/D3 observado en LB; sigue sin demostrar que el dato venga de flash persistente ni define el campo de LB dentro del bloque de 55 bytes.

## Decimotercera ejecución (referencias del envío genérico, 2026-10-17)

- En `Windows.Gaming.Input.dll`, los únicos callers directos resueltos de `FUN_1800348d8` son `FUN_18003568c` y `FUN_180040ef0`. El primero envía una consulta vacía de 6 bytes; el segundo reenvía un buffer de longitud variable, pero no identifica quién arma `01 1d 37` porque su entrada llega indirectamente.
- No se puede atribuir el paquete capturado al setter público `SetStandardControllerButtonRemapping`, ni a una API pública de perfil. La construcción del bloque de 55 bytes sigue sin tener caller/interfaz identificados.

## Decimocuarta ejecución (contrato WinRT del paquete oficial, 2026-10-17)

- El manifiesto instalado registra los protocolos `xboxaccessories` y `ms-xbl-2d34608b`, clases WinRT in-process de `Devices.Abstraction.dll` y tareas de fondo. No declara `windows.appService` ni un servicio out-of-process con contrato para guardar una ranura. El registro del protocolo acredita activación de la app, no una API de guardado.
- La metadata completa de `Devices.Abstraction.winmd` (SHA-256 `f75e3083c9058bd6928655ade395b6d33ae59d4009fa499acc3338c2bacb5ed6`) declara en `IGipRemappingApi` solo `CreateDefaultStandardControllerMapping`, `GetStandardControllerButtonRemapping(User, bool)` y `SetStandardControllerButtonRemapping(User, mapping, bool)`. Ninguna firma acepta ranura/perfil.
- `IProgrammableKeyDetailsStatics` sí tiene una operación de borrado por controlador y ranura, pero no una operación equivalente para asignar o guardar un perfil. No se llamó al borrado.
- La metadata también declara `MessageSender.CreateForController`/`SendAsync` y `MessageRequest` con `MessageId`, `Data`, `Class`, `Transport` y límite de respuesta. Es un transporte de mensajes genérico, no un setter de perfil documentado. Sus cuerpos de implementación no están en el `.winmd`; aún no se ha enlazado esa interfaz con la petición de 90 bytes ni con el byte LB `D2/D3`.
- Evidencia REA de las firmas: `ev_8475ef31cce8dc4c2fac2e2ef39e94a39926af6bdb23ae8163bbf7c7ff377b38`; resumen de metadata: `ev_8e8a7dca666e0af59885596583faf30e7b37cef870f815be83df978cd758f1bc`.
- Resultado: las clases públicas del paquete confirman remapeo estándar global y transporte GIP genérico; ninguna confirma escritura de Perfil 1/Ranura 1 en la memoria onboard del Elite Series 1. No se envió ningún mensaje ni se modificó el mando.

Fuentes: [contrato de LegacyGipGameControllerProvider](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.preview.legacygipgamecontrollerprovider?view=winrt-28000), [capacidad xboxAccessoryManagement](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations).

## Decimoquinta ejecución (transporte de MessageSender, 2026-10-17)

- El análisis de `Devices.Abstraction.dll` identifica `MessageSender.SendAsync`: para `Transport == 1`, la implementación rechaza `MessageId/IOCTL == 0x40001d94` con `IOCTL is not allowed`; para `Transport == 2`, valida que el `MessageId` quepa en un byte y despacha GIP mediante una llamada indirecta al objeto controller (vtable `+0x38`).
- La llamada indirecta no quedó resuelta y no se observó una llamada directa a `DeviceIoControl(0x40001ccc)` ni la construcción del token/selector que exige el wrapper del driver. Por eso no se puede afirmar que esta ruta genérica alcance la operación de perfil observada en Xbox Accessories.
- Evidencias REA: `ev_fb5bc90274f732a1f970a2d806e08fde37adb72395fb62851b08132b63dca151` (`SendAsync`), `ev_3e9a75d6276dd845b50bd190ca345f48b6e56616f9834b2f1d74c24c7201e057` (rechazo de transporte IOCTL), `ev_d76606bca305494c66981ba8d1591f6c4335881b92e0cc173510f8d4cc0dcc66` (despacho GIP indirecto) y `ev_ddc7c511c5ac8b8a58901754993d7ab0b68b3b38970cf4d7a46010e35e9c68d8` (registro activable del paquete).
- Resultado: `MessageSender` es una superficie interna de mensajería GIP, no un camino demostrado para guardar la ranura del Elite 1. No se llamó a `SendAsync` ni se escribió al mando.

## Decimosexta ejecución (IOCTL 0x40001d94 e interfaz de función, 2026-10-17)

- Corrección al análisis de coincidencias de bytes: el dispatcher `FUN_1400045e0` de `xboxgip.sys` sí contiene una rama para `0x40001d94`. Devuelve un DWORD `2` si el buffer de salida mide al menos cuatro bytes; la rama no lee token, selector ni datos de entrada y no cambia estado aparente. Es un IOCTL distinto de `0x40001ccc` y no prepara esa operación. Evidencia REA: `ev_99fefd6faa64f287f800429a8593fd9ceb62da71688504aa22d3c36be6a9fe76`, rama `ev_b2a35e556eca8ee42378637bc61fba25a878ff039c57d9365304ed3301b21667`.
- Microsoft SDL marca para el Elite Series 1 (`PID 02E3`) la característica `GIP_FEATURE_CONSOLE_FUNCTION_MAP` como filtrada; para Series 2 la habilita. Su driver solo utiliza esa característica para exponer el botón Share en el informe de entrada. Esto no aporta un método de escritura ni valida el protocolo de perfiles del Elite 1: [fuente SDL de referencia](https://0x4248.dev/nexus/ext/SDL/src/joystick/hidapi/SDL_hidapi_gip.c.html).
- Resultado: el IOCTL `0x40001d94` no es una vía de guardado del perfil. Sigue sin identificarse una interfaz de escritura y lectura de Perfil 1/Ranura 1 adecuada para Nexo. No se envió ningún IOCTL ni mensaje al mando.

## Decimoséptima ejecución (consulta por XboxGIP, 2026-10-18)

- El mando siguió conectado y Windows lo identificó como `USB\VID_045E&PID_02E3\7EED8B24F47E`. Se abrió `\\.\XboxGIP` y se envió únicamente la consulta observada `02 1D 37` dentro del envoltorio GIP; no se envió una petición de escritura ni se generó entrada de juego.
- La consulta completó con 59 bytes y el prefijo esperado `02 80 1D 37`. El byte de asignación LB (offset 10 de la respuesta) vale `D2`, que coincide con Menú. Esto confirma la asignación actual expuesta por el controlador GIP, no persistencia en flash ni un cambio nuevo aplicado por Nexo.
- La apertura de `XboxGIP` y el readback anterior a cualquier escritura ya funcionan desde el usuario normal. `XboxEliteProfileWriter` se cambió a ese objeto; su ruta de escritura D3→D2 todavía no se ha ejecutado. El token sigue derivándose de un serial PnP emparejado a la observación previa, no de `IGipControllerApi.GetDeviceId()`.

## Decimoctava ejecución (causa de acceso a XboxGIP_Admin, 2026-10-18)

- REA relacionó el callback `IRP_MJ_CREATE` de `XboxGIP_Admin` con una comprobación de token: acepta el contexto System/Local bajo la excepción de desarrollo o un token AppContainer con la capability Xbox Accessories. La inspección en vivo mostró esa capability en `Devices.App.exe` y no en Nexo desktop. La DACL permisiva permite llegar al objeto, pero el callback del driver rechaza después el proceso no empaquetado.
- No se intentó eludir la comprobación ni duplicar el handle de Xbox Accessories. Se usa `XboxGIP` para la consulta GIP que ya respondió correctamente; el significado de persistencia sigue pendiente de una prueba válida.

## Decimonovena ejecución (envío idempotente LB → Menú desde Nexo, 2026-10-18)

- Se ejecutó `XboxEliteProfileWriter` del código de Nexo contra el Elite identificado. Leyó los 55 bytes de perfil, confirmó LB como `D2` (Menú), envió el bloque sin alterar ningún otro byte, recibió ACK `01 80 1D` y volvió a leer los 55 bytes, idénticos al bloque enviado.
- LB ya estaba en Menú antes del envío; no se cambió a Ver ni se pulsó ningún control. La operación verifica el transporte de escritura y readback inmediato desde Nexo sin cambiar el mapeo visible. No comprueba una transición `D3`→`D2`, el cambio de ranura ni persistencia después de reconexión.

## Vigésima ejecución (contraste del mensaje 0x4D, 2026-10-18)

- Se contrastó el mensaje `0x4D` con SDL y el driver GIP de Linux. Ambos lo describen como un mensaje no documentado de Elite Series 2 y lo usan para habilitar informes raw; sus quirks filtran `GIP_FEATURE_CONSOLE_FUNCTION_MAP` en el Elite Series 1 (`VID_045E&PID_02E3`). No es evidencia de lectura/escritura de perfiles Series 1 y no se debe reutilizar en este mando.
- La fuente pública de Microsoft SDL confirma estos límites por PID: [SDL_hidapi_gip.c](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_gip.c). El parche de Linux confirma el uso del mensaje `0x4D` para informes raw: [xbox_gip patch](https://www.spinics.net/lists/linux-input/msg102354.html).
- Resultado: no se halló una operación pública que seleccione o persista la ranura física del Elite 1. El writer de Nexo conserva únicamente la evidencia del intercambio inmediato ya documentada; no se envió `0x4D`.

## Vigésima primera ejecución (transición LB desde Nexo 0.3.6, 2026-10-18)

- Corrección de esta nota: la captura diferencial de Xbox Accessories establece `D2` para LB→Menú y `D3` para LB→Ver. `DC` fue el byte incorrecto de una versión anterior de Nexo; la conclusión D2 normal/DC Menú era errónea.
- Se cerró Xbox Accessories antes de probar Nexo, se mantuvo FC27 cerrado y el mando conectado. En Perfil 1, el perfil local indicaba originalmente LB→Botón A. Desde la pestaña Asignación se cambió solo LB a Botón Menú; el guardado automático de Nexo 0.3.6 ejecutó el writer.
- Tras el cambio, el JSON local de Nexo reflejaba `LB: Botón Menú` y una lectura GIP devolvió `DC` en el byte de LB. Como `DC` no corresponde a Menú según la captura diferencial, este readback no valida el valor de remapeo escrito por esa versión.
- En una observación posterior, el usuario informó que el ajuste de Nexo sí cambió el comportamiento temporalmente, pero al abrir Xbox Accessories volvió a la configuración guardada allí. Esto apunta a una diferencia entre el estado activo y la copia local de Xbox Accessories; no valida la persistencia del byte `DC`.
- La consulta externa confirma solo el valor expuesto inmediatamente por GIP. La persistencia en la ranura y el resultado XInput siguen sin quedar demostrados.
- No se tocó ningún otro botón, no se usó el remapeo global de Windows y no se cortó la conexión del mando.

## Vigésima segunda ejecución (contraste de entrada y alcance del tester, 2026-10-18)

- El usuario confirmó que, tras configurar LB→Menú, la entrada observada seguía siendo LB. Por ello, el readback `DC` de la ejecución anterior no demuestra que el remapeo llegue al flujo XInput ni a los juegos.
- La página `hardwaretester.com/gamepad` detecta `Xbox 360 Controller`, XInput estándar, conectado; durante esta observación B0–B20 y los cuatro ejes están en cero. La página solo muestra entradas recibidas y no puede generar una pulsación real de LB, así que esta lectura en reposo no valida ni refuta el remapeo.
- La API pública `LegacyGipGameControllerProvider` persiste un mapeo global de Windows por usuario, no expone ranura Elite y exige `xboxAccessoryManagement`. Nexo está ejecutándose sin identidad de paquete; no se invocó el setter ni se modificó la configuración global.
- Resultado: el remapeo efectivo sigue sin verificarse; no se envió entrada sintética, no se cambió de ranura y no se desconectó el mando.

## Vigésima segunda ejecución (lectura en vivo Nexo 0.3.7, 2026-10-18)

- El archivo local de `xbox-one-elite` indicaba Perfil 1 con `LB: LB`. Se corrigió únicamente esa entrada local a `Botón Menú` y se invocó el writer validado para el Xbox One Elite (`VID_045E&PID_02E3`). Respondió «LB ya estaba en Menú; lectura GIP confirmada»; por tanto, esta ejecución no necesitó enviar una escritura nueva. El valor GIP `DC` confirma configuración expuesta, no el resultado físico al pulsar LB.
- La búsqueda real de Nexo devolvió la ficha `xbox-one-elite` asociada a XInput slot 0 (`USB / HID + XInput`). La misma clase `XboxInputViewModel.Refresh` usada por la aplicación devolvió `IsConnected=True`, estado `0000` y «Botones: ninguno». Un observador pasivo de XInput esperó tres minutos y no vio una transición de LB ni Start porque no hubo pulsación física durante la ventana.
- Nexo 0.3.7 muestra ahora esa lectura también en Asignación y etiqueta `0x0010` como «Menú (Start)». Se compiló, pasó los 8 grupos de self-check, se publicó en `dist/package-0.3.7` y el acceso directo de la raíz apunta a esa versión. El navegador del tester no se inspeccionó porque Computer Use detuvo la sesión al no poder verificar la URL actual.
- Resultado: detección y ruta de lectura en Nexo confirmadas. El efecto del remapeo en el informe físico/XInput, la ranura de memoria correspondiente y persistencia tras reconectar siguen pendientes de una pulsación física y una prueba de reinicio autorizada.

## Vigésima tercera ejecución (estado de ranura y contraste de la secuencia XAC, 2026-10-18)

- Lectura de solo observación de Accesorios de Xbox: el Elite informa `Perfil 1 en la ranura 1`, `FIFA sin joys en la ranura 2` y `Ranura 1 activa`. El perfil que se está verificando sí es el activo; una selección de ranura equivocada no explica por sí sola que LB siga llegando como LB.
- El proceso Nexo abierto en ese momento era `dist/package-0.3.6/nexohub.exe`; tras esta ejecución, el acceso directo raíz se actualizó a `dist/package-0.3.8/NexoHub.exe`. El proceso abierto sigue siendo 0.3.6 hasta que se reinicie.
- La captura pasiva de Xbox Accessories contiene peticiones privadas con cuerpo de un byte `03` repetidas antes, entre y después de escrituras `01 1D 37` y consultas `02 1D 37`; cada una responde `03 80 1D`. No se ha identificado si son consulta/sincronización u otra operación. No hay evidencia de que sea un commit ni una activación de perfil, por lo que no se añade al writer.
- Resultado: se descarta la ranura activa como explicación y se conserva el diagnóstico anterior: ACK y readback GIP no prueban salida remapeada por XInput. No se envió ningún comando nuevo ni se cambió otro botón.

## Vigésima cuarta ejecución (sincronización de Perfil 1 de Xbox Accessories, 2026-10-18)

- La app que seguía abierta era Nexo 0.3.6; el código fuente indica 0.3.8. El perfil activo de Nexo (`0bfe3cf7-9208-4f98-99e6-706dc42247d0`, Perfil 1 / ranura 1) tenía `LB: Botón Menú`.
- La copia local WGS de Xbox Accessories para ese mismo perfil tenía `HardwareUpperLeftPaddle=LeftBumper`, pero no definía `HardwareLeftBumper`; el importador de Nexo interpreta la ausencia como asignación predeterminada `LB → LB`. `configDisambig` confirma que el GUID es la ranura 1. Esto coincide con la configuración que el usuario vio restaurarse al abrir Accessories.
- Nexo sincronizó únicamente `HardwareLeftBumper=Menu` en esa copia local, conservó los otros campos, creó respaldo bajo `%LOCALAPPDATA%\NexoHub\DeviceProfiles\XboxAccessoriesBackups` y verificó la lectura de vuelta. La copia anterior quedó respaldada sin cambios.
- La compilación de Nexo 0.3.8 y los diez grupos de self-check finalizaron correctamente; el paquete se generó en `dist/package-0.3.8`. No se envió ninguna pulsación sintética ni se desconectó el mando. La app Accessories no se reinició durante esta verificación.
- Después se ejecutó una vez el writer de Nexo 0.3.8: cambió únicamente el byte de LB `DC → D2`, recibió ACK `01 80 1D` y verificó el readback inmediato. Esto confirma el cambio GIP en vivo reportado por el usuario, no persistencia en flash.
- El tester web muestra el mando conectado, pero botones y ejes están en cero. Hay un observador XInput pasivo de 10 minutos activo; todavía no ha registrado una entrada. No se enviaron pulsaciones artificiales ni se desconectó el mando.
- Resultado: la copia local que provocaba el restablecimiento ya coincide con Nexo y el writer volvió a aplicar LB→Menú. Falta comprobar que Accessories reabra Perfil 1 sin revertir y observar la salida XInput ante una entrada natural; el archivo WGS y el readback GIP no confirman flash onboard.

## Vigésima quinta ejecución (aplicación explícita del destino LB, 2026-10-18)

- El cambio de destino hecho desde la lista de Asignación se guarda inmediatamente; el writer recibe ese destino solo cuando la fuente elegida es LB. Guardar vibración, curvas, perfiles o asignar otro botón físico ya no vuelve a enviar el valor LB almacenado.
- El writer modifica solo el byte LB, exige ACK y lectura GIP idéntica. La copia WGS local se actualiza después de esa confirmación y mantiene respaldo. Menú/Ver tienen capturas comparativas; los códigos de otros destinos se derivan de `GipButtonMapVal` y no se han comprobado individualmente con XInput.
- Compilación Release: correcta, 0 avisos/errores. Self-checks: 10 grupos correctos. Se publicó `dist/package-0.3.9` y el acceso directo raíz apunta a ese ejecutable.
- En esta ejecución no se escribió al mando, no se enviaron pulsaciones y no se desconectó; el cambio se aplicará cuando Nexo 0.3.9 reciba una selección explícita de destino para LB. El remapeo de botones físicos distintos de LB sigue siendo local.
