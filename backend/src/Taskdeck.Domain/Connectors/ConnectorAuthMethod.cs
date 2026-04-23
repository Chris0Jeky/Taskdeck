namespace Taskdeck.Domain.Connectors;

public enum ConnectorAuthMethod
{
    None = 0,
    ApiKey = 1,
    OAuth2 = 2,
    PersonalAccessToken = 3,
    WebhookSecret = 4
}
