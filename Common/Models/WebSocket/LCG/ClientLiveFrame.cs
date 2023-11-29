﻿namespace OpenShock.Common.Models.WebSocket.LCG;

public class ClientLiveFrame
{
    public required Guid Shocker { get; set; }
    public required byte Intensity { get; set; }
    public required ControlType Type { get; set; }
}