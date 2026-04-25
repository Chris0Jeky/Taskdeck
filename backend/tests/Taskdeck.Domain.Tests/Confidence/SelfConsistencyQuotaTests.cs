using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class SelfConsistencyQuotaTests
{
    [Fact]
    public void Constructor_ShouldCreateQuota_WithValidInputs()
    {
        var quota = new SelfConsistencyQuota(10);

        quota.MaxCalls.Should().Be(10);
        quota.UsedCalls.Should().Be(0);
        quota.RemainingCalls.Should().Be(10);
        quota.HasBudget.Should().BeTrue();
        quota.CostCap.Should().BeNull();
        quota.CostUsed.Should().Be(0.0);
    }

    [Fact]
    public void Constructor_ShouldCreateQuota_WithCostCap()
    {
        var quota = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.3);

        quota.MaxCalls.Should().Be(5);
        quota.UsedCalls.Should().Be(2);
        quota.RemainingCalls.Should().Be(3);
        quota.CostCap.Should().Be(1.0);
        quota.CostUsed.Should().Be(0.3);
        quota.HasBudget.Should().BeTrue();
    }

    [Fact]
    public void Constructor_ShouldAcceptZeroMaxCalls()
    {
        var quota = new SelfConsistencyQuota(0);

        quota.MaxCalls.Should().Be(0);
        quota.RemainingCalls.Should().Be(0);
        quota.HasBudget.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMaxCallsNegative()
    {
        var act = () => new SelfConsistencyQuota(-1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUsedCallsNegative()
    {
        var act = () => new SelfConsistencyQuota(10, -1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUsedCallsExceedsMax()
    {
        var act = () => new SelfConsistencyQuota(5, 6);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostCapNegative()
    {
        var act = () => new SelfConsistencyQuota(10, costCap: -0.1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostUsedNegative()
    {
        var act = () => new SelfConsistencyQuota(10, costUsed: -0.1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostUsedExceedsCostCap()
    {
        var act = () => new SelfConsistencyQuota(10, costCap: 1.0, costUsed: 1.5);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostCapIsNaN()
    {
        var act = () => new SelfConsistencyQuota(10, costCap: double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostCapIsInfinity()
    {
        var act = () => new SelfConsistencyQuota(10, costCap: double.PositiveInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostUsedIsNaN()
    {
        var act = () => new SelfConsistencyQuota(10, costUsed: double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCostUsedIsInfinity()
    {
        var act = () => new SelfConsistencyQuota(10, costUsed: double.PositiveInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Consume_ShouldReturnNewQuotaWithUpdatedCounts()
    {
        var quota = new SelfConsistencyQuota(5, 0, costCap: 2.0, costUsed: 0.0);

        var updated = quota.Consume(0.3);

        updated.UsedCalls.Should().Be(1);
        updated.CostUsed.Should().BeApproximately(0.3, 1e-12);
        updated.RemainingCalls.Should().Be(4);

        // Original should be unchanged (immutable)
        quota.UsedCalls.Should().Be(0);
        quota.CostUsed.Should().Be(0.0);
    }

    [Fact]
    public void Consume_ShouldWorkWithZeroCost()
    {
        var quota = new SelfConsistencyQuota(3);

        var updated = quota.Consume(0.0);

        updated.UsedCalls.Should().Be(1);
        updated.CostUsed.Should().Be(0.0);
    }

    [Fact]
    public void Consume_ShouldThrow_WhenBudgetExhausted()
    {
        var quota = new SelfConsistencyQuota(2, 2);

        var act = () => quota.Consume();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.LlmQuotaExceeded);
    }

    [Fact]
    public void Consume_ShouldThrow_WhenCostCapWouldBeExceeded()
    {
        var quota = new SelfConsistencyQuota(10, 0, costCap: 1.0, costUsed: 0.9);

        var act = () => quota.Consume(0.2);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.LlmQuotaExceeded);
    }

    [Fact]
    public void Consume_ShouldThrow_WhenCallCostNegative()
    {
        var quota = new SelfConsistencyQuota(10);

        var act = () => quota.Consume(-0.1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Consume_ShouldThrow_WhenCallCostIsNaN()
    {
        var quota = new SelfConsistencyQuota(10);

        var act = () => quota.Consume(double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Consume_ShouldThrow_WhenCallCostIsInfinity()
    {
        var quota = new SelfConsistencyQuota(10);

        var act = () => quota.Consume(double.PositiveInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Consume_ShouldAllowExactCostCapMatch()
    {
        var quota = new SelfConsistencyQuota(10, 0, costCap: 1.0, costUsed: 0.8);

        var updated = quota.Consume(0.2);

        updated.CostUsed.Should().BeApproximately(1.0, 1e-12);
        updated.HasBudget.Should().BeFalse();
    }

    [Fact]
    public void Consume_ShouldTolerateFloatingPointDrift_NearCostCap()
    {
        // Simulate floating-point drift: 0.1 + 0.2 = 0.30000000000000004 in IEEE 754,
        // and three such sums can accumulate drift slightly above the cap.
        // The tolerance in Consume should prevent a spurious rejection.
        var quota = new SelfConsistencyQuota(10, 0, costCap: 0.3);

        // 0.1 + 0.2 = 0.30000000000000004 due to IEEE 754 representation
        var q1 = quota.Consume(0.1);
        var q2 = q1.Consume(0.2);

        // Should succeed despite newCostUsed being slightly > 0.3
        q2.CostUsed.Should().BeApproximately(0.3, 1e-12);
    }

    [Fact]
    public void Consume_ShouldStillReject_WhenMeaningfullyOverCostCap()
    {
        var quota = new SelfConsistencyQuota(10, 0, costCap: 1.0, costUsed: 0.9);

        // 0.9 + 0.2 = 1.1, which is meaningfully over the cap
        var act = () => quota.Consume(0.2);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.LlmQuotaExceeded);
    }

    [Fact]
    public void Consume_ChainedCalls_ShouldTrackBudget()
    {
        var quota = new SelfConsistencyQuota(3, 0, costCap: 1.0);

        var q1 = quota.Consume(0.3);
        var q2 = q1.Consume(0.3);
        var q3 = q2.Consume(0.3);

        q3.UsedCalls.Should().Be(3);
        q3.RemainingCalls.Should().Be(0);
        q3.CostUsed.Should().BeApproximately(0.9, 1e-12);
        q3.HasBudget.Should().BeFalse(); // No remaining calls
    }

    [Fact]
    public void HasBudget_ShouldBeFalse_WhenCallsExhausted()
    {
        var quota = new SelfConsistencyQuota(1, 1);

        quota.HasBudget.Should().BeFalse();
    }

    [Fact]
    public void HasBudget_ShouldBeFalse_WhenCostCapReached()
    {
        var quota = new SelfConsistencyQuota(10, 0, costCap: 1.0, costUsed: 1.0);

        quota.HasBudget.Should().BeFalse();
    }

    [Fact]
    public void HasBudget_ShouldBeTrue_WhenNoCostCap()
    {
        var quota = new SelfConsistencyQuota(10, 5);

        quota.HasBudget.Should().BeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForEquivalentQuotas()
    {
        var a = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5);
        var b = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5);

        a.Should().Be(b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenCostUsedIsNearButDistinct()
    {
        var a = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5 - 1e-13);
        var b = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5 + 1e-13);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equals_ShouldBeTransitive_ForNearAdjacentCostUsedValues()
    {
        var a = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5);
        var b = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5 + 7.5e-13);
        var c = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5 + 1.5e-12);

        a.Should().NotBe(b);
        b.Should().NotBe(c);
        a.Should().NotBe(c);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentQuotas()
    {
        var a = new SelfConsistencyQuota(5, 2);
        var b = new SelfConsistencyQuota(5, 3);

        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_ShouldContainBudgetInfo()
    {
        var quota = new SelfConsistencyQuota(5, 2, costCap: 1.0, costUsed: 0.5);

        var str = quota.ToString();
        str.Should().Contain("2/5");
        str.Should().Contain("0.5000");
    }
}
