using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetDaemon.AppModel;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.HassModel.Common;
using NetDaemon.HassModel.Entities;

// Use unique namespaces for your apps if you going to share with others to avoid conflicting names
namespace NotifyOnUpdate;

public class NotifyOnUpdateConfig
{
  public double? UpdateTimeInSec { get; set; }
  public string? NotifyTitle { get; set; }
  public string? NotifyId { get; set; }
}

/// <summary>
/// Creates a persistent notification in Home Assistant if a new Updates is available
/// </summary>
[NetDaemonApp]
public class NotifyOnUpdateApp : IAsyncInitializable
{
  private HttpClient mHttpClient = new HttpClient();
  private readonly IHaContext mHaContext;
  private readonly ILogger<NotifyOnUpdateApp> mLogger;
  private string mServiceDataTitle;
  private string mServiceDataId;
  private bool mHaUpdateAvailable;
  private bool mAddonUpdateAvailable;
  private string? mHassMessage;
  private string? mHacsMessage;

  private string HassMessage
  {
    get => mHassMessage ?? String.Empty;
    set
    {
      if (mHassMessage != value)
      {
        mHassMessage = value;
        if (!String.IsNullOrEmpty(mHassMessage))
        {
          if (mHaUpdateAvailable)
          {
            mLogger.LogInformation("New Home Assistant Update is available");
          }
          if (mAddonUpdateAvailable)
          {
            mLogger.LogInformation("New Addon Update is available");
          }
        }
        SetPersistentNotification();
      }
    }
  }
  private string HacsMessage
  {
    get => mHacsMessage ?? String.Empty;
    set
    {
      if (mHacsMessage != value)
      {
        mHacsMessage = value;
        if (!String.IsNullOrEmpty(mHassMessage))
        {
          mLogger.LogInformation("New Hacs Update is available");
        }
        SetPersistentNotification();
      }
    }
  }

  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    // Get Home Assistant Updates at startup
    mHaUpdateAvailable = false;
    mAddonUpdateAvailable = false;
    HassMessage = await GetHassMessage();
  }

  public NotifyOnUpdateApp(IHaContext ha, INetDaemonScheduler scheduler,
                            IAppConfig<NotifyOnUpdateConfig> config,
                            ILogger<NotifyOnUpdateApp> logger)
  {
    mHaContext = ha;
    mLogger = logger;

    if (String.IsNullOrEmpty(config.Value.NotifyTitle))
      mLogger.LogWarning("Default value 'Updates pending in Home Assistant' is used for NotifyTitle");
    if (String.IsNullOrEmpty(config.Value.NotifyId))
      mLogger.LogWarning("Default value 'updates_available' is used for NotifyId");
    if (config.Value.UpdateTimeInSec == null)
      mLogger.LogWarning("Default value '30' is used for UpdateTimeInSec");

    mServiceDataTitle = config.Value.NotifyTitle ?? "Updates pending in Home Assistant";
    mServiceDataId = config.Value.NotifyId ?? "updates_available";
    var updateTime = config.Value.UpdateTimeInSec ?? 30;

    // Get Home Assistant Updates
    try
    {
      scheduler.RunEvery(TimeSpan.FromSeconds(updateTime), async() =>
      {
          mHaUpdateAvailable = false;
          mAddonUpdateAvailable = false;
          HassMessage = await GetHassMessage();
      });
    }
    catch (Exception e)
    {
      mLogger.LogError("Exception caught.", e);
    }

    // Get HACS Updates
    var hacs = new NumericEntity<HacsAttributes>(ha, "sensor.hacs");
    hacs.StateAllChanges().Subscribe(s =>
      {
        var message = String.Empty;
        var hacsState = s.New?.State;
        var hacsRepos = s.New?.Attributes?.repositories;
        if (hacsState > 0 && (hacsRepos?.Any() ?? false))
        {
          message += "\n\n[HACS](/hacs)\n\n";
          foreach (var repo in hacsRepos)
          {
            message += $"* **{repo.display_name?.ToString()}**: {repo.installed_version?.ToString()} \u27A1 {repo.available_version?.ToString()}\n";
          }
        }
        HacsMessage = message;
      });
  }

  private async Task<string> GetHassMessage()
  {
    var message = String.Empty;
    message += await GetVersionByCurl("Core");
    message += await GetVersionByCurl("OS");
    message += await GetVersionByCurl("Supervisor");

    return message;
  }

  /// <summary>
  /// Sends a CURL (HTTP GET Request) message to get the current and actual versions from Home Assistant and its Addons
  /// </summary>
  private async Task<string> GetVersionByCurl(string versionType)
  {
    var message = String.Empty;
    var supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN") ?? String.Empty;
    if (String.IsNullOrEmpty(supervisorToken))
    {
      mLogger.LogError("Get Supervisor Token failed");
      return String.Empty;
    }

    using (var request = new HttpRequestMessage(HttpMethod.Get, $"http://supervisor/{versionType.ToLower()}/info"))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
      var response = await mHttpClient.SendAsync(request);
      if (!response.ToString().Contains("StatusCode: 200"))
      {
        mLogger.LogError($"HTTP GET failed.\n{response}");
        return String.Empty;
      }

      var responseContent = await response.Content.ReadAsStringAsync();
      if (String.IsNullOrEmpty(responseContent))
      {
        mLogger.LogError($"HTTP GET response content is null or empty.");
        return String.Empty;
      }

      var curlContent = JsonSerializer.Deserialize<CurlContent>(responseContent);
      var curlData = curlContent?.data;

      var update_available = curlData?.update_available ?? false;
      if (update_available)
      {
        mHaUpdateAvailable = true;
        message += $"* **{versionType}**: {curlData?.version} \u27A1 {curlData?.version_latest}\n";
      }

      if (curlData?.addons != null && curlData.addons.Where(x => x.update_available != null).Any(x => x.update_available == true))
      {
        message += $"\n\n[Add-ons](/config/dashboard)\n\n";
        foreach (var addon in curlData.addons)
        {
          var addon_update_available = addon?.update_available ?? false;
          if (addon_update_available)
          {
            mAddonUpdateAvailable = true;
            message += $"* [**{addon?.name}**](/hassio/addon/{addon?.slug}/info): {addon?.version} \u27A1 {addon?.version_latest}\n";
          }
        }
      }
    }

    return message;
  }

  /// <summary>
  /// Sets the persistent notification if there are any updates available
  /// </summary>
  private void SetPersistentNotification()
  {
    var serviceDataMessage = String.Empty;
    if (mHaUpdateAvailable)
    {
      serviceDataMessage += "[Home Assistant](/config/dashboard)\n\n";
    }
    serviceDataMessage += HassMessage;
    serviceDataMessage += HacsMessage;

    if (!String.IsNullOrEmpty(serviceDataMessage))
    {
      mHaContext.CallService("persistent_notification", "create", data: new
        {
          title = mServiceDataTitle,
          message = serviceDataMessage,
          notification_id = mServiceDataId
        });
    }
    else
    {
      mHaContext.CallService("persistent_notification", "dismiss", data: new
        {
          notification_id = mServiceDataId
        });
    }
  }
}

internal class CurlContent
{
  public string? result { get; set; }
  public CurlData? data { get; set; }
}

internal class CurlData
{
  public string? version { get; set; }
  public string? version_latest { get; set; }
  public bool? update_available { get; set; }
  public IEnumerable<CurlAddon>? addons { get; set; }
}

internal class CurlAddon
{
  public string? name { get; set; }
  public string? slug { get; set; }
  public string? version { get; set; }
  public string? version_latest { get; set; }
  public bool? update_available { get; set; }
}

record HacsAttributes
{
  public IEnumerable<HacsRepositories>? repositories { get; init; }
}

record HacsRepositories
{
  public string? name { get; init; }
  public string? display_name { get; init; }
  public string? installed_version { get; init; }
  public string? available_version { get; init; }
}
