using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SinalizacaoViaria.Core.Geometry;

namespace SinalizacaoViaria.Core.Serialization;

/// <summary>Configuração JSON única para catálogo, definições e configurações.</summary>
public static class JsonConfig
{
    public static JsonSerializerOptions Options { get; } = Create(indented: true);

    /// <summary>Versão compacta usada para gravar definições dentro dos elementos do Revit.</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new Vec2JsonConverter());
        return o;
    }
}

/// <summary>Serializa <see cref="Vec2"/> como [x, y].</summary>
public sealed class Vec2JsonConverter : JsonConverter<Vec2>
{
    public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read(); var x = reader.GetDouble();
            reader.Read(); var y = reader.GetDouble();
            reader.Read(); // EndArray
            return new Vec2(x, y);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            double x = 0, y = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase)) x = reader.GetDouble();
                else if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase)) y = reader.GetDouble();
                else reader.Skip();
            }
            return new Vec2(x, y);
        }
        throw new JsonException("Vec2 inválido.");
    }

    public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 6));
        writer.WriteNumberValue(Math.Round(value.Y, 6));
        writer.WriteEndArray();
    }
}
