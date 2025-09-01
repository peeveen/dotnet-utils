using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peeveen.Utils.Dynamic {
	/// <summary>
	/// JSON Converter for collections of dynamic objects.
	/// </summary>
	public class DynamicCollectionConverter : JsonConverter<dynamic[]> {
		/// <summary>
		/// Singleton instance, to save you creating multiple instances.
		/// The class has not been given a private constructor, as usage
		/// via the System.Text.Json [Converter] attribute requires an
		/// accessible constructor.
		/// </summary>
		public static readonly DynamicCollectionConverter Instance = new DynamicCollectionConverter();

		/// <inheritdoc/>
		public override dynamic[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			if (reader.TokenType != JsonTokenType.StartArray) {
				reader.Skip();
				return null;
			}
			return DynamicConverter.ReadDynamicJsonArray(ref reader, options);
		}

		/// <inheritdoc/>
		public override void Write(Utf8JsonWriter writer, dynamic[] value, JsonSerializerOptions options) {
			// Remove this converter to prevent recursion.
			var limitedOptions = new JsonSerializerOptions(options);
			limitedOptions.Converters.Remove(this);
			writer.WriteStartArray();
			foreach (dynamic item in value)
				// Standard serializer has no problem writing dynamics.
				JsonSerializer.Serialize(writer, item, limitedOptions);
			writer.WriteEndArray();
		}
	}
}
