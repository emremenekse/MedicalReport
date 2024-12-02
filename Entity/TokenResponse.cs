namespace MedicalReport.Entity
{
    public class TokenResponse
    {
        public Body Body { get; set; }
    }

    public class Body
    {
        public List<Token> Tokens { get; set; }
    }

    public class Token
    {
        public string Text { get; set; }
        public string EntityType { get; set; }
    }

}
