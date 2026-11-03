using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Order.Microservice.Data;

namespace Order.Microservice.Events;

public sealed class OutboxPublisher : BackgroundService
{
    private const string ExchangeName = "orders.events.v1";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox publishing failed; pending messages will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await db.OutboxMessages.Where(message => message.PublishedUtc == null)
            .OrderBy(message => message.OccurredUtc).Take(50).ToListAsync(cancellationToken);
        if (pending.Count == 0) return;

        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMq:HostName"] ?? "localhost",
            UserName = _configuration["RabbitMq:UserName"] ?? "guest",
            Password = _configuration["RabbitMq:Password"] ?? "guest"
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);

        foreach (var message in pending)
        {
            try
            {
                var properties = new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = message.Id.ToString() };
                await channel.BasicPublishAsync(ExchangeName, "", mandatory: true, basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload), cancellationToken: cancellationToken);
                message.PublishedUtc = DateTime.UtcNow;
                message.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = exception.Message;
                _logger.LogWarning(exception, "Could not publish outbox message {MessageId}.", message.Id);
                break;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
