namespace FocusAgent.Core.Sessions;

public interface ISessionUiHost
{
    Task<JoinDecision> ShowJoinConfirmationAsync(JoinConfirmation confirmation, CancellationToken ct = default);

    void DismissJoinConfirmation();
}
