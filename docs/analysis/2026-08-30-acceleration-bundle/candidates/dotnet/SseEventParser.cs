using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Taskdeck.Acceleration.Candidates.Streaming;

/// <summary>
/// One parsed Server-Sent Event. Data lines are joined with '\n'.
/// </summary>
public sealed record SseEvent(
    string EventName,
    string Data,
    string? Id,
    int? RetryMilliseconds);

/// <summary>
/// Incremental SSE parser that tolerates arbitrary CR/LF and buffer boundaries.
/// It deliberately does not log or retain completed event payloads.
/// </summary>
public sealed class SseEventParser
{
    private readonly int _maxLineChars;
    private readonly int _maxEventChars;
    private readonly StringBuilder _line = new();
    private readonly List<string> _dataLines = new();

    private string? _eventName;
    private string? _lastEventId;
    private int? _retryMilliseconds;
    private int _eventChars;
    private bool _previousWasCarriageReturn;

    public SseEventParser(int maxLineChars = 64 * 1024, int maxEventChars = 1024 * 1024)
    {
        if (maxLineChars <= 0) throw new ArgumentOutOfRangeException(nameof(maxLineChars));
        if (maxEventChars < maxLineChars) throw new ArgumentOutOfRangeException(nameof(maxEventChars));

        _maxLineChars = maxLineChars;
        _maxEventChars = maxEventChars;
    }

    public IReadOnlyList<SseEvent> Feed(string chunk, bool endOfStream = false)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return Feed(chunk.AsSpan(), endOfStream);
    }

    public IReadOnlyList<SseEvent> Feed(ReadOnlySpan<char> chunk, bool endOfStream = false)
    {
        var output = new List<SseEvent>();

        foreach (var current in chunk)
        {
            if (_previousWasCarriageReturn)
            {
                _previousWasCarriageReturn = false;
                if (current == '\n')
                {
                    continue; // CRLF is one line ending.
                }
            }

            if (current == '\r')
            {
                CompleteLine(output);
                _previousWasCarriageReturn = true;
                continue;
            }

            if (current == '\n')
            {
                CompleteLine(output);
                continue;
            }

            _line.Append(current);
            if (_line.Length > _maxLineChars)
            {
                throw new SseProtocolException("sse_line_too_large");
            }
        }

        if (endOfStream)
        {
            _previousWasCarriageReturn = false;
            if (_line.Length > 0)
            {
                CompleteLine(output);
            }

            Dispatch(output);
        }

        return output;
    }

    private void CompleteLine(List<SseEvent> output)
    {
        var line = _line.ToString();
        _line.Clear();

        if (line.Length == 0)
        {
            Dispatch(output);
            return;
        }

        _eventChars = checked(_eventChars + line.Length);
        if (_eventChars > _maxEventChars)
        {
            throw new SseProtocolException("sse_event_too_large");
        }

        if (line[0] == ':')
        {
            return; // Comment/keepalive.
        }

        var separator = line.IndexOf(':');
        string field;
        string value;

        if (separator < 0)
        {
            field = line;
            value = string.Empty;
        }
        else
        {
            field = line[..separator];
            value = line[(separator + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }
        }

        switch (field)
        {
            case "data":
                _dataLines.Add(value);
                break;
            case "event":
                _eventName = value;
                break;
            case "id" when value.IndexOf('\0') < 0:
                _lastEventId = value;
                break;
            case "retry" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var retry)
                                   && retry >= 0:
                _retryMilliseconds = retry;
                break;
            default:
                // Unknown fields are ignored by the SSE protocol.
                break;
        }
    }

    private void Dispatch(List<SseEvent> output)
    {
        // The SSE algorithm aborts dispatch when there is no data buffer.
        if (_dataLines.Count > 0)
        {
            output.Add(new SseEvent(
                string.IsNullOrEmpty(_eventName) ? "message" : _eventName,
                string.Join("\n", _dataLines),
                _lastEventId,
                _retryMilliseconds));
        }

        _dataLines.Clear();
        _eventName = null;
        _eventChars = 0;
    }
}

public sealed class SseProtocolException : Exception
{
    public SseProtocolException(string code) : base(code) => Code = code;

    public string Code { get; }
}
