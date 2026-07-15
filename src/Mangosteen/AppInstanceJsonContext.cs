using System.Text.Json.Serialization;

namespace Mangosteen;

[JsonSerializable(typeof(AppActivationRequest))]
internal sealed partial class AppInstanceJsonContext : JsonSerializerContext;
