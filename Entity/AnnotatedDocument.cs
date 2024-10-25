using System.Text.Json.Serialization;

namespace MedicalReport.Entity
{
    public class AnnotatedDocument
    {
        [JsonPropertyName("entities")]

        public List<Entity> Entities { get; set; }=null!;
        [JsonPropertyName("text")]

        public string Text { get; set; }= null!;
    }
}
