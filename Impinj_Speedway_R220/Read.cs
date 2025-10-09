using Impinj.OctaneSdk;

using System;
using Impinj.OctaneSdk;

class Read
{
    static ImpinjReader reader = new ImpinjReader();

    static void Main()
    {
        try
        {
            string ip = "169.254.116.164"; // Cambia a la IP de tu lector
            reader.Connect(ip);

            // Obtener config por defecto y ajustar
            Settings settings = reader.QueryDefaultSettings();
            settings.Report.Mode = ReportMode.Individual;
            settings.Report.IncludeAntennaPortNumber = true;
            settings.Report.IncludePeakRssi = true;
            settings.Report.IncludeFirstSeenTime = true;

            // Activar la antena 1
            settings.Antennas.DisableAll();
            var ant1 = settings.Antennas.GetAntenna(1);
            ant1.IsEnabled = true;
            ant1.TxPowerInDbm = 25.0;

            reader.ApplySettings(settings);

            // Suscribirse al evento de tags
            reader.TagsReported += OnTagsReported;

            // Iniciar lectura
            reader.Start();
            Console.WriteLine("Leyendo... pulsa ENTER para detener");
            Console.ReadLine();

            reader.Stop();
            reader.Disconnect();
        }
        catch (OctaneSdkException ex)
        {
            Console.WriteLine("Error SDK: " + ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error general: " + ex.Message);
        }
    }

    static void OnTagsReported(ImpinjReader sender, TagReport report)
    {
        foreach (Tag tag in report.Tags)
        {
            Console.WriteLine($"{tag.Epc} | ant: {tag.AntennaPortNumber} | RSSI: {tag.PeakRssiInDbm} dBm | t: {tag.FirstSeenTime.LocalDateTime}");
        }
    }
}
