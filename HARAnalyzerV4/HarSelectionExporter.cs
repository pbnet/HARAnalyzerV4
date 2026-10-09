using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HARAnalyzerV4
{
    internal static class HarSelectionExporter
    {
        public static void Write(
            Stream output,
            JsonElement root,
            IEnumerable<JsonElement> entries)
        {
            using Utf8JsonWriter writer = new(
                output,
                new JsonWriterOptions
                {
                    Indented = true
                });

            writer.WriteStartObject();

            foreach (JsonProperty property in root.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);

                if (property.Name != "log")
                {
                    property.Value.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();

                foreach (JsonProperty logProperty
                         in property.Value.EnumerateObject())
                {
                    writer.WritePropertyName(logProperty.Name);

                    if (logProperty.Name != "entries")
                    {
                        logProperty.Value.WriteTo(writer);
                        continue;
                    }

                    writer.WriteStartArray();

                    foreach (JsonElement entry in entries)
                    {
                        entry.WriteTo(writer);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.Flush();
        }
    }
}