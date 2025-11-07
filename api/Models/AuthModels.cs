using System.Text.Json.Serialization;
using KargoTakip.Services;

namespace KargoTakip.Models
{
    public class LoginRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";
    }

    public class LoginResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("requiresTwoFactor")]
        public bool RequiresTwoFactor { get; set; }

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("twoFactorCode")]
        public string? TwoFactorCode { get; set; }
    }

    public class TwoFactorRequest
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";
    }

    public class TwoFactorResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("data")]
        public List<KargoData>? Data { get; set; }
    }

    public class AuthSession
    {
        public string SessionId { get; set; } = "";
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? TwoFactorCode { get; set; }
        public bool IsAuthenticated { get; set; } = false;
        public string CurrentStatus { get; set; } = "Başlatılıyor...";
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class StatusResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
        
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
        
        [JsonPropertyName("isComplete")]
        public bool IsComplete { get; set; } = false;
    }
}
