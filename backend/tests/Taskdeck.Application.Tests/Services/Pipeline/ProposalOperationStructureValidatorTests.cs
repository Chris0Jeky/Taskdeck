using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class ProposalOperationStructureValidatorTests
{
    [Fact]
    public void Validate_ShouldRejectNullOperationParameters()
    {
        // The DTO declares Parameters non-nullable, but legacy rows / nullable DB data
        // can surface null at runtime. The shared validator must fail closed with a
        // ValidationError (identically on preview and apply) instead of throwing.
        var operation = CreateOperation(0, parameters: null!);

        var result = ProposalOperationStructureValidator.Validate(new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("parameters must be provided");
    }

    [Fact]
    public void Validate_ShouldAcceptExactlyMaxOperationCount()
    {
        // Boundary-PASS: the ceiling itself is valid; only count > MaxOperationCount fails.
        var operations = Enumerable.Range(0, ProposalOperationStructureValidator.MaxOperationCount)
            .Select(i => CreateOperation(i, "{}"))
            .ToList();

        var result = ProposalOperationStructureValidator.Validate(operations);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    private static ProposalOperationDto CreateOperation(int sequence, string parameters)
    {
        return new ProposalOperationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sequence,
            "update",
            "card",
            null,
            parameters,
            Guid.NewGuid().ToString(),
            null);
    }
}
