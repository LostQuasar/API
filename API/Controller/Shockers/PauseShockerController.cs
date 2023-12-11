﻿using System.Net;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.API.Models.Requests;
using OpenShock.Common.Models;

namespace OpenShock.API.Controller.Shockers;

public sealed partial class ShockerController
{
    /// <summary>
    /// Pauses or unpauses a shocker
    /// </summary>
    /// <param name="id"></param>
    /// <param name="data"></param>
    /// <response code="200">Successfully set pause state</response>
    /// <response code="404">Shocker not found or does not belong to you</response>
    [HttpPost("{id}/pause", Name = "PauseShocker")]
    [ProducesResponseType((int) HttpStatusCode.OK)]
    [ProducesResponseType((int) HttpStatusCode.NotFound)]
    [MapToApiVersion("1")]
    public async Task<BaseResponse<bool?>> Pause([FromRoute] Guid id, [FromBody] PauseRequest data)
    {
        var shocker = await _db.Shockers.Where(x => x.Id == id && x.DeviceNavigation.Owner == CurrentUser.DbUser.Id)
            .SingleOrDefaultAsync();
        if (shocker == null)
            return EBaseResponse<bool?>("Shocker not found or does not belong to you", HttpStatusCode.NotFound);
        shocker.Paused = data.Pause;
        await _db.SaveChangesAsync();
        return new BaseResponse<bool?>("Successfully set pause state", data.Pause);
    }
}