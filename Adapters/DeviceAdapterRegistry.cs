using NexoHub.Models;

namespace NexoHub.Adapters;

public sealed class DeviceAdapterRegistry
{
    private readonly IDeviceAdapter[] _adapters =
    [
        new AstroAdapter(),
        new YetiAdapter(),
        new LogitechAdapter(),
        new XboxAdapter(),
        new AjazzAdapter(),
        new GenericHidAdapter()
    ];

    public AdapterInfo Describe(HardwareDevice device) =>
        _adapters.First(adapter => adapter.Matches(device)).Describe(device);

    private sealed class AstroAdapter : IDeviceAdapter
    {
        public bool Matches(HardwareDevice device) =>
            device.VendorId == 0x9886 || device.Manufacturer.Contains("Astro", StringComparison.OrdinalIgnoreCase);

        public AdapterInfo Describe(HardwareDevice device) => new(
            "ASTRO",
            FriendlyName(device, "ASTRO A50"),
            device.AudioEndpoints?.Count > 0 ? "Audio de Windows disponible" : "Dispositivo detectado",
            "ASTRO A50 Wireless: salida Game, salida Voice y micrófono Voice agrupados como un solo auricular.",
            [
                new("Nivel de audio Windows", device.AudioEndpoints?.Count > 0 ? "Disponible" : "Sin endpoint", "Niveles de entrada/salida de Windows; no modifica EQ ni memoria del A50."),
                new("EQ y sidetone ASTRO", "Pendiente", "Los IDs de comando se reconocieron; falta validar el reporte completo de escritura."),
                new("Perfiles .agp", "Biblioteca local", "Importar/exportar copias; todavía no se aplican al dispositivo.")
            ]);
    }

    private sealed class LogitechAdapter : IDeviceAdapter
    {
        public bool Matches(HardwareDevice device) =>
            device.VendorId == 0x046D || device.Manufacturer.Contains("Logitech", StringComparison.OrdinalIgnoreCase);

        public AdapterInfo Describe(HardwareDevice device)
        {
            if (device.ModelKey is not ("logitech-pro-wireless" or "logitech-pro-x-superlight"))
                return new("LOGITECH G", FriendlyName(device, "Dispositivo Logitech"), "Dispositivo Logitech detectado",
                    "Windows detecta hardware Logitech, pero el modelo no está identificado para habilitar controles.",
                    [new("Configuración", "No identificada", "Los controles específicos requieren identificar el modelo exacto.")]);

            return new("LOGITECH G", FriendlyName(device, "Ratón Logitech"), "Ratón identificado · perfil Nexo local",
                "El DPI elegido se puede aplicar al sensor con lectura de vuelta. Las etapas, la tasa de respuesta y la memoria integrada siguen siendo funciones distintas.",
                [
                    new("DPI del sensor", "Aplicación directa", "La acción explícita envía el valor HID++ 0x2201 al modelo identificado y exige lectura de vuelta; no crea ni cambia etapas DPI."),
                    new("Etapas DPI", "Perfil Nexo local", "Los valores y la etapa seleccionada se guardan en este PC; no se escriben como tabla de etapas en el ratón."),
                    new("Tasa de respuesta", "Perfil Nexo local", "El valor se guarda en Nexo; el cambio al ratón no está habilitado."),
                    DescribeHidInterfaces(device),
                    new("HID++ / OMM", "Sin consultar", "La consulta manual lee la versión y los IDs de feature que filtra OMM. OMM también comprueba el tipo de dispositivo; esta lectura no confirma compatibilidad completa ni escribe perfiles."),
                    new("Memoria integrada", "Sin validar", "La persistencia a bordo requiere lectura de vuelta y reconexión probadas.")
                ]);
        }

        private static CapabilityInfo DescribeHidInterfaces(HardwareDevice device)
        {
            var interfaces = (device.HidInterfaces ?? []).Where(item => item.UsagePage >= 0xFF00).ToArray();
            var summary = interfaces.Length == 0
                ? "Windows no devolvió colecciones de página 0xFFxx."
                : string.Join(" · ", interfaces.Select(item =>
                    $"{item.UsagePage:X4}/{item.Usage:X4}, IN {item.InputReportBytes}, OUT {item.OutputReportBytes}, feature {item.FeatureReportBytes}"));
            return new("Descriptor HID", interfaces.Length == 0 ? "No leído" : $"{interfaces.Length} colección(es)",
                $"Datos pasivos del descriptor de Windows; no confirman funciones HID++ del ratón. {summary}");
        }
    }

