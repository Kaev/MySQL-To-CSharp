using System.Linq;

namespace MySQL_To_CSharp
{
    public static class StringExtensions
    {
        public static string FirstCharUpper(this string str)
        {
            return str.First().ToString().ToUpper() + str.Substring(1);
        }
    }
}