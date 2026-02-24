namespace Taskdeck.Domain.Enums;

public enum WebhookDeliveryStatus
{
    Pending = 0,
    Processing = 1,
    Delivered = 2,
    DeadLetter = 3
}
