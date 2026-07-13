using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Unit tests for create-time proposal operation input validation (#1125).
/// </summary>
public class ProposalOperationInputValidatorTests
{
    private static List<CreateProposalOperationDto> One(
        string actionType = "create",
        string targetType = "card",
        string parameters = "{}")
        => new() { new CreateProposalOperationDto(0, actionType, targetType, parameters, "key") };

    [Fact]
    public void Validate_NullOrEmptyOperations_Succeeds()
    {
        ProposalOperationInputValidator.Validate(null).IsSuccess.Should().BeTrue();
        ProposalOperationInputValidator.Validate(new List<CreateProposalOperationDto>())
            .IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("create", "card")]
    [InlineData("card.create", "Card")]    // dotted verb + capitalized target (real fixture style)
    [InlineData("bulk_move", "card")]
    [InlineData("create_column", "column")]
    [InlineData("reorder", "column")]
    [InlineData("card-move", "card")]
    public void Validate_WellFormedTokens_Succeeds(string actionType, string targetType)
    {
        var result = ProposalOperationInputValidator.Validate(One(actionType, targetType));
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE cards; --")]
    [InlineData("create card")]            // internal whitespace
    [InlineData("card\tcreate")]           // embedded tab
    [InlineData(" create ")]               // surrounding whitespace (no longer trimmed away)
    [InlineData("create\n")]               // trailing newline
    [InlineData("")]                       // empty
    [InlineData("   ")]                    // whitespace only
    [InlineData("1create")]                // must start with a letter
    [InlineData(".create")]                // must start with a letter
    public void Validate_MalformedActionType_Fails(string actionType)
    {
        var result = ProposalOperationInputValidator.Validate(One(actionType: actionType));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_MalformedTargetType_Fails()
    {
        var result = ProposalOperationInputValidator.Validate(One(targetType: "<b>card</b>"));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_OverlongActionType_Fails()
    {
        var longToken = "a" + new string('b', ProposalOperationInputValidator.MaxTokenLength);
        var result = ProposalOperationInputValidator.Validate(One(actionType: longToken));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_OverlongTargetType_Fails()
    {
        var longToken = "a" + new string('b', ProposalOperationInputValidator.MaxTokenLength);
        var result = ProposalOperationInputValidator.Validate(One(targetType: longToken));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_NullOperationElement_Fails()
    {
        var ops = new List<CreateProposalOperationDto> { null! };
        var result = ProposalOperationInputValidator.Validate(ops);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_ParametersAtDepthBound_Succeeds()
    {
        // MeasureDepth(BuildNested(n)) == n + 1; BuildNested(Max-1) -> depth == Max (allowed).
        var json = BuildNested(ProposalOperationInputValidator.MaxParametersDepth - 1);
        var result = ProposalOperationInputValidator.Validate(One(parameters: json));
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public void Validate_ParametersJustBeyondDepthBound_Fails()
    {
        // BuildNested(Max) -> depth == Max + 1 (just over the bound).
        var json = BuildNested(ProposalOperationInputValidator.MaxParametersDepth);
        var result = ProposalOperationInputValidator.Validate(One(parameters: json));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_InvalidOrEmptyParameters_Fails(string parameters)
    {
        var result = ProposalOperationInputValidator.Validate(One(parameters: parameters));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void Validate_NonObjectParameters_Fails(string parameters)
    {
        var result = ProposalOperationInputValidator.Validate(One(parameters: parameters));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("JSON object");
    }

    [Fact]
    public void Validate_ParametersWithinDepthBound_Succeeds()
    {
        var result = ProposalOperationInputValidator.Validate(One(parameters: BuildNested(5)));
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public void Validate_ParametersBeyondDepthBound_Fails()
    {
        var json = BuildNested(ProposalOperationInputValidator.MaxParametersDepth + 10);
        var result = ProposalOperationInputValidator.Validate(One(parameters: json));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_OversizedParameters_Fails()
    {
        var huge = "{\"note\":\"" +
                   new string('x', ProposalOperationInputValidator.MaxParametersBytes + 1024) + "\"}";
        var result = ProposalOperationInputValidator.Validate(One(parameters: huge));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Validate_ReportsOffendingOperationIndex()
    {
        var ops = new List<CreateProposalOperationDto>
        {
            new(0, "create", "card", "{}", "k0"),
            new(1, "<bad>", "card", "{}", "k1"),
        };

        var result = ProposalOperationInputValidator.Validate(ops);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Operation 1");
    }

    private static string BuildNested(int depth)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++) sb.Append("{\"a\":");
        sb.Append('1');
        for (var i = 0; i < depth; i++) sb.Append('}');
        return sb.ToString();
    }
}
