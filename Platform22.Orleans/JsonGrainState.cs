namespace Platform22.Orleans;

using global::Orleans;

/// <summary>
/// Wraps JSON payloads stored through Orleans grain storage providers. A plain
/// string state type breaks providers that build default instances on read.
/// </summary>
[GenerateSerializer]
public sealed record JsonGrainState
{
    [Id(0)]
    public string? Value { get; set; }
}
