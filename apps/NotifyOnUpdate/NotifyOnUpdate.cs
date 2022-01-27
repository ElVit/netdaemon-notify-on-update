using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Text.Json;
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
public class NotifyOnUpdateApp
{
  private HttpClient mHttpClient = new HttpClient();
  private readonly IHaContext mHaContext;
  private readonly ILogger<NotifyOnUpdateApp> mLogger;
  private string mServiceDataTitle;
  private string mServiceDataId;
  private bool mHaUpdateAvailable;
  private string? mHacsMessage;
  private string? mHaMessage;

  private string HacsMessage
  {
    get => mHacsMessage ?? String.Empty;
    set
    {
      if (mHacsMessage != value)
      {
        mHacsMessage = value;
        SetPersistentNotification();
      }
    }
  }
  private string HaMessage
  {
    get => mHaMessage ?? String.Empty;
    set
    {
      if (mHaMessage != value)
      {
        mHaMessage = value;
        SetPersistentNotification();
      }
    }
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
    scheduler.RunEvery(TimeSpan.FromSeconds(updateTime), async () =>
    {
      mHaUpdateAvailable = false;
      var message = String.Empty;
      message += await GetVersionByCurl("Core");
      message += await GetVersionByCurl("OS");
      message += await GetVersionByCurl("Supervisor");
      HaMessage = message;
    });

    // Get HACS Updates
    var hacs = new NumericEntity<HacsAttributes>(ha, "sensor.hacs");
    hacs.StateAllChanges().Subscribe(s =>
      {
        var message = String.Empty;
        if (s.New?.State > 0)
        {
          message = "[HACS](/hacs)\n\n";
          var hacsRepos = s.New?.Attributes?.repositories;
          if (hacsRepos == null) return;
          foreach (var repo in hacsRepos)
          {
            message += $"* **{repo.display_name?.ToString()}** {repo.installed_version?.ToString()} \u27A1 {repo.available_version?.ToString()}\n";
          }
        }
        HacsMessage = message;
      });
  }

  /// <summary>
  /// Sends a CURL (HTTP Request) message to get the current and actual versions from Home Assistant and its Addons
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
        mLogger.LogError($"HTTP GET did NOT respond with StatusCode: 200 (OK)\n{response}");
      }

      var responseContent = await response.Content.ReadAsStringAsync();
      var curlContent = JsonSerializer.Deserialize<CurlContent>(responseContent);
      var curlData = curlContent?.data;

      var update_available = curlData?.update_available ?? false;
      if (update_available)
      {
        mHaUpdateAvailable = true;
        mLogger.LogInformation("New Home Assistant Update is available");
        message += $"* **{versionType}** {curlData?.version} \u27A1 {curlData?.version_latest}\n";
      }

      if (curlData?.addons != null && curlData.addons.Where(x => x.update_available != null).Any(x => x.update_available == true))
      {
        mLogger.LogInformation("New Addon Update is available");
        message += $"\n\n[Add-ons](/config/dashboard)\n\n";
        foreach (var addon in curlData.addons)
        {
          var addon_update_available = addon?.update_available ?? false;
          if (addon_update_available)
          {
            message += $"* [**{addon?.name}**](/hassio/addon/{addon?.slug}/info) {addon?.version} \u27A1 {addon?.version_latest}\n";
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
    serviceDataMessage += HaMessage;
    if (!String.IsNullOrEmpty(serviceDataMessage))
    {
      serviceDataMessage += "\n\n";
    }
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
