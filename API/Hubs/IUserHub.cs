﻿using ShockLink.API.Models.WebSocket;
using ShockLink.Common.Models;
using ShockLink.Common.Models.WebSocket.User;

namespace ShockLink.API.Hubs;

public interface IUserHub
{
    Task DeviceStatus(IEnumerable<DeviceOnlineState> deviceOnlineStates);

    Task Log(GenericIni sender, IEnumerable<ControlLog> logs);
}