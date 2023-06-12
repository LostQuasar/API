﻿using System;
using System.Collections.Generic;
using ShockLink.Common.Models;

namespace ShockLink.Common.ShockLinkDb;

public partial class ApiToken
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Token { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime CreatedOn { get; set; }

    public string CreatedByIp { get; set; } = null!;

    public DateTime? ValidUntil { get; set; }
    
    public List<PermissionType> Permissions { get; set; }

    public virtual User User { get; set; } = null!;
}