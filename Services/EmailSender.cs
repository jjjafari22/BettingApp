using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using BettingApp.Data;

namespace BettingApp.Services;

public class EmailSender : IEmailSender<ApplicationUser>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        string subject = "Confirm your email";
        string message = $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.";
        await SendEmailAsync(email, subject, message);
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        string subject = "Reset your password";
        string message = $"Please reset your password by <a href='{resetLink}'>clicking here</a>.";
        await SendEmailAsync(email, subject, message);
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        string subject = "Reset your password";
        string message = $"Please reset your password using the following code: {resetCode}";
        await SendEmailAsync(email, subject, message);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string message)
    {
        var emailSettings = _configuration.GetSection("EmailSettings");
        
        // FIX: Use '?? ""' to handle nulls safely (Warnings CS8600)
        string server = emailSettings["Server"] ?? "";
        int port = int.Parse(emailSettings["Port"] ?? "587");
        string senderName = emailSettings["SenderName"] ?? "Castle of Happiness";
        string senderEmail = emailSettings["SenderEmail"] ?? "";
        string username = emailSettings["Username"] ?? "";
        string password = emailSettings["Password"] ?? "";

        // Validate critical fields are present before trying to send
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(password))
        {
            _logger.LogError("Email settings are incomplete. Server, SenderEmail, or Password is missing.");
            return;
        }

        try
        {
            var mailMessage = new MailMessage
            {
                // FIX: senderEmail is now guaranteed to be a string (Warning CS8604)
                From = new MailAddress(senderEmail, senderName),
                Subject = subject,
                Body = message,
                IsBodyHtml = true
            };
            mailMessage.To.Add(toEmail);

            using var client = new SmtpClient(server, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = true
            };

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation($"Email sent successfully to {toEmail}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send email to {toEmail}");
        }
    }
}