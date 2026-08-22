namespace PaulsTransitData.Providers;

using PaulsTransitData.Streams;

public interface ITransitProviderMapper<TStaticResponse, TRealtimeResponse>
{
    PTDProviderLineUpdate MapLineUpdate(TStaticResponse staticResponse, TRealtimeResponse realtimeResponse, string lineId);
}
