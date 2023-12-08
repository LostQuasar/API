﻿using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenShock.API.Models.Requests;
using OpenShock.API.Realtime;
using OpenShock.API.Utils;
using OpenShock.Common.Models;
using OpenShock.Common.OpenShockDb;
using OpenShock.Common.Redis;
using OpenShock.Common.Redis.PubSub;
using OpenShock.ServicesCommon.Authentication;
using Redis.OM.Contracts;
using Redis.OM.Searching;

namespace OpenShock.API.Controller.Devices;

[ApiController]
[Route("/{version:apiVersion}/devices")]
public class DeviceController : AuthenticatedSessionControllerBase
{
    private readonly OpenShockContext _db;
    private readonly IRedisCollection<DevicePair> _devicePairs;
    private readonly IRedisCollection<DeviceOnline> _devicesOnline;
    private readonly IRedisCollection<LcgNode> _lcgNodes;

    public DeviceController(OpenShockContext db, IRedisConnectionProvider provider)
    {
        _db = db;
        _devicePairs = provider.RedisCollection<DevicePair>();
        _devicesOnline = provider.RedisCollection<DeviceOnline>(false);
        _lcgNodes = provider.RedisCollection<LcgNode>(false);
    }

    [HttpGet]
    public async Task<BaseResponse<IEnumerable<Models.Response.ResponseDevice>>> GetList()
    {
        var devices = await _db.Devices.Where(x => x.Owner == CurrentUser.DbUser.Id)
            .Select(x => new Models.Response.ResponseDevice
            {
                Id = x.Id,
                Name = x.Name,
                CreatedOn = x.CreatedOn
            }).ToListAsync();
        return new BaseResponse<IEnumerable<Models.Response.ResponseDevice>>
        {
            Data = devices
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<BaseResponse<Models.Response.ResponseDeviceWithToken>> Get(Guid id)
    {
        var device = await _db.Devices.Where(x => x.Owner == CurrentUser.DbUser.Id && x.Id == id)
            .Select(x => new Models.Response.ResponseDeviceWithToken
            {
                Id = x.Id,
                Name = x.Name,
                CreatedOn = x.CreatedOn,
                Token = x.Token
            }).SingleOrDefaultAsync();
        if (device == null)
            return EBaseResponse<Models.Response.ResponseDeviceWithToken>("Device does not exist",
                HttpStatusCode.NotFound);
        return new BaseResponse<Models.Response.ResponseDeviceWithToken>
        {
            Data = device
        };
    }

    [HttpPatch("{id:guid}")]
    public async Task<BaseResponse<object>> Edit(Guid id, DeviceEdit data)
    {
        var device = await _db.Devices.Where(x => x.Owner == CurrentUser.DbUser.Id && x.Id == id)
            .SingleOrDefaultAsync();
        if (device == null)
            return EBaseResponse<object>("Device does not exist", HttpStatusCode.NotFound);

        device.Name = data.Name;
        await _db.SaveChangesAsync();

        await PubSubManager.SendDeviceUpdate(new DeviceUpdatedMessage
        {
            Id = device.Id
        });
        
        return new BaseResponse<object>
        {
            Message = "Successfully updated device"
        };
    }

    [HttpPut("{id:guid}")]
    public async Task<BaseResponse<object>> RegenToken(Guid id)
    {
        var device = await _db.Devices.Where(x => x.Owner == CurrentUser.DbUser.Id && x.Id == id)
            .SingleOrDefaultAsync();
        if (device == null)
            return EBaseResponse<object>("Device does not exist", HttpStatusCode.NotFound);

        device.Token = CryptoUtils.RandomString(256);

        var affected = await _db.SaveChangesAsync();
        if (affected <= 0)
            return EBaseResponse<object>("Failed to save regenerated token", HttpStatusCode.InternalServerError);

        return new BaseResponse<object>
        {
            Message = "Successfully regenerated device token"
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<BaseResponse<object>> Delete(Guid id)
    {
        var affected = await _db.Devices.Where(x => x.Owner == CurrentUser.DbUser.Id && x.Id == id)
            .ExecuteDeleteAsync();
        if (affected <= 0)
            return EBaseResponse<object>("Device does not exist", HttpStatusCode.NotFound);
        
        await PubSubManager.SendDeviceUpdate(new DeviceUpdatedMessage
        {
            Id = id
        });
        
        return new BaseResponse<object>
        {
            Message = "Successfully deleted device"
        };
    }

    [HttpPost]
    public async Task<BaseResponse<Guid>> CreateDevice()
    {
        var device = new Common.OpenShockDb.Device
        {
            Id = Guid.NewGuid(),
            Owner = CurrentUser.DbUser.Id,
            Name = $"New Device {DateTimeOffset.UtcNow}",
            Token = CryptoUtils.RandomString(256)
        };
        _db.Devices.Add(device);
        await _db.SaveChangesAsync();

        await PubSubManager.SendDeviceUpdate(new DeviceUpdatedMessage
        {
            Id = device.Id
        });
        
        Response.StatusCode = (int)HttpStatusCode.Created;
        return new BaseResponse<Guid>
        {
            Message = "Successfully created device",
            Data = device.Id
        };
    }

    [HttpGet("{id:guid}/pair")]
    public async Task<BaseResponse<string>> GetPairCode(Guid id)
    {
        var deviceExists = await _db.Devices.AnyAsync(x => x.Id == id && x.Owner == CurrentUser.DbUser.Id);
        if (!deviceExists)
            return EBaseResponse<string>("Device does not exists or does not belong to you", HttpStatusCode.NotFound);
        // replace with unlink?
        var existing = await _devicePairs.FindByIdAsync(id.ToString());
        if (existing != null) await _devicePairs.DeleteAsync(existing);

        var r = new Random();
        var pairCode = new DevicePair
        {
            Id = id,
            PairCode = r.Next(0, 1000000).ToString("000000")
        };
        await _devicePairs.InsertAsync(pairCode, TimeSpan.FromMinutes(15));

        return new BaseResponse<string>
        {
            Data = pairCode.PairCode
        };
    }

    /// <summary>
    /// Get LCG info for a device if it is online and connected to a LCG node
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet("{id:guid}/lcg")]
    public async Task<BaseResponse<LcgResponse>> GetLcgInfo(Guid id)
    {
        // Check if user owns device or has a share
        var deviceExistsAndYouHaveAccess = await _db.Devices.AnyAsync(x =>
            x.Id == id && (x.Owner == CurrentUser.DbUser.Id || x.Shockers.Any(y => y.ShockerShares.Any(
                z => z.SharedWith == CurrentUser.DbUser.Id))));
        if (!deviceExistsAndYouHaveAccess)
            return EBaseResponse<LcgResponse>("Device does not exists or does not belong to you",
                HttpStatusCode.NotFound);

        // Check if device is online
        var online = await _devicesOnline.FindByIdAsync(id.ToString());
        if (online == null) return EBaseResponse<LcgResponse>("Device is not online", HttpStatusCode.NotFound);

        // Check if device is connected to a LCG node
        if (online.Gateway == null)
            return EBaseResponse<LcgResponse>(
                "Device is online but not connected to a LCG node, you might need to upgrade your firmware to use this feature",
                HttpStatusCode.PreconditionFailed);

        // Get LCG node info
        var gateway = await _lcgNodes.FindByIdAsync(online.Gateway);
        if (gateway == null)
            return EBaseResponse<LcgResponse>("Internal server error, lcg node could not be found",
                HttpStatusCode.InternalServerError);


        return new BaseResponse<LcgResponse>
        {
            Data = new LcgResponse
            {
                Gateway = gateway.Fqdn,
                Country = gateway.Country
            }
        };
    }
}