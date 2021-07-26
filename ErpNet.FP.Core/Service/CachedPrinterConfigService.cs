namespace ErpNet.FP.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Configuration;
    using Drivers;
    using Newtonsoft.Json;
    using Serilog;
    using Transports;

    public class CachedPrinterConfigService
    {
        public Dictionary<string, BgFiscalPrinter> Printers { get; set; } = new Dictionary<string, BgFiscalPrinter>();

        private readonly string cacheFileLocation;

        public CachedPrinterConfigService(string cacheFileLocation = "cached_printers.json")
        {
            this.cacheFileLocation = cacheFileLocation;
            this.load();
        }

        private void load()
        {
            if (!File.Exists(this.cacheFileLocation))
            {
                return;
            }

            var result = JsonConvert.DeserializeObject<Dictionary<string, PrinterInitInfo>>(File.ReadAllText(this.cacheFileLocation));
            if (result != null)
            {
                var rebuiltPrinters = new Dictionary<string, BgFiscalPrinter>();

                foreach (KeyValuePair<string, PrinterInitInfo> pair in result)
                {
                    var transport = ReflectiveEnumerator
                        .GetEnumerableOfType<Transport>()
                        .First(t => t.TransportName.Equals(pair.Value.transport))
                    ;

                    IChannel channel = null;
                    switch (pair.Value.transport)
                    {
                        case "com":
                            channel = new ComTransport.Channel(
                                pair.Value.transportsInfo.portName,
                                pair.Value.transportsInfo.baudRate.Value
                            );
                            break;
                        case "tcp":
                            channel = new ComTransport.Channel(
                                pair.Value.transportsInfo.tcpHostName,
                                pair.Value.transportsInfo.tcpPort.Value
                            );
                            break;
                        default:
                            Log.Error($"Cached transport {pair.Value.transport} not found.");
                            break;
                    }

                    if (channel == null)
                    {
                        break;
                    }

                    var instance = BgFiscalPrinter.BuildFromCache(
                         pair.Value.driverName,
                         pair.Value.info,
                         channel,
                         pair.Value.serviceOptions,
                         pair.Value.options
                    );

                    rebuiltPrinters.Add(pair.Key, instance);
                }

                Printers = rebuiltPrinters;
            }
        }

        public void write(Dictionary<string, IFiscalPrinter> newPrinters)
        {
            try
            {
                var newStruct = new Dictionary<string, PrinterInitInfo>();
                foreach (KeyValuePair<string,IFiscalPrinter> pair in newPrinters)
                {
                    newStruct.Add(pair.Key, new PrinterInitInfo(
                        pair.Value.DeviceInfo,
                        pair.Value.DriverName,
                        pair.Value.Channel.TransportName,
                        (
                            pair.Value.Channel.TransportName.Equals("tcp")
                                ? TransportsInfo.TcpTransport(
                                    ((TcpTransport.Channel) pair.Value.Channel).HostName,
                                    ((TcpTransport.Channel) pair.Value.Channel).Port
                                )
                                : TransportsInfo.ComTransport(
                                    ((ComTransport.Channel) pair.Value.Channel).portName,
                                    ((ComTransport.Channel) pair.Value.Channel).baudRate
                                )
                        ),
                        pair.Value.Channel.Descriptor,
                        pair.Value.ServiceOptions,
                        pair.Value.Options
                    ));
                }
                File.WriteAllText(this.cacheFileLocation,
                    JsonConvert.SerializeObject(newStruct, Formatting.Indented));
                Log.Information($"Saved {newStruct.Count} printers to {this.cacheFileLocation}");
                this.load();
            }
            catch (Exception e)
            {
                Log.Error($"Error occurred when saving printers cache: {e.Message}");
            }
        }
    }

    class TransportsInfo
    {
        public string? portName;
        public int? baudRate;
        public string? tcpHostName;
        public int? tcpPort;

        public TransportsInfo()
        {
        }

        public static TransportsInfo ComTransport(string? portName, int? baudRate)
        {
            var instance = new TransportsInfo();
            instance.portName = portName;
            instance.baudRate = baudRate;
            return instance;
        }

        public static TransportsInfo TcpTransport(string? tcpHostName, int? tcpPort)
        {
            var instance = new TransportsInfo();
            instance.tcpHostName = tcpHostName;
            instance.tcpPort = tcpPort;
            return instance;
        }
    }

    class PrinterInitInfo
    {
        public DeviceInfo info;
        public string driverName;
        public string transport;
        public TransportsInfo transportsInfo;
        public string descriptor;
        public ServiceOptions serviceOptions;
        public IDictionary<string, string>? options = null;

        public PrinterInitInfo(
            DeviceInfo info,
            string driverName,
            string transport,
            TransportsInfo transportsInfo,
            string descriptor,
            ServiceOptions serviceOptions,
            IDictionary<string, string>? options
        )
        {
            this.info = info;
            this.driverName = driverName;
            this.transportsInfo = transportsInfo;
            this.transport = transport;
            this.descriptor = descriptor;
            this.serviceOptions = serviceOptions;
            this.options = options;
        }
    }
}