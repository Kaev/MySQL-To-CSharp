namespace MySQL_To_CSharp
{
    public class ApplicationArguments
    {
        public string IP { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Database { get; set; }
        public string Table { get; set; }
        public bool GenerateConstructorAndOutput { get; set; }
        public bool GenerateMarkupPages { get; set; }
        public string MarkupDatabaseNameReplacement { get; set; }
    }
}
