// POCOs espejo de los contratos JSON reales de Sigil (doc 04 §4) para el spike de sandbox.
// Replican ParticipantInput/ZoneInput de Sigil.Plugins.Core (Guid + int? + double con
// JsonPropertyName) para que la (de)serialización reflexiva de System.Text.Json que corre
// acá sea la MISMA ruta de código que el motor usará — no una versión de juguete.

using System;
using System.Text.Json.Serialization;

namespace SigilSpike.Plugin
{
    public sealed class ParticipantInputSpike
    {
        [JsonPropertyName("userId")]
        public Guid UserId { get; set; }

        [JsonPropertyName("order")]
        public int? Order { get; set; }
    }

    public sealed class ZoneInputSpike
    {
        [JsonPropertyName("userId")]
        public Guid UserId { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("w")]
        public double W { get; set; }

        [JsonPropertyName("h")]
        public double H { get; set; }
    }
}
