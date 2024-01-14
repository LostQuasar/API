﻿using Microsoft.EntityFrameworkCore;
using OpenShock.Common.Models;
using OpenShock.Common.OpenShockDb;
using Semver;

namespace OpenShock.ServicesCommon.Services.Ota;

public class OtaService : IOtaService
{
    private readonly OpenShockContext _db;

    /// <summary>
    /// DI constructor
    /// </summary>
    /// <param name="db"></param>
    public OtaService(OpenShockContext db)
    {
        _db = db;
    }

    public Task Started(Guid deviceId, int updateId, SemVersion version)
    {
        _db.DeviceOtaUpdates.Add(new DeviceOtaUpdate
        {
            Device = deviceId,
            UpdateId = updateId,
            Status = OtaUpdateStatus.Started,
            Version = version.ToString()
        });

        return _db.SaveChangesAsync();
    }

    public async Task Progress(Guid deviceId, int updateId)
    {
        var updateTask = await _db.DeviceOtaUpdates
            .Where(x => x.Device == deviceId && x.UpdateId == updateId)
            .FirstOrDefaultAsync();
        if (updateTask == null) return;
        updateTask.Status = OtaUpdateStatus.Running;

        await _db.SaveChangesAsync();
    }

    public async Task Error(Guid deviceId, int updateId)
    {
        var updateTask = await _db.DeviceOtaUpdates
            .Where(x => x.Device == deviceId && x.UpdateId == updateId)
            .FirstOrDefaultAsync();
        if (updateTask == null) return;
        updateTask.Status = OtaUpdateStatus.Error;

        await _db.SaveChangesAsync();
    }
    
    public async Task Success(Guid deviceId, int updateId)
    {
        var updateTask = await _db.DeviceOtaUpdates
            .Where(x => x.Device == deviceId && x.UpdateId == updateId)
            .FirstOrDefaultAsync();
        if (updateTask == null) return;
        updateTask.Status = OtaUpdateStatus.Error;

        await _db.SaveChangesAsync();
    }
    
}