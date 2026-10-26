using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceAPI.Tests.TestSupport;

/// <summary>Only registered in the test host; gives the pipeline an endpoint that fails the way a real bug would.</summary>
[ApiController]
[ApiVersionNeutral]
[Route("test")]
public sealed class FailureController : ControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("internal detail that must never reach a client");
}
