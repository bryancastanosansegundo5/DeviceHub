# Nexo Device Hub: instrucciones para Codex

## Objetivo del producto

Construir una aplicación propia para Windows que reúna la gestión de periféricos de varios fabricantes en una sola interfaz. Debe poder ampliarse mediante adaptadores por fabricante, modelo y función. El diseño puede inspirarse en la claridad de Logitech G HUB, pero la identidad visual y el código son propios.

Prioridades del usuario:
- Configurar Logitech, ASTRO, mandos Xbox y dispositivos que añada en el futuro.
- Guardar los ajustes en la memoria del dispositivo cuando el modelo lo permita y se haya verificado.
- Crear macros que graben teclas y acciones de ratón y asignarlas a botones.
- Mantener perfiles locales como alternativa cuando el hardware no permita guardar ajustes a bordo.

## Estado actual: versión 0.3.12

- Aplicación de escritorio WPF para Windows, con .NET 10.
- La detección agrupa interfaces HID por dispositivo físico y las combina con XInput y endpoints conocidos de Windows. La búsqueda automática se repite cada 12 segundos; receptores y controladores internos aparecen detrás de **Mostrar otros**.
- El inventario de este PC reconoce ASTRO A50, Yeti Classic, Xbox One Elite, el Logitech G PRO Wireless (`VID_046D&PID_C539`, nombre PnP `PRO WIRELESS`) y un AJAZZ AK820 con variante Pro probable por la diferencia de PID USB. El PID C547 por sí solo no identifica el modelo Logitech emparejado.
- Cada ficha tiene una miniatura vectorial por tipo, configuración específica y una lista de capacidades con evidencia y estado.
- El editor AJAZZ muestra el layout real de 81 teclas obtenido del paquete instalado; permite previsualizar y guardar colores por tecla en el perfil local Nexo.
- Los niveles de entrada/salida de Yeti y ASTRO se leen y aplican a endpoints de Windows por Core Audio; no se confunden con cambios al hardware.
- La ficha Xbox consulta XInput 12 veces por segundo mientras está seleccionada y enseña botones, LT/RT y sticks. Los umbrales de gatillo 0–10 son un perfil Nexo local.
- DPI/polling Logitech, iluminación AJAZZ, EQ ASTRO y demás valores de las fichas se guardan como JSON en `%LOCALAPPDATA%\NexoHub\DeviceProfiles`. La validación impide guardar valores fuera de los rangos admitidos.
- La ficha del PRO WIRELESS C539 permite consultar manualmente la versión HID++, las features usadas por el filtro estático de OMM y el byte bruto de tipo HID++; la consulta es informativa, no periódica, no confirma por sí sola compatibilidad OMM y no modifica ajustes.
- La biblioteca ASTRO importa, muestra y exporta copias de `.agp` y `.astroeq`; todavía no aplica ajustes al auricular.
- Las macros graban teclas, clics y rueda. Reproducen por software y pueden asignarse a los botones laterales 4/5 mientras Nexo está abierto. La reproducción empieza desactivada y las secuencias no se guardan en el ratón.
- Nexo 0.3.10 importa los nombres y ajustes de perfiles Elite desde la copia WGS de Xbox Accessories asociándolos por GUID, refleja remapeos no predeterminados en la vista previa y crea una fila no editable cuando una ranura apunta a un perfil cuyo archivo local falta. La ranura activa solo se infiere de forma de solo lectura cuando la asignación LB leída coincide con exactamente una ranura; si coincide con ambas o ninguna, los dos indicadores quedan neutros. En el WGS actual de este PC, `slot2` apunta a un GUID sin archivo y `FIFA sin joys` no está asignado a una ranura, así que no se atribuye a ese perfil. REA inspeccionó `Devices.Abstraction.winmd`: el API público de remapeo estándar no acepta una ranura ni expone la posición del switch. El usuario confirmó anteriormente LB→Menú; esta revisión no interactuó con el mando, no confirmó el cambio físico de switch ni persistencia en flash. La ruta Admin de Accessories requiere `xboxAccessoryManagement`.
- Nexo 0.3.12 afina la portada Xbox: reduce el mando al tamaño de la referencia, centra el perfil y el título, muestra los callouts de sticks/gatillos y solo las asignaciones configuradas, y sigue el flujo portada→perfiles→editor. Los indicadores priorizan la ranura inferida por LB; si no se puede distinguir, la portada señala el perfil que se está previsualizando. Esta lectura no escribe ni desconecta el mando.
- Los demás ajustes Xbox siguen siendo perfiles locales. REA identificó el writer AJAZZ de mapas (576 bytes en nueve Feature Reports), pero no un readback del mapa; Nexo no lo envía.
- `docs/device-inventory-analysis.md` recoge las observaciones PnP, los perfiles y recursos instalados de G HUB/AJAZZ, el estado de los endpoints ASTRO y la cobertura disponible de REA. El driver AJAZZ resuelve `HidD_SetFeature`, pero el proveedor de análisis nativo falla en este Windows y el protocolo sigue pendiente.

