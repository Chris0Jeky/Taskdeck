using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public enum RegistrationMode
{
    Open = 0,
    InviteOnly = 1,
    Closed = 2
}

public sealed class RegistrationSettings
{
    [EnumDataType(typeof(RegistrationMode))]
    public RegistrationMode Mode { get; set; } = RegistrationMode.Open;
}
