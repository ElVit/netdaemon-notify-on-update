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
  public string? Title { get; set; }
}

/// <summary>
/// Creates a persistent notification in Home Assistant if a new Updates is available
/// </summary>
[NetDaemonApp]
public class NotifyOnUpdate
{
  private HttpClient mHttpClient = new HttpClient();
  private readonly IHaContext mHaContext;
  private readonly ILogger<NotifyOnUpdate> mLogger;
  private string mTitle;
  private string? mHacsMessage;
  private string? mCoreMessage;
  private string? mOsMessage;
  private string? mSupervisorMessage;

  private string HacsMessage
  {
    get => mHacsMessage ?? String.Empty;
    set
    {
      if (value != mHacsMessage)
      {
        mHacsMessage = value;
        SetPersistenNotification();
      }
    }
  }
  private string CoreMessage
  {
    get => mCoreMessage ?? String.Empty;
    set
    {
      if (value != mCoreMessage)
      {
        mCoreMessage = value;
        SetPersistenNotification();
      }
    }
  }
  private string OsMessage
  {
    get => mOsMessage ?? String.Empty;
    set
    {
      if (value != mOsMessage)
      {
        mOsMessage = value;
        SetPersistenNotification();
      }
    }
  }
  private string SupervisorMessage
  {
    get => mSupervisorMessage ?? String.Empty;
    set
    {
      if (value != mSupervisorMessage)
      {
        mSupervisorMessage = value;
        SetPersistenNotification();
      }
    }
  }

  public NotifyOnUpdate(IHaContext ha, INetDaemonScheduler scheduler,
                        IAppConfig<NotifyOnUpdateConfig> config,
                        ILogger<NotifyOnUpdate> logger)
  {
    mHaContext = ha;
    mLogger = logger;
    mLogger.LogInformation("NotifyOnHAUpdate started");
    mTitle = config.Value.Title ?? "Updates pending in Home Assistant";
    var updateTime = config.Value.UpdateTimeInSec ?? 30;

    scheduler.RunEvery(TimeSpan.FromSeconds(updateTime), async () =>
    {
      // Get Home Assistant Updates
      CoreMessage = await GetVersionByCurl("Core");
      OsMessage = await GetVersionByCurl("OS");
      SupervisorMessage = await GetVersionByCurl("Supervisor");
    });

    // Get HACS Updates
    var hacs = new NumericEntity<HacsAttributes>(ha, "sensor.hacs");
    hacs.StateAllChanges().Subscribe(s =>
      {
        var message = String.Empty;
        if (s.New?.State > 0)
        {
          message = "[HACS](/hacs)\n\n";
          var hacsRepositories = s.New?.Attributes?.repositories;

          if (hacsRepositories == null) return;
          foreach(var repo in hacsRepositories)
          {
            message += $"* **{repo.display_name?.ToString()}** {repo.installed_version?.ToString()} -> {repo.available_version?.ToString()}\n";
          }
        }
        HacsMessage = message;
      });
  }

  /// <summary>
  /// Sets the persistent notification if there are any updates available
  /// </summary>
  private void SetPersistenNotification()
  {
    var serviceDataTitle = mTitle;
    var serviceDataId = "updates_available";

    var serviceDataMessage = String.Empty;
    if (!String.IsNullOrEmpty(CoreMessage) ||
        !String.IsNullOrEmpty(OsMessage) ||
        !String.IsNullOrEmpty(SupervisorMessage))
    {
      serviceDataMessage += "[Home Assistant](/config/dashboard)\n\n";
    }
    serviceDataMessage += CoreMessage + OsMessage + SupervisorMessage;
    if (!String.IsNullOrEmpty(serviceDataMessage))
    {
      serviceDataMessage += "\n\n";
    }
    serviceDataMessage += HacsMessage;

    if (!String.IsNullOrEmpty(serviceDataMessage))
    {
      mHaContext.CallService("persistent_notification", "create", data: new
        {
          title = serviceDataTitle,
          message = serviceDataMessage,
          notification_id = serviceDataId
        });
    }
    else
    {
      mHaContext.CallService("persistent_notification", "dismiss", data: new
        {
          notification_id = serviceDataId
        });
    }
  }

  /// <summary>
  /// Sends a CURL (HTTP Request) message to get the current and actual versions from Home Assistant and its Addons
  /// </summary>
  private async Task<string> GetVersionByCurl(string versionType)
  {
    var message = String.Empty;
    var supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN") ?? "(none)";

    using (var request = new HttpRequestMessage(HttpMethod.Get, $"http://supervisor/{versionType.ToLower()}/info"))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
      var response = await mHttpClient.SendAsync(request);
      var responseContent = await response.Content.ReadAsStringAsync();
      var curl = JsonSerializer.Deserialize<CurlContent>(responseContent);
      var curlData = curl?.data;

      var update_available = curlData?.update_available ?? false;
      if(update_available)
      {
        mLogger.LogInformation("New Home Assistant Update is available");
        message += $"* **{versionType}** {curlData?.version} \uD83E\uDC16 {curlData?.version_latest}\n";
      }

      if (curlData?.addons != null && curlData.addons.Where(x => x.update_available != null).Any(x => x.update_available == true))
      {
        mLogger.LogInformation("New Addon Update is available");
        message += $"\n\n[Add-ons](/config/dashboard)\n\n";;
        foreach(var addon in curlData.addons)
        {
          var addon_update_available = curlData?.update_available ?? false;
          if(addon_update_available)
          {
            message += $"* [**{addon?.name}**](/hassio/addon/{addon?.slug}/info) {addon?.version} \uD83E\uDC16 {addon?.version_latest}\n";
          }
        }
      }
    }

    return message;
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
