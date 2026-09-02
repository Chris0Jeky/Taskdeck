using Taskdeck.Acceleration.Candidates.Processing;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.Processing;

public sealed class WorkerSessionProofTests
{
    [Fact]
    public void Proof_is_bound_to_challenge_and_processor()
    {
        var secret = WorkerSessionProof.CreateSecret();
        var challenge = WorkerSessionProof.CreateChallenge();
        var proof = WorkerSessionProof.ComputeProof(secret, challenge, "v1-alpha", "pdfpig");

        Assert.True(WorkerSessionProof.VerifyProof(secret, challenge, "v1-alpha", "pdfpig", proof));
        Assert.False(WorkerSessionProof.VerifyProof(secret, WorkerSessionProof.CreateChallenge(), "v1-alpha", "pdfpig", proof));
        Assert.False(WorkerSessionProof.VerifyProof(secret, challenge, "v1-alpha", "mock", proof));
    }
}
