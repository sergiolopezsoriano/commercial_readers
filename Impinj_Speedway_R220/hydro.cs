using System;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;

class ReadHydro
{
    static void Main()
    {
        var reader = new ImpinjReader();
        reader.Connect("169.254.116.164");

        var s = reader.QueryDefaultSettings();
        s.ReaderMode = ReaderMode.AutoSetDenseReader;
        s.Report.Mode = ReportMode.Individual;
        s.Antennas.DisableAll();
        var a1 = s.Antennas.GetAntenna(1);
        a1.IsEnabled = true;
        a1.TxPowerInDbm = 27.0;
        reader.ApplySettings(s);

        // Mantén RF encendida un rato para que el tag mida
        reader.Start();
        Thread.Sleep(600);
        reader.Stop();

        var op = new TagReadOp
        {
            MemoryBank = MemoryBank.User,
            WordPointer = 0x10,   // posición típica para Hydro
            WordCount = 8         // lee más por si acaso
        };

        var seq = new TagOpSequence();
        seq.Ops.Add(op);
        // (Opcional) apuntar a un EPC concreto:
        // seq.TargetTag = new TargetTag { MemoryBank = MemoryBank.Epc, BitPointer = BitPointers.Epc, Data = "EPC_AQUI" };

        byte[] last = null;
        bool ok = false;

        reader.TagOpComplete += (snd, rep) =>
        {
            foreach (var r in rep.Results)
            {
                if (r is TagReadOpResult readResult && readResult.OpId == op.Id)
                {
                    if (readResult.Result == ReadResultStatus.Success)
                    {
                        last = readResult.Data?.ToList().Select(b => (byte)b).ToArray();
                        ok = (last != null && last.Length > 0 && last[0] == 0xAA); // header listo
                        Console.WriteLine("USER: " + BitConverter.ToString(last));
                    }
                    else
                    {
                        Console.WriteLine("Read failed: " + readResult.Result);
                    }
                }
            }
        };

        reader.AddOpSequence(seq);

        // Reintentos con más “calor” RF si hace falta
        for (int i = 0; i < 5 && !ok; i++)
        {
            reader.Start();
            Thread.Sleep(400 + i * 200); // cada vez un poco más
            reader.Stop();
            Thread.Sleep(100);
        }

        reader.Disconnect();

        if (!ok)
            Console.WriteLine("No apareció 0xAA. Sube potencia/acerca el tag y aumenta los delays.");
        else
        {
            Console.WriteLine("Header OK (0xAA). Ya puedes decodificar humedad.");
            if (ok && last != null && last.Length >= 2)
            {
                // Supón que la humedad está en el segundo byte
                byte humedad = last[1];
                Console.WriteLine($"Humedad: {humedad} %");
            }
        }
    }
}
