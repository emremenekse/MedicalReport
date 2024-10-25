using System.Text.Json.Serialization;

namespace MedicalReport.Entity
{
    public class Document
    {
        [JsonPropertyName("tokens")] // JSON'daki 'tokens' alanı ile eşleşir
        public List<string> Tokens { get; set; }

        [JsonPropertyName("tags")] // JSON'daki 'tags' alanı ile eşleşir
        public List<int> Tags { get; set; }
    }
}
