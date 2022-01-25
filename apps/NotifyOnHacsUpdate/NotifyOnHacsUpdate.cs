using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using NetDaemon.AppModel;
using NetDaemon.HassModel.Common;
using NetDaemon.HassModel.Entities;

// Use unique namespaces for your apps if you going to share with others to avoid
// conflicting names
namespace NotifyOnUpdate;

public class NotifyOnHacsUpdateConfig
{
  public string? Title { get; set; }
}

/// <summary>
/// Notifies of new updates in HACS
/// </summary>
[NetDaemonApp]
public class NotifyOnHacsUpdate
{
  private readonly ILogger<NotifyOnHacsUpdate> mLogger;

  public NotifyOnHacsUpdate(IHaContext ha,
                            IAppConfig<NotifyOnHacsUpdateConfig> config,
                            ILogger<NotifyOnHacsUpdate> logger)
  {
    mLogger = logger;
    mLogger.LogInformation("NotifyOnHacsUpdate started");

    var hacs = new NumericEntity<HacsAttributes>(ha, "sensor.hacs");
    hacs.StateAllChanges().Subscribe(s =>
      {
        var serviceDataTitle = "Updates pending in HACS";
        var serviceDataMessage = "[HACS](/hacs)\n\n";
        var serviceDataId = "updates_available";

        if (s.New?.State > 0)
        {
          var hacsRepositories = s.New?.Attributes?.repositories;

          if (hacsRepositories == null) return;
          foreach(var repo in hacsRepositories)
          {
            serviceDataMessage += $"* **{repo.display_name?.ToString()}** {repo.installed_version?.ToString()} -> {repo.available_version?.ToString()}\n";
          }

          ha.CallService("persistent_notification", "create", data: new
          {
            title = serviceDataTitle,
            message = serviceDataMessage,
            notification_id = serviceDataId
          });
        }
        else
        {
          ha.CallService("persistent_notification", "dismiss", data: new
          {
            notification_id = serviceDataId
          });
        }
      });
  }
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
