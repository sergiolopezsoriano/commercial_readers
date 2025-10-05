using System;
using System.IO;
using System.Text;
using System.Threading;
using Impinj.OctaneSdk;

namespace R220 // <-- namespace único para evitar choques
{
    public static class EM4152App
    {
        // Ajusta según la hoja de datos del EM4152 si vas a leer USER como “sensor”
        private const ushort USER_READ_START = 0x0124 / 2; // ejemplo
        private const ushort USER_READ_WORDS = 2;          // ejemplo

        private static long EpochMsFrom(ImpinjTimestamp ts)
        {
            // Fallback inmediato si no hay timestamp
            if (ts == null) return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                dynamic d = ts;

                // Caso A: Utc es DateTime
                try
                {
                    DateTime dt = (DateTime)d.Utc;
                    // Garantiza Kind=Utc por si viniera "Unspecified"
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
                }
                catch { /* no es DateTime */ }

                // Caso B: Utc es microsegundos (ulong/long)
                try
                {
                    ulong us = (ulong)d.Utc;
                    return (long)(us / 1000UL);
                }
                catch { /* no es ulong */ }

                try
                {
                    long us = (long)d.Utc;
                    return us / 1000L;
                }
                catch { /* no es long */ }

                // Caso C: propiedades alternativas (si existen en tu build)
                try { return (long)d.Milliseconds; } catch { }
                try { return ((long)d.Microseconds) / 1000L; } catch { }
            }
            catch
            {
                // ignoramos y devolvemos ahora
            }

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public static int Run(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.WriteLine("Usage: EM4152App <READER_IP> <TARGET_EPC> [SECONDS]");
                return 1;
            }

            string readerIp = args[0] ?? string.Empty;
            string targetEpc = (args[1] ?? string.Empty).Replace(" ", "").ToUpperInvariant();
            int durationSec = (args.Length >= 3 && int.TryParse(args[2], out var s)) ? s : 10;

            var reader = new ImpinjReader();

