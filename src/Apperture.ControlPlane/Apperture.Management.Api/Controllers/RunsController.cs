using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("runs")]
public sealed class RunsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<object>> GetRuns()
    {
        return Ok(Array.Empty<object>());
    }

    [HttpGet("{id}")]
    public ActionResult<object> GetRun(string id)
    {
        return NotFound();
    }
}
