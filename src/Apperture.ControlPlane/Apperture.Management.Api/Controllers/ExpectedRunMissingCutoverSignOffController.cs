using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ExpectedRunMissingCutoverSignOffController : ControllerBase
{
    private readonly IExpectedRunMissingCutoverSignOffService _signOffs;
    private readonly IExpectedRunMissingCutoverValidationService _validation;

    public ExpectedRunMissingCutoverSignOffController(
        IExpectedRunMissingCutoverSignOffService signOffs,
        IExpectedRunMissingCutoverValidationService validation)
    {
        _signOffs = signOffs;
        _validation = validation;
    }

    [HttpGet("api/integrations/{integrationId}/expected-run-missing/cutover-validation")]
    [ProducesResponseType(typeof(ExpectedRunMissingCutoverValidationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExpectedRunMissingCutoverValidationDto>> GetValidation(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _validation.ValidateAsync(integrationId, cancellationToken).ConfigureAwait(false);
            return Ok(ToValidationDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("api/integrations/{integrationId}/expected-run-missing/cutover-sign-off")]
    [ProducesResponseType(typeof(ExpectedRunMissingCutoverSignOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpectedRunMissingCutoverSignOffDto>> GetLatest(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _signOffs.GetLatestAsync(integrationId, cancellationToken).ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("api/integrations/{integrationId}/expected-run-missing/cutover-sign-off")]
    [ProducesResponseType(typeof(ExpectedRunMissingCutoverSignOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExpectedRunMissingCutoverSignOffDto>> CreateOrReplace(
        string integrationId,
        [FromBody] CreateExpectedRunMissingCutoverSignOffRequestDto body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _signOffs
                .CreateOrReplaceSignOffAsync(
                    integrationId,
                    new CreateExpectedRunMissingCutoverSignOffRequest(body.Status, body.OperatorNote),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("api/integrations/{integrationId}/expected-run-missing/cutover-sign-off/revoke")]
    [ProducesResponseType(typeof(ExpectedRunMissingCutoverSignOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExpectedRunMissingCutoverSignOffDto>> Revoke(
        string integrationId,
        [FromBody] RevokeExpectedRunMissingCutoverSignOffRequestDto? body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _signOffs
                .RevokeSignOffAsync(integrationId, body?.OperatorNote, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static ExpectedRunMissingCutoverValidationDto ToValidationDto(ExpectedRunMissingCutoverValidation v) =>
        new(
            v.IntegrationId,
            v.OperatingModes,
            v.OperatingModes.Select(ExpectedRunMissingCutoverOperatingModes.GetDisplayLabel).ToList(),
            v.HasReadyForCutoverSignOff,
            v.SignOffIsRevoked,
            v.SignOffIsStale,
            v.LatestSignOffStatus,
            v.LatestSignOffReviewedAtUtc,
            v.ParityCurrentlyAcceptable,
            v.HasDangerousConfigurationWarnings,
            v.ConfigurationWarningCodes,
            v.RecentFailedOutboxCount,
            v.RecentSucceededOutboxCount,
            v.HasUnresolvedFailedOutbox,
            v.HasRecipientParityGap,
            v.RecipientsMissingInPlatform,
            v.ReadinessBlockers,
            v.GeneratedAtUtc);

    private static ExpectedRunMissingCutoverSignOffDto ToDto(ExpectedRunMissingCutoverSignOff s) =>
        new(
            s.Id,
            s.IntegrationId,
            s.NotificationDefinitionId,
            s.Status,
            s.ReviewedAtUtc,
            s.ReviewedByActorId,
            s.ReviewedByDisplayName,
            s.ParityAcceptableAtReview,
            s.ConfigurationWarningCodes,
            s.BlockingFindings,
            s.NonBlockingFindings,
            s.OperatorNote,
            s.CreatedAtUtc,
            s.UpdatedAtUtc);
}
