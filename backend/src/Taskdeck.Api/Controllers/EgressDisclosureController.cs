using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/privacy/egress")]
[Produces("application/json")]
public class EgressDisclosureController : ControllerBase
{
    private readonly IEgressRegistry _egressRegistry;

    public EgressDisclosureController(IEgressRegistry egressRegistry)
    {
        _egressRegistry = egressRegistry;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EgressDisclosureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetDisclosure()
    {
        var entries = _egressRegistry.GetAllEntries();

        var destinations = entries.Select(e => new EgressDestinationDto(
            e.Host,
            e.PayloadCategory,
            e.ToolOrAgentName,
            e.Classification.ToString())).ToList();

        return Ok(new EgressDisclosureResponse(destinations, destinations.Count));
    }
}

public sealed record EgressDisclosureResponse(
    IReadOnlyList<EgressDestinationDto> Destinations,
    int TotalCount);

public sealed record EgressDestinationDto(
    string Host,
    string PayloadCategory,
    string ToolOrAgent,
    string DataClassification);