## Skills de Codex

Usar estas skills cuando la tarea corresponda:
- ponytail: cambios de código pequeños y mantenibles, con explicación clara.
- frontend-design: diseño o cambios de la interfaz.
- reverse-engineer-anything (REA): activarla cuando el usuario pida más ingeniería inversa de binarios, protocolos o comportamiento de dispositivos. Leer la skill vigente y usar las herramientas REA para recoger evidencia; distinguir resultados estáticos, observaciones en ejecución e inferencias. No enviar escrituras HID o firmware basadas en conjeturas.
- graphify: consultas de arquitectura y relaciones del código cuando aporte valor.

Las skills están instaladas en el entorno del usuario, no son dependencias del proyecto. Consultar sus instrucciones actuales en Codex cuando se apliquen.

## Uso de subagentes

- En tareas amplias de réplica, considera cinco frentes independientes: analizar la aplicación objetivo, explorar Nexo, desarrollar la interfaz, desarrollar la lógica e investigar el protocolo mediante ingeniería inversa. Usa un agente por frente solo cuando pueda aportar valor y avanzar en paralelo; omite los innecesarios. Evita que editen los mismos archivos; al terminar, detén cualquier agente que siga trabajando sin una tarea pendiente. El agente principal coordina, integra y valida.

## Reglas de implementación

- No presentar una función como compatible si solo está detectado el dispositivo. Mostrar el estado real por función.
- Implementar soporte con adaptadores específicos por modelo y mantener un adaptador HID genérico de solo inventario.
- No enviar reportes HID de escritura basados únicamente en IDs de comando o conjeturas. Verificar modelo, longitud, cabecera, secuencia, confirmación y valores leídos de vuelta.
- Para afirmar persistencia a bordo, probar lectura antes y después de escribir, reconexión o reinicio y lectura posterior.
- No aplicar firmware ni usar paquetes de firmware sin una solicitud explícita.
- Mantener la grabación/reproducción de macros desactivada por defecto; liberar teclas/botones si la reproducción se interrumpe.
- Conservar la interfaz oscura original de Nexo y diseñar controles accesibles y claros.

## Estructura y comandos

- NexoHub.csproj: aplicación WPF.
- checks/NexoHub.Checks: comprobaciones de perfiles, macros y selección de adaptador.
- docs/: notas de análisis técnico.
- dist/: paquete autónomo Windows x64 de la versión 0.3.12.

Requiere el SDK de .NET 10. Desde la raíz del repositorio, compila y ejecuta las comprobaciones con los comandos estándar:

PowerShell:
    dotnet build .\NexoHub.csproj -c Release
    dotnet run --project .\checks\NexoHub.Checks\NexoHub.Checks.csproj -c Release

Para generar un paquete autónomo de Windows x64:

    dotnet publish .\NexoHub.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist

Los binarios publicados, salidas de compilación, archivos temporales y el acceso directo local están excluidos de Git. El paquete se genera en cada equipo.

## Preferencias de trabajo del usuario

- Centrarse solo en el dispositivo que indique el usuario. Completar sus funcionalidades una por una antes de pasar a otra o a un dispositivo futuro.
- Por defecto, no controlar el escritorio ni manejar aplicaciones o dispositivos físicos. Hacerlo solo si el usuario lo indica expresamente.
- Al terminar cada tarea, generar la versión actualizada y crear o actualizar el acceso directo de la raíz para que apunte a ella.
- Tomar la aplicación objetivo como referencia para replicar su interfaz, comportamiento y transiciones, salvo que el usuario indique lo contrario.
- En la interfaz, mostrar solo información útil para elegir o configurar el dispositivo; dejar fuera detalles técnicos y diagnósticos salvo que el usuario los pida o sean necesarios para explicar una limitación.
- Mantener `AVANCES/APLICACION.md` breve, con porcentajes basados en evidencia, ordenados de mayor a menor y separados por dispositivo.
