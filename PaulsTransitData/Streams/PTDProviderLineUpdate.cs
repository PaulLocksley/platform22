namespace PaulsTransitData.Streams;

using PaulsTransitData.Models;

public sealed record PTDProviderLineUpdate(
    string SchemaVersion,
    string ProviderId,
    string LineId,
    string MessageId,
    DateTimeOffset OccurredAt,
    PTDLineSnapshot Snapshot);
