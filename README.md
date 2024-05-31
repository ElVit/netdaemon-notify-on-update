[![hacs_badge](https://img.shields.io/badge/HACS-Default-41BDF5.svg)](https://github.com/hacs/integration)
[![netdaemon_badge](https://img.shields.io/badge/NetDaemon-v3-pink)](https://netdaemon.xyz/docs/v3)

# DEPRECATED
NetDaemon apps are deprecated and will be removed in version 2.0.0 of HACS.  
Note that apps that use current V3 version of the NetDaemon runtime is not supported at all.  
[See HACS Notice](https://hacs.xyz/docs/categories/netdaemon_apps/)

---

# NetDaemonApp: Notify on Update
A NetDaemon App that will notify you if there is an update available in Home Assistant.  
This notification can be set as a persistent notification or send to your mobile devices if you are using the [companion app](https://companion.home-assistant.io/).  

This App can display updates for:  
- Home Assistant Core
- Home Assistant OS
- Home Assistant Supervisor
- Home Assistant Addon's
- HACS Repositories

## Installation
1. Install the [NetDaemon V3.X](https://netdaemon.xyz/docs/v3/started/installation) Addon
2. Change the addon option "app_config_folder" to "/config/netdaemon"
3. Install [HACS](https://hacs.xyz/docs/setup/download)
4. In Home Assistant go to HACS -> Automation -> Add repository -> Search for "Notify on Update"
5. Download this repository with HACS
6. Restart the NetDaemon V3 Addon

## Configuration  

Example configuration:

```yaml
NotifyOnUpdate.NotifyOnUpdateConfig:
  UpdateTimeInSec: 60
  NotifyTitle: 🎉 Updates available 🎉
  NotifyId: updates_available
  PersistentNotification: true
  ShowiOSBadge: true
  GetUpdatesMechanism: rest_api
  GetUpdatesFor:
    - Core
    - OS
    - Supervisor
    - HACS
  MobileNotifyServices:
    - notify.mobile_app_myphone
```

## Options:

### Option: `UpdateTimeInSec`

Defines the update time in seconds to search for new updates.  
This time does not apply to HACS repository updates because they are taken instantly from the "sensor.hacs" entity.  
*Default value:* &nbsp;&nbsp;&nbsp; `30`

### Option: `NotifyTitle`

Defines the title of the notification.  
*Default value:* &nbsp;&nbsp;&nbsp; `Update available in Home Assistant`

### Option: `NotifyId`

Defines the id of the notification so it can be updated.  
*Default value:* &nbsp;&nbsp;&nbsp; `updates_available`  

### Option: `PersistentNotification`

The persistent notification can be disabled if only mobile notifications are preferred.  
*Default value:* &nbsp;&nbsp;&nbsp; `true`  

### Option: `ShowiOSBadge`

If set to `true` you will see the count of updates in the app icon badge of the iOS [companion app](https://companion.home-assistant.io/).  
*Default value:* &nbsp;&nbsp;&nbsp; `true`  

### Option: `GetUpdatesMechanism`

Home Assistant 2022.4 introduced a new feature called [update entity](https://www.home-assistant.io/integrations/update/).  
To use this feature just set this option to `update_entities`.  
To use the old mechanism to get updates set this option to `rest_api`.  
*Possible values:* &nbsp; `update_entities, rest_api`.  
*Default value:* &nbsp;&nbsp;&nbsp; `update_entities`  

__NOTE:__ If this option is set to `update_entities` then the option `GetUpdatesFor` will have no effect.  

### Option: `GetUpdatesFor`

Here you can define a list of update types to be displayed when a new update is available.  
*Possible values:* &nbsp; `Core, OS, Supervisor, HACS`.  
*Default value:* &nbsp;&nbsp;&nbsp; `none`  

__NOTE:__ If the `Supervisor` updates are disabled then you will also NOT be notified about any Home Assistant addon updates.  

### Option: `MobileNotifyServices`

A list of notify services for mobile apps like the iOS or Android [companion app](https://companion.home-assistant.io/).  
If the notify service is valid then a notify message will be sent to your mobile device as soon as there is an update available.  
The notify service can be definded like "notify.mobile_app_myphone" or just "mobile_app_myphone".  
*Default value:* &nbsp;&nbsp;&nbsp; `none`  

## Contribution
This App was developed with help of this Home Assistant Community Thread:  
https://community.home-assistant.io/t/update-notifications-core-hacs-supervisor-and-addons/182295  

Also a special thanks to [FrankBakkerNl](https://github.com/FrankBakkerNl) and [helto4real](https://github.com/helto4real) for their support during development via [discord](https://discord.gg/K3xwfcX).
