[![hacs_badge](https://img.shields.io/badge/HACS-Default-41BDF5.svg)](https://github.com/hacs/integration)
[![netdaemon_badge](https://img.shields.io/badge/NetDaemon-v3-pink)](https://netdaemon.xyz/docs/v3)

# Update Notifications
A NetDaemon App that will create a persistent notification in Home Assistant if there is an update available.  
  
  
This App will display updates for:  
- Home Assistant Core
- Home Assistant OS
- Home Assistant Supervisor
- Home Assistant Addon
- HACS Repository
  
### Installation
1. Install the [NetDaemon v3](https://netdaemon.xyz/docs/v3/started/installation) Addon
2. Install [HACS](https://hacs.xyz/docs/setup/download)
2. Go to HACS -> Automation -> Add repository -> Search for "Notify on Update"
3. Download this repository with HACS
4. Restart the NetDaemon v3 Addon

### Configuration  
There are 3 Options available to modify this App:  
- UpdateTimeInSec
  - default: 30
  - description: Defines the update time in seconds to search for new updates
- NotifyTitle:
  - default: Update available in Home Assistant
  - description: Defines the title of the persistent notification
- NotifyId:
  - default: updates_available
  - description: Defines the id of the persistent notification
- PersistentNotification:
  - default: true
  - description: The persistent notification may be disabled if only mobile notifications are preferred
- MobileNotifyServices:
  - default: none
  - description: The user may define a list of notify services for mobile apps like ios or android [companion app](https://companion.home-assistant.io/).
                 If the notify service is valid then a notify message will be sent to your mobile device as soon there is an update available.
                 The notify service my be definded like "notify.mobile_app_myphone" or just "mobile_app_myphone".
  
This App was build with help of this Home Assistant Community Thread:  
https://community.home-assistant.io/t/update-notifications-core-hacs-supervisor-and-addons/182295