    private sealed class YetiAdapter : IDeviceAdapter
    {
        public bool Matches(HardwareDevice device) =>
            device.ModelKey == "blue-yeti-classic" || device.Product.Contains("Yeti Classic", StringComparison.OrdinalIgnoreCase);

        public AdapterInfo Describe(HardwareDevice device) => new(
            "BLUE",
            "Yeti Classic",
            device.AudioEndpoints?.Any(endpoint => endpoint.Flow == AudioFlow.Capture) == true
                ? "Micrófono conectado · entrada Windows configurable"
                : "Micrófono detectado",
            "La ganancia ajustable aquí es el nivel de entrada de Windows. Blue VO!CE y sus preajustes requieren el adaptador de Logitech.",
            [
                new("Nivel de entrada", "Disponible", "Se lee y cambia mediante Windows Core Audio; el nivel se conserva en Windows."),
                new("Blue VO!CE", "Pendiente", "No se han reconstruido los efectos DSP de G HUB."),
                new("Memoria del micrófono", "Sin validar", "No se afirma persistencia dentro del dispositivo.")
            ]);
    }

    private sealed class AjazzAdapter : IDeviceAdapter
    {
        public bool Matches(HardwareDevice device) =>
            device.ModelKey == "ajazz-ak820" || device.VendorId == 0x0C45;

        public AdapterInfo Describe(HardwareDevice device) => new(
            "AJAZZ",
            device.Product.Contains("AK820", StringComparison.OrdinalIgnoreCase) ? device.Product : "Teclado AJAZZ",
            "AK820 detectado · variante Pro probable",
            "El controlador AJAZZ Beta 1.0.0.5 define AK820 Pro para USB 8009/800A y receptor 2.4 GHz FDFD. Este teclado anuncia PID 8006; el idioma del controlador advierte que Bluetooth no admite ajustes.",
            [
                new("Mapa de 81 teclas", "Perfil local", "Basado en rgb-keyboard.xml del paquete AJAZZ; puedes guardar colores por tecla en Nexo."),
                new("Conexión del controlador", "USB / 2.4 GHz", "config.xml declara 8009, 800A y el receptor FDFD; el PID 8006 de este equipo requiere confirmar si es otra revisión."),
                new("Iluminación, macros y reasignación", "Pendiente", "El ejecutable referencia hid.dll y HidD_SetFeature; aún no se conoce el formato de los reportes ni se han validado escrituras."),
                new("Memoria integrada", "Sin validar", "No se escribe firmware ni memoria a bordo.")
            ]);
    }

    private sealed class XboxAdapter : IDeviceAdapter
    {
        public bool Matches(HardwareDevice device) =>
            device.XInputSlot is not null || device.VendorId == 0x045E ||
            device.Product.Contains("Xbox", StringComparison.OrdinalIgnoreCase);

        public AdapterInfo Describe(HardwareDevice device) => new(
            "XBOX",
            FriendlyName(device, "Mando Xbox"),
            device.XInputSlot is null ? "Mando detectado" : $"Conectado · XInput {device.XInputSlot + 1}",
            "Mando Xbox One Elite. La entrada XInput se detecta; los cambios persistentes siguen en el formato de Microsoft.",
            [
                new("Entrada XInput", device.XInputSlot is null ? "Detectado" : "Disponible", device.XInputSlot is null ? "Conecta el mando por USB o Bluetooth para probar entradas." : "Mando activo; el hub puede detectar el slot conectado."),
                new("Prueba de botones", "Disponible", "Lectura de botones y ejes sin enviar comandos al mando."),
                new("Perfiles y curvas", "Pendiente", "No se han validado escrituras a la memoria del mando.")
            ]);
    }

    private sealed class GenericHidAdapter : IDeviceAdapter
    {
        public bool Matches(HardwareDevice device) => true;

        public AdapterInfo Describe(HardwareDevice device) => new(
            device.Manufacturer.Length > 0 ? device.Manufacturer.ToUpperInvariant() : "HID",
            FriendlyName(device, "Dispositivo HID"),
            "Dispositivo detectado",
            "Dispositivo físico agrupado por su contenedor de Windows; no se han atribuido funciones no verificadas.",
            [new("Inventario", "Disponible", $"{device.InterfaceCount} colección(es) HID agrupada(s).")]);
    }

    private static string FriendlyName(HardwareDevice device, string fallback)
    {
        if (device.ModelKey == "blue-yeti-classic") return "Yeti Classic";
        if (device.ModelKey == "ajazz-ak820") return "AK820 · variante Pro probable";
        if (device.ModelKey == "logitech-pro-wireless") return "PRO WIRELESS";
        var product = device.Product.Trim();
        return string.IsNullOrWhiteSpace(product) || product.Equals("HID Device", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : product;
    }
}
