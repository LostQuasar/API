﻿using Microsoft.AspNetCore.Mvc;
using OpenShock.API.Models.Requests;
using System.Net;
using Asp.Versioning;
using OpenShock.API.Services.Account;
using OpenShock.ServicesCommon.Errors;
using OpenShock.ServicesCommon.Problems;

namespace OpenShock.API.Controller.Account;

public sealed partial class AccountController
{
    /// <summary>
    /// Authenticate a user
    /// </summary>
    /// <response code="200">User successfully logged in</response>
    /// <response code="401">Invalid username or password</response>
    [HttpPost("login")]
    [ProducesSuccess]
    [ProducesProblem(HttpStatusCode.Unauthorized, "InvalidCredentials")]
    [MapToApiVersion("1")]
    public async Task<IActionResult> Login(
        [FromBody] Login body,
        [FromServices] IAccountService accountService,
        [FromServices] ApiConfig apiConfig,
        CancellationToken cancellationToken)
    {
        var loginAction = await accountService.Login(body.Email, body.Password, new LoginContext
        {
            Ip = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? string.Empty,
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        }, cancellationToken);

        if (loginAction.IsT1) return Problem(LoginError.InvalidCredentials);
        

        HttpContext.Response.Cookies.Append("openShockSession", loginAction.AsT0.Value, new CookieOptions
        {
            Expires = new DateTimeOffset(DateTime.UtcNow.Add(accountService.SessionLifetime)),
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Domain = "." + apiConfig.Frontend.CookieDomain
        });

        return RespondSuccessSimple("Successfully logged in");
    }
}