using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Fuzz-style tests for LlmIntentClassifier.
/// Verifies that the classifier never throws unhandled exceptions regardless of input,
/// and that its regex patterns handle adversarial/pathological strings safely.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class LlmIntentClassifierFuzzTests
{
    private const int MaxTests = 300;

    [Property(MaxTest = MaxTests)]
    public Property Classify_NeverThrows_OnArbitraryString()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<string>(),
            input =>
            {
                var act = () => LlmIntentClassifier.Classify(input);
                act.Should().NotThrow("Classify must handle all string inputs gracefully");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_AlwaysReturnsTuple()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<string>(),
            input =>
            {
                var (isActionable, actionIntent) = LlmIntentClassifier.Classify(input);
                if (!isActionable)
                {
                    actionIntent.Should().BeNull("non-actionable results should have null intent");
                }
                else
                {
                    actionIntent.Should().NotBeNullOrEmpty("actionable results must have an intent");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_EmptyAndWhitespace_AlwaysNonActionable()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n", "\r\n", "   ", null!)),
            input =>
            {
                var (isActionable, _) = LlmIntentClassifier.Classify(input);
                isActionable.Should().BeFalse("empty/whitespace input should not be actionable");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_NeverThrows_OnLongInput()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1000, 10000).Select(len => new string('a', len))),
            longInput =>
            {
                var act = () => LlmIntentClassifier.Classify(longInput);
                act.Should().NotThrow("long inputs must not cause timeout or crash");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_NeverThrows_OnRepeatedPatternInput()
    {
        // Strings that could trigger catastrophic backtracking in naive regex
        return Prop.ForAll(
            Arb.From(Gen.OneOf(
                Gen.Choose(50, 500).Select(len => new string('a', len) + " card"),
                Gen.Choose(50, 500).Select(len => string.Concat(Enumerable.Repeat("word ", len)) + "card"),
                Gen.Choose(50, 200).Select(len => string.Concat(Enumerable.Repeat("create ", len))),
                Gen.Choose(50, 200).Select(len => string.Concat(Enumerable.Repeat("don't ", len)) + "create a card")
            )),
            pathological =>
            {
                var act = () => LlmIntentClassifier.Classify(pathological);
                act.Should().NotThrow("pathological patterns must not cause regex backtracking issues");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_ActionableIntent_AlwaysHasKnownPrefix()
    {
        // Known intents: card.create, card.move, card.archive, card.update,
        // board.create, board.update, column.reorder
        var knownPrefixes = new[] { "card.", "board.", "column." };

        return Prop.ForAll(
            ActionableInputArb(),
            input =>
            {
                var (isActionable, actionIntent) = LlmIntentClassifier.Classify(input);
                if (isActionable && actionIntent != null)
                {
                    actionIntent.Should().Match(
                        intent => knownPrefixes.Any(prefix => intent.StartsWith(prefix)),
                        "actionable intents must start with a known entity prefix");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_NegatedInput_NeverActionable()
    {
        // Tests negation patterns that the classifier currently handles.
        // Known gap: verb forms like "adding" (gerund) are not matched by the
        // negation regex which requires bare infinitives (add, create, move, etc.).
        // "avoid adding new tasks" is classified as actionable because "adding"
        // doesn't match \b(create|add|...)\b in the negation pattern, but "new tasks"
        // matches the NewCardPattern. Filed as a finding — see PR description.
        return Prop.ForAll(
            NegatedInputArb(),
            input =>
            {
                var (isActionable, _) = LlmIntentClassifier.Classify(input);
                isActionable.Should().BeFalse(
                    $"negated input '{input}' should not be actionable");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_NeverThrows_OnUnicodeInput()
    {
        return Prop.ForAll(
            UnicodeInputArb(),
            input =>
            {
                var act = () => LlmIntentClassifier.Classify(input);
                act.Should().NotThrow("unicode input must not cause exceptions");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Classify_NeverThrows_OnSpecialCharacters()
    {
        return Prop.ForAll(
            Arb.From(Gen.OneOf(
                Gen.Constant("create a card with <script>alert('xss')</script>"),
                Gen.Constant("create a card with'; DROP TABLE cards; --"),
                Gen.Constant("create a card\0with null bytes"),
                Gen.Constant("create\r\na\r\ncard"),
                Gen.Constant("create a card\twith\ttabs"),
                Gen.Constant("CREATE A CARD IN ALL CAPS"),
                Gen.Constant("CrEaTe A cArD iN mIxEd CaSe")
            )),
            input =>
            {
                var act = () => LlmIntentClassifier.Classify(input);
                act.Should().NotThrow("special characters must not cause exceptions");
            });
    }

    /// <summary>
    /// Generates inputs known to be actionable.
    /// </summary>
    private static Arbitrary<string> ActionableInputArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant("create a new card called test"),
            Gen.Constant("add a task for the meeting"),
            Gen.Constant("move card to done column"),
            Gen.Constant("archive the old task"),
            Gen.Constant("delete card number 5"),
            Gen.Constant("update card title to new name"),
            Gen.Constant("rename task to better name"),
            Gen.Constant("create a new board for the project"),
            Gen.Constant("rename board to Sprint 42"),
            Gen.Constant("reorder columns on the board")
        ));
    }

    /// <summary>
    /// Generates negated inputs that should be classified as non-actionable.
    /// Uses bare infinitive verbs that match the negation regex pattern.
    /// Note: gerund forms (e.g., "avoid adding") are a known gap — the negation
    /// pattern only matches bare infinitives (add, create, move, etc.).
    /// </summary>
    private static Arbitrary<string> NegatedInputArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant("don't create a card yet"),
            Gen.Constant("do not add a task"),
            Gen.Constant("never move the card"),
            Gen.Constant("stop create new cards"),
            Gen.Constant("cancel the delete of the card"),
            Gen.Constant("don't add new tasks"),
            Gen.Constant("do not create a board"),
            Gen.Constant("never add another task")
        ));
    }

    /// <summary>
    /// Generates unicode strings for robustness testing.
    /// </summary>
    private static Arbitrary<string> UnicodeInputArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant("create a card"),
            Gen.Constant("\u00e9\u00e8\u00ea\u00eb create a card"),
            Gen.Constant("create \u4e00\u4e8c\u4e09 card"),
            Gen.Constant("\u0410\u0411\u0412 create card"),
            Gen.Constant("create a card \ud83d\ude00\ud83d\ude01\ud83d\ude02"),
            Gen.Constant("\u200b\u200c\u200d create a card"),
            Gen.Constant("create\u00a0a\u00a0card") // non-breaking spaces
        ));
    }
}
