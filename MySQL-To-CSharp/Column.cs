using System;
using MySql.Data.MySqlClient;

namespace MySQL_To_CSharp
{
    public class Column
    {
        public Column(MySqlDataReader reader)
        {
            Name = reader.GetString(1);
            ColumnType = reader.GetString(2);
        }

        public string Name { get; set; }
        public Type Type { get; set; }
        public string ColumnType { get; set; }

        public override string ToString()
        {
            return $"public {Type.Name} {Name.FirstCharUpper()} {{ get; set; }}";
        }
    }
}