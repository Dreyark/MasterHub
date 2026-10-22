using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace MasterHub.Core.Integrations.HomeAssistant;

public class HomeAssistantEntityModel
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "off";

    [JsonPropertyName("attributes")]
    public HomeAssistantAttributesModel Attributes { get; set; } = new();

    [JsonIgnore]
    public string FriendlyName => !string.IsNullOrWhiteSpace(Attributes.FriendlyName)
        ? Attributes.FriendlyName
        : EntityId;

    [JsonIgnore]
    public string Domain => EntityId.Split('.').FirstOrDefault() ?? "";

    [JsonIgnore]
    public bool IsOn => string.Equals(State, "on", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(State, "open", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(State, "playing", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(State, "active", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(State, "home", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public int? Brightness => Attributes.Brightness;
}

public class HomeAssistantAttributesModel
{
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; set; }

    [JsonPropertyName("brightness")]
    public int? Brightness { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("unit_of_measurement")]
    public string? UnitOfMeasurement { get; set; }
}
