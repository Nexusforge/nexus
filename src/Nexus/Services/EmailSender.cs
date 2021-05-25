using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Nexus.Types;
using System;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public class EmailSender : IEmailSender
    {
        public ILogger<EmailSender> _logger { get; set; }

        public EmailSender(ILogger<EmailSender> logger)
        {
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            var emailOptions = Program.Options.Email;
            var logMessage = $"Sending mail to address {email} via server {emailOptions.ServerAddress}:{emailOptions.Port} ... ";
            _logger.LogInformation(logMessage);

            try
            {
                var client = new SmtpClient(emailOptions.ServerAddress, (int)emailOptions.Port);
                var mailMessage = new MailMessage(emailOptions.SenderEmail, email, subject, message);

                await client.SendMailAsync(mailMessage);

                _logger.LogInformation(logMessage + "Done.");
            }
            catch (Exception ex)
            {
                _logger.LogError(logMessage + $"Failed. Reason: {ex.GetFullMessage()}");
            }
        }
    }
}