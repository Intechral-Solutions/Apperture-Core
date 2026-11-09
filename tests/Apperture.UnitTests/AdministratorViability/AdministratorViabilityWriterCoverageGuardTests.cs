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
/// <b>integrated</b> (WP2: the person-driven administrator, credential and link writers; WP3: the Entra registration and sign-in
/// policy writers; WP4A: the Entra-primary policy's credential disable; each checked method by method for the gate before the row
/// locks and the writes, and for the check after them) or
/// explicitly <b>pending</b> (every other package), and a pending file must not already reference the gate or the coordinator, so
/// that the pending list cannot go stale unnoticed.
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
        MfaPendingWrite,
        MfaConsume,
        MfaStep,
        MfaCandidateStage,
        MfaCandidateVerify,
        MfaCandidateSwitch,
        MfaCandidateDiscard,
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
    private static readonly string[] IntegratedPackages = ["WP2", "WP3", "WP4A", "WP4B-D", "WP4B-C"];

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
        new(Security + "BootstrapPlatformAdminAssignment.cs", Writes.RoleAssign, 1, Class.GatedRaiser, "WP4B-D", "startup configuration assigns the administrator role"),
        new(Security + "BootstrapPlatformAdminAssignment.cs", Writes.UserCreatedWithRoleIds, 2, Class.GatedRaiser, "WP4B-D", "startup configuration creates the first administrator"),
        new(Security + "RealBootstrapRecoveryService.cs", Writes.RoleAssign, 1, Class.GatedRaiser, "WP4B-D", "bootstrap recovery restores the administrator role"),
        new(Security + "FirstAdminBootstrapService.cs", Writes.UserCreatedWithRoleIds, 1, Class.GatedRaiser, "WP4B-D", "first administrator setup"),
        new(Security + "EmergencyAccessService.cs", Writes.UserCreatedWithRoleIds, 1, Class.GatedRaiser, "WP4B-C", "Recovery Access provisioning: a new account that holds nothing before it, under the gate, asserted raise-only"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.RoleAssign, 1, Class.ExceptionE1, "-", "development-only bootstrap"),
        new(Security + "DevTestUserBootstrapHostedService.cs", Writes.UserCreatedWithRoleIds, 1, Class.ExceptionE1, "-", "development-only bootstrap"),

        // Credential disablement.
        new(Security + "UserManagementService.cs", Writes.CredentialDisable, 1, Class.Reducer, "WP2", "disable (reducer) or re-enable (raiser) a Local credential"),
        new(Security + "EntraPrimaryAuthenticationPolicy.cs", Writes.CredentialDisable, 1, Class.Reducer, "WP4A", "the Entra-primary policy disables a Local credential: it takes the gate, applies, verifies and defers; it is not covered by E-2"),

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
        new(Security + "EmergencyAccessService.cs", Writes.DesignationFlagUpdate, 1, Class.Reducer, "WP4B-C", "designate (both invariants) and revoke (strict) write the flag, through the one conditional setter, under the gate, decided last"),
        new(Security + "NativeMfaService.cs", Writes.MfaReset, 2, Class.Reducer, "WP4B-C", "the bare reset removes the TOTP credential and the recovery codes: under the gate, decided last on the posture or the Local reading"),
        new(Security + "NativeMfaService.cs", Writes.MfaEnrol, 3, Class.GatedRaiser, "WP4B-C", "the first enrolment's conditional completion and the regeneration of the codes only add: under the gate, asserted raise-only"),
        new(Security + "NativeMfaService.cs", Writes.MfaPendingWrite, 1, Class.OutOfCategory, "-", "a pending credential is not a posture input (the facts read only an enrolled one), and the conditional write never replaces an enrolled one (WP4B-A)"),
        new(Security + "NativeMfaService.cs", Writes.MfaConsume, 1, Class.RecoveryCodeConsumption, "-", "C-14: non-refusable, no gate"),
        new(Security + "NativeMfaService.cs", Writes.MfaStep, 1, Class.OutOfCategory, "-", "records the last verified TOTP step; no posture input reads it"),
        new(Security + "NativeMfaService.cs", Writes.MfaCandidateStage, 1, Class.OutOfCategory, "-", "a staged candidate is not an authenticator: the facts read only the enrolled credential and its unused codes, which staging never touches"),
        new(Security + "NativeMfaService.cs", Writes.MfaCandidateVerify, 1, Class.OutOfCategory, "-", "marking a candidate verified changes nothing the facts read"),
        new(Security + "NativeMfaService.cs", Writes.MfaCandidateDiscard, 4, Class.OutOfCategory, "-", "removing a candidate (abandoned or expired) never touches the active authenticator or its codes"),
        new(Security + "NativeMfaService.cs", Writes.MfaCandidateSwitch, 1, Class.Reducer, "WP4B-C", "the switch replaces the authenticator and its codes: under the gate, decided last on the posture alone, and only on the posture (I-R3-1)"),

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
    private static readonly Regex RegistrationActivateCall = R(@"\.SetActiveAsync\(");
    private static readonly Regex PolicySaveCall = R(@"\.SaveAsync\(");

    /// <summary>
    /// Every name a file gives to a holder of <paramref name="type"/> (a field, a parameter, a local), with and without a leading
    /// underscore. A write is found by the type its receiver was declared with, not by one of a few conventional variable names, so
    /// a writer that names its repository something new is found all the same.
    /// </summary>
    private static string[] NamesHolding(string code, string type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(code, @"\b" + Regex.Escape(type) + @"\??\s+(\w+)", RegexOptions.CultureInvariant))
        {
            var name = match.Groups[1].Value;
            names.Add(name);
            names.Add("_" + name.TrimStart('_'));
            names.Add(name.TrimStart('_'));
        }

        return [.. names];
    }

    private static int CallsOn(string code, string[] receivers, string method) =>
        receivers.Length == 0
            ? 0
            : Regex.Matches(code, @"\b(?:" + string.Join("|", receivers.Select(Regex.Escape)) + @")\." + method + @"\(", RegexOptions.CultureInvariant).Count;
    // The designation has one writer, the conditional setter. The generic update no longer writes it, so a call of the generic update
    // is not a writer of a viability input and is not counted here.
    private static readonly Regex DesignationSetterCall = R(@"\b(_users|_userRepository|userRepo|_userRepo)\.TrySetDesignationAsync\(");
    private static readonly Regex MfaResetCall = R(@"_totp\.DeleteAsync\(|_recoveryCodes\.DeleteAllAsync\(");
    private static readonly Regex MfaEnrolCall = R(@"_totp\.MarkEnrolledAsync\(|_recoveryCodes\.ReplaceAllAsync\(");
    private static readonly Regex MfaPendingWriteCall = R(@"_totp\.UpsertPendingAsync\(");
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

            var registrationRepositories = NamesHolding(code, "IEntraInstanceRegistrationRepository");
            Add(file, Writes.RegistrationCreate, CallsOn(code, registrationRepositories, "AddAsync"));
            Add(file, Writes.RegistrationUpdate, CallsOn(code, registrationRepositories, "UpdateAsync"));
            Add(file, Writes.RegistrationDelete, CallsOn(code, registrationRepositories, "DeleteAsync"));
            Add(file, Writes.RegistrationActivate, RegistrationActivateCall.Matches(code).Count);

            var policyRepositories = NamesHolding(code, "IInstanceAuthUiPolicyRepository");
            Add(file, Writes.PolicySave, CallsOn(code, policyRepositories, "SaveAsync"));

            if (code.Contains("IUserRepository", StringComparison.Ordinal))
                Add(file, Writes.DesignationFlagUpdate, DesignationSetterCall.Matches(code).Count);

            Add(file, Writes.MfaReset, MfaResetCall.Matches(code).Count);
            Add(file, Writes.MfaEnrol, MfaEnrolCall.Matches(code).Count);
            Add(file, Writes.MfaPendingWrite, MfaPendingWriteCall.Matches(code).Count);
            Add(file, Writes.MfaConsume, MfaConsumeCall.Matches(code).Count);

            // The replacement candidates, found by the type their receiver was declared with (U6): staging, verifying and removing a
            // candidate change no posture input; the switch does.
            var candidateStores = NamesHolding(code, "INativeMfaReplacementCandidateRepository");
            Add(file, Writes.MfaCandidateStage, CallsOn(code, candidateStores, "StageAsync"));
            Add(file, Writes.MfaCandidateVerify, CallsOn(code, candidateStores, "MarkVerifiedAsync"));
            Add(file, Writes.MfaCandidateSwitch, CallsOn(code, candidateStores, "SwitchAsync"));
            Add(file, Writes.MfaCandidateDiscard, CallsOn(code, candidateStores, "DiscardAsync"));
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
    public void Every_reducer_and_raiser_is_integrated_and_a_future_pending_writer_could_not_already_reference_the_gate()
    {
        // WP4B-C integrated the last pending writers (Recovery Access and native MFA). A writer a later change classifies pending must not
        // already reference the gate or the coordinator, so the pending list cannot go stale unnoticed.
        var pending = Inventory.Where(i => i.Pending).Select(i => i.File).Distinct().ToList();
        var integrated = Inventory.Where(i => i.Integrated).Select(i => i.File).Distinct().ToList();

        pending.Should().BeEmpty("every reducer and gated raiser of administrator viability takes the gate now");
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
                    // Resolves the coordinator from its scope and passes it to the integrated static writer; it writes nothing itself.
                    "Apperture.ControlPlane/Apperture.ControlPlane.Security/BootstrapPlatformAdminAssignmentHostedService.cs",
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
            [TakeGateForRole, "LockAuthoritySnapshotAsync(", "CaptureBeforeForRaiseAsync(", ".AssignToUserAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // Role removal: the gate, the authority locks, the authorization refusals, the no-op, the state before, the write, the verdict.
        new(Security + "UserManagementService.cs", "RemoveRoleAsync", Writes.RoleRemove,
            [TakeGateForRole, "LockAuthoritySnapshotAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", ".RemoveFromUserAsync(", "VerifyAfterAsync(", "ViabilityRefusal.For(", "StageAsync("]),

        // Credential disable or enable: the gate first, the no-op before the invariants (P-9, F-9), then the write and the verdict.
        new(Security + "UserManagementService.cs", "SetLocalCredentialDisabledAsync", Writes.CredentialDisable,
            [TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "CaptureBeforeForRaiseAsync(", ".SetDisabledAsync(", "VerifyAfterAsync(", "ViabilityRefusal.For(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // Link removal, Local or Entra: the same shape.
        new(Security + "UserManagementService.cs", "RemoveIdentityLinkAsync", Writes.LinkRemove,
            [TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "_userRepository.RemoveIdentityLinkAsync(", "VerifyAfterAsync(", "ViabilityRefusal.For(", "StageAsync("]),

        // Link addition: the gate before the first write; the helper holds the state before, the write and the assertion.
        new(Security + "UserManagementService.cs", "AddIdentityLinkAsync", Writes.LinkAdd,
            [TakeGate, "RequireLinkableUserAsync(userId, ct)", "RaiseOnlyAsync(", "StageAsync("]),
        new(Security + "UserManagementService.cs", "RaiseOnlyAsync", Writes.LinkAdd,
            ["CaptureBeforeForRaiseAsync(", "await write()", "AssertRaiseOnlyAsync("]),

        // The initial role of a provisioned person: the gate before the authority locks; the assignment is captured and asserted.
        new(Security + "UserProvisioningService.cs", "DecideInitialRoleAsync", Writes.RoleAssign,
            [TakeGateForRole, "LockAuthoritySnapshotAsync("]),
        new(Security + "UserProvisioningService.cs", "AssignInitialRoleAsync", Writes.RoleAssign,
            ["CaptureBeforeForRaiseAsync(", ".AssignToUserAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // WP3. The running options are built before the unit opens (R-1); inside the unit the gate comes before the first read of
        // a registration for the decision; the state is captured under the gate and before the first write; the change is applied;
        // the applied change is decided (instance-level rule, then viability); only then are rows staged and secrets retired.
        new(Api + "ConfigEntraTenantsController.cs", "Create", Writes.RegistrationCreate,
            [PrepareOptions, "Unit.ExecuteAsync(", TakeGate, "GetActiveAsync(", "StoreNewAsync(", "_repository.AddAsync(", "RequireActiveRegistrationUnchanged(", "StageAsync("]),
        new(Api + "ConfigEntraTenantsController.cs", "Patch", Writes.RegistrationUpdate,
            [PrepareOptions, "Unit.ExecuteAsync(", TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "StoreNewAsync(", "_repository.UpdateAsync(", DecideApplied, "StageAsync(", "RetireOwnedSecretAsync("]),
        new(Api + "ConfigEntraTenantsController.cs", "Delete", Writes.RegistrationDelete,
            [PrepareOptions, "Unit.ExecuteAsync", TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "_repository.DeleteAsync(", DecideApplied, "StageAsync(", "RetireOwnedSecretAsync("]),
        new(Api + "ConfigEntraTenantsController.cs", "Activate", Writes.RegistrationActivate,
            [PrepareOptions, "Unit.ExecuteAsync", TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "_repository.SetActiveAsync(", DecideApplied, "StageAsync("]),
        new(Api + "ConfigEntraTenantsController.cs", "DecideAppliedChangeAsync", Writes.RegistrationActivate,
            ["VerifyAfterAsync(", ".Captured", "NewlyViolates(", "ViabilityRefusal.For("]),

        // WP4A. The Entra-primary policy answers "nothing to disable" without the gate (it only ever answers no); a change to a
        // credential is decided inside the unit: the gate first, then the target and its credential, the state before, the write,
        // the state after and the verdict, and only then the success row. A deferral declares the attempt a no-op so the unit
        // rolls the disable back.
        new(Security + "EntraPrimaryAuthenticationPolicy.cs", "ApplyAfterEntraProvenAsync", Writes.CredentialDisable,
            ["NothingToDisableAsync(", "Unit.ExecuteAsync(", TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", ".SetDisabledAsync(", "VerifyAfterAsync(", "DeclareNoOp()", "StageAsync("]),

        // The sign-in policy takes the gate before it reads or writes the policy row, and reads no viability state: the chooser flags
        // are not an input of administrator viability, so this change cannot reduce either set.
        new(Api + "ConfigAuthUiPolicyController.cs", "Patch", Writes.PolicySave,
            [PrepareOptions, "Unit.ExecuteAsync(", TakeGate, "GetAsync(", "DeclareNoOp()", "_policyRepository.SaveAsync(", "StageAsync("]),

        // WP4B-D. The bootstrap writers take the gate through the gate-only entry (they can only add an administrator), first in
        // their unit and before the read that decides. The first administrator answers early without the gate or the unit; inside
        // the unit the gate comes before the count that decides, the identity lookup and the write.
        new(Security + "FirstAdminBootstrapService.cs", "TryCreateFirstAdminAsync", Writes.UserCreatedWithRoleIds,
            ["GetUserCountAsync(", "Unit.ExecuteAsync(", TakeGateOnly, "GetUserCountAsync(", "GetAsync(", "GetByIdentityLinkAsync(", "new User(", "AddAsync(", "StageAsync("]),

        // The configured administrator: one unit per principal; the gate before the lookup that decides between creating a user
        // and assigning the role, and before either write.
        new(Security + "BootstrapPlatformAdminAssignment.cs", "ExecuteAsync", Writes.RoleAssign,
            ["unit.ExecuteAsync(", TakeGateOnly, "GetByIdentityLinkAsync(", "CreateAsync(", "GetForUserAsync(", ".AssignToUserAsync(", "StageAsync("]),
        new(Security + "BootstrapPlatformAdminAssignment.cs", "CreateAsync", Writes.UserCreatedWithRoleIds,
            ["new User("]),

        // The recovery restore: the gate before the target is read, then the assignment and its row.
        new(Security + "RealBootstrapRecoveryService.cs", "ExecuteRecoveryAsync", Writes.RoleAssign,
            ["ExecuteAsync<UserId?>(", TakeGateOnly, "ResolveTargetUserAsync(", "DeclareNoOp()", ".AssignToUserAsync(", "StageAsync("]),

        // WP4B-C, Recovery Access. A designation: the gate first; the account loaded (its id is used from then on); the no-op before
        // the invariants; the state before; the demotion and the designation through the one setter; the rename; the transition read
        // back; the decision last, after every write; a refusal through the one mapping; only then the rows.
        new(Security + "EmergencyAccessService.cs", "DesignateAsync", Writes.DesignationFlagUpdate,
            ["Unit.ExecuteRefusableAsync(", TakeGate, "GetByIdAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "SetDesignationAsync(", "RenameToCanonicalAsync(", "RequireDesignationTransitionAsync(", "VerifyDesignationAsync(", "ViabilityRefusalAudit.Blocked(", "StageAsync("]),

        // A strict revocation: the gate first; the designated account loaded; the state before; the one write; the transition read back;
        // the decision last; the decision checked to be about this revocation; a refusal through the one mapping; only then the row.
        new(Security + "EmergencyAccessService.cs", "RevokeAsync", Writes.DesignationFlagUpdate,
            ["Unit.ExecuteRefusableAsync(", TakeGate, "FindDesignatedEmergencyUserAsync(", "CaptureBeforeAsync(", "SetDesignationAsync(", "RequireRevocationTransitionAsync(", "VerifyRevocationAsync(", "RequireDecidedOnTheRevocation(", "ViabilityRefusalAudit.Blocked(", "StageAsync("]),

        // The one conditional setter both reducers write through: a stored value it did not expect is an invariant failure.
        new(Security + "EmergencyAccessService.cs", "SetDesignationAsync", Writes.DesignationFlagUpdate,
            ["TrySetDesignationAsync(", "AuditInvariantException"]),

        // Provisioning, a gated raiser: the gate first; the designation decided again under it; the new account; the state before; the
        // account and its credential; the assertion that nothing was reduced; the row.
        new(Security + "EmergencyAccessService.cs", "ProvisionAsync", Writes.UserCreatedWithRoleIds,
            ["Unit.ExecuteAsync(", TakeGate, "FindDesignatedEmergencyUserAsync(", "new User(", "CaptureBeforeForRaiseAsync(", "AddAsync(", ".SetPasswordAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // WP4B-C, native MFA. The bare reset, a reducer: the gate first; the account loaded; the no-op before the invariants; the state
        // before; the removal of the authenticator and its codes; the decision last; a refusal through the one mapping; the row.
        new(Security + "NativeMfaService.cs", "ResetAsync", Writes.MfaReset,
            ["Unit.ExecuteRefusableAsync(", TakeGate, "AccountOfAsync(", "DeclareNoOp()", "CaptureBeforeAsync(", "_totp.DeleteAsync(", "_recoveryCodes.DeleteAllAsync(", "VerifyBareResetAsync(", "ViabilityRefusalAudit.Blocked(", "StageAsync("]),

        // The first enrolment's completion, a gated raiser: the gate first; the account loaded; the code checked; the state before; the
        // conditional enrolment and the codes; the assertion; the rows.
        new(Security + "NativeMfaService.cs", "CompleteEnrollmentAsync", Writes.MfaEnrol,
            ["Unit.ExecuteAsync(", TakeGate, "AccountOfAsync(", "Rfc6238Totp.Verify(", "CaptureBeforeForRaiseAsync(", "_totp.MarkEnrolledAsync(", "_recoveryCodes.ReplaceAllAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // The regeneration of the codes, a gated raiser.
        new(Security + "NativeMfaService.cs", "RegenerateRecoveryCodesAsync", Writes.MfaEnrol,
            ["Unit.ExecuteAsync(", TakeGate, "AccountOfAsync(", "CaptureBeforeForRaiseAsync(", "_recoveryCodes.ReplaceAllAsync(", "AssertRaiseOnlyAsync(", "StageAsync("]),

        // The switch, gated and net-judged: the gate first; the account loaded; the state before; the switch (inside the unit, never
        // standalone); the decision last; a decision not made on the posture alone refused (I-R3-1); a posture refusal; only then the rows.
        new(Security + "NativeMfaService.cs", "CompleteReplacementAsync", Writes.MfaCandidateSwitch,
            ["Unit.ExecuteRefusableAsync(", TakeGate, "AccountOfAsync(", "CaptureBeforeAsync(", "Candidates.SwitchAsync(", "VerifyReplacementAsync(", "ViabilityDecisionBasis.PostureOnly", "ViabilityRefusalAudit.Blocked(", "decision.Refusal()", "StageAsync("]),
    ];

    private const string TakeGateOnly = ".EnterGateOnlyAsync(";

    private const string PrepareOptions = "Viability.PrepareAsync(";
    private const string DecideApplied = "DecideAppliedChangeAsync(";

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

                var inChecked = checkedMethods.Sum(method => WriteMatches(entry.Kind, MethodBody(code, method), code));
                inChecked.Should().Be(entry.Count, $"every {entry.Kind} write of {file} must be in a method this guard checks ({string.Join(", ", checkedMethods)})");
            }
        }
    }

    /// <summary>The writes of <paramref name="kind"/> in <paramref name="code"/> (one method), the receivers' names being those the whole file <paramref name="fileCode"/> declares.</summary>
    private static int WriteMatches(Writes kind, string code, string fileCode) => kind switch
    {
        Writes.RoleAssign => RoleAssignCall.Matches(code).Count,
        Writes.RoleRemove => RoleRemoveCall.Matches(code).Count,
        Writes.CredentialDisable => DisableCall.Matches(code).Count,
        Writes.LinkAdd => LinkAddCall.Matches(code).Count,
        Writes.LinkRemove => LinkRemoveCall.Matches(code).Count,
        Writes.RegistrationCreate => CallsOn(code, NamesHolding(fileCode, "IEntraInstanceRegistrationRepository"), "AddAsync"),
        Writes.RegistrationUpdate => CallsOn(code, NamesHolding(fileCode, "IEntraInstanceRegistrationRepository"), "UpdateAsync"),
        Writes.RegistrationDelete => CallsOn(code, NamesHolding(fileCode, "IEntraInstanceRegistrationRepository"), "DeleteAsync"),
        Writes.RegistrationActivate => RegistrationActivateCall.Matches(code).Count,
        Writes.PolicySave => CallsOn(code, NamesHolding(fileCode, "IInstanceAuthUiPolicyRepository"), "SaveAsync"),
        Writes.UserCreatedWithRoleIds => NewUser.Matches(code).Count(m => !IsEmpty(RoleIdsArgument(code, m.Index + m.Length))),
        Writes.DesignationFlagUpdate => DesignationSetterCall.Matches(code).Count,
        Writes.MfaReset => MfaResetCall.Matches(code).Count,
        Writes.MfaEnrol => MfaEnrolCall.Matches(code).Count,
        Writes.MfaCandidateSwitch => CallsOn(code, NamesHolding(fileCode, "INativeMfaReplacementCandidateRepository"), "SwitchAsync"),
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
    public void The_entra_primary_policy_has_no_decision_of_its_own_beside_the_shared_guard()
    {
        var code = StripComments(Source(Security + "EntraPrimaryAuthenticationPolicy.cs"));

        code.Should().NotContainAny(
            new[]
            {
                "ListUserIdsWithRoleAsync(", "CountOtherEntraIndependent", "IEmergencyAccessService", "GetPostureAsync(", "IRoleRepository",
                "CountUserAssignmentsAsync(", "HasViableAlternateSignIn", "EntraConfigurationState",
            },
            "the shared viability guard is the one definition of a viable and of an Entra-independent administrator");
        MethodBody(code, "ApplyAfterEntraProvenAsync").Should().Contain("VerifyAfterAsync(", "the verdict is the coordinator's scope applying the shared guard");
    }

    [Fact]
    public void The_entra_primary_policy_turns_only_a_busy_gate_into_an_outcome_and_the_display_takes_no_gate()
    {
        var code = StripComments(Source(Security + "EntraPrimaryAuthenticationPolicy.cs"));
        var apply = MethodBody(code, "ApplyAfterEntraProvenAsync");

        Regex.Matches(apply, @"\bcatch\b").Count.Should().Be(1, "no blanket catch may turn an infrastructure failure into a policy outcome");
        apply.Should().Contain("catch (PlatformAdministratorSetBusyException)");
        apply.Should().Contain("NotAppliedBusy");

        foreach (var method in new[] { "DescribeAsync", "WouldDeferAsync" })
            MethodBody(code, method).Should().NotContain("EnterAsync(", "a display only describes the state; it takes no gate and writes nothing");
        Regex.Matches(code, @"TryWriteAsync\(").Count.Should().Be(1, "an attempt row is written after the unit, once, by the one place that knows the verdict");
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

    // ---- F-1: the designation has one writer ----

    private const string UserRepositoryFile = "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlUserRepository.cs";

    [Fact]
    public void The_generic_user_update_never_writes_the_designation_and_checks_it_under_a_row_lock()
    {
        var code = StripComments(Source(UserRepositoryFile));

        foreach (var method in new[] { "UpdateAsync", "UpdateCoreAsync" })
        {
            R(@"IsBreakGlassEligible\s*=[^=]").IsMatch(MethodBody(code, method))
                .Should().BeFalse($"{method}: a copy of a user read earlier must not set or clear the designation");
        }

        var core = MethodBody(code, "UpdateCoreAsync");
        var lockedRead = core.IndexOf("ReadDesignationUnderLockAsync(", StringComparison.Ordinal);
        lockedRead.Should().BeGreaterThanOrEqualTo(0, "the stored designation is read under a row lock before anything is written");
        core.IndexOf("UserDesignationChangedException", StringComparison.Ordinal).Should().BeGreaterThan(lockedRead, "a different stored designation is refused");
        core.IndexOf("SaveChangesAsync(", StringComparison.Ordinal).Should().BeGreaterThan(lockedRead);
        MethodBody(code, "ReadDesignationUnderLockAsync").Should().Contain("UPDLOCK", "the check holds the row against a concurrent designation or revocation");
    }

    [Fact]
    public void The_conditional_designation_setter_has_one_production_caller_and_it_is_the_recovery_access_service()
    {
        var callers = ProductionSource()
            .Select(s => (s.Relative, Count: Regex.Matches(StripComments(s.Text), @"\.TrySetDesignationAsync\(", RegexOptions.CultureInvariant).Count))
            .Where(s => s.Count > 0)
            .ToList();

        callers.Should().BeEquivalentTo(
            [(Security + "EmergencyAccessService.cs", 1)],
            "the designation is written only by the Recovery Access service, in its own audited unit");

        var code = StripComments(Source(Security + "EmergencyAccessService.cs"));
        MethodBody(code, "SetDesignationAsync").Should().Contain("TrySetDesignationAsync(").And.Contain("AuditInvariantException");
        Regex.Matches(code, @"\bSetDesignationAsync\(", RegexOptions.CultureInvariant).Count
            .Should().Be(4, "the declaration, the demotion of the previous account, the designation and the revocation");

        var setter = MethodBody(StripComments(Source(UserRepositoryFile)), "TrySetDesignationAsync");
        setter.Should().Contain("e.IsBreakGlassEligible == expected", "the write is conditional on the stored value");
    }

    [Fact]
    public void The_account_services_no_longer_carry_the_designation_and_reach_the_update_only_through_their_mapping()
    {
        Inventory.Should().NotContain(i => i.Class == Class.Finding, "F-1 is resolved: the generic update cannot write the designation");

        foreach (var file in new[] { Security + "AccountContactService.cs", Security + "AccountInvitationService.cs", Security + "AccountProfileService.cs" })
        {
            var code = StripComments(Source(file));
            Regex.Matches(code, @"_users\.UpdateAsync\(", RegexOptions.CultureInvariant).Count
                .Should().Be(1, $"{file}: every user write goes through the one wrapper that maps a changed designation");
            code.Should().Contain("catch (UserDesignationChangedException");
            code.Should().NotContain("IsBreakGlassEligible =");
        }
    }

    /// <summary>
    /// A production file that decides a restriction on a copy of a user and then writes the user, a credential, a link or a name
    /// must be named here with the recheck it makes at the write. A new such file fails until it is added with its recheck.
    /// </summary>
    private static readonly string[] StaleWriterFiles =
    [
        Security + "AccountContactService.cs",
        Security + "AccountInvitationService.cs",
        Security + "AccountProfileService.cs",
        Security + "ForgottenPasswordService.cs",
        Security + "EntraIdentityAssociationService.cs",
        Security + "UserManagementService.cs", // under the global gate, which the designation and the revocation take too; set-password also rechecks under the row lock
        Api + "AuthController.cs",
        "Apperture.Core/Apperture.Core.Application/Services/UsernameService.cs",
    ];

    [Fact]
    public void Every_writer_that_checks_the_system_managed_restriction_on_a_copy_is_named_with_its_recheck()
    {
        var protectedWrite = R(@"\.(UpdateAsync|SetPasswordAsync|ChangeUsernameAsync|AddIdentityLinkAsync|CompleteEntraLinkProofAsync)\(|\bUpdateUserAsync\(");
        var found = ProductionSource()
            .Where(s => !s.Relative.StartsWith("Apperture.Persistence/", StringComparison.Ordinal))
            .Select(s => (s.Relative, Code: StripComments(s.Text)))
            .Where(s => s.Code.Contains("IsSystemManaged(", StringComparison.Ordinal) && protectedWrite.IsMatch(s.Code))
            .Select(s => s.Relative)
            .Where(f => !f.EndsWith("EmergencyAccessIdentity.cs", StringComparison.Ordinal))
            .ToList();

        found.Should().BeEquivalentTo(StaleWriterFiles, "a writer that checks a restriction on a copy must recheck the stored designation at its write");
    }

    [Fact]
    public void Each_stale_writer_rechecks_the_stored_designation_before_its_protected_write()
    {
        // Passwords and links are written through other repositories, so the recheck is the locked read of the user's designation,
        // made in the same unit, immediately before the write it protects.
        void Precedes(string file, string method, string recheck, string write)
        {
            var body = MethodBody(StripComments(Source(file)), method);
            var at = body.IndexOf(recheck, StringComparison.Ordinal);
            at.Should().BeGreaterThanOrEqualTo(0, $"{file} {method} must recheck with '{recheck}'");
            body.IndexOf(write, at, StringComparison.Ordinal).Should().BeGreaterThan(at, $"{file} {method}: '{write}' must come after the recheck");
        }

        Precedes(Security + "AccountInvitationService.cs", "CompletePasswordSetupAsync", "GetDesignationForUpdateAsync(", ".SetPasswordAsync(");
        Precedes(Security + "ForgottenPasswordService.cs", "CompleteAsync", "GetDesignationForUpdateAsync(", ".SetPasswordAsync(");
        Precedes(Security + "EntraIdentityAssociationService.cs", "CompleteLinkAsync", "GetDesignationForUpdateAsync(", ".AddIdentityLinkAsync(");
        Precedes(Security + "EntraIdentityAssociationService.cs", "CompleteLinkAsync", "GetDesignationForUpdateAsync(", ".CompleteEntraLinkProofAsync(");
        Precedes(Api + "AuthController.cs", "ChangeLocalPassword", "CheckPasswordChangeAsync(", ".SetPasswordAsync(");
        MethodBody(StripComments(Source(Api + "AuthController.cs")), "CheckPasswordChangeAsync").Should().Contain("GetDesignationForUpdateAsync(");
        Precedes(Security + "UserManagementService.cs", "SetLocalPasswordAsync", "GetDesignationForUpdateAsync(", ".SetPasswordAsync(");

        var username = StripComments(Source("Apperture.Core/Apperture.Core.Application/Services/UsernameService.cs"));
        MethodBody(username, "RenameWithResultAsync").Should().Contain("expectedDesignation: false").And.Contain("catch (UserDesignationChangedException");
        MethodBody(username, "ApplyRecoveryAccessIdentityAsync").Should().Contain("expectedDesignation: true");

        var repository = MethodBody(StripComments(Source(UserRepositoryFile)), "ChangeUsernameCoreAsync");
        var locked = repository.IndexOf("ReadDesignationUnderLockAsync(", StringComparison.Ordinal);
        locked.Should().BeGreaterThanOrEqualTo(0);
        repository.IndexOf("_db.Users.FirstOrDefaultAsync(", StringComparison.Ordinal).Should().BeGreaterThan(locked, "the rename holds the row before it reads it for update");
    }

    [Fact]
    public void The_stale_writer_recheck_takes_no_gate()
    {
        foreach (var file in StaleWriterFiles.Where(f => !f.EndsWith("UserManagementService.cs", StringComparison.Ordinal)))
        {
            StripComments(Source(file)).Should().NotContainAny(
                new[] { "IPlatformAdministratorSetGate", "IAdministratorViabilityCoordinator", "EnterAsync(", "EnterGateOnlyAsync(" },
                $"{file}: the recheck is a row lock of its own; an ordinary profile or password change does not queue on the administrator-set gate");
        }
    }

    [Fact]
    public void The_administrators_password_change_takes_the_gate_before_it_reads_the_user_and_rechecks_the_designation_before_the_write()
    {
        // L-P3: the ordinary administration path to a Local password decides the system-managed restriction on the user it loads. The gate
        // (which the designation and the revocation now take) comes before that read, and the stored designation is read again under the
        // user's row lock immediately before the credential write, which is named by the loaded row's id.
        var body = MethodBody(StripComments(Source(Security + "UserManagementService.cs")), "SetLocalPasswordAsync");
        var at = -1;
        foreach (var step in new[] { "Unit.ExecuteAsync(", TakeGate, "GetByIdAsync(", "IsSystemManaged(", "GetDesignationForUpdateAsync(user.Id", ".SetPasswordAsync(user.Id", "StageAsync(" })
        {
            var next = body.IndexOf(step, at + 1, StringComparison.Ordinal);
            next.Should().BeGreaterThan(at, $"SetLocalPasswordAsync must reach '{step}' after the step before it");
            at = next;
        }
    }

    [Fact]
    public void The_recovery_access_writers_name_the_account_by_the_row_they_loaded()
    {
        // D-1: after the load, an operation uses the id of the row it loaded, never the id it was called with: the database finds a user
        // whatever the case of the id, and the decision and the facts compare ids ordinally.
        var code = StripComments(Source(Security + "EmergencyAccessService.cs"));
        var designate = MethodBody(code, "DesignateAsync");
        designate.Should().Contain("var target = user.Id;");
        designate[designate.IndexOf("var target = user.Id;", StringComparison.Ordinal)..].Should().NotContain("userId", "after the load the caller's spelling of the id is not used");
        MethodBody(code, "RevokeAsync").Should().Contain("var revoked = designated.Id;").And.Contain("VerifyRevocationAsync(revoked.Value");
        var setPassword = MethodBody(code, "SetLocalPasswordAsync");
        setPassword.Should().Contain(".SetPasswordAsync(user.Id").And.Contain("AuditEvents.PasswordSet(user.Id)");
    }

    [Fact]
    public void The_replacement_holds_nothing_across_the_operators_input_and_switches_only_inside_the_unit()
    {
        // Staging and verifying a candidate take no gate, open no audited unit and read no viability state: nothing is held while the
        // operator adds the new authenticator and types its code. The only switch is inside the audited unit, after the gate (S-1).
        var code = StripComments(Source(Security + "NativeMfaService.cs"));
        foreach (var method in new[] { "BeginReplacementAsync", "VerifyReplacementAsync", "AbandonReplacementAsync" })
        {
            MethodBody(code, method).Should().NotContainAny(
                new[] { "EnterAsync(", "Unit.", "Viability", "CaptureBefore", "SwitchAsync(" },
                $"{method} holds nothing across the operator's input and never switches");
        }

        Regex.Matches(code, @"\bCandidates\.SwitchAsync\(", RegexOptions.CultureInvariant).Count.Should().Be(1, "one switch, in CompleteReplacementAsync");
        var complete = MethodBody(code, "CompleteReplacementAsync");
        complete.IndexOf("Candidates.SwitchAsync(", StringComparison.Ordinal)
            .Should().BeGreaterThan(complete.IndexOf("Unit.ExecuteRefusableAsync(", StringComparison.Ordinal), "the switch runs inside the audited unit");
        complete.Should().NotContain("catch (Exception", "nothing inside the switch's unit is caught and continued after (S-2)");
    }

    [Fact]
    public void The_native_mfa_administrative_operations_name_the_account_by_the_row_they_loaded()
    {
        // D-1: every administrative operation names the account by the id of the user row it loaded, from the one helper that loads it.
        var code = StripComments(Source(Security + "NativeMfaService.cs"));
        foreach (var method in new[] { "BeginEnrollmentAsync", "CompleteEnrollmentAsync", "RegenerateRecoveryCodesAsync", "ResetAsync", "BeginReplacementAsync", "VerifyReplacementAsync", "CompleteReplacementAsync", "AbandonReplacementAsync" })
            MethodBody(code, method).Should().Contain("AccountOfAsync(userId", $"{method} loads the account first");

        MethodBody(code, "AccountOfAsync").Should().Contain("Users.GetByIdAsync(").And.Contain(").Id");
        MethodBody(code, "ResetAsync").Should().Contain("VerifyBareResetAsync(owner.Value").And.NotContain("VerifyBareResetAsync(userId");
        MethodBody(code, "CompleteReplacementAsync").Should().Contain("VerifyReplacementAsync(owner.Value").And.Contain("codes.For(owner)");
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

    // ---- WP3: registrations and the sign-in policy ----

    [Fact]
    public void The_hand_built_configuration_simulation_is_gone_from_the_registration_controller()
    {
        var code = StripComments(Source(Api + "ConfigEntraTenantsController.cs"));

        code.Should().NotContainAny(
            new[]
            {
                "SimulateEffectiveEntraAfterUpdate", "CloneEntra(", "ReadLegacyEntraFromConfiguration", "ValidateDeleteActiveRegistration",
                "_localOptions", "ValidateNoLockout(_",
            },
            "the instance-level rule is asked of the persisted state the change produced, and viability by the one shared definition; a second simulation could disagree with the runtime");
    }

    [Fact]
    public void A_registration_change_is_decided_after_it_is_applied_and_before_anything_is_staged()
    {
        foreach (var method in new[] { "Patch", "Delete", "Activate" })
        {
            var body = MethodBody(StripComments(Source(Api + "ConfigEntraTenantsController.cs")), method);
            body.IndexOf(DecideApplied, StringComparison.Ordinal).Should().BeGreaterThan(0, method);
            body.IndexOf(DecideApplied, StringComparison.Ordinal)
                .Should().BeLessThan(body.IndexOf("StageAsync(", StringComparison.Ordinal), $"{method} must decide before it stages a row, because staging forbids a refusal");
        }
    }

    [Fact]
    public void The_sign_in_policy_writer_reads_no_viability_state_because_the_chooser_flags_cannot_reduce_it()
    {
        var body = MethodBody(StripComments(Source(Api + "ConfigAuthUiPolicyController.cs")), "Patch");

        body.Should().NotContainAny(new[] { "VerifyAfterAsync", "CaptureBefore", "AssertRaiseOnlyAsync", "ViabilityRefusal" });
        Inventory.Single(i => i.Kind == Writes.PolicySave).Class.Should().Be(Class.GatedRaiser);
    }

    [Fact]
    public void A_registration_writer_that_names_its_repository_differently_is_still_found()
    {
        const string source = """
            public sealed class Sneaky
            {
                private readonly IEntraInstanceRegistrationRepository _entraRepo;
                public Sneaky(IEntraInstanceRegistrationRepository entraRepo) { _entraRepo = entraRepo; }
                public Task A() => _entraRepo.AddAsync(null!);
                public Task B() => _entraRepo.DeleteAsync("x");
            }
            """;
        var found = Scan([("Apperture.ControlPlane/Sneaky.cs", source)]);

        found[("Apperture.ControlPlane/Sneaky.cs", Writes.RegistrationCreate)].Should().Be(1);
        found[("Apperture.ControlPlane/Sneaky.cs", Writes.RegistrationDelete)].Should().Be(1);
        Differences(found, Inventory).Should().Contain(d => d.StartsWith("UNCLASSIFIED WRITER") && d.Contains("Sneaky.cs"));
    }

    [Fact]
    public void A_reader_of_registrations_is_not_a_writer()
    {
        const string source = """
            public sealed class Reader
            {
                private readonly IEntraInstanceRegistrationRepository _registrations;
                public Task<object?> A() => _registrations.GetActiveAsync();
                public Task B() => _registrations.UpdateOpenIdValidationOutcomeAsync("x");
            }
            """;

        Scan([("Apperture.ControlPlane/Reader.cs", source)]).Should().BeEmpty();
    }

    [Fact]
    public void Nothing_outside_the_persistence_layer_reads_a_registration_or_policy_table_directly()
    {
        var direct = R(@"\.(EntraInstanceRegistrations|InstanceAuthUiPolicies|EntraRegistrationDiagnostics)\b");
        const string secretInventory = "Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationSettingSecretInventory.cs";

        var users = ProductionSource()
            .Where(s => !s.Relative.StartsWith("Apperture.Persistence/Apperture.Persistence.SqlServer/", StringComparison.Ordinal))
            .Where(s => direct.IsMatch(StripComments(s.Text)))
            .ToList();

        // The one reader outside the persistence layer is the secret inventory, which projects the secret references to find
        // secrets nothing names any more. It reads; it writes nothing.
        users.Select(s => s.Relative).Should().BeEquivalentTo([secretInventory], "a registration or the policy is written through its repository, which the inventory counts");
        StripComments(users.Single().Text).Should().NotContainAny(
            new[] { "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSqlRaw", "_db.Add", "_db.Remove", "_db.Update", "EntityState.", "_db.Entry(" },
            "the inventory is read-only");
    }

    [Fact]
    public void The_registration_and_policy_inventory_entries_are_all_integrated_in_wp3()
    {
        var wp3 = Inventory.Where(i => i.Kind is Writes.RegistrationCreate or Writes.RegistrationUpdate or Writes.RegistrationDelete or Writes.RegistrationActivate or Writes.PolicySave).ToList();

        wp3.Should().HaveCount(5);
        wp3.Should().OnlyContain(i => i.Integrated, "WP3 integrated every registration and policy writer");
    }

    // ---- Gated, cannot reduce: the closed set of gate-only callers ----

    /// <summary>
    /// The methods that take the gate without reading viability state (<c>EnterGateOnlyAsync</c>): a writer proven unable to reduce, such
    /// as one that only creates an account holding no administrator facts yet. The set is closed. Moving a writer here is a decision
    /// of the package that integrates it, made by adding it with its reason; nothing else may call the entry.
    /// </summary>
    /// <remarks>
    /// WP4B-D moved the three system bootstrap writers here: the first administrator, the configured administrator and the bootstrap
    /// recovery restore. Each creates an account that holds no administrator facts or gives the administrator role to an account that
    /// does not hold it, so none can reduce; Recovery Access provisioning is a gated raiser of WP4B-C and is not in this set.
    /// </remarks>
    private static readonly IReadOnlyList<(string File, string Method)> GateOnlyCallers =
    [
        (Security + "FirstAdminBootstrapService.cs", "TryCreateFirstAdminAsync"),
        (Security + "BootstrapPlatformAdminAssignment.cs", "ExecuteAsync"),
        (Security + "RealBootstrapRecoveryService.cs", "ExecuteRecoveryAsync"),
    ];

    private const string GateOnlyEntry = ".EnterGateOnlyAsync(";

    /// <summary>What a gate-only method may not do, and must do: the pin of the class.</summary>
    private static List<string> GateOnlyViolations(string rawMethodBody)
    {
        var violations = new List<string>();
        var code = StripComments(rawMethodBody);
        if (!code.Contains(GateOnlyEntry, StringComparison.Ordinal))
            violations.Add("does not take the gate through EnterGateOnlyAsync");

        foreach (var read in new[]
                 {
                     "CaptureBeforeAsync(", "CaptureBeforeForRaiseAsync(", "VerifyAfterAsync(", "AssertRaiseOnlyAsync(", "PrepareAsync(",
                     "VerifyDesignationAsync(", "VerifyRevocationAsync(", "VerifyBareResetAsync(", "VerifyReplacementAsync(",
                     ".EnterAsync(", "IAdministratorViabilityService", "IDeploymentAuthenticationConfiguration", "ViabilityRefusal",
                 })
        {
            if (code.Contains(read, StringComparison.Ordinal))
                violations.Add($"reads or judges viability through {read}");
        }

        if (!Regex.IsMatch(rawMethodBody, @"^[ \t]*//[ \t]*Cannot reduce:[ \t]*\S", RegexOptions.Multiline | RegexOptions.CultureInvariant))
            violations.Add("does not say in a comment (\"Cannot reduce: ...\") why it cannot reduce");
        return violations;
    }

    [Fact]
    public void Only_the_closed_set_of_gate_only_callers_takes_the_gate_without_reading_viability()
    {
        var callers = ProductionSource()
            .Where(s => !s.Relative.EndsWith("AdministratorViability/AdministratorViabilityCoordinator.cs", StringComparison.Ordinal))
            .Where(s => StripComments(s.Text).Contains(GateOnlyEntry, StringComparison.Ordinal))
            .Select(s => s.Relative)
            .Distinct()
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        callers.Should().BeEquivalentTo(GateOnlyCallers.Select(c => c.File).Distinct(), "the gate-only entry is closed to the writers this guard names");

        foreach (var (file, method) in GateOnlyCallers)
        {
            GateOnlyViolations(MethodBody(Source(file), method)).Should().BeEmpty($"{file} {method}");
            Inventory.Where(i => i.File == file).Should().NotContain(i => i.Class == Class.Reducer, $"{file} holds a reducer, so it cannot be gate-only");
        }
    }

    [Fact]
    public void The_gate_only_pin_fails_a_writer_that_reads_viability_skips_the_entry_or_gives_no_reason()
    {
        const string good = """
            private async Task A()
            {
                // Cannot reduce: the account is new and holds no administrator role or credential before this change.
                await Viability.EnterGateOnlyAsync(ct);
                await _users.AddAsync(user, ct);
            }
            """;
        GateOnlyViolations(good).Should().BeEmpty();

        GateOnlyViolations(good.Replace("EnterGateOnlyAsync(", "EnterAsync(", StringComparison.Ordinal))
            .Should().Contain(v => v.Contains("EnterGateOnlyAsync")).And.Contain(v => v.Contains(".EnterAsync("));
        GateOnlyViolations(good.Replace("await _users", "await scope.VerifyAfterAsync(ct); await _users", StringComparison.Ordinal))
            .Should().Contain(v => v.Contains("VerifyAfterAsync("));
        GateOnlyViolations(good.Replace("await _users", "await Viability.PrepareAsync(ct); await _users", StringComparison.Ordinal))
            .Should().Contain(v => v.Contains("PrepareAsync("));
        GateOnlyViolations(good.Replace("Cannot reduce:", "Fine:", StringComparison.Ordinal))
            .Should().Contain(v => v.Contains("why it cannot reduce"));
        GateOnlyViolations(good.Replace("// Cannot reduce: the account is new and holds no administrator role or credential before this change.", "// Cannot reduce:", StringComparison.Ordinal))
            .Should().Contain(v => v.Contains("why it cannot reduce"), "an empty reason is no reason");
    }

    [Fact]
    public void The_first_administrator_answers_early_outside_the_unit_and_the_gate_and_decides_again_under_the_gate()
    {
        var body = MethodBody(StripComments(Source(Security + "FirstAdminBootstrapService.cs")), "TryCreateFirstAdminAsync");
        var unit = body.IndexOf("Unit.ExecuteAsync(", StringComparison.Ordinal);
        unit.Should().BeGreaterThan(0);

        var early = body[..unit];
        early.Should().Contain("GetUserCountAsync(").And.Contain("AlreadyExists", "an installation that is already set up is answered with one read");
        early.Should().NotContain("Viability", "an anonymous caller must not queue on the global gate once a user exists");

        var inUnit = body[unit..];
        inUnit.IndexOf(GateOnlyEntry, StringComparison.Ordinal).Should().BeGreaterThan(0);
        inUnit.IndexOf(GateOnlyEntry, StringComparison.Ordinal).Should().BeLessThan(inUnit.IndexOf("GetUserCountAsync(", StringComparison.Ordinal),
            "the count that decides is read only after the gate is held");
    }

    [Fact]
    public void The_gate_only_scope_offers_no_reading_and_the_coordinator_entry_reads_nothing()
    {
        var scope = typeof(Apperture.ControlPlane.Security.AdministratorViability.AdministratorGateOnlyScope);
        scope.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Should().BeEmpty("a scope that offered a capture or a verdict could be used to judge a reduction");

        var entry = MethodBody(StripComments(Source(Security + "AdministratorViability/AdministratorViabilityCoordinator.cs")), "EnterGateOnlyAsync");
        entry.Should().Contain("_gate.AcquireAsync(");
        entry.Should().NotContainAny(new[] { "_reads", "_deployment", "ReadAsync(", "EnsureInitialized(" }, "no deployment configuration is read, so no Entra options are built");
    }

    // ---- Dormant interfaces stay dormant ----

    [Fact]
    public void The_bootstrap_recovery_service_has_one_caller_and_is_registered_only_as_the_stub_in_the_command_line()
    {
        var users = ProductionSource()
            .Where(s => StripComments(s.Text).Contains("IBootstrapRecoveryService", StringComparison.Ordinal))
            .Select(s => s.Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        users.Should().BeEquivalentTo(
            [
                "Apperture.Bootstrap/Apperture.Bootstrap/IBootstrapRecoveryService.cs",
                "Apperture.Bootstrap/Apperture.Bootstrap/ServiceCollectionExtensions.cs",
                "Apperture.Bootstrap/Apperture.Bootstrap/Stubs/StubBootstrapRecoveryService.cs",
                "Apperture.Cli/Apperture.Cli.Bootstrap/BootstrapCliService.cs",
                "Apperture.ControlPlane/Apperture.ControlPlane.Security/RealBootstrapRecoveryService.cs",
                "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs",
            ],
            "the real recovery service is registered in the host and has no production caller; integrating its role assignment adds none");

        ProductionSource()
            .Where(s => StripComments(s.Text).Contains("_recoveryService.", StringComparison.Ordinal))
            .Select(s => s.Relative)
            .Should().BeEquivalentTo(["Apperture.Cli/Apperture.Cli.Bootstrap/BootstrapCliService.cs"], "only the bootstrap command line calls the recovery service");

        ProductionSource()
            .Where(s => StripComments(s.Text).Contains("AddAppertureRecovery(", StringComparison.Ordinal))
            .Select(s => s.Relative)
            .Should().BeEquivalentTo(
                [
                    "Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs",
                    "Apperture.ControlPlane/Apperture.Management.Host/Program.cs",
                ],
                "the command line composition never registers the real recovery service");
    }
}
