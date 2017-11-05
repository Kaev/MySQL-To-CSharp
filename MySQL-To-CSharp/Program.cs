using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Fclp;
using MySql.Data.MySqlClient;

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
    }

    public class Column
    {
        public string Name { get; set; }
        public Type Type { get; set; }

        public Column(MySqlDataReader reader)
        {
            this.Name = reader.GetString(1);
        }

        public override string ToString()
        {
            return $"public {this.Type.Name} {this.Name} {{ get; set; }}";
        }
    }

    class Program
    {
        private static void DbToClasses(string dbName, Dictionary<string, List<Column>> db, bool generateConstructorAndOutput)
        {
            if (!Directory.Exists(dbName))
                Directory.CreateDirectory(dbName);

            var sb = new StringBuilder();
            foreach (var table in db)
            {
                sb.AppendLine($"public class {table.Key}");
                sb.AppendLine("{");

                // properties
                foreach (var column in table.Value)
                    sb.AppendLine(column.ToString());

                // constructor
                sb.AppendLine($"{Environment.NewLine}public {table.Key}(MySqlDataReader reader)");
                sb.AppendLine("{");
                foreach (var column in table.Value)
                {
                    // check which type and use correct get method instead of casting
                    if (column.Type != typeof(string))
                        sb.AppendLine($"{column.Name} = Convert.To{column.Type.Name}(reader[\"{column.Name}\"].ToString());");
                    else
                        sb.AppendLine($"{column.Name} = reader[\"{column.Name}\"].ToString();");
                }
                   
                sb.AppendLine("}");

                // class closing
                sb.AppendLine("}");

                var sw = new StreamWriter($"{dbName}/{table.Key}.cs", false);
                sw.Write(sb.ToString());
                sw.Close();
                sb.Clear();
            }
        }

        static void Main(string[] args)
        {
            var parser = new FluentCommandLineParser<ApplicationArguments>();
            parser.Setup(arg => arg.IP).As('i', "ip").SetDefault("127.0.0.1").WithDescription("(optional) IP address of the MySQL server, will use 127.0.0.1 if not specified");
            parser.Setup(arg => arg.Port).As('n', "port").SetDefault(3306).WithDescription("(optional) Port number of the MySQL server, will use 3306 if not specified");
            parser.Setup(arg => arg.User).As('u', "user").SetDefault("root").WithDescription("(optional) Username, will use root if not specified");
            parser.Setup(arg => arg.Password).As('p', "password").SetDefault(String.Empty).WithDescription("(optional) Password, will use empty password if not specified");
            parser.Setup(arg => arg.Database).As('d', "database").Required().WithDescription("Database name");
            parser.Setup(arg => arg.Table).As('t', "table").SetDefault(String.Empty).WithDescription("(optional) Table name, will generate entire database if not specified");
            parser.Setup(arg => arg.GenerateConstructorAndOutput).As('g', "generateconstructorandoutput")
                .SetDefault(false).WithDescription("(optional) Generate a reading constructor and SQL statement output - Activate with -g true");
            parser.SetupHelp("?", "help").Callback(text => Console.WriteLine(text));

            #if DEBUG
            args = new [] { "-p", "123", "-d", "az_world", "-g", "true"};
            #endif

            var result = parser.Parse(args);
            if (!result.HasErrors)
            {
                var conf = parser.Object as ApplicationArguments;
                if (conf.Database is null)
                {
                    Console.WriteLine("You didn't specify a database");
                    return;
                }

                var confString =
                    $"Server={conf.IP};Port={conf.Port};Uid={conf.User};Pwd={conf.Password};Database={conf.Database}";
                Console.WriteLine(confString);

                var database = new Dictionary<string, List<Column>>();

                using (var con = new MySqlConnection(confString))
                {
                    con.Open();

                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText =
                            $"SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{conf.Database}'";
                        if (!conf.Table.Equals(string.Empty))
                            cmd.CommandText += $" AND TABLE_NAME = '{conf.Table}'";

                        var reader = cmd.ExecuteReader();
                        if (!reader.HasRows)
                            return;

                        while (reader.Read())
                            if (database.ContainsKey(reader.GetString(0)))
                                database[reader.GetString(0)].Add(new Column(reader));
                            else
                                database.Add(reader.GetString(0), new List<Column>() { new Column(reader) });
                    }

                    foreach (var table in database)
                    {
                        using (var cmd = con.CreateCommand())
                        {
                            // lul - is there a way to do this without this senseless statement?
                            cmd.CommandText = $"SELECT * FROM {table.Key} LIMIT 0";
                            var reader = cmd.ExecuteReader();
                            var schema = reader.GetSchemaTable();
                            foreach (var column in table.Value)
                                column.Type = schema.Select($"ColumnName = '{column.Name}'")[0]["DataType"] as Type;
                        }
                    }

                    con.Close();
                }

                DbToClasses(conf.Database, database, conf.GenerateConstructorAndOutput);
                Console.WriteLine("Successfully generated C# classes!");
            }
            Console.ReadLine();
        }
    }
}
