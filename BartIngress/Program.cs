using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using OSIsoft.Omf;
using OSIsoft.Omf.Converters;

namespace BartIngress
{
    public static class Program
    {
        private static readonly object _timerLock = new ();
        private static Timer _timer;

        public static AppSettings Settings { get; set; }
        private static OmfServices OmfServices { get; set; }
        private static int TimerInterval { get; set; } = 10000;

        public static void Main()
        {
            LoadConfiguration();
            _timer = new Timer(new TimerCallback(TimerTask), null, 0, TimerInterval);
            Console.WriteLine("Started, press Enter to quit");
            Console.ReadLine();
        }
        
        /// <summary>
        /// Loads configuration from the appsettings.json file and sets up configured OMF endpoints
        /// </summary>
        public static void LoadConfiguration()
        {
            Settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(Directory.GetCurrentDirectory() + "\\appsettings.json"));

            OmfServices = new OmfServices();

            if (Settings.SendToAdh)
            {
                OmfServices.ConfigureAdhOmfIngress(Settings.AdhUri, Settings.AdhTenantId, Settings.AdhNamespaceId, Settings.AdhClientId, Settings.AdhClientSecret);
            }

            if (Settings.SendToEds)
            {
                OmfServices.ConfigureEdsOmfIngress(Settings.EdsPort);
            }

            if (Settings.SendToPi)
            {
                OmfServices.ConfigurePiOmfIngress(Settings.PiWebApiUri, Settings.Username, Settings.Password, Settings.ValidateEndpointCertificate);
            }

            // Send OMF Type Message
            OmfServices.SendOmfType(typeof(BartStationEtd));

            // Send OMF Container Message
            Dictionary<string, IEnumerable<BartStationEtd>> data = BartApi.GetRealTimeEstimates(Settings.BartApiKey, Settings.BartApiOrig, Settings.BartApiDest);
            string typeId = ClrToOmfTypeConverter.Convert(typeof(BartStationEtd)).Id;
            OmfServices.SendOmfContainersForData(data, typeId);
        }

        /// <summary>
        /// Run BART API ingress to configured OMF endpoints
        /// </summary>
        public static void RunIngress()
        {
            Dictionary<string, IEnumerable<BartStationEtd>> data = BartApi.GetRealTimeEstimates(Settings.BartApiKey, Settings.BartApiOrig, Settings.BartApiDest);
            OmfServices.SendOmfData(data);
            Console.WriteLine($"{DateTime.Now}: Sent value for {data.Keys.Count} stream{(data.Keys.Count > 1 ? "s" : string.Empty)}");
        }

        /// <summary>
        /// Deletes type and containers that were created by the BART Ingress sample
        /// </summary>
        public static void Cleanup()
        {
            OmfServices.CleanupOmf();
        }

        /// <summary>
        /// Task callback when the timer fires, attempts to run ingress 
        /// </summary>
        private static void TimerTask(object timerState)
        {
            bool hasLock = false;

            try
            {
                Monitor.TryEnter(_timerLock, ref hasLock);
                if (!hasLock)
                {
                    return;
                }

                _timer.Change(Timeout.Infinite, Timeout.Infinite);

                RunIngress();
            }
            finally
            {
                if (hasLock)
                {
                    Monitor.Exit(_timerLock);
                    _timer.Change(TimerInterval, TimerInterval);
                }
            }
        }
    }
}
