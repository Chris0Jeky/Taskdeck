using System.Globalization;
using System.Text;
using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class CsvExternalImportAdapter : IExternalImportAdapter
{
    private const int MaxCardTitleLength = 200;
    private const int MaxCardDescriptionLength = 2000;
    private static readonly string[] SupportedLastTouchDateFormats =
    [
        "O",
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mmK",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.fffK"
    ];

    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] DisplayNameAliases = ["display_name", "display name", "name"];
    private static readonly string[] FirstNameAliases = ["first_name", "first name"];
    private static readonly string[] LastNameAliases = ["last_name", "last name"];
    private static readonly string[] CompanyAliases = ["company"];
    private static readonly string[] RoleAliases = ["role", "position"];
    private static readonly string[] EmailAliases = ["email", "email_address", "email address"];
    private static readonly string[] LinkedInAliases = ["linkedin_url", "linkedin url", "profile_url", "profile url"];
    private static readonly string[] LastTouchAliases = ["last_touch_at", "last touch at", "connected_on", "connected on"];

    public string Provider => ExternalImportProviders.Csv;

    public Result<ExternalImportParseResult> Parse(ExternalImportRequestDto request)
    {
        if (!string.Equals(request.Provider, Provider, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ExternalImportParseResult>(
                ErrorCodes.ValidationError,
                $"CSV adapter cannot parse provider '{request.Provider}'.");
        }

        if (string.IsNullOrWhiteSpace(request.Payload))
        {
            return Result.Failure<ExternalImportParseResult>(
                ErrorCodes.ValidationError,
                "CSV import payload cannot be empty.");
        }

        var profile = string.IsNullOrWhiteSpace(request.Profile)
            ? ExternalImportProfiles.OutreachContactsV1
            : request.Profile.Trim();
        if (!string.Equals(profile, ExternalImportProfiles.OutreachContactsV1, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ExternalImportParseResult>(
                ErrorCodes.ValidationError,
                $"Unsupported import profile '{profile}'.");
        }

        var parseRowsResult = ParseRows(request.Payload);
        if (!parseRowsResult.IsSuccess)
        {
            return Result.Failure<ExternalImportParseResult>(parseRowsResult.ErrorCode, parseRowsResult.ErrorMessage);
        }

        var parsedRows = parseRowsResult.Value;
        if (parsedRows.Count == 0)
        {
            return Result.Failure<ExternalImportParseResult>(
                ErrorCodes.ValidationError,
                "CSV payload must contain at least one non-empty header row.");
        }

        var header = parsedRows[0];
        if (header.Values.Count == 0)
        {
            return Result.Failure<ExternalImportParseResult>(
                ErrorCodes.ValidationError,
                "CSV header row cannot be empty.");
        }

        var indexByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicateHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < header.Values.Count; index++)
        {
            var headerName = NormalizeHeaderName(header.Values[index]);
            if (string.IsNullOrWhiteSpace(headerName))
            {
                continue;
            }

            if (!indexByHeader.TryAdd(headerName, index))
            {
                duplicateHeaders.Add(headerName);
            }
        }

        if (duplicateHeaders.Count > 0)
        {
            var duplicateHeaderList = string.Join(
                ", ",
                duplicateHeaders
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(value => $"'{value}'"));

            return Result.Failure<ExternalImportParseResult>(
                ErrorCodes.ValidationError,
                $"CSV header row contains duplicate column names after normalization: {duplicateHeaderList}.");
        }

        var explicitColumnValidation = ValidateExplicitColumnMappings(indexByHeader, request.Csv);
        if (!explicitColumnValidation.IsSuccess)
        {
            return Result.Failure<ExternalImportParseResult>(
                explicitColumnValidation.ErrorCode,
                explicitColumnValidation.ErrorMessage);
        }

        var displayNameIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.DisplayNameColumn,
            DisplayNameAliases);
        var firstNameIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.FirstNameColumn,
            FirstNameAliases);
        var lastNameIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.LastNameColumn,
            LastNameAliases);
        var companyIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.CompanyColumn,
            CompanyAliases);
        var roleIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.RoleColumn,
            RoleAliases);
        var emailIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.EmailColumn,
            EmailAliases);
        var linkedInIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.LinkedInUrlColumn,
            LinkedInAliases);
        var lastTouchIndex = ResolveHeaderIndex(
            indexByHeader,
            request.Csv?.LastTouchAtColumn,
            LastTouchAliases);

        var conflicts = new List<ExternalImportConflictDto>();
        var candidates = new List<ExternalImportCandidate>();
        var seenDedupeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in parsedRows.Skip(1))
        {
            var displayName = GetCell(row.Values, displayNameIndex);
            var firstName = GetCell(row.Values, firstNameIndex);
            var lastName = GetCell(row.Values, lastNameIndex);
            var company = GetCell(row.Values, companyIndex);
            var role = GetCell(row.Values, roleIndex);
            var email = GetCell(row.Values, emailIndex);
            var linkedInUrl = GetCell(row.Values, linkedInIndex);
            var lastTouchRaw = GetCell(row.Values, lastTouchIndex);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = string.Join(" ", new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            }

            var dedupeKey = BuildDedupeKey(linkedInUrl, email, displayName, company);
            if (dedupeKey is null)
            {
                conflicts.Add(new ExternalImportConflictDto(
                    "MissingDedupeKey",
                    $"$.rows[{row.RowNumber}]",
                    "Record is missing required dedupe fields. Expected linkedin_url, email, or display_name+company.",
                    IncomingValue: BuildDedupeSourceSummary(displayName, company, email, linkedInUrl)));
                continue;
            }

            if (!seenDedupeKeys.Add(dedupeKey))
            {
                conflicts.Add(new ExternalImportConflictDto(
                    "DuplicateInputRecord",
                    $"$.rows[{row.RowNumber}]",
                    $"Input contains duplicate record for dedupe key '{dedupeKey}'.",
                    IncomingValue: dedupeKey));
                continue;
            }

            DateTimeOffset? lastTouchAt = null;
            if (!string.IsNullOrWhiteSpace(lastTouchRaw))
            {
                if (TryParseLastTouchAt(lastTouchRaw, out var parsedDate))
                {
                    lastTouchAt = parsedDate;
                }
                else
                {
                    conflicts.Add(new ExternalImportConflictDto(
                        "InvalidDate",
                        $"$.rows[{row.RowNumber}].last_touch_at",
                        $"Could not parse last_touch_at value '{lastTouchRaw}'.",
                        IncomingValue: lastTouchRaw));
                    continue;
                }
            }

            var title = BuildTitle(displayName, email, linkedInUrl, row.RowNumber);
            var description = BuildDescription(profile, dedupeKey, displayName, company, role, email, linkedInUrl, lastTouchAt);

            if (title.Length > MaxCardTitleLength)
            {
                conflicts.Add(new ExternalImportConflictDto(
                    "TitleTooLong",
                    $"$.rows[{row.RowNumber}].title",
                    $"Card title exceeds max length of {MaxCardTitleLength} characters.",
                    IncomingValue: $"length={title.Length}"));
                continue;
            }

            if (description.Length > MaxCardDescriptionLength)
            {
                conflicts.Add(new ExternalImportConflictDto(
                    "DescriptionTooLong",
                    $"$.rows[{row.RowNumber}].description",
                    $"Card description exceeds max length of {MaxCardDescriptionLength} characters.",
                    IncomingValue: $"length={description.Length}"));
                continue;
            }

            candidates.Add(new ExternalImportCandidate(
                row.RowNumber,
                dedupeKey,
                title,
                description));
        }

        return Result.Success(new ExternalImportParseResult(
            Provider,
            profile,
            parsedRows.Count - 1,
            candidates.Count,
            candidates,
            conflicts));
    }

    private static int? ResolveHeaderIndex(
        Dictionary<string, int> indexByHeader,
        string? requestedHeader,
        IReadOnlyList<string> aliases)
    {
        if (!string.IsNullOrWhiteSpace(requestedHeader))
        {
            var trimmed = requestedHeader.Trim();
            return indexByHeader.TryGetValue(trimmed, out var explicitIndex)
                ? explicitIndex
                : null;
        }

        foreach (var alias in aliases)
        {
            if (indexByHeader.TryGetValue(alias, out var index))
            {
                return index;
            }
        }

        return null;
    }

    private static string NormalizeHeaderName(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        return header.Trim().TrimStart('\uFEFF');
    }

    private static bool TryParseLastTouchAt(string rawValue, out DateTimeOffset parsedDate)
    {
        return DateTimeOffset.TryParseExact(
            rawValue.Trim(),
            SupportedLastTouchDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out parsedDate);
    }

    private static Result ValidateExplicitColumnMappings(
        Dictionary<string, int> indexByHeader,
        ExternalImportCsvOptionsDto? csvOptions)
    {
        var explicitColumns = new (string OptionName, string? Header)[]
        {
            ("displayNameColumn", csvOptions?.DisplayNameColumn),
            ("firstNameColumn", csvOptions?.FirstNameColumn),
            ("lastNameColumn", csvOptions?.LastNameColumn),
            ("companyColumn", csvOptions?.CompanyColumn),
            ("roleColumn", csvOptions?.RoleColumn),
            ("emailColumn", csvOptions?.EmailColumn),
            ("linkedInUrlColumn", csvOptions?.LinkedInUrlColumn),
            ("lastTouchAtColumn", csvOptions?.LastTouchAtColumn)
        };

        foreach (var (optionName, header) in explicitColumns)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var trimmed = header.Trim();
            if (!indexByHeader.ContainsKey(trimmed))
            {
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"CSV column mapping '{optionName}' references header '{trimmed}' but it was not found in the payload header row.");
            }
        }

        return Result.Success();
    }

    private static string BuildDedupeSourceSummary(
        string displayName,
        string company,
        string email,
        string linkedInUrl)
    {
        return $"display_name='{displayName}', company='{company}', email='{email}', linkedin_url='{linkedInUrl}'";
    }

    private static string GetCell(IReadOnlyList<string> rowValues, int? index)
    {
        if (!index.HasValue || index.Value < 0 || index.Value >= rowValues.Count)
        {
            return string.Empty;
        }

        return rowValues[index.Value].Trim();
    }

    private static string BuildTitle(string? displayName, string? email, string? linkedInUrl, int rowNumber)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(linkedInUrl))
        {
            return linkedInUrl.Trim();
        }

        return $"Imported Contact {rowNumber}";
    }

    private static string BuildDescription(
        string profile,
        string dedupeKey,
        string displayName,
        string company,
        string role,
        string email,
        string linkedInUrl,
        DateTimeOffset? lastTouchAt)
    {
        var metadata = new CsvImportCardMetadata(
            ExternalImportProviders.Csv,
            profile,
            dedupeKey,
            displayName,
            company,
            role,
            email,
            linkedInUrl,
            lastTouchAt?.UtcDateTime);
        var metadataJson = JsonSerializer.Serialize(metadata, MetadataSerializerOptions);

        var lines = new List<string>
        {
            $"{ExternalImportMetadata.CardDescriptionPrefix}{metadataJson}",
            string.Empty,
            $"Display Name: {displayName}",
            $"Company: {company}",
            $"Role: {role}",
            $"Email: {email}",
            $"LinkedIn: {linkedInUrl}",
            $"Last Touch At: {(lastTouchAt.HasValue ? lastTouchAt.Value.ToString("O") : string.Empty)}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string? BuildDedupeKey(
        string linkedInUrl,
        string email,
        string displayName,
        string company)
    {
        var normalizedLinkedIn = NormalizeLinkedInUrl(linkedInUrl);
        if (!string.IsNullOrWhiteSpace(normalizedLinkedIn))
        {
            return $"linkedin:{normalizedLinkedIn}";
        }

        var normalizedEmail = NormalizeEmail(email);
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return $"email:{normalizedEmail}";
        }

        var normalizedDisplayName = Normalize(displayName);
        var normalizedCompany = Normalize(company);
        if (!string.IsNullOrWhiteSpace(normalizedDisplayName) && !string.IsNullOrWhiteSpace(normalizedCompany))
        {
            return $"name-company:{normalizedDisplayName}|{normalizedCompany}";
        }

        return null;
    }

    private static string NormalizeLinkedInUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (TryNormalizeLinkedInUri(trimmed, out var normalizedLinkedInUri))
        {
            return normalizedLinkedInUri;
        }

        var withInferredScheme = trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : $"https://{trimmed.TrimStart('/')}";
        if (TryNormalizeLinkedInUri(withInferredScheme, out normalizedLinkedInUri))
        {
            return normalizedLinkedInUri;
        }

        return Normalize(trimmed);
    }

    private static bool TryNormalizeLinkedInUri(string value, out string normalizedLinkedInUri)
    {
        normalizedLinkedInUri = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!IsLinkedInHost(uri.Host))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        };
        normalizedLinkedInUri = builder.Uri.ToString().TrimEnd('/').ToLowerInvariant();
        return true;
    }

    private static bool IsLinkedInHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost = host.Trim().ToLowerInvariant();
        return normalizedHost == "linkedin.com" ||
               normalizedHost.EndsWith(".linkedin.com", StringComparison.Ordinal);
    }

    private static string NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (!char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static Result<List<CsvParsedRow>> ParseRows(string payload)
    {
        var rows = new List<CsvParsedRow>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;

        for (var index = 0; index < payload.Length; index++)
        {
            var ch = payload[index];

            if (ch == '"')
            {
                if (inQuotes && index + 1 < payload.Length && payload[index + 1] == '"')
                {
                    currentField.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
                continue;
            }

            if ((ch == '\r' || ch == '\n') && !inQuotes)
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();

                if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(new CsvParsedRow(rowNumber, currentRow.ToList()));
                }

                currentRow.Clear();
                rowNumber++;

                if (ch == '\r' && index + 1 < payload.Length && payload[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            currentField.Append(ch);
        }

        if (inQuotes)
        {
            return Result.Failure<List<CsvParsedRow>>(
                ErrorCodes.ValidationError,
                "CSV payload contains an unclosed quoted field.");
        }

        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString());
            if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                rows.Add(new CsvParsedRow(rowNumber, currentRow.ToList()));
            }
        }

        return Result.Success(rows);
    }

    private sealed record CsvParsedRow(int RowNumber, List<string> Values);

    private sealed record CsvImportCardMetadata(
        string Provider,
        string Profile,
        string DedupeKey,
        string DisplayName,
        string Company,
        string Role,
        string Email,
        string LinkedInUrl,
        DateTime? LastTouchAtUtc);
}
