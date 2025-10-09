using Impinj.OctaneSdk;
using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

class MagnusS3Reader
{
    static ImpinjReader reader = new ImpinjReader();
    static string targetEpc = "E282403D000203DB0478B057"; // Default value, can be overwritten by argument

    static List<(int count, string epc, double freq, int sensor, long timestampMs)> readings = new List<(int, string, double, int, long)>();
    static bool found = false;
    static int readingCount = 0;
    static string csvPath = "";

    static void Main(string[] args)
    {
        Console.WriteLine("Type name_of_the_program --help for usage");
        Console.WriteLine();

        // Help: if the first argument is "--help", show instructions and exit
        if (args.Length > 0 && args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            string exeName = AppDomain.CurrentDomain.FriendlyName;
            Console.WriteLine("Usage:");
            Console.WriteLine($"  {exeName} [ip] [epc] [timeout_s]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  ip          IP address of the Impinj reader (default: 169.254.116.164)");
            Console.WriteLine("  epc         EPC of the Magnus S3 tag to measure (default: E282403D000203DB0478B057)");
            Console.WriteLine("  timeout_s   Maximum wait time in seconds (default: 15)");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine($"  {exeName} 192.168.1.100 E282403D000203DB0478B057 20");
            return;
        }

        // Arguments: [0]=ip [1]=epc [2]=timeout_s
        string ip = "169.254.116.164";
        int maxWaitMs = 15000;

        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            ip = args[0];
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            targetEpc = args[1];
        if (args.Length > 2 && int.TryParse(args[2], out int t))
            maxWaitMs = t * 1000;

        Console.WriteLine($"Using IP: {ip} | EPC: {targetEpc} | Timeout: {maxWaitMs} ms");

        // Prepare the CSV file name
        csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"magnus_S3_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        // Handle Ctrl+C to save the CSV before exiting
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\nCtrl+C detected. Saving readings to CSV...");
            SaveCsv();
            reader.Stop();
            reader.Disconnect();
            Environment.Exit(0);
        };

        try
        {
            reader.Connect(ip);

            reader.TagOpComplete += OnTagOpComplete;
            reader.DeleteAllOpSequences();

            // Basic configuration
            Settings settings = reader.QueryDefaultSettings();
            settings.Antennas.GetAntenna(1).TxPowerInDbm = 20.0;
            settings.Report.IncludeChannel = true;
            settings.Report.Mode = ReportMode.Individual;

            // Define Magnus S3 read operation
            TagReadOp readSensorCodeOp = new TagReadOp();
            readSensorCodeOp.MemoryBank = MemoryBank.Reserved;
            readSensorCodeOp.WordPointer = 0xC; // Typical Magnus S3 address
            readSensorCodeOp.WordCount = 1;

            settings.Report.OptimizedReadOps.Add(readSensorCodeOp);

            reader.ApplySettings(settings);

            reader.Start();

            // Wait until the target tag is found or until a reasonable timeout
            int waited = 0;
            int step = 200;
            while (waited < maxWaitMs)
            {
                Thread.Sleep(step);
                waited += step;
            }

            reader.Stop();
            reader.Disconnect();

            SaveCsv();

            if (readings.Count > 0)
                Console.WriteLine("Target tag FOUND and measured.");
            else
                Console.WriteLine("Target tag NOT found within the timeout.");
        }
        catch (OctaneSdkException ex)
        {
            Console.WriteLine("SDK Error: " + ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("General error: " + ex.Message);
        }
    }

    static void SaveCsv()
    {
        try
        {
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Count,EPC,FrequencyMHz,SensorCodeDecimal,TimestampMs");
                foreach (var l in readings)
                {
                    writer.WriteLine($"{l.count},{l.epc},{l.freq},{l.sensor},{l.timestampMs}");
                }
            }
            Console.WriteLine($"Results saved to {csvPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving CSV: {ex.Message}");
        }
    }

    static void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
    {
        foreach (TagOpResult result in report)
        {
            if (result is TagReadOpResult readResult)
            {
                string epc = readResult.Tag.Epc.ToHexString();
                string sensorCodeHex = readResult.Data.ToString();
                if (epc.Equals(targetEpc, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sensorCodeHex))
                {
                    int sensorCodeDecimal = Convert.ToInt32(sensorCodeHex, 16);
                    readingCount++;
                    long timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    readings.Add((readingCount, epc, readResult.Tag.ChannelInMhz, sensorCodeDecimal, timestampMs));
                    found = true;
                    Console.WriteLine($"Count: {readingCount} | EPC: {epc} | Frequency (MHz): {readResult.Tag.ChannelInMhz} | Sensor Code: {sensorCodeDecimal} | TimestampMs: {timestampMs}");
                }
            }
        }
    }
}