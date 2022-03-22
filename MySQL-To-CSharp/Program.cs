using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fclp;
using MySql.Data.MySqlClient;

namespace MySQL_To_CSharp
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var parser = new FluentCommandLineParser<ApplicationArguments>();
            parser.Setup(arg => arg.IP).As('i', "ip").SetDefault("127.0.0.1")
                .WithDescription("(optional) IP address of the MySQL server, will use 127.0.0.1 if not specified");
            parser.Setup(arg => arg.Port).As('n', "port").SetDefault(3306)
                .WithDescription("(optional) Port number of the MySQL server, will use 3306 if not specified");
            parser.Setup(arg => arg.User).As('u', "user").SetDefault("root")
                .WithDescription("(optional) Username, will use root if not specified");
            parser.Setup(arg => arg.Password).As('p', "password").SetDefault(string.Empty)
                .WithDescription("(optional) Password, will use empty password if not specified");
            parser.Setup(arg => arg.Database).As('d', "database").Required().WithDescription("Database name");
            parser.Setup(arg => arg.Table).As('t', "table").SetDefault(string.Empty)
                .WithDescription("(optional) Table name, will generate entire database if not specified");
            parser.Setup(arg => arg.GenerateConstructorAndOutput).As('g', "generateconstructorandoutput")
                .SetDefault(false)
                .WithDescription(
                    "(optional) Generate a reading constructor and SQL statement output - Activate with -g true");
            parser.Setup(arg => arg.GenerateMarkupPages).As('m', "generatemarkuppages")
                .SetDefault(false)
                .WithDescription(
                    "(optional) Generate markup pages for database and tables which can be used in wikis - Activate with -m true");
            parser.Setup(arg => arg.MarkupDatabaseNameReplacement).As('r', "markupdatabasenamereplacement")
                .SetDefault("")
                .WithDescription("(optional) Will use this instead of database name for wiki breadcrump generation");
            parser.SetupHelp("?", "help").Callback(text => Console.WriteLine(text));

            var result = parser.Parse(args);
            if (!result.HasErrors)
            {
                var conf = parser.Object;
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
                            $"SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{conf.Database}'";
                        if (!conf.Table.Equals(string.Empty))
                            cmd.CommandText += $" AND TABLE_NAME = '{conf.Table}'";

                        var reader = cmd.ExecuteReader();
                        if (!reader.HasRows)
                            return;

                        while (reader.Read())
                            if (database.ContainsKey(reader.GetString(0)))
                                database[reader.GetString(0)].Add(new Column(reader));
                            else
                                database.Add(reader.GetString(0), new List<Column> { new Column(reader) });

                        reader.Close();
                    }

                    foreach (var table in database)
                        using (var cmd = con.CreateCommand())
                        {
                            // TODO: Is there a way to do this without this senseless statement?
                            cmd.CommandText = $"SELECT * FROM `{table.Key}` LIMIT 0";
                            var reader = cmd.ExecuteReader();
                            var schema = reader.GetSchemaTable();
                            foreach (var column in table.Value)
                                column.Type = schema.Select($"ColumnName = '{column.Name}'")[0]["DataType"] as Type;

                            reader.Close();
                        }

                    con.Close();
                }

                DbToClasses(conf.Database, database, conf.GenerateConstructorAndOutput);
                if (conf.GenerateMarkupPages)
                    DbToMarkupPage(
                        string.IsNullOrEmpty(conf.MarkupDatabaseNameReplacement)
                            ? conf.Database
                            : conf.MarkupDatabaseNameReplacement, database);
                Console.WriteLine("Successfully generated C# classes!");
            }

            Console.ReadLine();
        }

        private static void DbToClasses(string dbName, Dictionary<string, List<Column>> db,
            bool generateConstructorAndOutput)
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

                if (generateConstructorAndOutput)
                {
                    // constructor
                    sb.AppendLine($"{Environment.NewLine}public {table.Key}(MySqlDataReader reader)");
                    sb.AppendLine("{");
                    foreach (var column in table.Value)
                        // check which type and use correct get method instead of casting
                        if (column.Type != typeof(string))
                            sb.AppendLine($"{column.Name.FirstCharUpper()} = reader.Get{column.Type.Name}(\"{column.Name}\");");
                        else
                            sb.AppendLine($"{column.Name.FirstCharUpper()} = reader.GetString(\"{column.Name}\");");
                    sb.AppendLine($"}}{Environment.NewLine}");

                    // update query
                    sb.AppendLine("public string UpdateQuery()");
                    sb.AppendLine("{");
                    sb.Append($"return $\"UPDATE `{table.Key}` SET");
                    foreach (var column in table.Value)
                        sb.Append($" {column.Name} = {{{column.Name.FirstCharUpper()}}},");
                    sb.Remove(sb.ToString().LastIndexOf(','), 1);
                    sb.AppendLine($" WHERE {table.Value[0].Name} = {{{table.Value[0].Name.FirstCharUpper()}}};\";");
                    sb.AppendLine($"}}{Environment.NewLine}");

                    // insert query
                    sb.AppendLine("public string InsertQuery()");
                    sb.AppendLine("{");
                    sb.Append($"return $\"INSERT INTO `{table.Key}` VALUES (");
                    foreach (var column in table.Value)
                        sb.Append($" {{{column.Name.FirstCharUpper()}}},");
                    sb.Remove(sb.ToString().LastIndexOf(','), 1);
                    sb.AppendLine($");\";{Environment.NewLine}}}{Environment.NewLine}");

                    // delete query
                    sb.AppendLine("public string DeleteQuery()");
                    sb.AppendLine("{");
                    sb.AppendLine(
                        $"return $\"DELETE FROM `{table.Key}` WHERE {table.Value[0].Name} = {{{table.Value[0].Name.FirstCharUpper()}}};\";");
                    sb.AppendLine("}");
                }

                // class closing
                sb.AppendLine("}");

                var sw = new StreamWriter($"{dbName}/{table.Key}.cs", false);
                sw.Write(sb.ToString());
                sw.Close();
                sb.Clear();
            }
        }

        private static void DbToMarkupPage(string dbName, Dictionary<string, List<Column>> db)
        {
            var wikiDir = "wiki";
            var wikiDbDir = $"{wikiDir}/{dbName}";
            var wikiTableDir = $"{wikiDbDir}/tables";

            if (!Directory.Exists(wikiDir))
                Directory.CreateDirectory(wikiDir);
            if (!Directory.Exists(wikiTableDir))
                Directory.CreateDirectory(wikiTableDir);

            var sb = new StringBuilder();

            sb.AppendLine($"* [[{dbName}|{dbName}]]");

            var sw = new StreamWriter($"{wikiDir}/index.txt", true);
            sw.Write(sb.ToString());
            sw.Close();
            sb.Clear();

            sb.AppendLine($"[[Database Structure|Database Structure]] > [[{dbName}|{dbName}]]");

            // generate index pages
            foreach (var table in db)
                sb.AppendLine($"* [[{table.Key.FirstCharUpper()}|{table.Key.ToLower()}]]");

            sw = new StreamWriter($"{wikiDbDir}/{dbName}.txt");
            sw.Write(sb.ToString());
            sw.Close();
            sb.Clear();

            foreach (var table in db)
            {
                sb.AppendLine(
                    $"[[Database Structure|Database Structure]] > [[{dbName}|{dbName}]] > [[{table.Key}|{table.Key}]]");
                sb.AppendLine("");
                sb.AppendLine("Column | Type | Description");
                sb.AppendLine("--- | --- | ---");

                foreach (var column in table.Value)
                    sb.AppendLine($"{column.Name.FirstCharUpper()} | {column.ColumnType} | ");
                sw = new StreamWriter($"{wikiTableDir}/{table.Key}.txt");
                sw.Write(sb.ToString());
                sw.Close();
                sb.Clear();
            }
        }
    }
}