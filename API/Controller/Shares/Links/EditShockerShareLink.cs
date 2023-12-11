﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.API.Models.Requests;
using OpenShock.API.Models.Response;
using OpenShock.Common.Models;
using System.Net;

namespace OpenShock.API.Controller.Shares.Links;

public sealed partial class ShareLinksController
{
    /// <summary>
    /// Edit a shocker in a share link
    /// </summary>
    /// <param name="id"></param>
    /// <param name="shockerId"></param>
    /// <param name="data"></param>
    /// <response code="200">Successfully updated shocker</response>
    /// <response code="404">Share link or shocker does not exist</response>
    /// <response code="400">Shocker does not exist in share link</response>
    [HttpPatch("{id}/{shockerId}", Name = "EditShockerShareLink")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.NotFound)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    public async Task<BaseResponse<ShareLinkResponse>> EditShocker([FromRoute] Guid id, [FromRoute] Guid shockerId, [FromBody] ShareLinkEditShocker data)
    {
        var exists = await _db.ShockerSharesLinks.AnyAsync(x => x.OwnerId == CurrentUser.DbUser.Id && x.Id == id);
        if (!exists)
            return EBaseResponse<ShareLinkResponse>("Share link could not be found", HttpStatusCode.NotFound);

        var shocker =
            await _db.ShockerSharesLinksShockers.FirstOrDefaultAsync(x =>
                x.ShareLinkId == id && x.ShockerId == shockerId);
        if (shocker == null)
            return EBaseResponse<ShareLinkResponse>("Shocker does not exist in share link, consider adding a new one");

        shocker.PermSound = data.Permissions.Sound;
        shocker.PermVibrate = data.Permissions.Vibrate;
        shocker.PermShock = data.Permissions.Shock;
        shocker.LimitDuration = data.Limits.Duration;
        shocker.LimitIntensity = data.Limits.Intensity;
        shocker.Cooldown = data.Cooldown;

        await _db.SaveChangesAsync();
        return new BaseResponse<ShareLinkResponse>
        {
            Message = "Successfully updated shocker"
        };
    }
}