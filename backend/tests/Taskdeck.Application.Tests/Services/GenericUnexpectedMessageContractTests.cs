using System.Reflection;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// #2351 / R6: the two generic unexpected-failure strings are deliberately different
/// texts for two different readers, and each has exactly one definition. These tests pin
/// both facts so a future edit cannot silently converge them or fork a third copy.
/// </summary>
public class GenericUnexpectedMessageContractTests
{
    [Fact]
    public void TheTwoGenericMessages_AreDistinctAndNonEmpty()
    {
        SensitiveDataRedactor.GenericUnexpectedErrorMessage.Should().NotBeNullOrWhiteSpace();
        SensitiveDataRedactor.GenericUnexpectedFailureMessage.Should().NotBeNullOrWhiteSpace();

        SensitiveDataRedactor.GenericUnexpectedErrorMessage
            .Should().NotBe(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
    }

    /// <summary>
    /// The wire-facing string stays free of the correlation-ID instruction that belongs
    /// to the operator-facing string; that difference is the reason the two exist.
    /// </summary>
    [Fact]
    public void TheWireFacingMessage_DoesNotCarryTheOperatorInstruction()
    {
        SensitiveDataRedactor.GenericUnexpectedErrorMessage
            .Should().NotContain("correlation ID");
        SensitiveDataRedactor.GenericUnexpectedFailureMessage
            .Should().Contain("correlation ID");
    }

    [Theory]
    [InlineData(typeof(AutomationExecutorService))]
    [InlineData(typeof(BatchProposalExecutionService))]
    public void ApplicationServices_UseTheSharedGenericErrorDefinition(System.Type serviceType)
    {
        var field = serviceType.GetField(
            "GenericUnexpectedErrorMessage",
            BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull(
            $"{serviceType.Name} still declares the generic unexpected-error constant");
        field!.GetRawConstantValue()
            .Should().Be(SensitiveDataRedactor.GenericUnexpectedErrorMessage);
    }
}
