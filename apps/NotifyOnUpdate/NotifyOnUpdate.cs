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
using NetDaemon.Client;
using NetDaemon.Client.HomeAssistant.Extensions;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.HassModel;
using NetDaemon.HassModel.Entities;

// Use unique namespaces for your apps if you going to share with others to avoid conflicting names
namespace NotifyOnUpdate;

public class NotifyOnUpdateConfig
{
  public double? UpdateTimeInSec { get; set; }
  public string? NotifyTitle { get; set; }
  public string? NotifyId { get; set; }
  public bool? PersistentNotification { get; set; }
  public IEnumerable<string>? MobileNotifyServices { get; set; }
}

/// <summary>
/// Creates a persistent notification in Home Assistant if a new Updates is available
/// </summary>
[NetDaemonApp]
public class NotifyOnUpdateApp : IAsyncInitializable
{
  private HttpClient mHttpClient = new HttpClient();
  private readonly IHaContext mHaContext;
  private readonly IHomeAssistantConnection mHaConnection;
  private readonly ILogger<NotifyOnUpdateApp> mLogger;
  private string mServiceDataTitle;
  private string mServiceDataId;
  private bool mPersistentNotification;
  private IEnumerable<string> mMobileNotifyServices;
  private IEnumerable<UpdateText> mHassUpdates = new List<UpdateText>();
  private IEnumerable<UpdateText> mHacsUpdates = new List<UpdateText>();

  private IEnumerable<UpdateText> HassUpdates
  {
    get => mHassUpdates ?? new List<UpdateText>();
    set
    {
      if (value != null && (!IsEqual(mHassUpdates, value)))
      {
        mHassUpdates = value;
        mLogger.LogInformation("Supervisor update list changed.");
        SetPersistentNotification();
      }
    }
  }

  private IEnumerable<UpdateText> HacsUpdates
  {
    get => mHacsUpdates ?? new List<UpdateText>();
    set
    {
      if (value != null && (!IsEqual(mHacsUpdates, value)))
      {
        mHacsUpdates = value;
        mLogger.LogInformation("Hacs update list changed.");
        SetPersistentNotification();
      }
    }
  }

  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    // Check if user defined notify services are valid
    mMobileNotifyServices = await GetAvailableServices("notify", mMobileNotifyServices);

    // Get Home Assistant Updates once at startup;
    HassUpdates = await GetHassUpdates();

