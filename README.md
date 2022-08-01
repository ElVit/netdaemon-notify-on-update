[![hacs_badge](https://img.shields.io/badge/HACS-Default-41BDF5.svg)](https://github.com/hacs/integration)
[![netdaemon_badge](https://img.shields.io/badge/NetDaemon-v3-pink)](https://netdaemon.xyz/docs/v3)

# Update Notifications
A NetDaemon App that will notify you if there is an update available in Home Assistant.  
This notification can be sent as a persistent notification or to your mobile devices if you are using the [companion app](https://companion.home-assistant.io/).  

This App can display updates for:  
- Home Assistant Core
- Home Assistant OS
- Home Assistant Supervisor
- Home Assistant Addon's
- HACS Repositories
  
## Installation
1. Install the [NetDaemon v3](https://netdaemon.xyz/docs/v3/started/installation) Addon
2. Change the addon option "app_config_folder" to "/config/netdaemon" (since NetDaemon v3 is still in beta)
3. Install [HACS](https://hacs.xyz/docs/setup/download)
4. In Home Assistant go to HACS -> Automation -> Add repository -> Search for "Notify on Update"
5. Download this repository with HACS
6. Restart the NetDaemon v3 Addon

## Configuration  

Example configuration:

```yaml
NotifyOnUpdate.NotifyOnUpdateConfig:
  UpdateTimeInSec: 60
  NotifyTitle: 🎉 Updates available 🎉
  NotifyId: updates_available
  PersistentNotification: true
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
*Default:* `30`

### Option: `NotifyTitle`

Defines the title of the notification.  
*Default:* `Update available in Home Assistant`

### Option: `NotifyId`

Defines the id of the notification so it can be updated.  
*Default:* `updates_available`  

### Option: `PersistentNotification`

The persistent notification can be disabled if only mobile notifications are preferred.  
*Default:* `true`  

### Option: `GetUpdatesFor`

Here you can define a list of update types to be displayed when a new update is available.  
Possible update types are: &nbsp; `Core, OS, Supervisor and HACS`.  
*Default:* `(all possible types, see above)`  

__NOTE:__ If the `Supervisor` updates are disabled then you will also NOT be notified about any Home Assistant addon updates.  

### Option: `MobileNotifyServices`

A list of notify services for mobile apps like the iOS or Android [companion app](https://companion.home-assistant.io/).  
If the notify service is valid then a notify message will be sent to your mobile device as soon as there is an update available.  
The notify service can be definded like "notify.mobile_app_myphone" or just "mobile_app_myphone".  
*Default:* `none`  

## Contribution
This App was developed with help of this Home Assistant Community Thread:  
https://community.home-assistant.io/t/update-notifications-core-hacs-supervisor-and-addons/182295  

Also a special thanks to [FrankBakkerNl](https://github.com/FrankBakkerNl) and [helto4real](https://github.com/helto4real) for their support during development via [discord](https://discord.gg/K3xwfcX).