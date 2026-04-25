namespace Taskdeck.Domain.Entities;

/// <summary>
/// Typed compiler contract for validating and compiling proposal operations.
/// Implementations are responsible for checking whether operations are supported,
/// assessing risk, and producing a validation result.
/// </summary>
public interface IProposalCompiler
{
    /// <summary>
    /// Validates the given proposal operations and returns a structured result
    /// indicating whether they can be compiled, any risks, and any unsupported operations.
    /// </summary>
    /// <param name="operations">The operations to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A compiler validation result with risks and/or failures.</returns>
    Task<CompilerValidationResult> ValidateAsync(
        IReadOnlyList<AutomationProposalOperation> operations,
        CancellationToken cancellationToken = default);
}