            try
            {
                reader.Connect(readerIp);

                var settings = reader.QueryDefaultSettings();

                // Reportes disponibles en SDK 3.0.0
                settings.Report.IncludeAntennaPortNumber = true;
                settings.Report.IncludePeakRssi = true;
                settings.Report.IncludeFirstSeenTime = true;
                settings.Report.IncludeLastSeenTime = true;
                // (No existe IncludeXPC en 3.0.0)

                // Modos de lectura
                settings.ReaderMode = ReaderMode.AutoSetDenseReader;
                settings.SearchMode = SearchMode.SingleTarget;
                settings.Session = 1;

                // Antena 1 (ajusta potencias a normativa/escenario)
                var ant1 = settings.Antennas.GetAntenna(1);
                ant1.IsEnabled = true;
                ant1.MaxTxPower = false;
                ant1.TxPowerInDbm = 28.0;
                ant1.RxSensitivityInDbm = -70;

                // Filtro EPC (si tu build 3.0 lo soporta; si no, filtramos en el handler)
                try
                {
                    if (settings.Filters.TagFilter1 != null)
                    {
                        settings.Filters.TagFilter1.MemoryBank = MemoryBank.Epc;
                        settings.Filters.TagFilter1.BitPointer = 32;        // salta CRC+PC
                        settings.Filters.TagFilter1.TagMask = targetEpc;  // string hex
                        // Algunas builds traen:
                        // settings.Filters.TagFilter1.BitCount = (ushort)(targetEpc.Length * 4);
                        // settings.Filters.TagFilter1.Enabled  = true;
                    }
                }
                catch { /* si tu build no tiene estas props, no pasa nada */ }

                reader.ApplySettings(settings);

                // Resultados de TagOps (SDK 3.0.0)
                reader.TagOpComplete += OnTagOpComplete;

                bool opsAdded = false;
                ulong counter = 0;

                using (var log = new StreamWriter("taglog.csv", false, Encoding.UTF8))
                {
                    log.WriteLine("timestamp_ms,epc,antenna,rssi_dbm,counter,read_hex");

                    reader.TagsReported += (snd, report) =>
                    {
                        foreach (Tag tag in report) // tipado explícito evita 'object'
                        {
                            string epcHex = (tag.Epc != null ? tag.Epc.ToHexString() : string.Empty).ToUpperInvariant();
                            if (epcHex != targetEpc) continue; // filtrado por EPC si no hay filtro HW

                            // Timestamp en ms (ImpinjTimestamp en 3.0.0 tiene .Utc)
                            long tsMs = EpochMsFrom(tag.LastSeenTime);

                            int ant = tag.AntennaPortNumber;
                            double rssi = tag.PeakRssiInDbm;

                            Console.WriteLine($"{tsMs} | EPC: {epcHex} | Ant: {ant} | RSSI: {rssi:F1} dBm | Cnt: {++counter}");
                            log.WriteLine($"{tsMs},{epcHex},{ant},{rssi:F1},{counter},");
                            log.Flush();

                            // Añadimos la secuencia de ops la primera vez que vemos el EPC objetivo
                            if (!opsAdded)
                            {
                                opsAdded = true;

                                var seq = new TagOpSequence();

                                // Target por EPC (en 3.0.0: TargetTag.Data es string hex)
                                seq.TargetTag = new TargetTag
                                {
                                    MemoryBank = MemoryBank.Epc,
                                    BitPointer = 32,
                                    Data = targetEpc
                                    // Algunas builds tienen BitCount:
                                    // BitCount = (ushort)(targetEpc.Length * 4)
                                };

                                // Escribir USER @0x0120
                                var writeSysConf = new TagWriteOp
                                {
                                    MemoryBank = MemoryBank.User,
                                    WordPointer = 0x0120 / 2,
                                    Data = TagData.FromWordArray(new ushort[] { 0x1390 })
                                };

                                // Escribir USER @0x0123
                                var writeSensorCtrl = new TagWriteOp
                                {
                                    MemoryBank = MemoryBank.User,
                                    WordPointer = 0x0123 / 2,
                                    Data = TagData.FromWordArray(new ushort[] { 0x2000 })
                                };

                                // Leer USER (ejemplo)
                                var readUser = new TagReadOp
                                {
                                    MemoryBank = MemoryBank.User,
                                    WordPointer = USER_READ_START,
                                    WordCount = USER_READ_WORDS
                                };

                                seq.Ops.Add(writeSysConf);
                                seq.Ops.Add(writeSensorCtrl);
                                seq.Ops.Add(readUser);

                                reader.AddOpSequence(seq);
                            }
                        }
                    };

                    reader.Start();
                    Console.WriteLine("[OK] Inventory started. Present tag to antenna...");

                    Thread.Sleep(durationSec * 1000);

                    reader.Stop();
                    Console.WriteLine("[OK] Inventory stopped.");
                }

                return 0;
            }
            catch (OctaneSdkException ex)
            {
                Console.WriteLine($"[ERR] SDK: {ex.Message}");
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERR] {ex.Message}");
                return 3;
            }
            finally
            {
                if (reader.IsConnected) reader.Disconnect();
            }
        }

        // Handler de resultados de TagOps (SDK 3.0.0)
        private static void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            foreach (var res in report.Results)
            {
                var w = res as TagWriteOpResult;
                if (w != null)
                {
                    // 3.0.0: w.Result (WriteResult), w.NumWordsWritten
                    Console.WriteLine($"[WRITE] result={w.Result} words={w.NumWordsWritten}");
                    continue;
                }

                var r = res as TagReadOpResult;
                if (r != null)
                {
                    // 3.0.0: TagData -> usa ToHexString() para loguear
                    string hex = (r.Data != null) ? r.Data.ToHexString() : string.Empty;
                    Console.WriteLine($"[READ] data={hex}");
                }
            }
        }
    }

    // Clase “proxy” con Main para proyectos que esperan ‘EM4152’ como Startup object.
    public sealed class EM4152
    {
        public static void Main(string[] args)
        {
            Environment.ExitCode = EM4152App.Run(args);
        }
    }
}
