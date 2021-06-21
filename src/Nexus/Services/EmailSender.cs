using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using System;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public class EmailSender : IEmailSender
    {
        private ILogger<EmailSender> _logger;
        private SmtpOptions _smtpOptions;

        public EmailSender(ILogger<EmailSender> logger, IOptions<SmtpOptions> smtpOptions)
        {
            _logger = logger;
            _smtpOptions = smtpOptions.Value;
        }

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            var logMessage = $"Sending mail to address {email} via host {_smtpOptions.Host}:{_smtpOptions.Port} ... ";
            _logger.LogInformation(logMessage);

            try
            {
                var client = new SmtpClient(_smtpOptions.Host, _smtpOptions.Port);
                var fromMailAddress = new MailAddress(_smtpOptions.FromAddress, _smtpOptions.FromName);
                var toMailAddress = new MailAddress(email);

                var mailMessage = new MailMessage(fromMailAddress, toMailAddress)
                {
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = true
                };

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