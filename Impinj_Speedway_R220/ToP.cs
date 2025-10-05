using Impinj.OctaneSdk;
using System;
using System.Collections.Generic;
using System.IO;

class ToP
{
    static ImpinjReader reader = new ImpinjReader();
    static bool found = false;
    static string epcObjetivo = "000000A93C0000000003000E00000DAD"; // <-- Cambia aquí el EPC del tag a medir

    // Para guardar resultados
    class Result
    {
        public double FrequencyMHz { get; set; }
        public double TurnOnPowerDbm { get; set; }
        public bool Detected { get; set; }
    }

    static List<Result> resultados = new List<Result>();

    static void Main()
    {
        try
        {
            string ip = "169.254.116.164"; // Cambia a la IP de tu lector
            reader.Connect(ip);

            reader.TagsReported += OnTagsReported;

            var features = reader.QueryFeatureSet();
            var freqs = features.TxFrequencies;

            foreach (var freq in freqs)
            {
                found = false;
                double turnOnPower = 0;
                // Bucle de potencia: de 10 a 30 dBm
                for (double power = 10.0; power <= 30.0; power += 0.5)
                {
                    Settings settings = reader.QueryDefaultSettings();
                    settings.Report.Mode = ReportMode.Individual;
                    settings.Report.IncludeAntennaPortNumber = true;
                    settings.Report.IncludePeakRssi = true;
                    settings.Report.IncludeFirstSeenTime = true;
                    settings.Antennas.DisableAll();
                    var ant1 = settings.Antennas.GetAntenna(1);
                    ant1.IsEnabled = true;
                    ant1.TxPowerInDbm = power;

                    // Configura la frecuencia globalmente en Settings, no en la antena
                    settings.TxFrequenciesInMhz.Clear();
                    settings.TxFrequenciesInMhz.Add(freq);

                    reader.ApplySettings(settings);

                    Console.WriteLine($"Probando potencia: {power} dBm y frecuencia {freq} MHz...");
                    reader.Start();
                    System.Threading.Thread.Sleep(400); // Espera breve para leer
                    reader.Stop();

                    if (found)
                    {
                        turnOnPower = power;
                        Console.WriteLine($"Tag {epcObjetivo} detectado a {turnOnPower} dBm en frecuencia {freq} MHz");
                        break;
                    }
                }
                resultados.Add(new Result
                {
                    FrequencyMHz = freq,
                    TurnOnPowerDbm = turnOnPower,
                    Detected = found
                });
            }

            reader.Disconnect();

            // Guardar resultados en CSV
            string csvPath = "turn_on_power_results.csv";
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("FrequencyMHz,TurnOnPowerDbm,Detected");
                foreach (var r in resultados)
                {
                    writer.WriteLine($"{r.FrequencyMHz},{r.TurnOnPowerDbm},{r.Detected}");
                }
            }
            Console.WriteLine($"Resultados guardados en {csvPath}");

            if (!resultados.Exists(r => r.Detected))
                Console.WriteLine($"Tag {epcObjetivo} no detectado en el rango de potencia y frecuencia.");
            else
                Console.WriteLine($"Turn-on power por frecuencia guardado en CSV.");
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
            if (tag.Epc.ToHexString().Equals(epcObjetivo, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                Console.WriteLine($"Detectado: {tag.Epc} | ant: {tag.AntennaPortNumber} | RSSI: {tag.PeakRssiInDbm} dBm | t: {tag.FirstSeenTime.LocalDateTime}");
            }
        }
    }
}
