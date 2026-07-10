using System.Text.Json.Serialization;

namespace Mangosteen;

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
