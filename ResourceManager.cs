using System.Drawing.Printing;
using System.IO.Ports;
using System.Runtime.Versioning;

namespace NSTProxy;

/// <summary>
/// Discovers available local resources and dispatches data to them.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ResourceManager
{
    public static List<(ResourceType Type, string Name)> DiscoverAll()
    {
        var resources = new List<(ResourceType, string)>();

        foreach (string printer in PrinterSettings.InstalledPrinters)
            resources.Add((ResourceType.Printer, printer));

        foreach (var port in SerialPort.GetPortNames())
            resources.Add((ResourceType.SerialPort, port));

        return resources;
    }

    public static bool SendData(EndpointMapping mapping, byte[] data)
    {
        return mapping.ResourceType switch
        {
            ResourceType.Printer    => RawPrinterHelper.SendRawData(mapping.ResourceName, data),
            ResourceType.SerialPort => SendToSerialPort(mapping.ResourceName, data),
            _ => false
        };
    }

    private static bool SendToSerialPort(string portName, byte[] data)
    {
        try
        {
            using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
            port.Open();
            port.Write(data, 0, data.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }
}


