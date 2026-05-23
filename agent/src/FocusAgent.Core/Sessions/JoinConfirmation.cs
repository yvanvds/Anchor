using FocusAgent.Core.Dtos;

namespace FocusAgent.Core.Sessions;

public sealed class JoinConfirmation : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _tick;
    private readonly TaskCompletionSource<JoinDecision> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private ITimer? _timer;
    private DateTimeOffset _deadline;
    private JoinDecision _decision = JoinDecision.Pending;

    public JoinConfirmation(SessionStartedPayload payload, string teacherDisplayName, TimeSpan duration, TimeProvider clock, TimeSpan? tickInterval = null)
    {
        Payload = payload;
        TeacherDisplayName = teacherDisplayName;
        _duration = duration;
        _clock = clock;
        _tick = tickInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public SessionStartedPayload Payload { get; }
    public string TeacherDisplayName { get; }
    public TimeSpan Duration => _duration;

    public JoinDecision Decision
    {
        get { lock (_gate) return _decision; }
    }

    public Task<JoinDecision> Completion => _completion.Task;

    public event EventHandler<TimeSpan>? Tick;
    public event EventHandler<JoinDecision>? Finished;

    public TimeSpan Remaining
    {
        get
        {
            var left = _deadline - _clock.GetUtcNow();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_decision != JoinDecision.Pending || _timer is not null)
                return;
            _deadline = _clock.GetUtcNow() + _duration;
            _timer = _clock.CreateTimer(OnTick, state: null, dueTime: _tick, period: _tick);
        }
        Tick?.Invoke(this, _duration);
    }

    public void Cancel() => Finish(JoinDecision.Declined);

    public void Abort() => Finish(JoinDecision.Aborted);

    private void OnTick(object? _)
    {
        TimeSpan remaining;
        bool elapsed;
        lock (_gate)
        {
            if (_decision != JoinDecision.Pending)
                return;
            remaining = Remaining;
            elapsed = remaining <= TimeSpan.Zero;
        }

        if (elapsed)
        {
            Finish(JoinDecision.Confirmed);
            return;
        }

        Tick?.Invoke(this, remaining);
    }

    private void Finish(JoinDecision decision)
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_decision != JoinDecision.Pending)
                return;
            _decision = decision;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
        Finished?.Invoke(this, decision);
        _completion.TrySetResult(decision);
    }

    public void Dispose() => Abort();
}
