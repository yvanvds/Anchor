namespace FocusAgent.Core.Focus;

public interface IForegroundWatcher : IDisposable
{
    event Action<ForegroundChange>? Changed;

    bool IsRunning { get; }
    void Start();
    void Stop();
}
