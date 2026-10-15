# Inventario e ingeniería inversa de dispositivos

Fecha de comprobación: 16 de octubre de 2026. Equipo de desarrollo Windows del usuario. El inventario depende de lo conectado y de los nombres publicados por sus controladores.

## Evidencia observada en el PC

| Nombre en Nexo | Evidencia de Windows / software instalado | Confianza y límites |
|---|---|---|
| Xbox One Elite | El nodo PnP se presenta como `Mando Xbox One Elite`, VID `045E`, PID USB padre `02E3`. XInput informa slot 1; una colección HID de entrada usa PID `02FF`. | Modelo leído de Windows. Nexo lee el estado XInput y no escribe perfiles en el mando. |
| ASTRO A50 | HID `VID_9886&PID_0015`; los endpoints activos de Windows incluyen micrófono Voice, salida Game y salida Voice. | Asociación por VID/PID y nombres de endpoint. Se excluyó el endpoint «Línea … Game» para no duplicar el auricular como otra tarjeta. |
| Yeti Classic | Audio `VID_B58E&PID_9E84`, endpoints «Micrófono (Yeti Classic)» y «Altavoces (Yeti Classic)». La base local `%LOCALAPPDATA%\LGHUB\settings.db` contiene la firma `MICROPHONE.yeti_classic` y la ranura `yeti-classic`. | Identificación alta por nombre de Windows y firma de G HUB. VO!CE y DSP siguen sin implementarse. |
| Logitech PRO X SUPERLIGHT | Windows detecta `VID_046D&PID_C547` como receptor LIGHTSPEED. La interfaz de G HUB mostrada por el usuario presenta un PRO X SUPERLIGHT; el receptor adicional `VID_046D&PID_C52B` se deja como elemento auxiliar. | La asociación del receptor C547 con el ratón se infiere al combinar la evidencia del equipo y la pantalla G HUB; el ID USB por sí solo no revela el modelo del ratón. DPI y memoria integrada no se escriben. |
| AJAZZ AK820 | Windows identifica AK820 en USB `VID_0C45&PID_8006` y publica también una instancia Bluetooth. El paquete AJAZZ Beta 1.0.0.5 declara USB `8009`/`800A` y receptor 2.4 GHz `FDFD`. | AK820 está confirmado; AK820 Pro es probable, pero el PID `8006` no figura en el `config.xml` local. La traducción inglesa del controlador avisa que los ajustes Bluetooth no están soportados. |

Se ocultan de forma inicial los receptores USB y el controlador interno ASUS Aura `VID_0B05&PID_19AF`; el botón **Mostrar otros** permite consultarlos. El objetivo del filtro es presentar periféricos físicos sin mezclar cada colección HID con un aparato separado.

## Funciones que ya se probaron

- La enumeración agrupa HID por los ancestros PnP y el `ContainerId`; los endpoints de audio conocidos se asocian al mismo modelo.
- Core Audio pudo enumerar 10 endpoints activos en este PC, incluidos el Yeti y el ASTRO A50. Nexo lee el nivel y, al pulsar **Aplicar en Windows**, cambia el endpoint de Windows y comprueba el valor leído de vuelta. No cambia memoria interna ni EQ.
- XInput informa el mando conectado en el slot 1. La pantalla seleccionada lee botones, dos gatillos y dos ejes cada 80 ms. Esta función es de lectura.
- Los perfiles Nexo se serializan como JSON por modelo en `%LOCALAPPDATA%\NexoHub\DeviceProfiles`. La app valida los rangos de DPI/polling, RGB, gatillos y EQ antes de guardar.
- El icono de cada ficha es una ilustración vectorial propia por tipo de periférico; no se presentan imágenes genéricas como fotografías de producto.
- El editor AJAZZ muestra el mapa real de 81 teclas del `rgb-keyboard.xml`; los colores por tecla se guardan en el JSON local de Nexo y se pueden borrar desde el mapa.
- El inventario, la carga de audio, los perfiles, las macros y la selección de adaptadores se comprueban con `checks/NexoHub.Checks`.

## Ingeniería inversa local

- G HUB: inspección de solo lectura de `settings.db` y su WAL; se encontró la firma del Yeti Classic. La interfaz aportada identifica el ratón como PRO X SUPERLIGHT.
- ASTRO Command Center: se dispone de la nota `docs/astro-command-center-analysis.md` y se asociaron el VID/PID y los endpoints al A50 presente. Los IDs de algunos comandos no bastan para reconstruir un reporte de escritura completo.
- `config.xml` llama al paquete `AJAZZ AK820Pro`, versión Beta 1.0.0.5, y define los PID USB `8009` y `800A` y el receptor `FDFD` (SHA-256 `025e16c70edfb1eed0c5401a464ea711273586a1b9a909d68764a512f1a88e9c`). El teclado observado anuncia `8006`, así que no se conoce si corresponde a otra revisión o a otro modo de conexión.
- `layouts/rgb-keyboard.xml` define 81 teclas, seis filas, una pantalla de 128 × 128, un valor de espera de comando de 35 ms y escalas de brillo/velocidad de 0–5 (SHA-256 `55bb5e48628a7434b7b0b9529d650a0553def3bf54cb419c9d83aab6effd6752`). Se usa únicamente el mapa de teclas para la previsualización local.
- `language/1033.lan` incluye la advertencia de que la configuración Bluetooth no está soportada y que hay que usar USB o 2.4 GHz. También nombra opciones como Colorful, Rainbow, Reverse Rainbow, Gradient, Spectrum Cycle y Off; Nexo las presenta como valores locales, sin asociarlas a bytes de comando (SHA-256 `e4b117ddae7e346347df6e4ff01d1705a22bc24602c17887b962975cadc0153d`).
- El ejecutable AJAZZ `DeviceDriver.exe` es PE32 x86, SHA-256 `14b6919b59b86f6bd1465e544032b41ca4f12ca35e36bd81d3ab33aca14a605a`. Un parser local de solo lectura observó importaciones SetupAPI, `LoadLibrary`/`GetProcAddress` y cadenas como `hid.dll`, `HidD_GetAttributes`, `HidD_GetFeature`, `HidD_SetFeature`, `HidP_GetCaps` y funciones de datos preparsed. Esa combinación es compatible con resolver la API HID dinámicamente y usar feature reports, aunque no confirma qué ruta se ejecuta. No revela por sí sola longitud, cabecera, bytes de comando, confirmaciones ni persistencia.
- REA pudo leer el directorio PE de recursos, pero `inspect_with_objdump` y Rizin no pudieron arrancar en este Windows (`ERR_REA_WINDOWS_NATIVE`). Ghidra/IDA/Hopper tampoco están disponibles como proveedor nativo de REA en este entorno. Por eso no hay pseudocódigo validado ni se afirma haber reconstruido el protocolo AJAZZ.

## Límite de escritura y persistencia

No se enviaron escrituras HID o firmware en estas pruebas. Para activar ajustes por modelo hay que comprobar primero el dispositivo exacto, el informe y su longitud, los bytes de comando, la secuencia, el acuse y el valor leído de vuelta. Para afirmar que algo persiste en el periférico, hay que releerlo después de desconectar y volver a conectar. Hasta entonces los perfiles editables pertenecen a Nexo y a este PC.
