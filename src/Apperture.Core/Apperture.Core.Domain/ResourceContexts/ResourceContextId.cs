using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// Typed identifier for a ResourceContext.
/// </summary>
/// <remarks>
/// A ResourceContext identifier is an <b>ownership / namespace</b> value. It is not an authorization
/// boundary and must never be confused with <c>ScopeKey</c> (see <see cref="Apperture.Shared.Kernel.ScopeKeyConstants"/>),
/// which is the authorization concept. See <c>ResourceContextId = ownership / namespace</c> versus
/// <c>ScopeKey = authorization</c>.
/// </remarks>
public sealed record ResourceContextId(string Value) : EntityId(Value);
