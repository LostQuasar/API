﻿using System.ComponentModel.DataAnnotations;

namespace OpenShock.API.Models.Requests;

public sealed class Login
{
    [MinLength(1)]
    public required string Password { get; set; }
    [MinLength(1)]
    public required string Email { get; set; }
}