using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmKillSwitchServiceTests
{
    [Fact]
    public async Task IsKilledAsync_ShouldReturnFalse_WhenNoKillSwitchActive()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);

        var result = await service.IsKilledAsync(LlmSurface.Chat, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsKilledAsync_ShouldReturnTrue_WhenGlobalKillActive()
    {
        var settings = new LlmKillSwitchSettings { GlobalKill = true };
        var service = new LlmKillSwitchService(settings);

        var result = await service.IsKilledAsync(LlmSurface.Chat, Guid.NewGuid());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GlobalKill_ShouldBlockAllSurfaces()
    {
        var settings = new LlmKillSwitchSettings { GlobalKill = true };
        var service = new LlmKillSwitchService(settings);

        (await service.IsKilledAsync(LlmSurface.Chat)).Should().BeTrue();
        (await service.IsKilledAsync(LlmSurface.CaptureTriage)).Should().BeTrue();
        (await service.IsKilledAsync(LlmSurface.Worker)).Should().BeTrue();
    }

    [Fact]
    public async Task SurfaceKill_ShouldBlockOnlyThatSurface()
    {
        var settings = new LlmKillSwitchSettings
        {
            KilledSurfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Chat" }
        };
        var service = new LlmKillSwitchService(settings);

        (await service.IsKilledAsync(LlmSurface.Chat)).Should().BeTrue();
        (await service.IsKilledAsync(LlmSurface.CaptureTriage)).Should().BeFalse();
        (await service.IsKilledAsync(LlmSurface.Worker)).Should().BeFalse();
    }

    [Fact]
    public async Task IdentityKill_ShouldBlockOnlyThatUser()
    {
        var killedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var settings = new LlmKillSwitchSettings
        {
            KilledUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { killedUserId.ToString() }
        };
        var service = new LlmKillSwitchService(settings);

        (await service.IsKilledAsync(LlmSurface.Chat, killedUserId)).Should().BeTrue();
        (await service.IsKilledAsync(LlmSurface.Chat, otherUserId)).Should().BeFalse();
    }

    [Fact]
    public async Task SetKillSwitchAsync_Global_ShouldEnableAndDisable()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);

        var enableResult = await service.SetKillSwitchAsync(KillSwitchScope.Global, null, true, "emergency");
        enableResult.IsSuccess.Should().BeTrue();
        (await service.IsKilledAsync()).Should().BeTrue();

        var disableResult = await service.SetKillSwitchAsync(KillSwitchScope.Global, null, false, null);
        disableResult.IsSuccess.Should().BeTrue();
        (await service.IsKilledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SetKillSwitchAsync_Surface_ShouldToggle()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);

        await service.SetKillSwitchAsync(KillSwitchScope.Surface, "Chat", true, "maintenance");
        (await service.IsKilledAsync(LlmSurface.Chat)).Should().BeTrue();
        (await service.IsKilledAsync(LlmSurface.Worker)).Should().BeFalse();

        await service.SetKillSwitchAsync(KillSwitchScope.Surface, "Chat", false, null);
        (await service.IsKilledAsync(LlmSurface.Chat)).Should().BeFalse();
    }

    [Fact]
    public async Task SetKillSwitchAsync_Surface_ShouldRejectInvalidSurface()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);

        var result = await service.SetKillSwitchAsync(KillSwitchScope.Surface, "InvalidSurface", true, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SetKillSwitchAsync_Identity_ShouldToggle()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);
        var userId = Guid.NewGuid();

        await service.SetKillSwitchAsync(KillSwitchScope.Identity, userId.ToString(), true, "abuse");
        (await service.IsKilledAsync(LlmSurface.Chat, userId)).Should().BeTrue();

        await service.SetKillSwitchAsync(KillSwitchScope.Identity, userId.ToString(), false, null);
        (await service.IsKilledAsync(LlmSurface.Chat, userId)).Should().BeFalse();
    }

    [Fact]
    public async Task SetKillSwitchAsync_Identity_ShouldRejectInvalidGuid()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);

        var result = await service.SetKillSwitchAsync(KillSwitchScope.Identity, "not-a-guid", true, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SetKillSwitchAsync_ShouldStoreAndReturnReason()
    {
        var settings = new LlmKillSwitchSettings();
        var service = new LlmKillSwitchService(settings);

        await service.SetKillSwitchAsync(KillSwitchScope.Global, null, true, "emergency shutdown");
        await service.SetKillSwitchAsync(KillSwitchScope.Surface, "Chat", true, "maintenance window");

        var status = await service.GetStatusAsync();

        status.Entries.Should().Contain(e => e.Scope == KillSwitchScope.Global && e.Reason == "emergency shutdown");
        status.Entries.Should().Contain(e => e.Scope == KillSwitchScope.Surface && e.Target == "Chat" && e.Reason == "maintenance window");
    }

    [Fact]
    public async Task GetStatusAsync_ShouldReflectCurrentState()
    {
        var settings = new LlmKillSwitchSettings
        {
            GlobalKill = true,
            KilledSurfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Worker" },
            KilledUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Guid.NewGuid().ToString() }
        };
        var service = new LlmKillSwitchService(settings);

        var status = await service.GetStatusAsync();

        status.GlobalKilled.Should().BeTrue();
        status.Entries.Should().HaveCount(3); // global + 1 surface + 1 identity
        status.Entries.Should().Contain(e => e.Scope == KillSwitchScope.Global && e.Enabled);
        status.Entries.Should().Contain(e => e.Scope == KillSwitchScope.Surface && e.Target == "Worker");
        status.Entries.Should().Contain(e => e.Scope == KillSwitchScope.Identity && e.Enabled);
    }
}
