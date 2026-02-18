namespace Api.Services;

public class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _logger;

    public NullEmailService(ILogger<NullEmailService> logger)
    {
        _logger = logger;
    }

    public Task<EmailResult> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "NullEmailService: Would send email to {Email} with subject \"{Subject}\". " +
            "Configure a real email service for production.",
            toEmail, subject);

        return Task.FromResult(new EmailResult(
            Success: false,
            FailureReason: "NullEmailService: emails disabled in development"));
    }
}
