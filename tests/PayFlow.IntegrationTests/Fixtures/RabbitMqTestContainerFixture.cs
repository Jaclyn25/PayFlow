using Testcontainers.RabbitMq;
using Xunit;

namespace PayFlow.IntegrationTests.Fixtures;

public class RabbitMqTestContainerFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    public string HostName => _container?.Hostname 
        ?? throw new InvalidOperationException("RabbitMQ container is not initialized.");

    public int Port => _container?.GetMappedPublicPort(5672) 
        ?? throw new InvalidOperationException("RabbitMQ container is not initialized.");

    public string UserName => "guest";
    public string Password => "guest";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder("rabbitmq:3-management-alpine")
                .WithUsername(UserName)
                .WithPassword(Password)
                .Build();

            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            _container = null;
            throw new InvalidOperationException($"[SKIP/FAIL] Real RabbitMQ Testcontainers require a running Docker daemon. Docker API connection failed: {ex.Message}", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
