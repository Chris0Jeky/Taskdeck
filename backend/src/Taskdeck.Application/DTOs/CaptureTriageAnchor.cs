namespace Taskdeck.Application.DTOs;

/// <summary>
/// The calendar day a triage run resolves partial dates against (#2193).
/// <para>
/// The anchor is the CAPTURE's own day, not the day triage happens to run. A queued capture can be
/// triaged minutes, hours, or (after retries or a stalled worker) days after it was recorded, and
/// anchoring to "today" made a transcript that says "Monday 1 September" resolve against the wrong
/// day — and, across a year boundary, the wrong year.
/// </para>
/// <para>
/// <see cref="CapturedAtServer"/> is the server-stamped instant of the capture row and is the only
/// authority for WHEN the capture happened; a client cannot move it.
/// <see cref="CaptureLocalOffset"/> is the UTC offset the client reported alongside the capture. It
/// carries no instant and no date of its own — it only says which calendar day the server instant
/// fell on where the speaker was, which is the day the speaker meant. A far-west capture just
/// before UTC midnight is on the previous local day, and resolving "1 September" against the UTC
/// day would push it a day (and at a year boundary a year) forward.
/// </para>
/// </summary>
public sealed record CaptureTriageAnchor
{
    /// <summary>Widest real UTC offset in use (UTC+14). Also the clamp for a reported offset.</summary>
    private static readonly TimeSpan MaxLocalOffset = TimeSpan.FromHours(14);

    private CaptureTriageAnchor(DateTimeOffset capturedAtServer, TimeSpan captureLocalOffset)
    {
        CapturedAtServer = capturedAtServer;
        CaptureLocalOffset = captureLocalOffset;
    }

    /// <summary>The server-stamped capture instant. Never client-supplied.</summary>
    public DateTimeOffset CapturedAtServer { get; }

    /// <summary>
    /// The capture's local UTC offset, or <see cref="TimeSpan.Zero"/> when the capture reported
    /// none. Only the OFFSET is taken from the client; the instant it is applied to is the server's.
    /// </summary>
    public TimeSpan CaptureLocalOffset { get; }

    /// <summary>
    /// The capture's own calendar day: the server instant read in the capture's local offset.
    /// ADR-0058 makes a due date a calendar day rather than an instant, so this is a day.
    /// </summary>
    public DateOnly ReferenceDate =>
        DateOnly.FromDateTime(CapturedAtServer.ToOffset(CaptureLocalOffset).DateTime);

    /// <summary>
    /// Builds the anchor for a capture from its server stamp and, optionally, the client-reported
    /// creation timestamp whose OFFSET (and nothing else) names the capture's local calendar day.
    /// An offset outside the real UTC range is ignored rather than trusted.
    /// </summary>
    public static CaptureTriageAnchor FromCapture(
        DateTimeOffset capturedAtServer,
        DateTimeOffset? clientCreatedAt = null)
    {
        var offset = clientCreatedAt?.Offset ?? TimeSpan.Zero;
        if (offset < -MaxLocalOffset || offset > MaxLocalOffset)
        {
            offset = TimeSpan.Zero;
        }

        return new CaptureTriageAnchor(capturedAtServer, offset);
    }

    /// <summary>
    /// Fallback anchor for a caller that holds no capture row of its own (in-process callers and
    /// tests). It treats NOW as the capture instant, which is only true when triage runs at capture
    /// time; every production caller passes the capture's stamped
    /// <c>CreatedAt</c> to <see cref="FromCapture"/> instead. Named and explicit on purpose: the
    /// defect this type exists to fix was an unnamed <c>DateTime.UtcNow</c> inside the prompt.
    /// </summary>
    public static CaptureTriageAnchor ForImmediateTriage(CapturePayloadV1? payload = null) =>
        FromCapture(DateTimeOffset.UtcNow, payload?.ClientCreatedAt);
}
