namespace Beats.Production.Middleware.Eventing;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "beats";
    public string Username { get; set; } = "beats";
    public string Password { get; set; } = "beats-dev";
}
