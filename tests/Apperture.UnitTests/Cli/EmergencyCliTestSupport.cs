using System.Text;
using Apperture.Cli.Host;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Moq;

namespace Apperture.UnitTests.Cli;

/// <summary>
/// A console a test scripts. Everything written and every read is also appended to one ordered event log, so a case can state that
/// something was shown before or after a service call returned.
/// </summary>
internal sealed class ScriptedConsole : IEmergencyConsole
{
    private readonly Queue<object?> _inputs;

    /// <summary>Each item is a line to type, null for end of input, or an exception to throw from the read (a cancellation).</summary>
    public ScriptedConsole(List<string> events, bool inputRedirected, params object?[] inputs)
    {
        Events = events;
        IsInputRedirected = inputRedirected;
        _inputs = new Queue<object?>(inputs);
        Out = new RecordingWriter("out", events, OutText);
        Error = new RecordingWriter("err", events, ErrorText);
    }

    public List<string> Events { get; }

    public StringBuilder OutText { get; } = new();

    public StringBuilder ErrorText { get; } = new();

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    public bool IsInputRedirected { get; }

    public int Reads { get; private set; }

    public Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        Events.Add("read");
        var next = _inputs.Count > 0 ? _inputs.Dequeue() : null;
        return next switch
        {
            Exception exception => throw exception,
            string line => Task.FromResult<string?>(line),
            _ => Task.FromResult<string?>(null),
        };
    }

    public string AllOutput => OutText.ToString() + ErrorText;

    private sealed class RecordingWriter(string channel, List<string> events, StringBuilder text) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => text.Append(value);

        public override void Write(string? value) => text.Append(value);

        public override void WriteLine(string? value)
        {
            text.AppendLine(value);
            events.Add($"{channel}:{value}");
        }
    }
}

/// <summary>Strict doubles of the services the emergency commands use: a call nobody set up fails the case, so a command cannot reach a service it should not.</summary>
internal sealed class CliWorld
{
    public const string UserId = "usr-recovery";
    public static readonly UserId Id = new(UserId);

    public List<string> Events { get; } = [];

    public Mock<INativeMfaService> Mfa { get; } = new(MockBehavior.Strict);

    public Mock<IEmergencyAccessService> Emergency { get; } = new(MockBehavior.Strict);

    public Mock<IUserRepository> Users { get; } = new(MockBehavior.Strict);

    public CliWorld()
    {
        Users.Setup(u => u.GetByIdAsync(Id, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<User?>(
                new User(Id, "Recovery Access", [new IdentityLink(IdentitySource.Local, "breakglass", null)], [], false) { Username = "breakglass" }));
    }

    public ScriptedConsole Console(bool redirected, params object?[] inputs) => new(Events, redirected, inputs);

    public EmergencyCommandRunner Runner(ScriptedConsole console) => new(Mfa.Object, Emergency.Object, Users.Object, console);

    public static string[] OnAccount(params string[] args) => [.. args, "--user-id", UserId];

    public Task<int> RunAsync(ScriptedConsole console, string[] args, CancellationToken cancellationToken = default)
    {
        EmergencyCommandLine.TryParse(args, out var command, out var error).Should().BeTrue(error);
        return Runner(console).RunAsync(command!, cancellationToken);
    }

    // ---- Typical results ----

    public static NativeMfaReplacementChallenge Challenge { get; } = new(
        Id, "cand-1", 1, "otpauth://totp/Apperture:breakglass?secret=STAGEDSECRETKEY", "STAGEDSECRETKEY", new DateTimeOffset(2026, 11, 9, 12, 10, 0, TimeSpan.Zero));

    public static IReadOnlyList<string> NewCodes { get; } = ["AAAA-1111", "BBBB-2222", "CCCC-3333"];

    public void StageReturns() =>
        Mfa.Setup(m => m.BeginReplacementAsync(Id, "breakglass", It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Challenge));

    public void VerifyAccepts(string code, int version = 2) =>
        Mfa.Setup(m => m.VerifyReplacementAsync(Id, Challenge.CandidateId, Challenge.Version, code, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new NativeMfaReplacementVerification(Challenge.CandidateId, version)));

    public void VerifyRejects(string code) =>
        Mfa.Setup(m => m.VerifyReplacementAsync(Id, Challenge.CandidateId, Challenge.Version, code, It.IsAny<CancellationToken>()))
            .Returns(() => Faulted<NativeMfaReplacementVerification>(new NativeMfaCodeNotValidException()));

    public void VerifyThrows(Exception exception) =>
        Mfa.Setup(m => m.VerifyReplacementAsync(Id, Challenge.CandidateId, Challenge.Version, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Faulted<NativeMfaReplacementVerification>(exception));

    public void SwitchReturns(NativeMfaReplacementResult result, int version = 2) =>
        Mfa.Setup(m => m.CompleteReplacementAsync(Id, Challenge.CandidateId, version, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Events.Add("service:switch-returned");
                return ValueTask.FromResult(result);
            });

    public void SwitchThrows(Exception exception, int version = 2) =>
        Mfa.Setup(m => m.CompleteReplacementAsync(Id, Challenge.CandidateId, version, AuditActor.EmergencyCli, It.IsAny<CancellationToken>()))
            .Returns(() => Faulted<NativeMfaReplacementResult>(exception));

    public void AbandonReturns(bool removed = true) =>
        Mfa.Setup(m => m.AbandonReplacementAsync(Id, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Events.Add("service:abandoned");
                return ValueTask.FromResult(removed);
            });

    public void Enrolled(bool enrolled = true) =>
        Mfa.Setup(m => m.IsEnrolledAsync(Id, It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(enrolled));

    public static ValueTask<T> Faulted<T>(Exception exception) => new(Task.FromException<T>(exception));
}
