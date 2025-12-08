using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMCPServer.Services
{
    internal static class SerializationUtility
    {
        public static object ConvertJTokenToObjectSmart(JToken token)
        {
            if(token == null)
            {
                return null!;
            }

            switch (token.Type)
            {
                case JTokenType.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (JProperty prop in token.Children<JProperty>())
                    {
                        dict[prop.Name] = ConvertJTokenToObjectSmart(prop.Value);
                    }
                    return dict;

                case JTokenType.Array:
                    return token.Select(ConvertJTokenToObjectSmart).ToList();

                case JTokenType.Integer:
                    var intValue = token.Value<long>();
                    // Return int if it fits, otherwise long
                    return intValue >= int.MinValue && intValue <= int.MaxValue
                        ? (object)(int)intValue
                        : intValue;

                case JTokenType.Float:
                    // Try to preserve precision
                    var floatStr = token.ToString();
                    if (decimal.TryParse(floatStr, out decimal decValue))
                        return decValue;
                    return token.Value<double>();

                case JTokenType.String:
                    var strValue = token.Value<string>();
                    // Optionally try to parse known formats
                    if (DateTime.TryParse(strValue, out DateTime dateValue))
                        return dateValue;
                    if (Guid.TryParse(strValue, out Guid guidValue))
                        return guidValue;
                    return strValue;

                case JTokenType.Boolean:
                    return token.Value<bool>();

                case JTokenType.Date:
                    return token.Value<DateTime>();

                case JTokenType.Null:
                    return null!;

                default:
                    return token.Value<string>()!;
            }
        }
    }
}
