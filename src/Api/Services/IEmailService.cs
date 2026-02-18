namespace Api.Services;

public record EmailResult(
    bool Success,
    string? FailureReason = null
);

public interface IEmailService
{
    Task<EmailResult> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
