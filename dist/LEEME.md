# Nexo Device Hub 0.3.0

Paquete autónomo para Windows x64. Abre `NexoHub.exe`; no requiere instalar el SDK de desarrollo ni copiar archivos adicionales.

La pantalla **Dispositivos** agrupa interfaces HID y endpoints de audio conocidos. Oculta receptores e interfaces auxiliares hasta que pulses **Mostrar otros**. El acceso directo de la raíz y `NexoHub.exe` abren este paquete autónomo.

## Funciones listas

- ASTRO A50 y Yeti Classic: niveles de endpoints de Windows con lectura posterior.
- Xbox One Elite: lectura XInput en vivo de botones, gatillos y sticks; perfiles LT/RT locales de 0–10.
- Logitech G PRO Wireless: editor y perfil local para cuatro niveles DPI y polling; todavía no se aplica al ratón.
- AJAZZ AK820: mapa real del driver con 81 teclas, vista previa de color por tecla y perfiles locales. El paquete AK820 Pro admite USB `8009`/`800A` o receptor 2.4 GHz `FDFD`; el teclado detectado anuncia `8006`.
- Macros: teclas, clics y rueda, asignación a botones laterales 4/5 por software, con Nexo abierto.
- Perfiles ASTRO: importar, inspeccionar y exportar copias `.agp` / `.astroeq`.

Los perfiles Nexo se guardan en `%LOCALAPPDATA%\NexoHub`. Los cambios de audio afectan a Windows; los ajustes de periféricos todavía no se escriben al firmware ni a la memoria integrada. Cada función pendiente aparece indicada en su ficha.
