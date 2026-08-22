namespace PaulsTransitData.Abstractions;

using PaulsTransitData.Models;

public interface IPTDStationSubscription : IAsyncDisposable
{
    string StopId { get; }

    PTDStationSnapshot Current { get; }

    IAsyncEnumerable<PTDStationSnapshot> Updates { get; }
}
