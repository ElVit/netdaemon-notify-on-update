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
  public bool? ShowiOSBadge { get; set; }
  public string? MechanismToGetUpdates { get; set; }
  public IEnumerable<string>? GetUpdatesFor { get; set; }
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
  private bool mShowiOSBadge;
  private UpdateMechanism mMechanismToGetUpdates;
  private IEnumerable<string> mGetUpdatesFor;
  private IEnumerable<string> mMobileNotifyServices;
  private IEnumerable<UpdateText> mHassUpdates = new List<UpdateText>();
  private IEnumerable<UpdateText> mHacsUpdates = new List<UpdateText>();
  private IEnumerable<UpdateText> mEntityUpdates = new List<UpdateText>();

  private IEnumerable<UpdateText> HassUpdates
  {
    get => mHassUpdates ?? new List<UpdateText>();
    set
    {
      if (value != null && (!IsEqual(mHassUpdates, value)))
      {
        mHassUpdates = value;
        mLogger.LogInformation("Supervisor update list changed.");
        SetUpdateNotification();
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
        SetUpdateNotification();
      }
    }
  }

  private IEnumerable<UpdateText> EntityUpdates
  {
    get => mEntityUpdates ?? new List<UpdateText>();
    set
    {
      if (value != null && (!IsEqual(mEntityUpdates, value)))
      {
        mEntityUpdates = value;
        mLogger.LogInformation("Entity update list changed.");
        SetUpdateNotification();
      }
    }
  }

  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    // Check if user defined notify services are valid
    mMobileNotifyServices = await GetServicesOfType("notify", mMobileNotifyServices);

    if (mMechanismToGetUpdates == UpdateMechanism.UpdateEntities)
    {
      var updateEntities = mHaContext.GetAllEntities().Where(entity => entity.EntityId.StartsWith("update."));
      foreach (var entity in updateEntities)
      {
        var updateEntity = new Entity<UpdateAttributes>(entity);
        EntityUpdates = GetEntityUpdates(updateEntity.EntityState);
      }
    }
    else if (mMechanismToGetUpdates == UpdateMechanism.RestAPI)
    {
      // Get Home Assistant Updates once at startup;
      HassUpdates = await GetHassUpdates();

      // Get Hacs Updates once at statup
      HacsUpdates = GetHacsUpdates();

      // Remove old notifications or app badge if there are no updates available
      if (!HacsUpdates.Any() || !HassUpdates.Any())
      {
        SetPersistentNotification();
      }
    }
  }

  public NotifyOnUpdateApp(IHaContext ha, INetDaemonScheduler scheduler,
                            IHomeAssistantConnection haConnection,
                            IAppConfig<NotifyOnUpdateConfig> config,
                            ILogger<NotifyOnUpdateApp> logger)
  {
    mHaContext = ha;
    mHaConnection = haConnection;
    mLogger = logger;

    // Check options against null and set a default value if true
    mServiceDataTitle = config.Value.NotifyTitle ?? "Updates pending in Home Assistant";
    mServiceDataId = config.Value.NotifyId ?? "updates_available";
    mPersistentNotification = config.Value.PersistentNotification ?? true;
    mShowiOSBadge = config.Value.ShowiOSBadge ?? true;
    mGetUpdatesFor = config.Value.GetUpdatesFor ?? new List<string>();
    mMobileNotifyServices = config.Value.MobileNotifyServices ?? new List<string>();
    var updateTime = config.Value.UpdateTimeInSec ?? 30;

    switch (config.Value.MechanismToGetUpdates)
    {
      case "rest_api":
        mMechanismToGetUpdates = UpdateMechanism.RestAPI;
        break;
      case "update_entities":
        mMechanismToGetUpdates = UpdateMechanism.UpdateEntities;
        break;
      default:
        mMechanismToGetUpdates = UpdateMechanism.RestAPI;
        break;
    }

    // Check options against empty/invalid values and set a default value if true
    if (String.IsNullOrEmpty(config.Value.NotifyTitle))
    {
      mLogger.LogWarning("Option 'NotifyTitle' not found. Default value 'Updates pending in Home Assistant' is used.");
      mServiceDataTitle = "Updates pending in Home Assistant";
    }
    if (String.IsNullOrEmpty(config.Value.NotifyId))
    {
      mLogger.LogWarning("Option 'NotifyId' not found. Default value 'updates_available' is used.");
      mServiceDataId = "updates_available";
    }
    if (config.Value.PersistentNotification == null)
    {
      mLogger.LogWarning("Option 'PersistentNotification' not found. Default value 'true' is used.");
    }
    if (config.Value.ShowiOSBadge == null)
    {
      mLogger.LogWarning("Option 'ShowiOSBadge' not found. Default value 'true' is used.");
    }
    if (config.Value.GetUpdatesFor == null || !config.Value.GetUpdatesFor.Any())
    {
      mLogger.LogWarning("Option 'GetUpdatesFor' not found. Default values 'Core, OS, Supervisor, HACS' are used.");
      mGetUpdatesFor = new List<string>() { "Core", "OS", "Supervisor", "HACS" };
    }
    if (config.Value.UpdateTimeInSec == null || config.Value.UpdateTimeInSec <= 0)
    {
      mLogger.LogWarning("Option 'UpdateTimeInSec' not found. Default value '30' is used.");
    }

    if (mMechanismToGetUpdates == UpdateMechanism.UpdateEntities)
    {
      var updateEntities = mHaContext.GetAllEntities().Where(entity => entity.EntityId.StartsWith("update."));
      foreach (var entity in updateEntities)
      {
        var updateEntity = new Entity<UpdateAttributes>(entity);
        updateEntity.StateAllChanges().Subscribe(state =>
          {
            EntityUpdates = GetEntityUpdates(state.New);
          });
      }
    }
    else if (mMechanismToGetUpdates == UpdateMechanism.RestAPI)
    {
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
      hacs.StateAllChanges().Subscribe(state =>
        {
          HacsUpdates = GetHacsUpdates(state.New);
        });
    }
  }

  private async Task<IEnumerable<string>> GetServicesOfType(string serviceType, IEnumerable<string> definedServices)
  {
    var availableServices = new List<string>();

    mLogger.LogInformation($"{definedServices.Count()} notify service(s) defined.");
    if (definedServices.Count() < 1) return availableServices;

    var allServices = await mHaConnection.GetServicesAsync(CancellationToken.None).ConfigureAwait(false);
    var notifyService = new JsonElement();
    allServices.GetValueOrDefault().TryGetProperty(serviceType, out notifyService);
    var filteredServices = JsonSerializer.Deserialize<Dictionary<string, object>>(notifyService) ?? new Dictionary<string, object>();
    foreach (var definedService in definedServices)
    {
      var service = definedService;
      // If notifyService starts with "notify." then remove this part
      if (service.StartsWith("notify."))
        service = service.Substring(7);

      if (filteredServices.ContainsKey(service))
      {
        availableServices.Add(service);
        mLogger.LogInformation($"- Service '{service}' is available");
      }
      else
      {
        mLogger.LogInformation($"- Service '{service}' is NOT available");
      }
    }

    return availableServices;
  }

  /// <summary>
  /// Compares two Lists for equality containing UpdateTextes
  /// </summary>
  private bool IsEqual(IEnumerable<UpdateText> list1, IEnumerable<UpdateText> list2)
  {
    return Enumerable.SequenceEqual(
      list1.Select(element => element.Hash).OrderBy(element => element),
      list2.Select(element => element.Hash).OrderBy(element => element));
  }

  private IEnumerable<UpdateText> GetEntityUpdates(EntityState<UpdateAttributes>? entityState)
  {
    var updateList = EntityUpdates.Where(entity => entity.EntityId != entityState?.EntityId).ToList();
    if (entityState?.State == "on")
    {
      var update = new UpdateText(UpdateType.Entity);
      update.Name = entityState?.Attributes?.friendly_name;
      update.CurrentVersion = entityState?.Attributes?.installed_version;
      update.NewVersion = entityState?.Attributes?.latest_version;
      update.EntityId = entityState?.EntityId;
      update.CalcHash();
      updateList.Add(update);
    }

    return updateList;
  }

  /// <summary>
  /// Get HACS update informations from the hacs sensor
  /// </summary>
  private IEnumerable<UpdateText> GetHacsUpdates(NumericEntityState<HacsAttributes>? hacs = null)
  {
    var updates = new List<UpdateText>();
    if (!mGetUpdatesFor.Contains("HACS")) return updates;

    if (hacs == null)
    {
      hacs = new NumericEntity<HacsAttributes>(mHaContext, "sensor.hacs").EntityState;
    }
    var hacsState = hacs?.State ?? 0;
    var hacsRepos = hacs?.Attributes?.repositories ?? new List<HacsRepositories>();

    if (hacsState > 0 && hacsRepos.Any())
    {
      foreach (var repo in hacsRepos)
      {
        var update = new UpdateText(UpdateType.Hacs);
        update.Name = repo.display_name;
        update.CurrentVersion = repo.installed_version;
        update.NewVersion = repo.available_version;
        update.CalcHash();
        updateList.Add(update);
      }
    }

    return updateList;
  }

  /// <summary>
  /// Get Home Assistant update informations
  /// </summary>
  private async Task<IEnumerable<UpdateText>> GetHassUpdates()
  {
    var updates = new List<UpdateText>();
    if (mGetUpdatesFor.Contains("Core")) updates.AddRange(await GetVersionsByCurl("Core"));
    if (mGetUpdatesFor.Contains("OS")) updates.AddRange(await GetVersionsByCurl("OS"));
    if (mGetUpdatesFor.Contains("Supervisor")) updates.AddRange(await GetVersionsByCurl("Supervisor"));

    return updates;
  }

  /// <summary>
  /// Extracts the relevant update informations of a CURL Response Data
  /// </summary>
  private async Task<IEnumerable<UpdateText>> GetVersionsByCurl(string versionType)
  {
    var updateList = new List<UpdateText>();
    var curlData = await GetCurlData(versionType);

    if (curlData?.update_available ?? false)
    {
      updateList.Add(new UpdateText(UpdateType.HomeAssistant, versionType, curlData?.version, curlData?.version_latest));
    }

    if (curlData?.addons != null && curlData.addons.Where(addon => addon.update_available != null).Any(addon => addon.update_available == true))
    {
      foreach (var addon in curlData.addons)
      {
        var addon_update_available = addon?.update_available ?? false;
        if (addon_update_available)
        {
          var update = new UpdateText(UpdateType.Addon);
          update.Name = addon?.name;
          update.CurrentVersion = addon?.version;
          update.NewVersion = addon?.version_latest;
          update.Path = $"/hassio/addon/{addon?.slug}/info";
          update.CalcHash();
          updateList.Add(update);
        }
      }
    }

    return updateList;
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
  private void SetUpdateNotification()
  {
    var persistentMessage = String.Empty;
    var companionMessage = String.Empty;
    var hassUpdates = HassUpdates.Where(update => update.Type == UpdateType.HomeAssistant);
    var addonUpdates = HassUpdates.Where(update => update.Type == UpdateType.Addon);
    var badgeCounter = 0;

    if (hassUpdates.Any())
    {
      persistentMessage += "[Home Assistant](/config/dashboard)\n\n";
      companionMessage += "Home Assistant\n";
      foreach (var update in hassUpdates)
      {
        persistentMessage += $"* **{update.Name}**: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        badgeCounter++;
      }
    }
    if (addonUpdates.Any())
    {
      persistentMessage += $"\n\n[Add-ons](/config/dashboard)\n\n";
      companionMessage += "Add-ons\n";
      foreach (var update in addonUpdates)
      {
        persistentMessage += $"* [**{update.Name}**]({update.Path}): {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        badgeCounter++;
      }
    }
    if (HacsUpdates.Any())
    {
      persistentMessage += "\n\n[HACS](/hacs)\n\n";
      companionMessage += "HACS\n";
      foreach(var update in HacsUpdates)
      {
        persistentMessage += $"* **{update.Name}**: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        badgeCounter++;
      }
    }
    if (EntityUpdates.Any())
    {
      persistentMessage += "\n\n[Updates](/config/dashboard)\n\n";
      companionMessage += "Updates\n";
      foreach (var update in EntityUpdates)
      {
        persistentMessage += $"* **{update.Name}**: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        companionMessage += $"- {update.Name}: {update.CurrentVersion} \u27A1 {update.NewVersion}\n";
        badgeCounter++;
      }
    }

    if (!String.IsNullOrEmpty(persistentMessage))
    {
      // persistent notification
      if (mPersistentNotification)
      {
        var notifyData = new
          {
            title = mServiceDataTitle,
            message = persistentMessage,
            notification_id = mServiceDataId
          };
        mHaContext.CallService("persistent_notification", "create", data: notifyData);
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
              tag = mServiceDataId,
              url = "/config/dashboard",          // iOS URL
              clickAction = "/config/dashboard",  // Android URL
              actions = new List<object>
              {
                new
                {
                  action = "URI",
                  title = "Open Addons",
                  uri = "/hassio/dashboard"
                },
                new
                {
                  action = "URI",
                  title = "Open HACS",
                  uri = "/hacs"
                },
              }
            }
          });

        if (mShowiOSBadge)
        {
          mHaContext.CallService("notify", notifyService, data: new
            {
              message = "delete_alert",
              data = new
              {
                push = new
                {
                  badge = badgeCounter
                }
              }
            });
        }
      }
    }
    else
    {
      // persistent notification
      if (mPersistentNotification)
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

        if (mShowiOSBadge)
        {
          mHaContext.CallService("notify", notifyService, data: new
            {
              message = "delete_alert",
              data = new
                {
                  push = new
                  {
                    badge = 0
                  }
                }
            });
        }
      }
    }
  }
}

