using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using bank.Data;
using Microsoft.EntityFrameworkCore;

namespace bank.Services.ExternalPayments
{
    public class PaymentPollingBackgroundService : BackgroundService
    {
        private readonly ILogger<PaymentPollingBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public PaymentPollingBackgroundService(ILogger<PaymentPollingBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var rtpService = scope.ServiceProvider.GetRequiredService<RtpService>();
                        var fedNowService = scope.ServiceProvider.GetRequiredService<FedNowService>();
                        var achService = scope.ServiceProvider.GetRequiredService<AchService>();
                        // var dbContext = scope.ServiceProvider.GetRequiredService<BankDbContext>();

                        // Poll RTP
                        var rtpIncoming = await rtpService.FetchIncomingRtpAsync();
                        if (!string.IsNullOrEmpty(rtpIncoming))
                        {
                            _logger.LogInformation("Received RTP Incoming messages: {Data}", rtpIncoming);
                            // TODO: Parse the pacs.008 XML, find target account, credit balance
                            // and send pacs.002 settlement confirmation via POST /transfers/settle
                        }

                        // Poll FedNow
                        var fedNowIncoming = await fedNowService.FetchIncomingFedNowAsync();
                        if (!string.IsNullOrEmpty(fedNowIncoming))
                        {
                            _logger.LogInformation("Received FedNow Incoming message: {Data}", fedNowIncoming);
                            // TODO: Parse FedNow XML, credit account
                        }

                        // Poll ACH
                        // (Can use AchService to connect SFTP, list /outbound/ files, process and delete)
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during payment polling.");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
