# Nexo Device Hub: instrucciones para Codex

## Objetivo del producto

Construir una aplicación propia para Windows que reúna la gestión de periféricos de varios fabricantes en una sola interfaz. Debe poder ampliarse mediante adaptadores por fabricante, modelo y función. El diseño puede inspirarse en la claridad de Logitech G HUB, pero la identidad visual y el código son propios.

Prioridades del usuario:
- Configurar Logitech, ASTRO, mandos Xbox y dispositivos que añada en el futuro.
- Guardar los ajustes en la memoria del dispositivo cuando el modelo lo permita y se haya verificado.
- Crear macros que graben teclas y acciones de ratón y asignarlas a botones.
- Mantener perfiles locales como alternativa cuando el hardware no permita guardar ajustes a bordo.

## Estado actual: versión 0.3.0

- Aplicación de escritorio WPF para Windows, con .NET 10.
- La detección agrupa interfaces HID por dispositivo físico y las combina con XInput y endpoints conocidos de Windows. La búsqueda automática se repite cada 12 segundos; receptores y controladores internos aparecen detrás de **Mostrar otros**.
- El inventario de este PC reconoce ASTRO A50, Yeti Classic, Xbox One Elite, el Logitech G PRO Wireless (`VID_046D&PID_C539`, nombre PnP `PRO WIRELESS`) y un AJAZZ AK820 con variante Pro probable por la diferencia de PID USB. El PID C547 por sí solo no identifica el modelo Logitech emparejado.
- Cada ficha tiene una miniatura vectorial por tipo, configuración específica y una lista de capacidades con evidencia y estado.
- El editor AJAZZ muestra el layout real de 81 teclas obtenido del paquete instalado; permite previsualizar y guardar colores por tecla en el perfil local Nexo.
- Los niveles de entrada/salida de Yeti y ASTRO se leen y aplican a endpoints de Windows por Core Audio; no se confunden con cambios al hardware.
- La ficha Xbox consulta XInput 12 veces por segundo mientras está seleccionada y enseña botones, LT/RT y sticks. Los umbrales de gatillo 0–10 son un perfil Nexo local.
- DPI/polling Logitech, iluminación AJAZZ, EQ ASTRO y demás valores de las fichas se guardan como JSON en `%LOCALAPPDATA%\NexoHub\DeviceProfiles`. La validación impide guardar valores fuera de los rangos admitidos.
- La biblioteca ASTRO importa, muestra y exporta copias de `.agp` y `.astroeq`; todavía no aplica ajustes al auricular.
- Las macros graban teclas, clics y rueda. Reproducen por software y pueden asignarse a los botones laterales 4/5 mientras Nexo está abierto. La reproducción empieza desactivada y las secuencias no se guardan en el ratón.
- No se escriben informes HID, firmware ni perfiles a la memoria de los dispositivos. La única escritura de ajustes del equipo es el cambio explícito de volumen/nivel a Windows Core Audio.
- `docs/device-inventory-analysis.md` recoge las observaciones PnP, los perfiles y recursos instalados de G HUB/AJAZZ, el estado de los endpoints ASTRO y la cobertura disponible de REA. El driver AJAZZ resuelve `HidD_SetFeature`, pero el proveedor de análisis nativo falla en este Windows y el protocolo sigue pendiente.

## Skills de Codex

Usar estas skills cuando la tarea corresponda:
- ponytail: cambios de código pequeños y mantenibles, con explicación clara.
- frontend-design: diseño o cambios de la interfaz.
- reverse-engineer-anything (REA): activarla cuando el usuario pida más ingeniería inversa de binarios, protocolos o comportamiento de dispositivos. Leer la skill vigente y usar las herramientas REA para recoger evidencia; distinguir resultados estáticos, observaciones en ejecución e inferencias. No enviar escrituras HID o firmware basadas en conjeturas.
- graphify: consultas de arquitectura y relaciones del código cuando aporte valor.

Las skills están instaladas en el entorno del usuario, no son dependencias del proyecto. Consultar sus instrucciones actuales en Codex cuando se apliquen.

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
- dist/: paquete autónomo Windows x64 de la versión 0.3.0.

Requiere el SDK de .NET 10. Desde la raíz del repositorio, compila y ejecuta las comprobaciones con los comandos estándar:

PowerShell:
    dotnet build .\NexoHub.csproj -c Release
    dotnet run --project .\checks\NexoHub.Checks\NexoHub.Checks.csproj -c Release

Para generar un paquete autónomo de Windows x64:

    dotnet publish .\NexoHub.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o .\dist

Los binarios publicados, salidas de compilación, archivos temporales y el acceso directo local están excluidos de Git. El paquete se genera en cada equipo.
