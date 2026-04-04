using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CommandRunStateMachineTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private const string ValidTemplate = "deploy";
    private const string ValidCorrelation = "corr-001";

    private static CommandRun CreateQueuedRun() =>
        new(ValidTemplate, ValidUserId, ValidCorrelation);

    private static CommandRun CreateRunningRun()
    {
        var run = CreateQueuedRun();
        run.Start();
        return run;
    }

    #region Constructor validation

    [Fact]
    public void Constructor_ValidArgs_CreatesQueuedRun()
    {
        var run = CreateQueuedRun();

        run.TemplateName.Should().Be(ValidTemplate);
        run.RequestedByUserId.Should().Be(ValidUserId);
        run.CorrelationId.Should().Be(ValidCorrelation);
        run.Status.Should().Be(CommandRunStatus.Queued);
        run.Truncated.Should().BeFalse();
        run.StartedAt.Should().BeNull();
        run.CompletedAt.Should().BeNull();
        run.ExitCode.Should().BeNull();
        run.ErrorMessage.Should().BeNull();
        run.OutputPreview.Should().BeNull();
        run.Logs.Should().BeEmpty();
        run.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyTemplateName_Throws(string? templateName)
    {
        var act = () => new CommandRun(templateName!, ValidUserId, ValidCorrelation);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new CommandRun(ValidTemplate, Guid.Empty, ValidCorrelation);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyCorrelationId_Throws(string? correlationId)
    {
        var act = () => new CommandRun(ValidTemplate, ValidUserId, correlationId!);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region Valid transitions

    [Fact]
    public void Queued_Start_TransitionsToRunning()
    {
        var run = CreateQueuedRun();
        var beforeStart = run.UpdatedAt;

        run.Start();

        run.Status.Should().Be(CommandRunStatus.Running);
        run.StartedAt.Should().NotBeNull();
        run.UpdatedAt.Should().BeOnOrAfter(beforeStart);
    }

    [Fact]
    public void Queued_Fail_TransitionsToFailed()
    {
        var run = CreateQueuedRun();

        run.Fail("Queue error");

        run.Status.Should().Be(CommandRunStatus.Failed);
        run.ErrorMessage.Should().Be("Queue error");
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Queued_Cancel_TransitionsToCancelled()
    {
        var run = CreateQueuedRun();

        run.Cancel();

        run.Status.Should().Be(CommandRunStatus.Cancelled);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Running_CompleteZeroExit_TransitionsToCompleted()
    {
        var run = CreateRunningRun();

        run.Complete(0);

        run.Status.Should().Be(CommandRunStatus.Completed);
        run.ExitCode.Should().Be(0);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Running_CompleteNonZeroExit_TransitionsToCompleted()
    {
        var run = CreateRunningRun();

        run.Complete(1);

        run.Status.Should().Be(CommandRunStatus.Completed);
        run.ExitCode.Should().Be(1);
    }

    [Fact]
    public void Running_CompleteNegativeExit_TransitionsToCompleted()
    {
        var run = CreateRunningRun();

        run.Complete(-1);

        run.Status.Should().Be(CommandRunStatus.Completed);
        run.ExitCode.Should().Be(-1);
    }

    [Fact]
    public void Running_Fail_TransitionsToFailed()
    {
        var run = CreateRunningRun();

        run.Fail("Segmentation fault");

        run.Status.Should().Be(CommandRunStatus.Failed);
        run.ErrorMessage.Should().Be("Segmentation fault");
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Running_Timeout_TransitionsToTimedOut()
    {
        var run = CreateRunningRun();

        run.Timeout();

        run.Status.Should().Be(CommandRunStatus.TimedOut);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Running_Cancel_TransitionsToCancelled()
    {
        var run = CreateRunningRun();

        run.Cancel();

        run.Status.Should().Be(CommandRunStatus.Cancelled);
        run.CompletedAt.Should().NotBeNull();
    }

    #endregion

    #region Invalid transitions from terminal states

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    public void TerminalState_Start_Throws(string terminalState)
    {
        var run = CreateRunToState(terminalState);

        var act = () => run.Start();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    public void TerminalState_Complete_Throws(string terminalState)
    {
        var run = CreateRunToState(terminalState);

        var act = () => run.Complete(0);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    public void TerminalState_Fail_Throws(string terminalState)
    {
        var run = CreateRunToState(terminalState);

        var act = () => run.Fail("error");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    public void TerminalState_Timeout_Throws(string terminalState)
    {
        var run = CreateRunToState(terminalState);

        var act = () => run.Timeout();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("TimedOut")]
    [InlineData("Cancelled")]
    public void TerminalState_Cancel_Throws(string terminalState)
    {
        var run = CreateRunToState(terminalState);

        var act = () => run.Cancel();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    #endregion

    #region Invalid transitions from non-terminal states

    [Fact]
    public void Queued_Complete_Throws()
    {
        var run = CreateQueuedRun();

        var act = () => run.Complete(0);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Queued_Timeout_Throws()
    {
        var run = CreateQueuedRun();

        var act = () => run.Timeout();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Running_Start_Throws()
    {
        var run = CreateRunningRun();

        var act = () => run.Start();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    #endregion

    #region Fail validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Fail_EmptyErrorMessage_Throws(string? errorMessage)
    {
        var run = CreateRunningRun();

        var act = () => run.Fail(errorMessage!);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region SetOutputPreview

    [Fact]
    public void SetOutputPreview_NullValue_Succeeds()
    {
        var run = CreateQueuedRun();

        run.SetOutputPreview(null!);

        run.OutputPreview.Should().BeNull();
    }

    [Fact]
    public void SetOutputPreview_ValidString_Succeeds()
    {
        var run = CreateQueuedRun();

        run.SetOutputPreview("some output");

        run.OutputPreview.Should().Be("some output");
    }

    [Fact]
    public void SetOutputPreview_Exactly1000Chars_Succeeds()
    {
        var run = CreateQueuedRun();
        var preview = new string('x', 1000);

        run.SetOutputPreview(preview);

        run.OutputPreview.Should().HaveLength(1000);
    }

    [Fact]
    public void SetOutputPreview_Over1000Chars_Throws()
    {
        var run = CreateQueuedRun();
        var preview = new string('x', 1001);

        var act = () => run.SetOutputPreview(preview);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void SetOutputPreview_UpdatesTimestamp()
    {
        var run = CreateQueuedRun();
        var before = run.UpdatedAt;

        run.SetOutputPreview("out");

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region SetTruncated

    [Fact]
    public void SetTruncated_SetsTruncatedTrue()
    {
        var run = CreateQueuedRun();

        run.SetTruncated();

        run.Truncated.Should().BeTrue();
    }

    [Fact]
    public void SetTruncated_IsIdempotent()
    {
        var run = CreateQueuedRun();

        run.SetTruncated();
        run.SetTruncated();

        run.Truncated.Should().BeTrue();
    }

    [Fact]
    public void SetTruncated_UpdatesTimestamp()
    {
        var run = CreateQueuedRun();
        var before = run.UpdatedAt;

        run.SetTruncated();

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region AddLog

    [Fact]
    public void AddLog_AddsToLogCollection()
    {
        var run = CreateQueuedRun();
        var log = new CommandRunLog(run.Id, "Info", "test", "hello");

        run.AddLog(log);

        run.Logs.Should().HaveCount(1);
        run.Logs[0].Should().BeSameAs(log);
    }

    [Fact]
    public void AddLog_MultipleLogs_PreservesOrder()
    {
        var run = CreateQueuedRun();
        var log1 = new CommandRunLog(run.Id, "Info", "src1", "msg1");
        var log2 = new CommandRunLog(run.Id, "Error", "src2", "msg2");

        run.AddLog(log1);
        run.AddLog(log2);

        run.Logs.Should().HaveCount(2);
        run.Logs[0].Message.Should().Be("msg1");
        run.Logs[1].Message.Should().Be("msg2");
    }

    [Fact]
    public void AddLog_UpdatesTimestamp()
    {
        var run = CreateQueuedRun();
        var before = run.UpdatedAt;
        var log = new CommandRunLog(run.Id, "Info", "test", "hello");

        run.AddLog(log);

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region Touch verification on transitions

    [Fact]
    public void Start_UpdatesTimestamp()
    {
        var run = CreateQueuedRun();
        var before = run.UpdatedAt;

        run.Start();

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Complete_UpdatesTimestamp()
    {
        var run = CreateRunningRun();
        var before = run.UpdatedAt;

        run.Complete(0);

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Fail_UpdatesTimestamp()
    {
        var run = CreateRunningRun();
        var before = run.UpdatedAt;

        run.Fail("err");

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Timeout_UpdatesTimestamp()
    {
        var run = CreateRunningRun();
        var before = run.UpdatedAt;

        run.Timeout();

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Cancel_UpdatesTimestamp()
    {
        var run = CreateQueuedRun();
        var before = run.UpdatedAt;

        run.Cancel();

        run.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region Helpers

    private static CommandRun CreateRunToState(string state)
    {
        var run = CreateQueuedRun();
        switch (state)
        {
            case "Running":
                run.Start();
                break;
            case "Completed":
                run.Start();
                run.Complete(0);
                break;
            case "Failed":
                run.Start();
                run.Fail("failed");
                break;
            case "TimedOut":
                run.Start();
                run.Timeout();
                break;
            case "Cancelled":
                run.Cancel();
                break;
            default:
                throw new ArgumentException($"Unknown state: {state}");
        }
        return run;
    }

    #endregion
}
