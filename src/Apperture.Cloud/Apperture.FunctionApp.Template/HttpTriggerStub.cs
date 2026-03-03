using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Apperture.FunctionApp.Template;

public sealed class HttpTriggerStub
{
    [Function("HttpTriggerStub")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(HttpTriggerStub));
        logger.LogInformation("Stub HTTP trigger invoked");
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("Stub").ConfigureAwait(false);
        return response;
    }
}
