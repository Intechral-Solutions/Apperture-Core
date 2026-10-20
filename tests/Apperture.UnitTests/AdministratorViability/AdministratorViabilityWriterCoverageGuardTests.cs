using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The writer guard of administrator viability (IAM-10 gate A10.2b.14, with the approved departures E-1 and E-2 recorded in the
/// WP0 reconciliation). Every production place that can change a viability input is classified: a <b>reducer</b> or a <b>gated
/// raiser</b> (it takes the global gate; <i>pending</i> until the package that integrates it), an approved <b>exception</b>, the
/// non-refusable <b>recovery-code consumption</b> (C-14), or <b>out of category</b> (it cannot change a viability input, and a test
/// pins why). A writer in none of them fails here, so a new writer cannot appear unclassified. A reducer or raiser is either
/// <b>integrated</b> (WP2: the person-driven administrator, credential and link writers, checked method by method for the gate before
/// the authority row locks and the writes, and for the check after them) or explicitly <b>pending</b> (every other package), and a
/// pending file must not already reference the gate or the coordinator, so that the pending list cannot go stale unnoticed.
/// </summary>
public sealed class AdministratorViabilityWriterCoverageGuardTests
{
    private enum Writes
    {
        RoleAssign,
        RoleRemove,
        UserCreatedWithRoleIds,
        CredentialDisable,
        LinkAdd,
        LinkRemove,
        LinkProof,
        RegistrationCreate,
        RegistrationUpdate,
        RegistrationDelete,
        RegistrationActivate,
        PolicySave,
        DesignationFlagUpdate,
        MfaReset,
        MfaEnrol,
        MfaConsume,
        MfaStep,
        PasswordSet,
        UsernameChange,
    }

    private enum Class
    {
        /// <summary>Takes the gate, applies, verifies, refuses or defers.</summary>
        Reducer,

        /// <summary>Takes the gate and asserts that nothing was reduced; never refuses.</summary>
        GatedRaiser,

        /// <summary>Approved departure E-1 (development-only bootstrap).</summary>
        ExceptionE1,

        /// <summary>Approved departure E-2 (Entra link addition and pending-link proof at sign-in).</summary>
        ExceptionE2,

        /// <summary>Ruling C-14: legitimate consumption of a recovery code is non-refusable and takes no gate.</summary>
        RecoveryCodeConsumption,

        /// <summary>Cannot change a viability input; the reason is pinned by a test.</summary>
        OutOfCategory,

        /// <summary>A defect found while reconciling, scheduled for the package named, and counted so it cannot spread.</summary>
        Finding,
    }

    /// <summary>The packages whose writers are integrated. Moving a package here is what makes the method-level checks apply to it.</summary>
    private static readonly string[] IntegratedPackages = ["WP2"];

    private sealed record Classified(string File, Writes Kind, int Count, Class Class, string Package, string Why)
    {
        /// <summary>A reducer or gated raiser whose package is integrated: it takes the gate, and the method-level checks apply.</summary>
        public bool Integrated => Class is Class.Reducer or Class.GatedRaiser && IntegratedPackages.Contains(Package);

        /// <summary>A reducer or gated raiser that a later package has not integrated yet.</summary>
        public bool Pending => Class is Class.Reducer or Class.GatedRaiser && !IntegratedPackages.Contains(Package);
    }

    private const string Security = "Apperture.ControlPlane/Apperture.ControlPlane.Security/";
    private const string Api = "Apperture.ControlPlane/Apperture.Management.Api/Controllers/";
    private const string Cli = "Apperture.Cli/Apperture.Cli.Host/";

    private static readonly IReadOnlyList<Classified> Inventory =
    [
        // The administrator role's assignments (IAM-10.2a guards the authority rules; this guards viability).
        new(Security + "UserManagementService.cs", Writes.RoleAssign, 1, Class.GatedRaiser, "WP2", "assign a role"),
        new(Security + "UserManagementService.cs", Writes.RoleRemove, 1, Class.Reducer, "WP2", "remove a role: may remove the administrator role"),
        new(Security + "UserProvisioningService.cs", Writes.RoleAssign, 1, Class.GatedRaiser, "WP2", "the initial role of a provisioned person"),
        new(Security + "SqlRoleProvider.cs", Writes.RoleAssign, 1, Class.OutOfCategory, "-", "the role provider adapter, which nothing in production calls (the role writer guard pins that)"),
        new(Security + "SqlRoleProvider.cs", Writes.RoleRemove, 1, Class.OutOfCategory, "-", "as above"),
        new(Security + "BootstrapPlatformAdminAssignment.cs", Writes.RoleAssign, 1, Class.GatedRaiser, "WP4B", "startup configuration assigns the administrator role"),
        new(Security + "BootstrapPlatformAdminAssignment.cs", Writes.UserCreatedWithRoleIds, 2, Class.GatedRaiser, "WP4B", "startup configuration creates the first administrator"),
        new(Security + "RealBootstrapRecoveryService.cs", Writes.RoleAssign, 1, Class.GatedRaiser, "WP4B", "bootstrap recovery restores the administrator role"),
        new(Security + "FirstAdminBootstrapService.cs", Writes.UserCreatedWithRoleIds, 1, Class.GatedRaiser, "WP4B", "first administrator setup"),
        new(Security + "EmergencyAccessService.cs", Writes.UserCreatedWithRoleIds, 1, Class.GatedRaiser, "WP4B", "Recovery Access provisioning"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.RoleAssign, 1, Class.ExceptionE1, "-", "development-only bootstrap"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.UserCreatedWithRoleIds, 1, Class.ExceptionE1, "-", "development-only bootstrap"),

        // Credential disablement.
        new(Security + "UserManagementService.cs", Writes.CredentialDisable, 1, Class.Reducer, "WP2", "disable (reducer) or re-enable (raiser) a Local credential"),
        new(Security + "EntraPrimaryAuthenticationPolicy.cs", Writes.CredentialDisable, 1, Class.Reducer, "WP4A", "the policy disables a Local credential; it takes the gate itself and is not covered by E-2"),

        // Identity links.
        new(Security + "UserManagementService.cs", Writes.LinkAdd, 3, Class.GatedRaiser, "WP2", "an administrator adds a link"),
        new(Security + "UserManagementService.cs", Writes.LinkRemove, 1, Class.Reducer, "WP2", "an administrator removes a Local or Entra link"),
        new(Security + "EntraIdentityAssociationService.cs", Writes.LinkAdd, 1, Class.ExceptionE2, "-", "an Entra link is added at link completion"),
        new(Security + "EntraIdentityAssociationService.cs", Writes.LinkProof, 4, Class.ExceptionE2, "-", "an Entra link is proven at sign-in and link completion"),
        new(Security + "SqlPrincipalToUserResolver.cs", Writes.LinkProof, 3, Class.ExceptionE2, "-", "the refresh of a proven Entra link, the proof of a pending one, and the refresh of the Local link's timestamp"),

        // Entra registrations and the sign-in policy.
        new(Api + "ConfigEntraTenantsController.cs", Writes.RegistrationCreate, 1, Class.GatedRaiser, "WP3", "create a registration (inactive)"),
        new(Api + "ConfigEntraTenantsController.cs", Writes.RegistrationUpdate, 1, Class.Reducer, "WP3", "edit or disable a registration"),
        new(Api + "ConfigEntraTenantsController.cs", Writes.RegistrationDelete, 1, Class.Reducer, "WP3", "delete a registration"),
        new(Api + "ConfigEntraTenantsController.cs", Writes.RegistrationActivate, 1, Class.Reducer, "WP3", "activate or switch the registration"),
        new(Api + "ConfigAuthUiPolicyController.cs", Writes.PolicySave, 1, Class.GatedRaiser, "WP3", "chooser flags only: they cannot reduce either view, and a test pins that no viability input reads them"),

        // The designation flag and the posture inputs.
        new(Security + "EmergencyAccessService.cs", Writes.DesignationFlagUpdate, 1, Class.Reducer, "WP4B", "designate (both invariants) and revoke (strict) write the flag"),
        new(Security + "AccountContactService.cs", Writes.DesignationFlagUpdate, 2, Class.Finding, "WP4B", "F-1: a generic user update writes the flag from the record the caller holds"),
        new(Security + "AccountInvitationService.cs", Writes.DesignationFlagUpdate, 2, Class.Finding, "WP4B", "F-1"),
        new(Security + "AccountProfileService.cs", Writes.DesignationFlagUpdate, 4, Class.Finding, "WP4B", "F-1"),
        new(Security + "NativeMfaService.cs", Writes.MfaReset, 2, Class.Reducer, "WP4B", "reset removes the TOTP credential and the recovery codes"),
        new(Security + "NativeMfaService.cs", Writes.MfaEnrol, 4, Class.GatedRaiser, "WP4B", "enrolment and recovery-code regeneration only add"),
        new(Security + "NativeMfaService.cs", Writes.MfaConsume, 1, Class.RecoveryCodeConsumption, "-", "C-14: non-refusable, no gate"),
        new(Security + "NativeMfaService.cs", Writes.MfaStep, 1, Class.OutOfCategory, "-", "records the last verified TOTP step; no posture input reads it"),

        // Writers that cannot change a viability input.
        new(Security + "AccountInvitationService.cs", Writes.PasswordSet, 1, Class.OutOfCategory, "-", "SetPasswordAsync never writes IsDisabled on an existing credential, and a new credential is enabled"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.PasswordSet, 2, Class.ExceptionE1, "-", "development-only bootstrap"),
        new(Security + "EmergencyAccessService.cs", Writes.PasswordSet, 2, Class.OutOfCategory, "-", "SetPasswordAsync never writes IsDisabled on an existing credential, and a new credential is enabled"),
        new(Security + "FirstAdminBootstrapService.cs", Writes.PasswordSet, 1, Class.OutOfCategory, "-", "as above"),
        new(Security + "ForgottenPasswordService.cs", Writes.PasswordSet, 1, Class.OutOfCategory, "-", "as above"),
        new(Security + "UserManagementService.cs", Writes.PasswordSet, 2, Class.OutOfCategory, "-", "as above"),
        new(Api + "AuthController.cs", Writes.PasswordSet, 1, Class.OutOfCategory, "-", "as above"),
        new(Cli + "Program.cs", Writes.PasswordSet, 1, Class.OutOfCategory, "-", "the bootstrap command line's own credential, not a user's Local credential"),
        new("Apperture.Core/Apperture.Core.Application/Services/UsernameService.cs", Writes.UsernameChange, 2, Class.OutOfCategory, "-", "a rename keeps the Local link; viability reads its existence, not its name"),
    ];

    // ---------------------------------------------------------------- scanning

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<(string Relative, string Text)> ProductionSource()
    {
        var src = Path.Combine(RepoRoot(), "src");
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal))
                continue;
            yield return (relative, File.ReadAllText(path));
        }
    }

    private static string Source(string relativeToSrc) => File.ReadAllText(Path.Combine(RepoRoot(), "src", relativeToSrc));

    private static string StripComments(string text) =>
        Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static Regex R(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    private static readonly Regex RoleAssignCall = R(@"\.AssignToUserAsync\(");
    private static readonly Regex RoleRemoveCall = R(@"\.RemoveFromUserAsync\(");
    private static readonly Regex NewUser = R(@"new User\(");
    private static readonly Regex DisableCall = R(@"\.SetDisabledAsync\(");
    private static readonly Regex LinkAddCall = R(@"_userRepository\.AddIdentityLinkAsync\(");
    private static readonly Regex LinkRemoveCall = R(@"_userRepository\.RemoveIdentityLinkAsync\(");
    private static readonly Regex LinkProofCall = R(@"_userRepository\.(CompleteEntraLinkProofAsync|UpdateLastAuthenticatedAtAsync)\(");
    private static readonly Regex RegistrationCreateCall = R(@"\b(_repository|_registrations|_entraRegistrations|registrations)\.AddAsync\(");
    private static readonly Regex RegistrationUpdateCall = R(@"\b(_repository|_registrations|_entraRegistrations|registrations)\.UpdateAsync\(");
    private static readonly Regex RegistrationDeleteCall = R(@"\b(_repository|_registrations|_entraRegistrations|registrations)\.DeleteAsync\(");
    private static readonly Regex RegistrationActivateCall = R(@"\.SetActiveAsync\(");
    private static readonly Regex PolicySaveCall = R(@"\.SaveAsync\(");
    private static readonly Regex UserUpdateCall = R(@"\b(_users|_userRepository|userRepo|_userRepo)\.UpdateAsync\(");
    private static readonly Regex MfaResetCall = R(@"_totp\.DeleteAsync\(|_recoveryCodes\.DeleteAllAsync\(");
    private static readonly Regex MfaEnrolCall = R(@"_totp\.(UpsertPendingAsync|MarkEnrolledAsync)\(|_recoveryCodes\.ReplaceAllAsync\(");
    private static readonly Regex MfaConsumeCall = R(@"_recoveryCodes\.MarkConsumedAsync\(");
    private static readonly Regex MfaStepCall = R(@"_totp\.UpdateLastUsedAsync\(");
    private static readonly Regex PasswordSetCall = R(@"\.SetPasswordAsync\(");
    private static readonly Regex UsernameChangeCall = R(@"_users\.ChangeUsernameAsync\(");

    private static Dictionary<(string File, Writes Kind), int> Scan(IEnumerable<(string Relative, string Text)> sources)
    {
        var counts = new Dictionary<(string, Writes), int>();
        void Add(string file, Writes kind, int n)
        {
            if (n > 0)
                counts[(file, kind)] = counts.GetValueOrDefault((file, kind)) + n;
        }

        foreach (var (file, text) in sources)
        {
            // The implementations and the contracts are the mechanism, not writers.
            if (file.Contains("Persistence.SqlServer/", StringComparison.Ordinal))
                continue;
            var code = StripComments(text);

            Add(file, Writes.RoleAssign, RoleAssignCall.Matches(code).Count);
            Add(file, Writes.RoleRemove, RoleRemoveCall.Matches(code).Count);
            foreach (Match match in NewUser.Matches(code))
            {
                if (!IsEmpty(RoleIdsArgument(code, match.Index + match.Length)))
                    Add(file, Writes.UserCreatedWithRoleIds, 1);
            }

            Add(file, Writes.CredentialDisable, DisableCall.Matches(code).Count);
            Add(file, Writes.LinkAdd, LinkAddCall.Matches(code).Count);
            Add(file, Writes.LinkRemove, LinkRemoveCall.Matches(code).Count);
            Add(file, Writes.LinkProof, LinkProofCall.Matches(code).Count);

            if (code.Contains("IEntraInstanceRegistrationRepository", StringComparison.Ordinal))
            {
                Add(file, Writes.RegistrationCreate, RegistrationCreateCall.Matches(code).Count);
                Add(file, Writes.RegistrationUpdate, RegistrationUpdateCall.Matches(code).Count);
                Add(file, Writes.RegistrationDelete, RegistrationDeleteCall.Matches(code).Count);
            }

            Add(file, Writes.RegistrationActivate, RegistrationActivateCall.Matches(code).Count);

            if (code.Contains("IInstanceAuthUiPolicyRepository", StringComparison.Ordinal))
                Add(file, Writes.PolicySave, PolicySaveCall.Matches(code).Count);

            if (code.Contains("IUserRepository", StringComparison.Ordinal))
                Add(file, Writes.DesignationFlagUpdate, UserUpdateCall.Matches(code).Count);

            Add(file, Writes.MfaReset, MfaResetCall.Matches(code).Count);
            Add(file, Writes.MfaEnrol, MfaEnrolCall.Matches(code).Count);
            Add(file, Writes.MfaConsume, MfaConsumeCall.Matches(code).Count);
            Add(file, Writes.MfaStep, MfaStepCall.Matches(code).Count);
            Add(file, Writes.PasswordSet, PasswordSetCall.Matches(code).Count);
            Add(file, Writes.UsernameChange, UsernameChangeCall.Matches(code).Count);
        }

        return counts;
    }

    /// <summary>The fourth argument of a <c>new User(...)</c> (the role ids), read by balanced brackets.</summary>
    private static string RoleIdsArgument(string code, int afterOpenParenthesis)
    {
        var depth = 1;
        var argument = 0;
        var start = afterOpenParenthesis;
        for (var i = afterOpenParenthesis; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                    return argument == 3 ? code[start..i] : "";
            }
            else if (c == ',' && depth == 1)
            {
                if (argument == 3)
                    return code[start..i];
                argument++;
                start = i + 1;
            }
        }

        return "";
    }

    private static bool IsEmpty(string roleIds) =>
        Regex.IsMatch(roleIds.Trim(), @"^(\[\s*\]|Array\.Empty<RoleId>\(\)|new RoleId\[0\]|new\[\]\s*\{\s*\}|)$", RegexOptions.CultureInvariant);

    private static List<string> Differences(
        IReadOnlyDictionary<(string File, Writes Kind), int> found,
        IReadOnlyList<Classified> inventory)
    {
        var expected = inventory.ToDictionary(i => (i.File, i.Kind), i => i.Count);
        var differences = new List<string>();
        foreach (var ((file, kind), count) in found.OrderBy(kv => kv.Key.File, StringComparer.Ordinal).ThenBy(kv => kv.Key.Kind))
        {
            if (!expected.TryGetValue((file, kind), out var want))
                differences.Add($"UNCLASSIFIED WRITER: {file} has {count} x {kind}; classify it in the inventory");
            else if (want != count)
                differences.Add($"COUNT DIFFERS: {file} {kind}: classified {want}, source {count}");
        }

        foreach (var ((file, kind), count) in expected)
            if (!found.ContainsKey((file, kind)))
                differences.Add($"CLASSIFIED BUT NOT FOUND: {file} {kind} x{count}");
        return differences;
    }

    /// <summary>The text of a method, from its declaration to the brace that closes it. A call of the same name is not its declaration.</summary>
    private static string MethodBody(string code, string methodName)
    {
        var declaration = Regex.Match(
            code,
            @"^[ \t]*(?:public|private|internal|protected)[^\r\n]* " + Regex.Escape(methodName) + @"\(",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        declaration.Success.Should().BeTrue($"{methodName} must be declared");
        var at = declaration.Index;
        var open = code.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
                depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[at..(i + 1)];
        }

        throw new InvalidOperationException($"{methodName} has no closing brace.");
    }

    // ------------------------------------------------------------------- tests

    [Fact]
    public void Every_production_writer_of_a_viability_input_is_classified()
    {
        var found = Scan(ProductionSource());

        found.Should().NotBeEmpty();
        Differences(found, Inventory).Should().BeEmpty(
            "a writer of an administrator role assignment, a credential's disablement, an identity link, a registration, the sign-in policy, the designation flag or a posture input must be classified in the inventory");
    }

    [Fact]
    public void The_guard_itself_fails_on_a_new_a_missing_and_a_miscounted_writer()
    {
        var found = new Dictionary<(string, Writes), int>
        {
            [("Some/New/Writer.cs", Writes.CredentialDisable)] = 1,
            [(Security + "UserManagementService.cs", Writes.RoleRemove)] = 2,
        };
        var inventory = new List<Classified>
        {
            new(Security + "UserManagementService.cs", Writes.RoleRemove, 1, Class.Reducer, "WP2", "x"),
            new(Security + "Gone.cs", Writes.LinkAdd, 1, Class.GatedRaiser, "WP2", "x"),
        };

        var differences = Differences(found, inventory);

        differences.Should().Contain(d => d.StartsWith("UNCLASSIFIED WRITER") && d.Contains("Writer.cs"));
        differences.Should().Contain(d => d.StartsWith("COUNT DIFFERS") && d.Contains("RoleRemove"));
        differences.Should().Contain(d => d.StartsWith("CLASSIFIED BUT NOT FOUND") && d.Contains("Gone.cs"));
    }

    [Fact]
    public void The_two_approved_departures_are_exactly_the_enumerated_files()
    {
        Inventory.Where(i => i.Class == Class.ExceptionE1).Select(i => i.File).Distinct()
            .Should().BeEquivalentTo([Security + "DevTestUserBootstrapHostedService.cs"]);
        Inventory.Where(i => i.Class == Class.ExceptionE2).Select(i => i.File).Distinct()
            .Should().BeEquivalentTo([Security + "EntraIdentityAssociationService.cs", Security + "SqlPrincipalToUserResolver.cs"]);
        Inventory.Where(i => i.File.EndsWith("EntraPrimaryAuthenticationPolicy.cs", StringComparison.Ordinal))
            .Should().OnlyContain(i => i.Class == Class.Reducer, "the Entra-primary policy is not covered by E-2: its credential disable takes the gate");
    }

    [Fact]
    public void A_pending_writer_does_not_yet_reference_the_gate_or_the_coordinator_and_no_file_is_both_pending_and_integrated()
    {
        var pending = Inventory.Where(i => i.Pending).Select(i => i.File).Distinct().ToList();
        var integrated = Inventory.Where(i => i.Integrated).Select(i => i.File).Distinct().ToList();

        pending.Should().NotBeEmpty();
        integrated.Should().NotBeEmpty();
        pending.Intersect(integrated).Should().BeEmpty("a file is integrated or pending as a whole, so the method-level checks cannot miss a writer in it");
        foreach (var file in pending)
        {
            StripComments(Source(file)).Should().NotContainAny(
                new[] { "IPlatformAdministratorSetGate", "IAdministratorViabilityCoordinator", "AdministratorViabilityScope" },
                $"{file} is classified pending; integrating it is the work of a later package, which also moves its inventory entries to integrated");
        }
    }

    [Fact]
    public void Only_the_gate_its_contract_the_registration_and_the_coordinator_name_the_gate()
    {
        var users = ProductionSource()
            .Where(s => StripComments(s.Text).Contains("IPlatformAdministratorSetGate", StringComparison.Ordinal))
            .Select(s => s.Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        users.Should().BeEquivalentTo(
            [
                "Apperture.Core/Apperture.Core.Application/AdministratorViability/IPlatformAdministratorSetGate.cs",
                "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs",
                "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/AdministratorViabilityCoordinator.cs",
                "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlPlatformAdministratorSetGate.cs",
            ]);
    }

    [Fact]
    public void Only_the_integrated_writers_the_coordinator_and_its_helper_name_the_coordinator()
    {
        var users = ProductionSource()
            .Where(s => StripComments(s.Text).Contains("IAdministratorViabilityCoordinator", StringComparison.Ordinal))
            .Select(s => s.Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        users.Should().BeEquivalentTo(
            Inventory.Where(i => i.Integrated).Select(i => i.File)
                .Concat(
                [
                    "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/AdministratorViabilityCoordinator.cs",
                    "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/AdministratorRoleGate.cs",
                    "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs",
                ])
                .Distinct(),
            "an unclassified file that takes the coordinator would be a writer the inventory does not know");
    }

    // ---- The integrated writers, method by method ----

    /// <summary>What an integrated writer must do, in this order, inside its method.</summary>
    /// <param name="File">The source file.</param>
    /// <param name="Method">The method that holds the writes.</param>
    /// <param name="Kind">The write kind the inventory counts in this file.</param>
    /// <param name="Steps">Fragments that must all appear, each after the one before it.</param>
    private sealed record Sequence(string File, string Method, Writes Kind, string[] Steps);

    private const string TakeGate = "Viability.EnterAsync(";
    private const string TakeGateForRole = "EnterForAdministratorRoleAsync(";

    private static readonly IReadOnlyList<Sequence> IntegratedSequences =
    [
        // Role assignment: the gate before the authority row locks, then the write, then the assertion. A raiser.
        new(Security + "UserManagementService.cs", "AssignRoleAsync", Writes.RoleAssign,
            [TakeGateForRole, "LockAuthoritySnapshotAsync(", "CaptureBeforeAsync(", ".AssignToUserAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // Role removal: the gate, the authority locks, the authorization refusals, the no-op, the state before, the write, the verdict.
        new(Security + "UserManagementService.cs", "RemoveRoleAsync", Writes.RoleRemove,
            [TakeGateForRole, "LockAuthoritySnapshotAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", ".RemoveFromUserAsync(", "VerifyAfterAsync(", "ViabilityRefusal.For(", "StageAsync("]),

        // Credential disable or enable: the gate first, the no-op before the invariants (P-9, F-9), then the write and the verdict.
        new(Security + "UserManagementService.cs", "SetLocalCredentialDisabledAsync", Writes.CredentialDisable,
            [TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", ".SetDisabledAsync(", "VerifyAfterAsync(", "ViabilityRefusal.For(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // Link removal, Local or Entra: the same shape.
        new(Security + "UserManagementService.cs", "RemoveIdentityLinkAsync", Writes.LinkRemove,
            [TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "_userRepository.RemoveIdentityLinkAsync(", "VerifyAfterAsync(", "ViabilityRefusal.For(", "StageAsync("]),

        // Link addition: the gate before the first write; the helper holds the state before, the write and the assertion.
        new(Security + "UserManagementService.cs", "AddIdentityLinkAsync", Writes.LinkAdd,
            [TakeGate, "RequireLinkableUserAsync(userId, ct)", "RaiseOnlyAsync(", "StageAsync("]),
        new(Security + "UserManagementService.cs", "RaiseOnlyAsync", Writes.LinkAdd,
            ["CaptureBeforeAsync(", "await write()", "AssertRaiseOnlyAsync("]),

        // The initial role of a provisioned person: the gate before the authority locks; the assignment is captured and asserted.
        new(Security + "UserProvisioningService.cs", "DecideInitialRoleAsync", Writes.RoleAssign,
            [TakeGateForRole, "LockAuthoritySnapshotAsync("]),
        new(Security + "UserProvisioningService.cs", "AssignInitialRoleAsync", Writes.RoleAssign,
            ["CaptureBeforeAsync(", ".AssignToUserAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),
    ];

    [Fact]
    public void Each_integrated_writer_takes_the_gate_before_its_locks_and_writes_and_checks_after_them()
    {
        foreach (var sequence in IntegratedSequences)
        {
            var body = MethodBody(StripComments(Source(sequence.File)), sequence.Method);

            var at = -1;
            foreach (var step in sequence.Steps)
            {
                var next = body.IndexOf(step, at + 1, StringComparison.Ordinal);
                next.Should().BeGreaterThan(at, $"{sequence.File} {sequence.Method} must reach '{step}' after the step before it ({string.Join(" -> ", sequence.Steps)})");
                at = next;
            }
        }
    }

    [Fact]
    public void Each_counted_write_of_an_integrated_file_is_inside_a_method_whose_order_is_checked()
    {
        // The inventory's count for an integrated file is the writes found in the methods checked above, so a write added
        // anywhere else in the file changes the count of the whole file and the first test, or is found here.
        foreach (var file in Inventory.Where(i => i.Integrated).Select(i => i.File).Distinct())
        {
            var code = StripComments(Source(file));
            foreach (var entry in Inventory.Where(i => i.File == file && i.Integrated))
            {
                var checkedMethods = IntegratedSequences.Where(s => s.File == file && s.Kind == entry.Kind).Select(s => s.Method).ToList();
                checkedMethods.Should().NotBeEmpty($"{file} {entry.Kind} is integrated, so a method must be checked for it");

                var inChecked = checkedMethods.Sum(method => WriteMatches(entry.Kind, MethodBody(code, method)));
                inChecked.Should().Be(entry.Count, $"every {entry.Kind} write of {file} must be in a method this guard checks ({string.Join(", ", checkedMethods)})");
            }
        }
    }

    private static int WriteMatches(Writes kind, string code) => kind switch
    {
        Writes.RoleAssign => RoleAssignCall.Matches(code).Count,
        Writes.RoleRemove => RoleRemoveCall.Matches(code).Count,
        Writes.CredentialDisable => DisableCall.Matches(code).Count,
        Writes.LinkAdd => LinkAddCall.Matches(code).Count,
        Writes.LinkRemove => LinkRemoveCall.Matches(code).Count,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not an integrated write kind"),
    };

    [Fact]
    public void The_superseded_assignment_count_and_entra_options_predicates_are_gone_from_the_integrated_writers()
    {
        var code = StripComments(Source(Security + "UserManagementService.cs"));

        code.Should().NotContainAny(
            new[] { "CountUserAssignmentsAsync(", "HasViableAlternateSignIn", "EntraConfigurationState" },
            "the shared viability guard is the one definition; a second predicate beside it could disagree with it");
    }

    [Fact]
    public void The_gate_helper_refuses_to_proceed_when_the_role_is_reached_without_the_gate()
    {
        var code = StripComments(Source(Security + "AdministratorViability/AdministratorRoleGate.cs"));

        MethodBody(code, "RequireGateHeldFor").Should().Contain("AuditInvariantException");
        MethodBody(code, "EnterForAdministratorRoleAsync").Should().Contain("GetAsync(").And.Contain("IsPlatformAdministratorRole(");
    }

    [Fact]
    public void Nothing_else_calls_the_application_lock_for_the_gates_resource()
    {
        var literal = "apperture:platform-administrator-set";
        var owners = ProductionSource()
            .Where(s => StripComments(s.Text).Contains(literal, StringComparison.Ordinal)
                        || StripComments(s.Text).Contains("PlatformAdministratorSetLock.ResourceName", StringComparison.Ordinal))
            .Select(s => s.Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        owners.Should().BeEquivalentTo(
            [
                "Apperture.Core/Apperture.Core.Application/AdministratorViability/IPlatformAdministratorSetGate.cs",
                "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlPlatformAdministratorSetGate.cs",
            ]);
    }

    [Fact]
    public void No_production_code_uses_snapshot_transaction_isolation()
    {
        var snapshot = R(@"IsolationLevel\s*\.\s*Snapshot|ISOLATION\s+LEVEL\s+SNAPSHOT|ALLOW_SNAPSHOT_ISOLATION");

        ProductionSource()
            .Where(s => snapshot.IsMatch(StripComments(s.Text)))
            .Select(s => s.Relative)
            .Should().BeEmpty("the invariant reads rely on statement-level reads after the gate, which transaction-level SNAPSHOT would defeat");
    }

    // ---- C-14 ----

    [Fact]
    public void Recovery_code_consumption_stays_non_refusable_and_takes_no_gate()
    {
        var consume = MethodBody(StripComments(Source(Security + "NativeMfaService.cs")), "ConsumeRecoveryCodeAsync");

        consume.Should().NotContain("AdministratorSet").And.NotContain("Viability").And.NotContain("Gate");
        consume.Should().NotContain("Refuse", "legitimate consumption of the last code must never be refused");
        Inventory.Single(i => i.Kind == Writes.MfaConsume).Class.Should().Be(Class.RecoveryCodeConsumption);
    }

    // ---- E-1 ----

    [Fact]
    public void The_development_bootstrap_is_restricted_to_development_and_only_adds()
    {
        var code = StripComments(Source(Security + "DevTestUserBootstrapHostedService.cs"));
        var firstWrite = new[] { ".AddAsync(", ".AssignToUserAsync(", ".SetPasswordAsync(" }
            .Select(w => code.IndexOf(w, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .Min();

        var environmentCheck = code.IndexOf("IsDevelopment()", StringComparison.Ordinal);
        environmentCheck.Should().BeGreaterThanOrEqualTo(0);
        environmentCheck.Should().BeLessThan(firstWrite, "the environment check must precede the first write");
        code.Should().Contain("opts.Enabled");
        code.Should().NotContainAny(
            ".RemoveFromUserAsync(", ".SetDisabledAsync(", ".RemoveIdentityLinkAsync(", ".DeleteAsync(", ".Remove(",
            ".UpdateAsync(", "IsDisabled");
    }

    // ---- E-2 ----

    [Fact]
    public void The_entra_link_sign_in_paths_cannot_reduce_viability()
    {
        foreach (var file in new[] { Security + "EntraIdentityAssociationService.cs", Security + "SqlPrincipalToUserResolver.cs" })
        {
            StripComments(Source(file)).Should().NotContainAny(
                new[]
                {
                    ".RemoveIdentityLinkAsync(", ".SetDisabledAsync(", ".RemoveFromUserAsync(", ".AssignToUserAsync(",
                    ".UpdateAsync(", "IsBreakGlassEligible",
                },
                $"{file} may only add or prove a link");
        }

        // The repository writes the proof marker to a time, never back to null, apart from the pending link a new link starts as.
        var repository = StripComments(Source("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlUserRepository.cs"));
        var nullings = R(@"LastAuthenticatedAt\s*=\s*null").Matches(repository);
        nullings.Should().ContainSingle("only the entity of a newly added link starts without a proof");
        repository[Math.Max(0, nullings[0].Index - 400)..nullings[0].Index].Should().Contain("new IdentityLinkEntity");
        MethodBody(repository, "CompleteEntraLinkProofAsync").Should().Contain("LastAuthenticatedAt = provenAt");
    }

    // ---- F-1 ----

    [Fact]
    public void Finding_F1_remains_visible_until_the_package_that_resolves_it()
    {
        var update = MethodBody(
            StripComments(Source("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlUserRepository.cs")),
            "UpdateAsync");

        update.Should().Contain(
            "entity.IsBreakGlassEligible = user.IsBreakGlassEligible",
            "F-1: the generic update writes the designation flag from the caller's record; WP4B removes this line and adds a dedicated, gated writer, and this test then changes");
        Inventory.Where(i => i.Class == Class.Finding).Should().OnlyContain(i => i.Package == "WP4B");
    }

    // ---- Out of category ----

    [Fact]
    public void Setting_a_password_never_changes_whether_a_credential_is_disabled_and_a_new_credential_is_enabled()
    {
        var set = MethodBody(
            StripComments(Source("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlLocalCredentialRepository.cs")),
            "SetPasswordAsync");

        R(@"IsDisabled").Matches(set).Count.Should().Be(1);
        set.Should().Contain("IsDisabled = false");
    }

    // ---- The definitions read nothing the chooser decides ----

    [Fact]
    public void No_viability_input_reads_a_chooser_or_policy_flag()
    {
        var files = new[]
        {
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/AdministratorViabilityEvaluator.cs",
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/AdministratorViabilityGuard.cs",
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/AdministratorViabilityService.cs",
            "Apperture.ControlPlane/Apperture.ControlPlane.Security/AdministratorViability/EffectiveSignInConfigurationProvider.cs",
            "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAdministratorFactsReader.cs",
        };

        foreach (var file in files)
        {
            StripComments(Source(file)).Should().NotContainAny(
                new[] { "ShowLocalSignInInPrimaryChooser", "AutoRedirectToEntra", "PreferEntraInChooser" },
                $"{file}: the direct Local login route does not depend on the chooser");
        }
    }

    // ---- One tenant rule ----

    [Fact]
    public void The_sign_in_and_the_viability_evaluation_share_one_tenant_rule_and_one_comparison()
    {
        StripComments(Source(Security + "EntraIdentityAssociationService.cs"))
            .Should().Contain("EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(registration)")
            .And.Contain("EntraTokenClaims.TenantMatches(");
        StripComments(Source(Security + "AdministratorViability/AdministratorViabilityEvaluator.cs"))
            .Should().Contain("EntraTokenClaims.TenantMatches(");
        StripComments(Source(Security + "AdministratorViability/EffectiveSignInConfigurationProvider.cs"))
            .Should().Contain("EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(");
    }
}
