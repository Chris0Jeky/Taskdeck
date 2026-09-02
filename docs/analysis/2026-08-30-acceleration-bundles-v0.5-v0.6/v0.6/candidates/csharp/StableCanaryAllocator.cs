
using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.Acceleration.V06;

public enum CanaryCohort
{
    Shadow = 0,
    Canary = 1,
    Holdout = 2
}

public static class StableCanaryAllocator
{
    public static CanaryCohort Allocate(
        Guid subjectId,
        string policyVersion,
        string saltVersion,
        int canaryBasisPoints,
        int holdoutBasisPoints)
    {
        if (canaryBasisPoints < 0 || holdoutBasisPoints < 0 ||
            canaryBasisPoints + holdoutBasisPoints > 10_000)
            throw new ArgumentOutOfRangeException();

        var input = $"{saltVersion}:{policyVersion}:{subjectId:D}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bucket = ((hash[0] << 8) | hash[1]) % 10_000;

        if (bucket < canaryBasisPoints) return CanaryCohort.Canary;
        if (bucket < canaryBasisPoints + holdoutBasisPoints) return CanaryCohort.Holdout;
        return CanaryCohort.Shadow;
    }
}
