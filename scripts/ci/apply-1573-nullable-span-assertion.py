from pathlib import Path

path = Path("backend/tests/Taskdeck.Application.Tests/Services/LlmCaptureTriageExtractorTests.cs")
text = path.read_text(encoding="utf-8")
old = ".And.OnlyContain(span => span == (0, transcript.Length));"
new = ".And.OnlyContain(span => span.HasValue && span.Value == (0, transcript.Length));"
if text.count(old) != 1:
    raise SystemExit(f"expected one nullable-span assertion, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