enum UpdateType
{
  HomeAssistant, Addon, Hacs, Entity
}

enum UpdateMechanism
{
  RestAPI, UpdateEntities
}

internal class UpdateText
{
  public UpdateType Type { get; }
  public string? Name { get; set; }
  public string? Path { get; set; }
  public string? CurrentVersion { get; set; }
  public string? NewVersion { get; set; }
  public string? EntityId { get; set; }
  public int Hash { get; private set;}

  public UpdateText(UpdateType type)
  {
    Type = type;
  }
  public UpdateText(UpdateType type, string? name, string? currentVersion, string? newVersion, string? path = null)
  {
    Type = type;
    Name = name;
    Path = path;
    CurrentVersion = currentVersion;
    NewVersion = newVersion;
    Hash = HashCode.Combine(type, name, path, currentVersion, newVersion);
  }

  public void CalcHash()
  {
    Hash = HashCode.Combine(Type, Name, Path, CurrentVersion, NewVersion, EntityId);
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

record UpdateAttributes
{
  public bool? auto_update { get; init; }
  public string? installed_version { get; init; }
  public bool? in_progress { get; init; }
  public string? latest_version { get; init; }
  public string? release_summary { get; init; }
  public string? release_url { get; init; }
  public string? skipped_version { get; init; }
  public string? title { get; init; }
  public string? entity_picture { get; init; }
  public string? friendly_name { get; init; }
  public int? supported_features { get; init; }
}
