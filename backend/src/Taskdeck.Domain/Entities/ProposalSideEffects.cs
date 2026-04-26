namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object aggregating the full side-effect analysis for a proposal:
/// the 7-row breakdown and the reversibility posture.
/// </summary>
public sealed class ProposalSideEffects : IEquatable<ProposalSideEffects>
{
    /// <summary>The 7-category side-effect breakdown.</summary>
    public IReadOnlyList<SideEffectRow> Rows { get; }

    /// <summary>The reversibility posture for this proposal.</summary>
    public Reversibility Reversibility { get; }

    public ProposalSideEffects(IReadOnlyList<SideEffectRow> rows, Reversibility reversibility)
    {
        if (rows is null || rows.Count == 0)
            throw new ArgumentException("Side-effect rows cannot be null or empty.", nameof(rows));
        Reversibility = reversibility ?? throw new ArgumentNullException(nameof(reversibility));

        Rows = rows;
    }

    public bool Equals(ProposalSideEffects? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Rows.Count != other.Rows.Count) return false;
        for (int i = 0; i < Rows.Count; i++)
        {
            if (!Rows[i].Equals(other.Rows[i])) return false;
        }
        return Reversibility.Equals(other.Reversibility);
    }

    public override bool Equals(object? obj) => Equals(obj as ProposalSideEffects);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var row in Rows)
            hash.Add(row);
        hash.Add(Reversibility);
        return hash.ToHashCode();
    }
}
