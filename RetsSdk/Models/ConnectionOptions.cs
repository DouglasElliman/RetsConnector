using CrestApps.RetsSdk.Models.Enums;
using System;

namespace CrestApps.RetsSdk.Models
{
    public class ConnectionOptions
    {
        public string PublicUsername { get; set; }
        public string PublicPassword { get; set; }
        public string PrivateUsername { get; set; }
        public string PrivatePassword { get; set; }
        
        public AuthenticationType Type { get; set; }
        public string UserAgent { get; set; }
        public string UserAgentPassword { get; set; }
        public SupportedRetsVersion RetsServerVersion { get; set; } = SupportedRetsVersion.Version_1_7_2;
        public string LoginUrl { get; set; }
        public TimeSpan Timeout { get; set; }
        public string? BaseUrl { get; set; }
        public ConnectionOptions()
        {
            Timeout = TimeSpan.FromHours(1);
        }

        public RetsVersion Version => new RetsVersion(RetsServerVersion);
    }
}
