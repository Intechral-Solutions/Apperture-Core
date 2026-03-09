using Apperture.RuntimePlane.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("modules")]
public sealed class ModulesController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<object>> GetModules()
    {
        return Ok(Array.Empty<object>());
    }

    [HttpGet("{id}/workflows")]
    public ActionResult<IEnumerable<object>> GetWorkflows(string id)
    {
        return Ok(Array.Empty<object>());
    }
}
