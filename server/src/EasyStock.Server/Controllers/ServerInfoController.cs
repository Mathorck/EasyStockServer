using EasyStock.Server.Handlers.ServerInfo;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

[ApiController]
[Route("api/v1/server-info")]
public sealed class ServerInfoController(ServerInfoHandler info) : ControllerBase
{
    [HttpGet]
    public Task<ServerInfo> Get(CancellationToken ct) => info.GetAsync(ct);
}
