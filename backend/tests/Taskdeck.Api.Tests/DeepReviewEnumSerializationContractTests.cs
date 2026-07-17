using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class DeepReviewEnumSerializationContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DeepReviewEnumSerializationContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ApiJsonOptions_ShouldSerializeDeepReviewEnumsAsNumericOrdinals()
    {
        var serializerOptions = _factory.Services
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.JsonSerializerOptions;
        var payload = new
        {
            Conflicts = new[]
            {
                new ConflictRowDto(ConflictTone.Warn, "warn", "v"),
                new ConflictRowDto(ConflictTone.Info, "info", "v"),
                new ConflictRowDto(ConflictTone.Ok, "ok", "v")
            },
            History = new[]
            {
                new CardHistoryRowDto("#1", "pending", "now", CardHistoryStatus.Pending),
                new CardHistoryRowDto("#2", "applied", "now", CardHistoryStatus.Applied),
                new CardHistoryRowDto("#3", "past", "now", CardHistoryStatus.Past)
            }
        };

        var json = JsonSerializer.Serialize(payload, serializerOptions);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("conflicts")
            .EnumerateArray()
            .Select(row => row.GetProperty("tone").GetInt32())
            .Should().Equal(0, 1, 2);
        document.RootElement.GetProperty("history")
            .EnumerateArray()
            .Select(row => row.GetProperty("status").GetInt32())
            .Should().Equal(0, 1, 2);
    }
}
