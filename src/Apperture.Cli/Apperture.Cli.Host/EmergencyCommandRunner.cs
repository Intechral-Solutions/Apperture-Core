using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Apperture.ControlPlane.Security;

namespace Apperture.Cli.Host;

/// <summary>
/// Runs the emergency commands over the application services. Every change goes through <see cref="IEmergencyAccessService"/> or
/// <see cref="INativeMfaService"/>: the audited unit, the administrator-set gate and the final viability decision belong to them, and this
/// class holds no repository other than the read-only user lookup. What a service throws is reported by <see cref="EmergencyOutcomeMapper"/>.
/// </summary>
public sealed class EmergencyCommandRunner(
    INativeMfaService mfa,
    IEmergencyAccessService emergency,
    IUserRepository users,
    IEmergencyConsole console)
{
    /// <summary>How many times an operator at a prompt may mistype a code before the staged replacement is discarded.</summary>
    public const int MaxCodeAttempts = 3;

    private static readonly ActorId CliActor = new(EmergencyAccessService.CliActorId);

    public async Task<int> RunAsync(EmergencyCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return command.Name switch
            {
                "status" => await StatusAsync(),
                "provision" => await ProvisionAsync(cancellationToken),
                "designate" => await DesignateAsync(command),
                "set-password" => await SetPasswordAsync(command, cancellationToken),
                "enroll-mfa" => await EnrollMfaAsync(command, cancellationToken),
                "reset-mfa" => command.BareReset
                    ? await BareResetAsync(command)
                    : await ReplaceAuthenticatorAsync(command, cancellationToken),
                "regenerate-codes" => await RegenerateCodesAsync(command),
                "revoke" => await RevokeAsync(command),
                _ => Usage($"Unknown emergency command: {command.Name}"),
            };
        }
        catch (OperationCanceledException)
        {
            // Reached only while waiting for operator input: nothing that changes state is ever started with the token.
            console.Error.WriteLine("Cancelled. Nothing was changed.");
            return EmergencyExitCodes.Refused;
        }
        catch (Exception ex)
        {
            return Report(EmergencyOutcomeMapper.Map(ex));
        }
    }

    // ---------------------------------------------------------------------------------------------- commands

    private async Task<int> StatusAsync()
    {
        try
        {
            await emergency.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli);
        }
        catch (EmergencyAccessException ex) when (ex.Code == EmergencyAccessErrorCodes.UsernameConflict)
        {
            console.Error.WriteLine(ex.Message);
        }

        var posture = await emergency.GetPostureAsync();
        var o = console.Out;
        o.WriteLine($"Healthy: {posture.IsHealthy}");
        o.WriteLine($"Fallback safeguard required: {posture.FallbackSafeguardRequired}");
        o.WriteLine($"Account: {posture.DesignatedDisplayName ?? "(none)"}");
        o.WriteLine($"Username: {posture.DesignatedUsername ?? "(none)"}");
        o.WriteLine($"Canonical identity: {posture.CanonicalIdentityApplied}");
        if (posture.CanonicalUsernameConflict)
            o.WriteLine("Username conflict: another user already owns the reserved username. Recovery Access was not renamed.");
        if (posture.HasEntraIdentityLink)
            o.WriteLine("Warning: Recovery Access has a Microsoft identity linked. Recovery still depends only on local password + native MFA.");
        o.WriteLine($"Designated user id: {posture.DesignatedUserId ?? "(none)"}");
        o.WriteLine($"Platform Administrator: {posture.DesignatedIsPlatformAdministrator}");
        o.WriteLine($"Local credential enabled: {posture.LocalCredentialEnabled}");
        o.WriteLine($"Authenticator: {(posture.NativeMfaEnrolled ? "Enrolled" : "Not enrolled")}");
        o.WriteLine($"Recovery codes: {posture.UnusedRecoveryCodeCount} available");
        // Ruling C-14 (A10.2b.12): the last code can be used at a sign-in that is never refused, which leaves Recovery Access unhealthy.
        // The status says so and names the remedy, which needs no healthy posture.
        if (!string.IsNullOrEmpty(posture.DesignatedUserId) && posture.NativeMfaEnrolled && posture.UnusedRecoveryCodeCount == 0)
            o.WriteLine(RecoveryCodesExhaustedMessage);
        return EmergencyExitCodes.Success;
    }

    /// <summary>What the status says when the designated account's authenticator is enrolled and every recovery code has been used.</summary>
    private const string RecoveryCodesExhaustedMessage =
        "Recovery codes are exhausted: every code has been used, so Recovery Access is not healthy. Run 'emergency regenerate-codes' to get a new set.";

    private async Task<int> ProvisionAsync(CancellationToken cancellationToken)
    {
        var posture = await emergency.GetPostureAsync();
        if (!string.IsNullOrEmpty(posture.DesignatedUserId))
        {
            await emergency.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli);
            console.Out.WriteLine($"Emergency Access already provisioned userId={posture.DesignatedUserId}");
            console.Out.WriteLine($"Account: {EmergencyAccessIdentity.DisplayName}  Username: {EmergencyAccessIdentity.LocalUsername}");
            return EmergencyExitCodes.Success;
        }

        var password = await ReadAcceptedPasswordAsync("Enter emergency local password:", cancellationToken);
        if (password is null)
            return EmergencyExitCodes.InvalidUsage;

        var userId = await emergency.ProvisionAsync(password, CliActor);
        console.Out.WriteLine($"Provisioned Recovery Access userId={userId.Value}");
        console.Out.WriteLine($"Username: {EmergencyAccessIdentity.LocalUsername}");
        console.Out.WriteLine("Next: run 'emergency enroll-mfa' and store the recovery codes.");
        return EmergencyExitCodes.Success;
    }

    private async Task<int> DesignateAsync(EmergencyCommand command)
    {
        var user = await ResolveUserAsync(command);
        if (user is null)
            return EmergencyExitCodes.InvalidUsage;
        await emergency.DesignateAsync(user.Id, CliActor);
        console.Out.WriteLine($"Designated emergency administrator userId={user.Id.Value}");
        return EmergencyExitCodes.Success;
    }

    private async Task<int> SetPasswordAsync(EmergencyCommand command, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(command);
        if (user is null)
            return EmergencyExitCodes.InvalidUsage;
        var password = await ReadAcceptedPasswordAsync("Enter new local password:", cancellationToken);
        if (password is null)
            return EmergencyExitCodes.InvalidUsage;

        await emergency.SetLocalPasswordAsync(user.Id, password, CliActor);
        console.Out.WriteLine("Local password updated.");
        return EmergencyExitCodes.Success;
    }

    /// <summary>The first enrolment of an authenticator. An enrolled account is changed with <c>reset-mfa</c>, never re-enrolled over.</summary>
    private async Task<int> EnrollMfaAsync(EmergencyCommand command, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(command);
        if (user is null)
            return EmergencyExitCodes.InvalidUsage;

        if (await mfa.IsEnrolledAsync(user.Id))
        {
            return Refuse(
                "An authenticator is already enrolled for this account.",
                "Run 'emergency reset-mfa' to replace it without a gap.");
        }

        try
        {
            var challenge = await mfa.BeginEnrollmentAsync(user.Id, AccountNameOf(user));
            console.Out.WriteLine("Add this account in an authenticator app:");
            console.Out.WriteLine(challenge.OtpAuthUri);
            console.Out.WriteLine($"Manual key: {challenge.ManualEntryKey}");

            var totp = await ReadSecretAsync("Enter the 6-digit authenticator code:", cancellationToken);
            if (totp is null)
                return EmergencyExitCodes.InvalidUsage;

            // The codes are shown only from the committed result: nothing is printed before the enrolment returns.
            var codes = await mfa.CompleteEnrollmentAsync(user.Id, totp, AuditActor.EmergencyCli);
            return ShowCommittedRecoveryCodes(codes, "The authenticator is enrolled.");
        }
        catch (InvalidOperationException ex) when (ex.GetType() == typeof(InvalidOperationException))
        {
            // The service states "already enrolled" and "no pending enrolment" as plain exceptions. When the account became enrolled while
            // this command ran (another operator finished first) that is a refusal, found out by reading the state and never the message.
            if (!await mfa.IsEnrolledAsync(user.Id))
                throw;
            return Refuse(
                "An authenticator is already enrolled for this account.",
                "Run 'emergency reset-mfa' to replace it without a gap.");
        }
    }

    /// <summary>
    /// The normal change of an authenticator: stage a new one, show its secret once, read a code from it, prove possession, then switch. The
    /// current authenticator and its codes stay valid and nothing is held while the operator types; only the switch, in one audited unit,
    /// changes them. A failure of any step leaves the current authenticator in place and never turns into a reset or an enrolment.
    /// </summary>
    private async Task<int> ReplaceAuthenticatorAsync(EmergencyCommand command, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(command);
        if (user is null)
            return EmergencyExitCodes.InvalidUsage;

        var challenge = await mfa.BeginReplacementAsync(user.Id, AccountNameOf(user));
        console.Out.WriteLine("A replacement authenticator was staged. The current authenticator and its recovery codes keep working until the replacement is verified.");
        console.Out.WriteLine("Add this account in an authenticator app:");
        console.Out.WriteLine(challenge.OtpAuthUri);
        console.Out.WriteLine($"Manual key: {challenge.ManualEntryKey}");
        console.Out.WriteLine($"The replacement expires at {challenge.ExpiresAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.");

        NativeMfaReplacementVerification verified;
        try
        {
            verified = await VerifyStagedAsync(user.Id, challenge, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await DiscardStagedAsync(user.Id);
            console.Error.WriteLine("Cancelled. The current authenticator is unchanged.");
            return EmergencyExitCodes.Refused;
        }
        catch (StagedReplacementAbandonedException abandoned)
        {
            await DiscardStagedAsync(user.Id);
            console.Error.WriteLine(abandoned.Message);
            console.Error.WriteLine("The current authenticator is unchanged. Run 'emergency reset-mfa' to start again.");
            return EmergencyExitCodes.InvalidUsage;
        }

        // From here no operator input is read: the switch is one audited unit, judged on the Recovery Access posture alone.
        var result = await mfa.CompleteReplacementAsync(user.Id, challenge.CandidateId, verified.Version, AuditActor.EmergencyCli);

        if (result.AlreadyApplied)
        {
            console.Out.WriteLine("This replacement had already been applied by an earlier request. The new authenticator is active.");
            console.Out.WriteLine("Its recovery codes were shown by that request and cannot be shown again. If they were lost, run 'emergency regenerate-codes'.");
            return EmergencyExitCodes.Success;
        }

        return ShowCommittedRecoveryCodes(result.RecoveryCodes, "The authenticator was replaced. The previous authenticator and recovery codes no longer work.");
    }

    /// <summary>
    /// Reads and checks a code from the staged authenticator. A mistyped code is asked for again at a prompt (never when input is piped) and
    /// leaves everything unchanged; running out of attempts or of input discards the staged replacement.
    /// </summary>
    private async Task<NativeMfaReplacementVerification> VerifyStagedAsync(
        UserId userId,
        NativeMfaReplacementChallenge challenge,
        CancellationToken cancellationToken)
    {
        var attempts = console.IsInputRedirected ? 1 : MaxCodeAttempts;
        for (var attempt = 1; ; attempt++)
        {
            var code = await ReadSecretAsync("Enter the 6-digit code from the new authenticator:", cancellationToken);
            if (code is null)
                throw new StagedReplacementAbandonedException("No code was entered, so the replacement was not verified.");

            try
            {
                return await mfa.VerifyReplacementAsync(userId, challenge.CandidateId, challenge.Version, code);
            }
            catch (NativeMfaCodeNotValidException)
            {
                console.Error.WriteLine("That code is not valid.");
                if (attempt >= attempts)
                    throw new StagedReplacementAbandonedException("No valid code was entered, so the replacement was not verified.");
            }
        }
    }

    private async Task DiscardStagedAsync(UserId userId)
    {
        try
        {
            await mfa.AbandonReplacementAsync(userId);
            console.Out.WriteLine("The staged replacement was discarded.");
        }
        catch (Exception)
        {
            // Best effort: the candidate expires by itself, and never disables the active authenticator.
            console.Error.WriteLine("The staged replacement could not be discarded now; it expires by itself and never affects the current authenticator.");
        }
    }

    /// <summary>
    /// The destructive reset, only ever asked for with <c>--bare-reset</c>: the authenticator and every recovery code are removed with nothing
    /// in their place. The service decides whether that may happen (a reducer under the administrator-set gate); a refusal is reported as it
    /// is and is never answered by a replacement or an enrolment.
    /// </summary>
    private async Task<int> BareResetAsync(EmergencyCommand command)
    {
        var user = await ResolveUserAsync(command);
        if (user is null)
            return EmergencyExitCodes.InvalidUsage;

        await mfa.ResetAsync(user.Id, AuditActor.EmergencyCli);
        console.Out.WriteLine("Bare reset complete: the account has no authenticator and no recovery codes.");
        console.Out.WriteLine("Next: run 'emergency enroll-mfa' to enrol a new authenticator.");
        return EmergencyExitCodes.Success;
    }

    private async Task<int> RegenerateCodesAsync(EmergencyCommand command)
    {
        var user = await ResolveUserAsync(command);
        if (user is null)
            return EmergencyExitCodes.InvalidUsage;

        if (!await mfa.IsEnrolledAsync(user.Id))
        {
            return Refuse(
                "No authenticator is enrolled for this account, so there are no recovery codes to regenerate.",
                "Run 'emergency enroll-mfa' first.");
        }

        var codes = await mfa.RegenerateRecoveryCodesAsync(user.Id, AuditActor.EmergencyCli);
        return ShowCommittedRecoveryCodes(codes, "Recovery codes regenerated. The previous codes no longer work.");
    }

    private async Task<int> RevokeAsync(EmergencyCommand command)
    {
        await emergency.RevokeAsync(command.ConfirmFallback, CliActor);
        console.Out.WriteLine("Emergency designation cleared. Last-administrator fallback protection is required until a new emergency account is provisioned.");
        return EmergencyExitCodes.Success;
    }

    // ---------------------------------------------------------------------------------------------- shared

    /// <summary>
    /// Shows recovery codes that a service returned from a committed change. Never called before the change returns, so an operation that
    /// failed, was refused or whose outcome is unknown shows none. An empty or blank list is not shown as if it were a set of active codes.
    /// </summary>
    private int ShowCommittedRecoveryCodes(IReadOnlyList<string> codes, string confirmation)
    {
        if (codes.Count == 0 || codes.Any(string.IsNullOrWhiteSpace))
        {
            console.Out.WriteLine(confirmation);
            return Report(new EmergencyOutcome(
                EmergencyExitCodes.OutcomeUnknown,
                "The change was applied, but no usable recovery codes were returned, so none are shown.",
                "Run 'emergency regenerate-codes' to get a new set."));
        }

        console.Out.WriteLine(confirmation);
        console.Out.WriteLine("Store these recovery codes now. They will not be shown again.");
        foreach (var code in codes)
            console.Out.WriteLine(code);
        return EmergencyExitCodes.Success;
    }

    private int Report(EmergencyOutcome outcome)
    {
        var tag = outcome.Code is null ? string.Empty : $" [{outcome.Code}]";
        console.Error.WriteLine($"Error{tag}: {outcome.Message}");
        if (!string.IsNullOrEmpty(outcome.NextStep))
            console.Error.WriteLine($"Next: {outcome.NextStep}");
        return outcome.ExitCode;
    }

    private int Refuse(string message, string nextStep) =>
        Report(new EmergencyOutcome(EmergencyExitCodes.Refused, message, nextStep));

    private int Usage(string message)
    {
        console.Error.WriteLine(message);
        return EmergencyExitCodes.InvalidUsage;
    }

    private static string AccountNameOf(User user) =>
        user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Local)?.ExternalId ?? user.DisplayName;

    /// <summary>The account the command names, or the designated Recovery Access account, or the reserved Recovery Access username.</summary>
    private async Task<User?> ResolveUserAsync(EmergencyCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.UserId))
        {
            var byId = await users.GetByIdAsync(new UserId(command.UserId));
            if (byId is null)
                console.Error.WriteLine("User not found.");
            return byId;
        }

        var username = command.Username;
        if (string.IsNullOrWhiteSpace(username))
        {
            var designated = await users.FindDesignatedEmergencyUserAsync();
            if (designated is not null)
                return designated;
            username = EmergencyAccessIdentity.LocalUsername;
        }

        var byName = await users.GetByIdentityLinkAsync(IdentitySource.Local, LocalAuthHelpers.NormalizeUsername(username));
        if (byName is null)
            console.Error.WriteLine("User not found.");
        return byName;
    }

    private async Task<string?> ReadAcceptedPasswordAsync(string prompt, CancellationToken cancellationToken)
    {
        while (true)
        {
            var password = await ReadSecretAsync(prompt, cancellationToken);
            if (password is null)
                return null;
            var confirm = await ReadSecretAsync("Confirm password:", cancellationToken);
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                console.Error.WriteLine("Passwords do not match.");
                if (console.IsInputRedirected)
                    return null;
                continue;
            }

            try
            {
                LocalPasswordPolicy.RequireRecoveryAccess(
                    password,
                    LocalPasswordPolicy.ForLocalIdentity(EmergencyAccessIdentity.LocalUsername, EmergencyAccessIdentity.DisplayName));
                return password;
            }
            catch (PasswordPolicyException ex)
            {
                console.Error.WriteLine(ex.Message);
                foreach (var suggestion in ex.Evaluation.Suggestions)
                    console.Error.WriteLine(suggestion);
                if (console.IsInputRedirected)
                    return null;
            }
        }
    }

    private async Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken)
    {
        var value = await console.ReadSecretAsync(prompt, cancellationToken);
        if (string.IsNullOrEmpty(value))
        {
            console.Error.WriteLine("No input.");
            return null;
        }

        return value;
    }

    /// <summary>The operator gave no valid code in the attempts allowed: the staged replacement is to be discarded.</summary>
    private sealed class StagedReplacementAbandonedException(string message) : Exception(message);
}
