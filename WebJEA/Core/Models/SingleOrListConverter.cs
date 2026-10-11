using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebJEA;

/// <summary>
/// Lets a config property declared as a list also be written as one bare string:
/// "PermittedGroups": "*" loads the same as "PermittedGroups": ["*"].
/// </summary>
public class SingleOrListConverter : JsonConverter<List<string>>
{
    public override bool CanWrite => false;

    public override List<string> ReadJson(JsonReader reader, Type objectType, List<string> existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonToken.String)
        {
            return new List<string> { (string)reader.Value };
        }

        return JArray.Load(reader).ToObject<List<string>>(serializer);
    }

    public override void WriteJson(JsonWriter writer, List<string> value, JsonSerializer serializer)
    {
        throw new NotSupportedException();
    }
}
