using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private const int QueueDepthDegradedThreshold = 100;
    private readonly IServiceProvider _serviceProvider;

    public HealthController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    [HttpGet("live")]
    public IActionResult LiveCheck()
    {
        return Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> ReadyCheck(CancellationToken ct = default)
    {
        var checks = new Dictionary<string, object>();
        var isReady = true;

        // DB connectivity check
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await dbContext.Database.CanConnectAsync(ct);
            checks["database"] = new { status = "Healthy" };
        }
        catch (Exception ex)
        {
            checks["database"] = new { status = "Unhealthy", error = ex.Message };
            isReady = false;
        }

        // Queue lag check
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var pending = await unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending, ct);
            var queueDepth = pending.Count();
            checks["queue"] = new { status = queueDepth > QueueDepthDegradedThreshold ? "Degraded" : "Healthy", depth = queueDepth };
            if (queueDepth > QueueDepthDegradedThreshold) isReady = false;
        }
        catch (Exception ex)
        {
            checks["queue"] = new { status = "Unhealthy", error = ex.Message };
            isReady = false;
        }

        var statusCode = isReady ? 200 : 503;
        return StatusCode(statusCode, new
        {
            status = isReady ? "Ready" : "NotReady",
            timestamp = DateTimeOffset.UtcNow,
            checks
        });
    }
}
