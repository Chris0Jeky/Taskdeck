from pathlib import Path

path = Path("backend/tests/Taskdeck.Application.Tests/Services/LlmCaptureTriageExtractorTests.cs")
text = path.read_text(encoding="utf-8")
replacements = {
    ".And.OnlyContain(span => span.HasValue && span.Value == (0, transcript.Length));":
        ".And.OnlyContain(span => span.HasValue && span.Value.Start == 0 && span.Value.End == transcript.Length);",
    ".And.OnlyContain(span => span is null);":
        ".And.OnlyContain(span => !span.HasValue);",
}
for old, new in replacements.items():
    if text.count(old) != 1:
        raise SystemExit(f"expected one match for {old!r}, found {text.count(old)}")
    text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
