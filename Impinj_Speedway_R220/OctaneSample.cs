using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Impinj.OctaneSdk;

namespace OctaneSample
{
    class Program
    {
        static ImpinjReader reader = new ImpinjReader(); // Create a new Reader object

        static void Main(string[] args)
        {
            reader.Connect("169.254.116.164"); // Connect to the reader
            reader.TagOpComplete += OnTagOpComplete; // Assign an event handler which runs when a tag is read
            reader.DeleteAllOpSequences(); // Reset the reader

            Settings settings = reader.QueryDefaultSettings(); // Get the default settings
            settings.Antennas.GetAntenna(1).TxPowerInDbm = 20; // Set antenna 1 to an output power of 20 dBm

            TagReadOp readSensorCodeOp = new TagReadOp(); // Define a read operation for a Magnus-S2 chip
            readSensorCodeOp.MemoryBank = MemoryBank.Reserved; // Sensor Code is in the Reserved Bank
            readSensorCodeOp.WordPointer = 0x8; // ... at word address hex 8
            readSensorCodeOp.WordCount = 1; // Read one word

            settings.Report.OptimizedReadOps.Add(readSensorCodeOp); // Load the read operation to the settings object
            settings.Report.IncludeChannel = true; // Request channel frequency information
            settings.Report.Mode = ReportMode.BatchAfterStop; // Read for 2 seconds, then stop

            reader.ApplySettings(settings); // Load the settings into the reader
            reader.Start(); // Start reading
            System.Threading.Thread.Sleep(2000); // Wait for 2 seconds
            reader.Stop(); // Stop reading
            reader.Disconnect(); // Disconnect the reader
        }

        // This is the event handler which prints the results to the console window when the reader reads a tag.
        static void OnTagOpComplete(ImpinjReader reader, TagOpReport report)
        {
            foreach (TagOpResult result in report)
            {
                if (result is TagReadOpResult)
                {
                    TagReadOpResult readResult = result as TagReadOpResult;
                    string EPC = readResult.Tag.Epc.ToString();
                    string frequency = readResult.Tag.ChannelInMhz.ToString();
                    string sensorCode = readResult.Data.ToString();
                    Console.WriteLine("EPC: " + EPC + " Frequency (MHz): " + frequency + " Sensor Code: " + sensorCode);
                }
            }
        }
    }
}

/*
Example program output:
---------------------------------
EPC: 002C 0000 0000 0000 0000 1234 Frequency (MHz): 924.25 Sensor Code: 0006
EPC: 002C 0000 0000 0000 0000 1234 Frequency (MHz): 917.25 Sensor Code: 0007
EPC: 002C 0000 0000 0000 0000 1234 Frequency (MHz): 909.25 Sensor Code: 0008
*/
