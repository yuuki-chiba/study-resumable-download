using System;

using Microsoft.Extensions.Configuration;

namespace Company.Function
{
    public class ConfigWrapper
    {
        private readonly IConfiguration _config;

        public ConfigWrapper(IConfiguration config)
        {
            _config = config;
        }

        public string DataDirectory => _config["DataDirectory"];

        public string TargetFileName => _config["TargetFileName"];
        public string BufferSize => _config["BufferSize"];
    }
}
