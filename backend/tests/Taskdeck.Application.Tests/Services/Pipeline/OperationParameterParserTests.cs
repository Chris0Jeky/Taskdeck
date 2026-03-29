using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.Services.Pipeline;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class OperationParameterParserTests
{
    #region TryDeserializeParameters

    [Fact]
    public void TryDeserializeParameters_ShouldReturnFalse_ForEmptyString()
    {
        var result = OperationParameterParser.TryDeserializeParameters("", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("cannot be empty");
    }

    [Fact]
    public void TryDeserializeParameters_ShouldReturnFalse_ForNullString()
    {
        var result = OperationParameterParser.TryDeserializeParameters(null!, out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("cannot be empty");
    }

    [Fact]
    public void TryDeserializeParameters_ShouldReturnFalse_ForInvalidJson()
    {
        var result = OperationParameterParser.TryDeserializeParameters("{invalid", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("Invalid operation parameters JSON");
    }

    [Fact]
    public void TryDeserializeParameters_ShouldReturnTrue_ForValidJson()
    {
        var result = OperationParameterParser.TryDeserializeParameters("""{"key":"value"}""", out var parameters, out var error);

        result.Should().BeTrue();
        error.Should().BeEmpty();
        parameters.GetProperty("key").GetString().Should().Be("value");
    }

    #endregion

    #region TryGetRequiredString

    [Fact]
    public void TryGetRequiredString_ShouldReturnFalse_WhenPropertyMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        var result = OperationParameterParser.TryGetRequiredString(json, "name", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("Missing required parameter 'name'");
    }

    [Fact]
    public void TryGetRequiredString_ShouldReturnFalse_WhenPropertyIsNotString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"name":42}""");
        var result = OperationParameterParser.TryGetRequiredString(json, "name", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("must be a string");
    }

    [Fact]
    public void TryGetRequiredString_ShouldReturnFalse_WhenPropertyIsEmptyString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"name":""}""");
        var result = OperationParameterParser.TryGetRequiredString(json, "name", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("cannot be empty");
    }

    [Fact]
    public void TryGetRequiredString_ShouldReturnTrue_ForValidString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"name":"hello"}""");
        var result = OperationParameterParser.TryGetRequiredString(json, "name", out var value, out var error);

        result.Should().BeTrue();
        value.Should().Be("hello");
        error.Should().BeEmpty();
    }

    #endregion

    #region GetOptionalString

    [Fact]
    public void GetOptionalString_ShouldReturnNull_WhenPropertyMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        OperationParameterParser.GetOptionalString(json, "desc").Should().BeNull();
    }

    [Fact]
    public void GetOptionalString_ShouldReturnNull_WhenPropertyIsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"desc":null}""");
        OperationParameterParser.GetOptionalString(json, "desc").Should().BeNull();
    }

    [Fact]
    public void GetOptionalString_ShouldReturnNull_WhenPropertyIsNotString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"desc":123}""");
        OperationParameterParser.GetOptionalString(json, "desc").Should().BeNull();
    }

    [Fact]
    public void GetOptionalString_ShouldReturnValue_WhenValid()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"desc":"hello"}""");
        OperationParameterParser.GetOptionalString(json, "desc").Should().Be("hello");
    }

    [Fact]
    public void GetOptionalString_ShouldReturnNull_WhenWhitespaceOnly()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"desc":"   "}""");
        OperationParameterParser.GetOptionalString(json, "desc").Should().BeNull();
    }

    #endregion

    #region GetOptionalBoolean

    [Fact]
    public void GetOptionalBoolean_ShouldReturnNull_WhenPropertyMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        OperationParameterParser.GetOptionalBoolean(json, "flag").Should().BeNull();
    }

    [Fact]
    public void GetOptionalBoolean_ShouldReturnTrue_WhenTrue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"flag":true}""");
        OperationParameterParser.GetOptionalBoolean(json, "flag").Should().BeTrue();
    }

    [Fact]
    public void GetOptionalBoolean_ShouldReturnFalse_WhenFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"flag":false}""");
        OperationParameterParser.GetOptionalBoolean(json, "flag").Should().BeFalse();
    }

    [Fact]
    public void GetOptionalBoolean_ShouldReturnNull_WhenNonBooleanType()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"flag":"yes"}""");
        OperationParameterParser.GetOptionalBoolean(json, "flag").Should().BeNull();
    }

    #endregion

    #region TryGetRequiredGuid

    [Fact]
    public void TryGetRequiredGuid_ShouldReturnFalse_WhenPropertyMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        var result = OperationParameterParser.TryGetRequiredGuid(json, "id", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("Missing required parameter");
    }

    [Fact]
    public void TryGetRequiredGuid_ShouldReturnFalse_WhenNotValidGuid()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"id":"not-a-guid"}""");
        var result = OperationParameterParser.TryGetRequiredGuid(json, "id", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("Invalid id");
    }

    [Fact]
    public void TryGetRequiredGuid_ShouldReturnTrue_ForValidGuid()
    {
        var guid = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($$"""{"id":"{{guid}}"}""");
        var result = OperationParameterParser.TryGetRequiredGuid(json, "id", out var value, out _);

        result.Should().BeTrue();
        value.Should().Be(guid);
    }

    #endregion

    #region TryGetRequiredInt32

    [Fact]
    public void TryGetRequiredInt32_ShouldReturnFalse_WhenPropertyMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        var result = OperationParameterParser.TryGetRequiredInt32(json, "pos", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("Missing required parameter");
    }

    [Fact]
    public void TryGetRequiredInt32_ShouldReturnFalse_WhenNotNumber()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"pos":"abc"}""");
        var result = OperationParameterParser.TryGetRequiredInt32(json, "pos", out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("must be an integer");
    }

    [Fact]
    public void TryGetRequiredInt32_ShouldReturnTrue_ForValidInt()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"pos":5}""");
        var result = OperationParameterParser.TryGetRequiredInt32(json, "pos", out var value, out _);

        result.Should().BeTrue();
        value.Should().Be(5);
    }

    #endregion

    #region TryGetGuidFromParameters

    [Fact]
    public void TryGetGuidFromParameters_ShouldReturnFalse_WhenMissing()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        OperationParameterParser.TryGetGuidFromParameters(json, "cardId", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetGuidFromParameters_ShouldReturnFalse_WhenNotString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""{"cardId":42}""");
        OperationParameterParser.TryGetGuidFromParameters(json, "cardId", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetGuidFromParameters_ShouldReturnTrue_ForValidGuid()
    {
        var guid = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($$"""{"cardId":"{{guid}}"}""");
        var result = OperationParameterParser.TryGetGuidFromParameters(json, "cardId", out var value);

        result.Should().BeTrue();
        value.Should().Be(guid);
    }

    #endregion
}