    // Get Hacs Updates once at statup
    HacsUpdates = GetHacsUpdates();
  }

  public NotifyOnUpdateApp(IHaContext ha, INetDaemonScheduler scheduler,
                            IHomeAssistantConnection haConnection,
                            IAppConfig<NotifyOnUpdateConfig> config,
                            ILogger<NotifyOnUpdateApp> logger)
  {
    mHaContext = ha;
    mHaConnection = haConnection;
    mLogger = logger;

    // Check against null and set a default value if true
    mServiceDataTitle = config.Value.NotifyTitle ?? "Updates pending in Home Assistant";
    mServiceDataId = config.Value.NotifyId ?? "updates_available";
    mPersistentNotification = config.Value.PersistentNotification ?? true;
    mMobileNotifyServices = config.Value.MobileNotifyServices ?? new List<string>();
    var updateTime = config.Value.UpdateTimeInSec ?? 30;

    // Check against empty/invalid values and set a default value if true
    if (String.IsNullOrEmpty(config.Value.NotifyTitle))
    {
      mLogger.LogWarning("Default value 'Updates pending in Home Assistant' is used for NotifyTitle.");
      mServiceDataTitle = "Updates pending in Home Assistant";
    }
    if (String.IsNullOrEmpty(config.Value.NotifyId))
    {
      mLogger.LogWarning("Default value 'updates_available' is used for NotifyId.");
      mServiceDataId = "updates_available";
    }
    if (config.Value.PersistentNotification == null)
    {
      mLogger.LogWarning("Default value 'true' is used for PersistentNotification.");
    }
    if (config.Value.UpdateTimeInSec == null || config.Value.UpdateTimeInSec <= 0)
    {
      mLogger.LogWarning("Default value '30' is used for UpdateTimeInSec.");
    }

    // Get Home Assistant Updates cyclic
    try
    {
      scheduler.RunEvery(TimeSpan.FromSeconds(updateTime), async() =>
      {
          HassUpdates = await GetHassUpdates();
      });
    }
    catch (Exception e)
    {
      mLogger.LogError("Exception caught: ", e);
    }

    // Get Hacs Updates on state change
    var hacs = new NumericEntity<HacsAttributes>(mHaContext, "sensor.hacs");
    hacs.StateAllChanges().Subscribe(s =>
      {
        HacsUpdates = GetHacsUpdates();
      });
  }

  private async Task<IEnumerable<string>> GetAvailableServices(string serviceType, IEnumerable<string> definedServices)
  {
    var availableServices = new List<string>();

    mLogger.LogInformation($"{definedServices.Count()} notify service(s) defined.");
    if(definedServices.Count() < 1) return availableServices;

    var allServices = await mHaConnection.GetServicesAsync(CancellationToken.None).ConfigureAwait(false);
    var notifyService = new JsonElement();
    allServices.GetValueOrDefault().TryGetProperty(serviceType, out notifyService);
    var filteredServices = JsonSerializer.Deserialize<Dictionary<string, object>>(notifyService) ?? new Dictionary<string, object>();
    foreach(var definedService in definedServices)
    {
      var service = definedService;
      // If notifyService starts with "notify." then remove this part
      if(service.StartsWith("notify."))
        service = service.Substring(7);

      if(filteredServices.ContainsKey(service))
      {
        availableServices.Add(service);
        mLogger.LogInformation($"- Service '{service}' is available");
      }
      else
      {
        mLogger.LogInformation($"- Service '{service}' is NOT available");
      }
    }
    mLogger.LogInformation($"{availableServices.Count()} notify service(s) available.");

    return availableServices;
  }

  /// <summary>
  /// Compares two Lists for equality containing UpdateTextes
  /// </summary>
  private bool IsEqual(IEnumerable<UpdateText> list1, IEnumerable<UpdateText> list2)
  {
    return Enumerable.SequenceEqual(
      list1.Select(x => x.hash).OrderBy(x => x),
      list2.Select(x => x.hash).OrderBy(x => x));
  }

  /// <summary>
  /// Get HACS update informations from the hacs sensor
  /// </summary>
  private IEnumerable<UpdateText> GetHacsUpdates()
  {
    var updates = new List<UpdateText>();
    var hacs = new NumericEntity<HacsAttributes>(mHaContext, "sensor.hacs").EntityState;
    var hacsState = hacs?.State;
    var hacsRepos = hacs?.Attributes?.repositories;

    if (hacsState > 0 && (hacsRepos?.Any() ?? false))
    {
      foreach (var repo in hacsRepos)
      {
        updates.Add(new UpdateText(UpdateType.Hacs, repo.display_name?.ToString(), repo.installed_version?.ToString(), repo.available_version?.ToString()));
      }
    }

    return updates;
  }

  /// <summary>
  /// Get Home Assistant update informations
  /// </summary>
  private async Task<IEnumerable<UpdateText>> GetHassUpdates()
  {
    var updates = new List<UpdateText>();
    updates.AddRange(await GetVersionsByCurl("Core"));
    updates.AddRange(await GetVersionsByCurl("OS"));
    updates.AddRange(await GetVersionsByCurl("Supervisor"));

    return updates;
  }

  /// <summary>
  /// Extracts the relevant update informations of a CURL Response Data
  /// </summary>
  private async Task<IEnumerable<UpdateText>> GetVersionsByCurl(string versionType)
  {
    var updates = new List<UpdateText>();
    var curlData = await GetCurlData(versionType);

    if (curlData?.update_available ?? false)
    {
      updates.Add(new UpdateText(UpdateType.HomeAssistant, versionType, curlData?.version, curlData?.version_latest));
    }

    if (curlData?.addons != null && curlData.addons.Where(x => x.update_available != null).Any(x => x.update_available == true))
    {
      foreach (var addon in curlData.addons)
      {
        var addon_update_available = addon?.update_available ?? false;
        if (addon_update_available)
        {
          updates.Add(new UpdateText(UpdateType.Addon, addon?.name, addon?.version, addon?.version_latest, $"/hassio/addon/{addon?.slug}/info"));
        }
      }
    }

    return updates;
  }

  /// <summary>
  /// Sends a CURL (HTTP GET Request) message to get the current installed and newest
  /// available versions of Home Assistant and its Addons
  /// </summary>
  private async Task<CurlData?> GetCurlData(string versionType)
  {
    var curlData = new CurlData();

    var supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN") ?? String.Empty;
    if (String.IsNullOrEmpty(supervisorToken))
    {
      mLogger.LogError("Get Supervisor Token failed");
      return null;
    }

    using (var request = new HttpRequestMessage(HttpMethod.Get, $"http://supervisor/{versionType.ToLower()}/info"))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);
      var response = await mHttpClient.SendAsync(request);
      if (!response.ToString().Contains("StatusCode: 200"))
      {
        mLogger.LogError($"HTTP GET failed.\n{response}");
        return null;
      }

      var responseContent = await response.Content.ReadAsStringAsync();
      if (String.IsNullOrEmpty(responseContent))
      {
        mLogger.LogError($"HTTP GET response content is null or empty.");
        return null;
      }

      var curlContent = JsonSerializer.Deserialize<CurlContent>(responseContent);
      return curlContent?.data;
    }
  }

  /// <summary>
  /// Sets a notification if there are any updates available
  /// </summary>
  private void SetPersistentNotification()
  {
    var persistentMessage = String.Empty;
    var companionMessage = String.Empty;
    var hassUpdates = HassUpdates.Where(x => x.Type == UpdateType.HomeAssistant);
    var addonUpdates = HassUpdates.Where(x => x.Type == UpdateType.Addon);

    if (hassUpdates.Any())
    {
      persistentMessage += "[Home Assistant](/config/dashboard)\n\n";
      companionMessage += "Home Assistant\n";
      foreach(var update in hassUpdates)
      {
        persistentMessage += $"* **{update.Name}**: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
      }
    }
    if (addonUpdates.Any())
    {
      persistentMessage += $"\n\n[Add-ons](/config/dashboard)\n\n";
      companionMessage += "\nAdd-ons\n";
      foreach(var update in addonUpdates)
      {
        persistentMessage += $"* [**{update.Name}**]({update.Path}): {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
      }
    }
    if (HacsUpdates.Any())
    {
      persistentMessage += "\n\n[HACS](/hacs)\n\n";
      companionMessage += "\nHACS\n";
      foreach(var update in HacsUpdates)
      {
        persistentMessage += $"* **{update.Name}**: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
      }
    }

    if (!String.IsNullOrEmpty(persistentMessage))
    {
      // persistent notification
      if(mPersistentNotification)
      {
        mHaContext.CallService("persistent_notification", "create", data: new
          {
            title = mServiceDataTitle,
            message = persistentMessage,
            notification_id = mServiceDataId
          });
      }
      // mobile notification
      foreach (var notifyService in mMobileNotifyServices)
      {
        mHaContext.CallService("notify", notifyService, data: new
        {
          title = mServiceDataTitle,
          message = companionMessage,
          data = new
          {
            tag = mServiceDataId
          }
        });
      }
    }
    else
    {
      // persistent notification
      if(mPersistentNotification)
      {
        mHaContext.CallService("persistent_notification", "dismiss", data: new
          {
            notification_id = mServiceDataId
          });
      }
      // mobile notification
      foreach (var notifyService in mMobileNotifyServices)
      {
        mHaContext.CallService("notify", notifyService, data: new
        {
          message = "clear_notification",
          data = new
          {
            tag = mServiceDataId
          }
        });
      }
    }
  }
}

enum UpdateType
{
  HomeAssistant, Addon, Hacs
}

internal class UpdateText
{
  public UpdateType Type { get; }
  public string? Name { get; }
  public string? Path { get; }
  public string? CurrentVersion { get; }
  public string? NewVersion { get; }
  public int hash { get; }

  public UpdateText(UpdateType type, string? name, string? currentVersion, string? newVersion, string? path = null)
  {
    Type = type;
    Name = name;
    Path = path;
    CurrentVersion = currentVersion;
    NewVersion = newVersion;
    hash = HashCode.Combine(type, name, path, currentVersion, newVersion);
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
