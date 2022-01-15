using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using Nexus.Core;
using System.Net.Mail;

namespace Nexus.Services
{
    internal class EmailSender : IEmailSender
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
            if (_smtpOptions.Host is null || 
                _smtpOptions.Port == 0 ||
                _smtpOptions.FromAddress is null || 
                _smtpOptions.FromName is null)
            {
                _logger.LogTrace("Unable to send mail because SMPT options are not configured properly.");
                return;
            }

            _logger.LogDebug("Send mail to address {MailAddress}", email);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Send mail to address {MailAddress} failed", email);
            }
        }
    }
}