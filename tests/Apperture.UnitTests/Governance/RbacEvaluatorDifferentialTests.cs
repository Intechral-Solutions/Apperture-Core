using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.UnitTests.Governance.AuthorizationEquivalenceFixtures;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A10.2.1: differential equivalence. The evaluator before the held-set refactor (a frozen copy, the oracle) and the
/// refactored evaluator are run side by side over generated role shapes, questions, target scopes and actors, and must
/// give the same answer, fail the same way, and make the same calls to their collaborators. The same inputs are run
/// against <see cref="EffectivePermissionSet"/> used directly, and against deliberately broken copies of the oracle to
/// prove that the inputs would notice a change to any element of the rule.
/// </summary>
public sealed class RbacEvaluatorDifferentialTests
{
    private readonly ITestOutputHelper _output;

    public RbacEvaluatorDifferentialTests(ITestOutputHelper output) => _output = output;

    // ---------------------------------------------------------------------------------------------------------------
    // The comparison machinery
    // ---------------------------------------------------------------------------------------------------------------

    private sealed record Outcome(bool? Allowed, string? Failure, int ResolveCalls, int RoleLookups)
    {
        public override string ToString() =>
            Failure is null ? $"{(Allowed == true ? "ALLOW" : "DENY")} (resolve x{ResolveCalls}, roles x{RoleLookups})" : $"THROWS {Failure} (resolve x{ResolveCalls}, roles x{RoleLookups})";
    }

