using System.Text.Json.Serialization;

namespace MedicalReport.Entity
{
    public class Entity
    {
        [JsonPropertyName("class")]
        public string Class { get; set; } = null!;
        [JsonPropertyName("start")]

        public int Start { get; set; }
        [JsonPropertyName("end")]

        public int End { get; set; }
    }
}
