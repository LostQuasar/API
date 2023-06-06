﻿using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Redis.OM;
using Redis.OM.Contracts;
using Serilog;
using ShockLink.API.Authentication;
using ShockLink.API.ExceptionHandle;
using ShockLink.API.Hubs;
using ShockLink.API.Realtime;
using ShockLink.API.Utils;
using ShockLink.Common.Models;
using ShockLink.Common.Redis;
using ShockLink.Common.ShockLinkDb;
using StackExchange.Redis;
using StackExchange.Redis.Extensions.Core.Configuration;
using StackExchange.Redis.Extensions.Newtonsoft;
using WebSocketOptions = Microsoft.AspNetCore.Builder.WebSocketOptions;

namespace ShockLink.API;

public class Startup
{
    public static string EnvString { get; private set; } = null!;

    private readonly ForwardedHeadersOptions _forwardedSettings = new()
    {
        ForwardedHeaders = ForwardedHeaders.All,
        RequireHeaderSymmetry = false,
        ForwardLimit = null
    };

    public void ConfigureServices(IServiceCollection services)
    {
        NpgsqlConnection.GlobalTypeMapper.MapEnum<ControlType>();
        NpgsqlConnection.GlobalTypeMapper.MapEnum<CfImagesType>();
        NpgsqlConnection.GlobalTypeMapper.MapEnum<PermissionType>();
        NpgsqlConnection.GlobalTypeMapper.MapEnum<ShockerModel>();
        services.AddDbContextPool<ShockLinkContext>(builder =>
        {
            builder.UseNpgsql(ApiConfig.Db);
            builder.EnableSensitiveDataLogging();
            builder.EnableDetailedErrors();
        });

        var redis = new RedisConnectionProvider($"redis://:{ApiConfig.RedisPassword}@{ApiConfig.RedisHost}:6379");
        redis.Connection.CreateIndex(typeof(LoginSession));
        redis.Connection.CreateIndex(typeof(DeviceOnline));
        redis.Connection.CreateIndex(typeof(DevicePair));
        services.AddSingleton<IRedisConnectionProvider>(redis);
        var redisConf = new RedisConfiguration
        {
            AbortOnConnectFail = true,
            Hosts = new[]
            {
                new RedisHost
                {
                    Host = ApiConfig.RedisHost,
                    Port = 6379
                }
            },
            Database = 0,
            Password = ApiConfig.RedisPassword
        };

        services.AddStackExchangeRedisExtensions<NewtonsoftSerializer>(redisConf);

        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddScoped<IClientAuthService<LinkUser>, ClientAuthService<LinkUser>>();
        services.AddScoped<IClientAuthService<Device>, ClientAuthService<Device>>();

        services.AddWebEncoders();
        services.TryAddSingleton<ISystemClock, SystemClock>();
        new AuthenticationBuilder(services)
            .AddScheme<LoginSessionAuthenticationSchemeOptions, LoginSessionAuthentication>(
                ShockLinkAuthSchemas.SessionTokenCombo, _ => { })
            .AddScheme<DeviceAuthenticationSchemeOptions, DeviceAuthentication>(
                ShockLinkAuthSchemas.DeviceToken, _ => { });
        services.AddAuthenticationCore();
        services.AddAuthorization();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.SetIsOriginAllowed(s => true);
                builder.AllowAnyHeader();
                builder.AllowCredentials();
                builder.AllowAnyMethod();
                builder.SetPreflightMaxAge(TimeSpan.FromHours(24));
            });
        });
        services.AddSignalR().AddStackExchangeRedis($"{ApiConfig.RedisHost}:6379");

        services.AddApiVersioning();
        services.AddControllers().AddJsonOptions(x => { x.JsonSerializerOptions.PropertyNameCaseInsensitive = true; });

        services.AddSwaggerGen();
        //services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
    }

    private static readonly string[] CloudflareProxies =
    {
        "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "104.16.0.0/13", "104.24.0.0/14", "108.162.192.0/18",
        "131.0.72.0/22", "141.101.64.0/18", "162.158.0.0/15", "172.64.0.0/13", "173.245.48.0/20", "188.114.96.0/20",
        "190.93.240.0/20", "197.234.240.0/22", "198.41.128.0/17", "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32",
        "2405:b500::/32", "2405:8100::/32", "2c0f:f248::/32", "2a06:98c0::/29"
    };

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
    {
        EnvString = env.EnvironmentName;
        foreach (var proxy in CloudflareProxies)
        {
            var split = proxy.Split('/');
            _forwardedSettings.KnownNetworks.Add(new IPNetwork(IPAddress.Parse(split[0]), int.Parse(split[1])));
        }

        app.UseForwardedHeaders(_forwardedSettings);
        app.UseSerilogRequestLogging();
        ApplicationLogging.LoggerFactory = loggerFactory;

        app.ConfigureExceptionHandler();

        // global cors policy
        app.UseCors();

        var redisConfiguration = new ConfigurationOptions
        {
            EndPoints = { { ApiConfig.RedisHost, 6379 } },
            Password = ApiConfig.RedisPassword,
            DefaultDatabase = 0,
            ClientName = "shocklink-api"
        };

        PubSubManager.Initialize(ConnectionMultiplexer.Connect(redisConfiguration), app.ApplicationServices);

        var webSocketOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(1)
        };

        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShockLink API"); });
        }

        //app.UseHttpsRedirection();
        app.UseWebSockets(webSocketOptions);
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            /*endpoints.MapHealthChecks("/{version:apiVersion}/public/healthcheck",
                new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                {
                    ResponseWriter = UiResponseWriter.WriteHealthCheckUiResponse
                });*/
            endpoints.MapControllers();
            endpoints.MapHub<UserHub>("/1/hubs/user",
                options => { options.Transports = HttpTransportType.WebSockets; });
        });
    }
}