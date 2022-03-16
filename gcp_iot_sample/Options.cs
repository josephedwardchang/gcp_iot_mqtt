using CommandLine;

namespace GoogleCloudIoTSamples
{
    class Options
    {
        [Option('p', "GoogleCloudProjectId", HelpText = "Google Cloud IoT project ID.", Required = true)]
        public string projectId { get; set; }

        [Option('r', "RegistryId", HelpText = "Google Cloud IoT Registry ID.", Required = true)]
        public string registryId { get; set; }

        [Option('d', "DeviceId", HelpText = "Google Cloud IoT Device ID.", Required = true)]
        public string deviceId { get; set; }

        [Option('a', "cloudRegion", HelpText = "Google Cloud IoT Project Cloud Region.", Required = true)]
        public string cloudRegion { get; set; }

        [Option('t', "topic", HelpText = "Google Cloud pub/sub topic.", Required = true)]
        public string topic { get; set; }

        [Option('s', "subscriptionId", HelpText = "Google Cloud pub/sub subscription Id", Required = false, DefaultValue = "")]
        public string subscriptionId { get; set; }

        [Option('h', "help", HelpText = "Usage: dotnet gcp_iot_sample.dll -p projiotid", Required = false, DefaultValue = false)]
        public bool Help { get; set; }

        [Option('c', "clock", HelpText = "Usage: dotnet gcp_iot_sample.dll -c timer", Required = false, DefaultValue = 5000)]
        public int clock { get; set; }

        [Option('b', "begin-date", HelpText = "Usage: dotnet gcp_iot_sample.dll -b 2022-01-15T23:53:00Z", Required = false, DefaultValue = "")]
        public string beginDate { get; set; }

        [Option('e', "end-date", HelpText = "Usage: dotnet gcp_iot_sample.dll -e 2022-01-16T00:53:00Z", Required = false, DefaultValue = "")]
        public string endDate { get; set; }

        [Option('k', "seek", HelpText = "Usage: dotnet gcp_iot_sample.dll -k 2022-01-16T00:53:00Z", Required = false, DefaultValue = "")]
        public string seek { get; set; }

        [Option('l', "sslFile", HelpText = "Usage: dotnet gcp_iot_sample.dll -l rsa_cert.p12", Required = true, DefaultValue = "rsa_cert.p12")]
        public string sslFile { get; set; }

        [Option('w', "sslPass", HelpText = "Usage: dotnet gcp_iot_sample.dll -w password", Required = true, DefaultValue = "")]
        public string sslPass { get; set; }
    }
}
