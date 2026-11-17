using EasyStock.Server.Handlers.ServerInfo;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers.Settings;

[ApiController]
[Route("api/v1/server-info")]
public sealed class ServerInfoController(ServerInfoHandler info) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ServerInfo>(StatusCodes.Status200OK)]
    public Task<ServerInfo> Get(CancellationToken ct) => info.GetAsync(ct);
}