    private sealed class RecordingResolver : IPrincipalToUserResolver
    {
        public bool Resolves { get; set; }
        public int Calls { get; set; }

        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<UserId?>(Resolves ? new UserId(UserKey) : null);
        }
    }

    private sealed class RecordingRoleProvider : IRoleProvider
    {
        public IReadOnlyList<Role> Roles { get; set; } = [];
        public Exception? LoaderFailure { get; set; }
        public int Calls { get; set; }

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return LoaderFailure is null ? ValueTask.FromResult(Roles) : throw LoaderFailure;
        }

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The roles of one scenario as the repository would hand them out, or the exception it would throw reading them.</summary>
    private sealed record BuiltRoles(string Description, IReadOnlyList<Role> Roles, Exception? LoaderFailure)
    {
        public static BuiltRoles From(IReadOnlyList<RoleSpec> specs)
        {
            var roles = new List<Role>();
            foreach (var spec in specs)
            {
                var role = spec.TryBuild(out var failure);
                if (failure is not null)
                    return new BuiltRoles(string.Join(" + ", specs.Select(s => s.Name)), [], failure);
                roles.Add(role!);
            }

            return new BuiltRoles(string.Join(" + ", specs.Select(s => s.Name)), roles, null);
        }
    }

    private delegate IAuthorizationPolicyEvaluator Subject(
        IPrincipalToUserResolver resolver, IRoleProvider roles, IScopeContainmentEvaluator scope);

    private static readonly Subject Oracle = (r, p, s) => new LegacyRbacEvaluatorReference(r, p, s);
    private static readonly Subject Refactored = (r, p, s) => new RbacAuthorizationPolicyEvaluator(r, p, s);
    private static Subject Mutant(EvaluatorMutation mutation) => (r, p, s) => new LegacyRbacEvaluatorReference(r, p, s, mutation);

    /// <summary>
    /// Evaluates the held set directly, which is only defined for a resolved human without the session marker: it reads the
    /// roles and asks the set, with none of the evaluator's actor handling around it.
    /// </summary>
    private sealed class DirectSetSubject : IAuthorizationPolicyEvaluator
    {
        private readonly IRoleProvider _roles;
        private readonly IScopeContainmentEvaluator _scope;

        public DirectSetSubject(IRoleProvider roles, IScopeContainmentEvaluator scope)
        {
            _roles = roles;
            _scope = scope;
        }

        public async ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource, IGovernedResourceIdentity? resourceIdentity, CancellationToken cancellationToken = default)
        {
            var roles = await _roles.GetRolesForUserAsync(new UserId(UserKey), cancellationToken);
            return EffectivePermissionSet.FromRoles(roles, _scope).Allows(permission, resource, resourceIdentity);
        }
    }

    private static readonly Subject DirectSet = (r, p, s) => new DirectSetSubject(p, s);

    /// <summary>Reusable collaborators for one subject, so a large run does not allocate stubs per question.</summary>
    private sealed class Rig
    {
        public RecordingResolver Resolver { get; } = new();
        public RecordingRoleProvider Roles { get; } = new();
        public IAuthorizationPolicyEvaluator Evaluator { get; }

        public Rig(Subject subject) => Evaluator = subject(Resolver, Roles, new ScopeContainmentEvaluator());

        public Outcome Run(ActorSpec actor, BuiltRoles roles, PermissionDescriptor permission, TargetSpec target, bool recordCalls)
        {
            Resolver.Resolves = actor.Resolves;
            Resolver.Calls = 0;
            Roles.Roles = roles.Roles;
            Roles.LoaderFailure = roles.LoaderFailure;
            Roles.Calls = 0;

            bool? allowed = null;
            string? failure = null;
            try
            {
                allowed = Evaluator.EvaluateAsync(actor.Actor!, permission, target.Resource, target.Identity).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex.GetType().FullName;
            }

            return recordCalls ? new Outcome(allowed, failure, Resolver.Calls, Roles.Calls) : new Outcome(allowed, failure, 0, 0);
        }
    }

    private sealed class Comparison
    {
        private readonly List<string> _mismatches = [];
        private readonly int _stopAfter;

        public Comparison(int stopAfter = int.MaxValue) => _stopAfter = stopAfter;

        public bool Done => _mismatches.Count >= _stopAfter;
        public int Scenarios { get; private set; }
        public int Allowed { get; private set; }
        public int Denied { get; private set; }
        public int Threw { get; private set; }
        public int Mismatches => _mismatches.Count;
        public IReadOnlyList<string> First(int count) => _mismatches.Take(count).ToList();

        public void Record(string scenario, Outcome expected, Outcome actual)
        {
            Scenarios++;
            if (expected.Failure is not null) Threw++;
            else if (expected.Allowed == true) Allowed++;
            else Denied++;

            if (expected != actual)
                _mismatches.Add($"{scenario}\n      oracle:  {expected}\n      subject: {actual}");
        }
    }

    private static void Compare(
        Comparison comparison, Rig oracle, Rig subject, ActorSpec actor, BuiltRoles roles, PermissionDescriptor permission, TargetSpec target, bool recordCalls)
    {
        var expected = oracle.Run(actor, roles, permission, target, recordCalls);
        var actual = subject.Run(actor, roles, permission, target, recordCalls);
        comparison.Record(
            $"actor={actor.Name}; roles=[{roles.Description}]; ask={permission.Action}@{permission.Resource?.ResourceType}:{permission.Resource?.ScopeKey}; target={target.Name}",
            expected,
            actual);
    }

    private void Report(string title, Comparison comparison) =>
        _output.WriteLine($"{title}: {comparison.Scenarios:N0} scenarios, {comparison.Allowed:N0} allowed, {comparison.Denied:N0} denied, {comparison.Threw:N0} threw, {comparison.Mismatches:N0} differences");

    private static void AssertEquivalent(Comparison comparison) =>
        comparison.Mismatches.Should().Be(0, "the subject must agree with the oracle everywhere; first differences:\n{0}", string.Join("\n", comparison.First(8)));

    private static readonly ActorSpec ResolvedHuman = Actors.Single(a => a.Name == "human-resolved");

    // ---------------------------------------------------------------------------------------------------------------
    // The generated inputs, as three runs
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Every single-role shape asked every question against every target scope source.</summary>
    private static Comparison RunExhaustiveSingleRole(Subject subject, bool recordCalls = true, int stopAfter = int.MaxValue)
    {
        var comparison = new Comparison(stopAfter);
        var oracle = new Rig(Oracle);
        var rig = new Rig(subject);
        foreach (var spec in SingleRoleShapes())
        {
            var roles = BuiltRoles.From([spec]);
            foreach (var permission in RequiredPermissions)
                foreach (var target in Targets)
                {
                    if (comparison.Done)
                        return comparison;
                    Compare(comparison, oracle, rig, ResolvedHuman, roles, permission, target, recordCalls);
                }
        }

        return comparison;
    }

    /// <summary>Every actor and resolution state against representative role sets and the questions that matter.</summary>
    private static Comparison RunActorMatrix(Subject subject, int stopAfter = int.MaxValue)
    {
        var comparison = new Comparison(stopAfter);
        var oracle = new Rig(Oracle);
        var rig = new Rig(subject);

        var roleSets = new List<IReadOnlyList<RoleSpec>>
        {
            Array.Empty<RoleSpec>(),
            new[] { new RoleSpec("admin-empty", AdminRoleId, Array.Empty<PermissionDescriptor>()) },
            new[] { new RoleSpec("admin-unreadable", AdminRoleId, null, "{not json") },
            new[] { new RoleSpec("role-manage-only", "role-custom-a", [new PermissionDescriptor("role.manage", new ResourceDescriptor("Role", "instance"))]) },
            new[] { new RoleSpec("holds-connection-read", "role-custom-a", [new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "instance"))]) },
            new[] { new RoleSpec("loader-throws", "role-custom-a", null, "[null]") },
            new[]
            {
                new RoleSpec("admin-empty", AdminRoleId, Array.Empty<PermissionDescriptor>()),
                new RoleSpec("holds-connection-read", "role-custom-a", [new PermissionDescriptor("connection.read", new ResourceDescriptor("Connection", "instance"))]),
            },
        };

        var questions = RequiredPermissions
            .Where(p => p.Resource is { ScopeKey: "instance" } && (p.Action?.StartsWith("user.", StringComparison.Ordinal) == true || p.Action is "connection.read" or "role.manage" or "role.assign" or "workflow.execute"))
            .ToList();
        var targets = Targets.Where(t => t.Name is "neither" or "resource:instance" or "identity:module:X" or "resource:module:X:slot:Y").ToList();

        foreach (var actor in Actors)
            foreach (var specs in roleSets)
            {
                var roles = BuiltRoles.From(specs);
                foreach (var permission in questions)
                    foreach (var target in targets)
                    {
                        if (comparison.Done)
                            return comparison;
                        Compare(comparison, oracle, rig, actor, roles, permission, target, recordCalls: true);
                    }
            }

        return comparison;
    }

    /// <summary>Reproducible random role sets (0 to 4 roles, mixed ids, grants and stored JSON), questions, targets and actors.</summary>
    private static Comparison RunRandom(Subject subject, int seed, int iterations, bool humanOnlyResolved = false, int stopAfter = int.MaxValue)
    {
        var random = new Random(seed);
        var comparison = new Comparison(stopAfter);
        var oracle = new Rig(Oracle);
        var rig = new Rig(subject);
        for (var i = 0; i < iterations && !comparison.Done; i++)
        {
            var roles = BuiltRoles.From(RandomRoles(random));
            var permission = RequiredPermissions[random.Next(RequiredPermissions.Count)];
            var target = Targets[random.Next(Targets.Count)];
            var actor = humanOnlyResolved || random.Next(5) != 0 ? ResolvedHuman : Actors[random.Next(Actors.Count)];
            Compare(comparison, oracle, rig, actor, roles, permission, target, recordCalls: !humanOnlyResolved);
        }

        return comparison;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.1: the refactored evaluator against the oracle
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Refactored_evaluator_matches_the_oracle_for_every_single_role_shape_question_and_target()
    {
        var comparison = RunExhaustiveSingleRole(Refactored);
        Report("single-role exhaustive", comparison);

        AssertEquivalent(comparison);
        comparison.Allowed.Should().BeGreaterThan(1000, "the inputs must reach allow decisions through floor, grants and the implication");
        comparison.Denied.Should().BeGreaterThan(1000);
        comparison.Threw.Should().BeGreaterThan(0, "role data the loader cannot read must be part of the inputs");
    }

    [Fact]
    public void Refactored_evaluator_matches_the_oracle_for_every_actor_kind_and_resolution_state()
    {
        var comparison = RunActorMatrix(Refactored);
        Report("actor matrix", comparison);

        AssertEquivalent(comparison);
        comparison.Allowed.Should().BeGreaterThan(100);
        comparison.Denied.Should().BeGreaterThan(100);
        comparison.Threw.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(20261016, 40_000)]
    [InlineData(1, 20_000)]
    [InlineData(987654321, 20_000)]
    public void Refactored_evaluator_matches_the_oracle_for_random_multi_role_sets(int seed, int iterations)
    {
        var comparison = RunRandom(Refactored, seed, iterations);
        Report($"random seed {seed}", comparison);

        AssertEquivalent(comparison);
        comparison.Allowed.Should().BeGreaterThan(iterations / 50);
        comparison.Denied.Should().BeGreaterThan(iterations / 50);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.1: EffectivePermissionSet.Allows directly against the oracle
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Held_set_matches_the_oracle_for_every_single_role_shape_question_and_target()
    {
        var comparison = RunExhaustiveSingleRole(DirectSet, recordCalls: false);
        Report("held set, single-role exhaustive", comparison);

        AssertEquivalent(comparison);
    }

    [Fact]
    public void Held_set_matches_the_oracle_for_random_multi_role_sets()
    {
        var comparison = RunRandom(DirectSet, seed: 424242, iterations: 40_000, humanOnlyResolved: true);
        Report("held set, random", comparison);

        AssertEquivalent(comparison);
    }

    /// <summary>A grantor holds G exactly when the evaluator, asked for G as its own resource, allows it.</summary>
    [Fact]
    public void Holds_equals_the_oracle_evaluating_the_grant_as_its_own_resource()
    {
        var scope = new ScopeContainmentEvaluator();
        var oracle = new Rig(Oracle);
        var candidates = GrantPool.Concat(RequiredPermissions).Where(p => p.Action is not null).Distinct().ToList();
        var mismatches = new List<string>();
        var holds = 0;
        var total = 0;

        foreach (var spec in SingleRoleShapes())
        {
            var roles = BuiltRoles.From([spec]);
            if (roles.LoaderFailure is not null)
                continue;

            var set = EffectivePermissionSet.FromRoles(roles.Roles, scope);
            foreach (var grant in candidates)
            {
                var expected = oracle.Run(ResolvedHuman, roles, grant, new TargetSpec("grant-as-resource", grant.Resource, null), recordCalls: false);
                bool? actual;
                try { actual = set.Holds(grant); }
                catch (Exception ex) { actual = null; _ = ex; }

                total++;
                if (actual == true) holds++;
                if (expected.Allowed != actual)
                    mismatches.Add($"roles=[{spec.Name}] grant={grant.Action}@{grant.Resource?.ResourceType}:{grant.Resource?.ScopeKey} oracle={expected} holds={actual}");
            }
        }

        _output.WriteLine($"holds: {total:N0} questions, {holds:N0} held, {mismatches.Count:N0} differences");
        mismatches.Should().BeEmpty("Holds must be the evaluator called with the grant as its own resource; first differences:\n{0}", string.Join("\n", mismatches.Take(8)));
        holds.Should().BeGreaterThan(500);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The inputs are sensitive: every deliberately broken oracle is told apart from the real one
    // ---------------------------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> Mutations() =>
        Enum.GetValues<EvaluatorMutation>().Where(m => m != EvaluatorMutation.None).Select(m => new object[] { m });

    [Theory]
    [MemberData(nameof(Mutations))]
    public void The_inputs_detect_a_change_to_each_element_of_the_rule(EvaluatorMutation mutation)
    {
        // Each of the three input families is run on its own, so the output also shows which of them notices the defect.
        var exhaustive = RunExhaustiveSingleRole(Mutant(mutation), stopAfter: 1);
        var actors = RunActorMatrix(Mutant(mutation), stopAfter: 1);
        var random = RunRandom(Mutant(mutation), seed: 20261016, iterations: 20_000, stopAfter: 1);

        _output.WriteLine($"{mutation}: noticed by single-role={exhaustive.Mismatches > 0}, actor-matrix={actors.Mismatches > 0}, random={random.Mismatches > 0}");

        (exhaustive.Mismatches + actors.Mismatches + random.Mismatches).Should().BeGreaterThan(
            0, "a defect '{0}' in the evaluator must make the differential tests fail", mutation);
    }

    [Fact]
    public void The_unmutated_oracle_is_told_apart_from_nothing()
    {
        // Control for the sensitivity test: the oracle against a second instance of itself shows no difference.
        var comparison = RunExhaustiveSingleRole(Oracle);
        comparison.Mismatches.Should().Be(0);
    }
}
