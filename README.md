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
  - Default Value: 30
  - Description: defines the update time in seconds to search for new updates
- NotifyTitle:
  - Default Value: Update available in Home Assistant
  - Description: defines the title of the persistent notification
- NotifyId:
  - Default Value: updates_available
  - Description: defines the id of the persistent notification
  
  
This App was build with help of this Home Assistant Community Thread:  
https://community.home-assistant.io/t/update-notifications-core-hacs-supervisor-and-addons/182295
