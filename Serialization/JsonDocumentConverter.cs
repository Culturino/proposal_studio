using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace ProposalStudio.Serialization
{
    /// <summary>
    /// Newtonsoft has no built-in handling for System.Text.Json documents. Without this it
    /// reflects over their public members and emits {"RootElement":{"ValueKind":1}} in place of
    /// the stored JSON, which is how jsonb columns such as products.dimensions reach the client.
    /// </summary>
    public class JsonDocumentConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(JsonDocument) ||
            objectType == typeof(JsonElement) ||
            objectType == typeof(JsonElement?);

        public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
        {
            var raw = value switch
            {
                JsonDocument document => document.RootElement.GetRawText(),
                JsonElement element => element.GetRawText(),
                _ => null
            };

            if (raw == null)
                writer.WriteNull();
            else
                writer.WriteRawValue(raw);
        }

        public override object? ReadJson(
            JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var raw = JToken.Load(reader).ToString(Formatting.None);
            var document = JsonDocument.Parse(raw);

            // Clone so the element stays valid independently of the document's pooled buffers.
            return objectType == typeof(JsonDocument) ? document : document.RootElement.Clone();
        }
    }
}
