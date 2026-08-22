namespace Platform22.Tui;

internal static class SshChannelGuard
{
    /// <summary>FxSsh throws a bare NRE from Session.SocketWrite when the socket is gone.</summary>
    public static bool IsClosedConnectionException(Exception exception)
    {
        return exception is NullReferenceException
            && exception.StackTrace?.Contains("FxSsh.Session.SocketWrite", StringComparison.Ordinal) == true;
    }
}
