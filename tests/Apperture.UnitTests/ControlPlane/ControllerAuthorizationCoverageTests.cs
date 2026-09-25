using System.Reflection;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// SEC-1A1 guard (design gate §23 guard 2). The client's "a 401 proves the mutation did not run, so it is safe to
/// return the operator to the page and never replay" rests on authorization being evaluated BEFORE any action body:
/// a class-level <c>[Authorize]</c> rejects in the authorization middleware. This pins that every controller either
/// carries it, or is on a short, explicit list of controllers that declare authorization per action, and that on
/// that list no action is left to be anonymous by omission.
/// </summary>
public sealed class ControllerAuthorizationCoverageTests
{
    /// <summary>
    /// Controllers with no class-level <c>[Authorize]</c>. Each is either wholly anonymous by declaration or declares
    /// authorization on every action. Adding to this list is a security decision: the browser-recovery contract and
    /// the request-forgery slice both depend on it.
    /// </summary>
    private static readonly string[] ControllersWithoutClassLevelAuthorize =
    [
        nameof(AuthController),
        nameof(BootstrapController),
    ];

    private static IReadOnlyList<Type> Controllers() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic)
            .OrderBy(t => t.Name)
            .ToArray();

    private static bool HasClassLevel<T>(Type type) where T : Attribute => type.GetCustomAttribute<T>(inherit: true) is not null;

    private static IEnumerable<MethodInfo> Actions(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

    [Fact]
    public void Every_controller_has_a_class_level_Authorize_or_is_on_the_explicit_list()
    {
        var unprotected = Controllers()
            .Where(t => !HasClassLevel<AuthorizeAttribute>(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();

        unprotected.Should().BeEquivalentTo(
            ControllersWithoutClassLevelAuthorize,
            "a controller without a class-level [Authorize] would run its actions for an unauthenticated caller unless every action says otherwise");
    }

    [Fact]
    public void Controllers_on_the_list_leave_no_action_anonymous_by_omission()
    {
        var offenders = new List<string>();
        foreach (var type in Controllers().Where(t => ControllersWithoutClassLevelAuthorize.Contains(t.Name)))
        {
            var classAnonymous = HasClassLevel<AllowAnonymousAttribute>(type);
            foreach (var action in Actions(type))
            {
                var declared = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                    || action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
                if (!classAnonymous && !declared)
                    offenders.Add($"{type.Name}.{action.Name}");
            }
        }

        offenders.Should().BeEmpty("each action must state [Authorize] or [AllowAnonymous]; silence means anonymous");
    }

    [Fact]
    public void Every_unsafe_action_outside_the_list_is_covered_by_the_class_level_Authorize()
    {
        // The recovery contract is about mutations: POST/PUT/PATCH/DELETE. Anonymous mutations (login, password reset,
        // enrollment, bootstrap, access-request sign-out) are the ones that may legitimately not be authorized.
        var anonymousMutations = new List<string>();
        foreach (var type in Controllers().Where(t => HasClassLevel<AuthorizeAttribute>(t)))
        {
            foreach (var action in Actions(type).Where(IsUnsafe))
            {
                if (action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
                    anonymousMutations.Add($"{type.Name}.{action.Name}");
            }
        }

        // Pinned so that making a mutation anonymous is a visible decision. These are the approved browser-facing or
        // machine-facing anonymous mutations (design gate §18.2): access-request sign-out, runtime enrollment.
        anonymousMutations.Should().BeEquivalentTo(
        [
            "AccessRequestController.SignOutSession",
            "EnrollController.Enroll",
        ]);
    }

    private static bool IsUnsafe(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
            .SelectMany(a => a.HttpMethods)
            .Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE");
}
