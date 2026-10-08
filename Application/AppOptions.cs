namespace TranslationBackend.Application
{
    /// <summary>Appsettings section "Jwt".</summary>
    public class JwtOptions
    {
        public const string Section = "Jwt";
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = "TranslationBackend";
        public string Audience { get; set; } = "TranslationFrontend";
        /// <summary>Token lifetime; 480 = 8 hours, as in the SupplyChain app.</summary>
        public int ExpiryInMinutes { get; set; } = 480;
    }

    /// <summary>Appsettings section "Microsoft": the OAuth app registration used for sign-in.</summary>
    public class MicrosoftOptions
    {
        public const string Section = "Microsoft";
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string TenantId { get; set; } = "common";
        public string RedirectUri { get; set; } = string.Empty;
        public string[] Scopes { get; set; } = ["openid", "profile", "email", "User.Read"];
        public string AuthorizationEndpoint { get; set; } = "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize";
        public string TokenEndpoint { get; set; } = "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";
        public string UserInfoEndpoint { get; set; } = "https://graph.microsoft.com/v1.0/me";
    }

    /// <summary>Appsettings section "Auth".</summary>
    public class AuthOptions
    {
        public const string Section = "Auth";
        /// <summary>Only these email domains may sign in or register. Empty = any account of the Microsoft tenant.</summary>
        public string[] AllowedEmailDomains { get; set; } = [];
    }

    /// <summary>Appsettings section "Extraction": the translation (FastAPI) service.</summary>
    public class ExtractionOptions
    {
        public const string Section = "Extraction";
        public string BaseUrl { get; set; } = "http://127.0.0.1:8002";
        /// <summary>Sent as the X-API-Key header; must equal EXTRACTION_API_KEY of the service.</summary>
        public string ApiKey { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
        /// <summary>How often running jobs are asked for their progress.</summary>
        public int PollSeconds { get; set; } = 3;
        /// <summary>Jobs are marked Failed when the service has been unreachable this long.</summary>
        public int UnavailableMinutes { get; set; } = 5;
    }

    /// <summary>Appsettings section "Storage": the folder shared with the extraction service.</summary>
    public class StorageOptions
    {
        public const string Section = "Storage";
        /// <summary>Must be the same folder as EXTRACTION_FILES_ROOT of the service.</summary>
        public string Root { get; set; } = string.Empty;
    }

    /// <summary>Appsettings section "Jobs".</summary>
    public class JobOptions
    {
        public const string Section = "Jobs";
        public long MaxUploadBytes { get; set; } = 50L * 1024 * 1024;
        public int MaxActivePerUser { get; set; } = 5;
        /// <summary>Files of finished jobs are deleted after this many days; the history rows stay.</summary>
        public int RetentionDays { get; set; } = 30;
        public int CleanupIntervalHours { get; set; } = 6;
    }
}
