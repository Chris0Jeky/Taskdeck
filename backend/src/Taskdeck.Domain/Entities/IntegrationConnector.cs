using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class IntegrationConnector : Entity
{
    private const int MaxNameLength = 100;
    private const int MaxConfigurationLength = 4000;

    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Connector name cannot be empty.");

            var trimmed = value.Trim();
            if (trimmed.Length > MaxNameLength)
                throw new DomainException(
                    ErrorCodes.ValidationError,
                    $"Connector name cannot exceed {MaxNameLength} characters.");

            _name = trimmed;
        }
    }

    public ConnectorType ConnectorType { get; private set; }
    public ConnectorDirection Direction { get; private set; }
    public ConnectorStatus Status { get; private set; }
    public string? Configuration { get; private set; }
    public Guid UserId { get; private set; }

    private IntegrationConnector() : base() { }

    public IntegrationConnector(
        string name,
        ConnectorType connectorType,
        ConnectorDirection direction,
        Guid userId,
        string? configuration = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty.");

        if (!Enum.IsDefined(connectorType))
            throw new DomainException(ErrorCodes.ValidationError, $"Invalid connector type: {connectorType}.");

        if (!Enum.IsDefined(direction))
            throw new DomainException(ErrorCodes.ValidationError, $"Invalid connector direction: {direction}.");

        Name = name;
        ConnectorType = connectorType;
        Direction = direction;
        Status = ConnectorStatus.Active;
        UserId = userId;
        SetConfiguration(configuration);
    }

    public void UpdateName(string name)
    {
        Name = name;
        Touch();
    }

    public void UpdateConfiguration(string? configuration)
    {
        SetConfiguration(configuration);
        Touch();
    }

    public void Enable()
    {
        if (Status == ConnectorStatus.Active)
            throw new DomainException(ErrorCodes.InvalidOperation, "Connector is already active.");

        Status = ConnectorStatus.Active;
        Touch();
    }

    public void Disable()
    {
        if (Status == ConnectorStatus.Disabled)
            throw new DomainException(ErrorCodes.InvalidOperation, "Connector is already disabled.");

        Status = ConnectorStatus.Disabled;
        Touch();
    }

    public void MarkError()
    {
        Status = ConnectorStatus.Error;
        Touch();
    }

    private void SetConfiguration(string? configuration)
    {
        if (configuration != null && configuration.Length > MaxConfigurationLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Configuration cannot exceed {MaxConfigurationLength} characters.");

        Configuration = configuration;
    }
}
