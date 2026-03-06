using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public sealed class StarterPackCatalogService : IStarterPackCatalogService
{
    private readonly IStarterPackManifestValidator _manifestValidator;

    public StarterPackCatalogService(IStarterPackManifestValidator manifestValidator)
    {
        _manifestValidator = manifestValidator;
    }

    public IReadOnlyList<StarterPackCatalogEntryDto> GetCatalog()
    {
        List<StarterPackCatalogEntryDto> catalog =
        [
            BuildCommonLabelsPack(),
            BuildCommonColumnFlowPack(),
            BuildEngineeringSprintBlueprint(),
            BuildSupportTriageBlueprint(),
            BuildContentCalendarBlueprint()
        ];

        ValidateCatalog(catalog);
        return catalog;
    }

    private void ValidateCatalog(IReadOnlyList<StarterPackCatalogEntryDto> catalog)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog)
        {
            if (!ids.Add(entry.Id))
            {
                errors.Add($"Duplicate first-party pack id '{entry.Id}'.");
            }

            if (!string.Equals(entry.Id, entry.Manifest.PackId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Catalog id '{entry.Id}' does not match manifest packId '{entry.Manifest.PackId}'.");
            }

            if (!string.Equals(entry.Manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            {
                errors.Add($"Pack '{entry.Id}' must use schemaVersion 1.0.");
            }

            var validation = _manifestValidator.Validate(entry.Manifest);
            if (!validation.IsValid)
            {
                foreach (var validationError in validation.Errors)
                {
                    errors.Add($"Pack '{entry.Id}' invalid at {validationError.Path}: {validationError.Message}");
                }
            }
        }

        var hasLabelPack = catalog.Any(entry =>
            string.Equals(entry.Category, StarterPackCatalogCategories.LabelPack, StringComparison.Ordinal));
        if (!hasLabelPack)
        {
            errors.Add("First-party catalog must include at least one label-pack entry.");
        }

        var hasColumnFlow = catalog.Any(entry =>
            string.Equals(entry.Category, StarterPackCatalogCategories.ColumnFlow, StringComparison.Ordinal));
        if (!hasColumnFlow)
        {
            errors.Add("First-party catalog must include at least one column-flow entry.");
        }

        var boardBlueprintCount = catalog.Count(entry =>
            string.Equals(entry.Category, StarterPackCatalogCategories.BoardBlueprint, StringComparison.Ordinal));
        if (boardBlueprintCount != 3)
        {
            errors.Add($"First-party catalog must include exactly 3 board-blueprint entries. Found {boardBlueprintCount}.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static StarterPackCatalogEntryDto BuildCommonLabelsPack()
    {
        return new StarterPackCatalogEntryDto(
            Id: "common-labels-core",
            Category: StarterPackCatalogCategories.LabelPack,
            Title: "Common Labels Core",
            Summary: "Reusable label taxonomy for priority, risk, and review state.",
            Highlights:
            [
                "Priority and blocked labels",
                "Review-state labels",
                "Compatible with existing boards"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "common-labels-core",
                DisplayName = "Common Labels Core",
                Description = "Reusable label taxonomy for common delivery workflows.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "labels", "core"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "priority-high", Color = "#E85D5D", Description = "High urgency" },
                    new StarterPackLabelDto { Name = "priority-medium", Color = "#F59E0B", Description = "Medium urgency" },
                    new StarterPackLabelDto { Name = "blocked", Color = "#6B7280", Description = "Blocked by dependency" },
                    new StarterPackLabelDto { Name = "needs-review", Color = "#2563EB", Description = "Awaiting review" }
                ],
                Columns = [],
                Templates = [],
                SeedCards = []
            });
    }

    private static StarterPackCatalogEntryDto BuildCommonColumnFlowPack()
    {
        return new StarterPackCatalogEntryDto(
            Id: "common-column-flow-kanban",
            Category: StarterPackCatalogCategories.ColumnFlow,
            Title: "Common Column Flow - Kanban",
            Summary: "Standard delivery lane flow with explicit review and completion states.",
            Highlights:
            [
                "Backlog to Done lane progression",
                "WIP limits for active lanes",
                "No labels or seed cards required"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "common-column-flow-kanban",
                DisplayName = "Common Column Flow - Kanban",
                Description = "Reusable kanban column flow for most product delivery teams.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "columns"]
                },
                Tags = ["starter", "columns", "kanban"],
                Labels = [],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Backlog", Position = 0 },
                    new StarterPackColumnDto { Name = "Ready", Position = 1 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 2, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Review", Position = 3, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Done", Position = 4 }
                ],
                Templates = [],
                SeedCards = []
            });
    }

    private static StarterPackCatalogEntryDto BuildEngineeringSprintBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-engineering-sprint",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Engineering Sprint",
            Summary: "Sprint-ready engineering board with triage labels and bug template.",
            Highlights:
            [
                "Sprint lane defaults",
                "Engineering-focused labels",
                "Bug-report template and kickoff card"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-engineering-sprint",
                DisplayName = "Board Blueprint - Engineering Sprint",
                Description = "Starter blueprint for engineering sprint execution.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "engineering"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "priority-high", Color = "#E85D5D", Description = "High urgency" },
                    new StarterPackLabelDto { Name = "bug", Color = "#DC2626", Description = "Defect issue" },
                    new StarterPackLabelDto { Name = "tech-debt", Color = "#7C3AED", Description = "Technical debt task" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Backlog", Position = 0 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 1, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Review", Position = 2, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Done", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "bug-report",
                        Title = "Bug Report",
                        Description = "Template for reproducible defect triage.",
                        Checklist = ["Reproduction steps", "Expected behavior", "Actual behavior"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Plan sprint goals",
                        Description = "Define sprint scope and ownership.",
                        ColumnName = "Backlog",
                        Labels = ["priority-high"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildSupportTriageBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-support-triage",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Support Triage",
            Summary: "Support-centric queue with urgency and SLA-aware handling labels.",
            Highlights:
            [
                "Intake and triage lane model",
                "Customer support labels",
                "Response template for ticket handling"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-support-triage",
                DisplayName = "Board Blueprint - Support Triage",
                Description = "Starter blueprint for support intake and SLA triage.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "support"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "customer-impact", Color = "#2563EB", Description = "Customer-facing issue" },
                    new StarterPackLabelDto { Name = "sla-risk", Color = "#B91C1C", Description = "At risk of SLA breach" },
                    new StarterPackLabelDto { Name = "waiting-on-customer", Color = "#0D9488", Description = "Pending customer reply" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Inbox", Position = 0 },
                    new StarterPackColumnDto { Name = "Triage", Position = 1, WipLimit = 6 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 2, WipLimit = 5 },
                    new StarterPackColumnDto { Name = "Resolved", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "support-response",
                        Title = "Support Response",
                        Description = "Template for support issue handling.",
                        Checklist = ["Initial response", "Root cause", "Resolution summary"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Define triage rotation",
                        Description = "Assign weekly support triage ownership.",
                        ColumnName = "Inbox",
                        Labels = ["customer-impact"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildContentCalendarBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-content-calendar",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Content Calendar",
            Summary: "Editorial planning board with review workflow and publishing cadence.",
            Highlights:
            [
                "Editorial lane structure",
                "Content workflow labels",
                "Brief template and planning seed card"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-content-calendar",
                DisplayName = "Board Blueprint - Content Calendar",
                Description = "Starter blueprint for content planning and publishing workflows.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "content"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "needs-draft", Color = "#7C3AED", Description = "Drafting required" },
                    new StarterPackLabelDto { Name = "needs-review", Color = "#D97706", Description = "Editorial review required" },
                    new StarterPackLabelDto { Name = "publish-week", Color = "#059669", Description = "Target publish week" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Ideas", Position = 0 },
                    new StarterPackColumnDto { Name = "Drafting", Position = 1, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Review", Position = 2, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Scheduled", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "content-brief",
                        Title = "Content Brief",
                        Description = "Template for article and campaign planning.",
                        Checklist = ["Audience", "Core message", "Call to action"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Plan weekly editorial slate",
                        Description = "Choose top topics and assign owners.",
                        ColumnName = "Ideas",
                        TemplateId = "content-brief",
                        Labels = ["publish-week"]
                    }
                ]
            });
    }
}
