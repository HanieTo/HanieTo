using Microsoft.AspNetCore.Mvc;

namespace HanieTo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "healthy", timeUtc = DateTime.UtcNow });
}
