# NetDaemon App: Update Notifications
A NetDaemon App that will create a persistent notification if there is an update available.  
  
  
This App will display updates for:  
- Home Assistant Core
- Home Assistant OS
- Home Assistant Supervisor
- Home Assistant Addon
- HACS Repository
  
  
There are 3 Options available to modify this App:  
- UpdateTimeInSec
  - Default Value: 30
  - Description: defines the update time to serch for new updates
- NotifyTitle:
  - Default Value: Update available in Home Assistant
  - Description: defines the title of the persistent notification
- NotifyId:
  - Default Value: updates_available
  - Description: defines the id of the persistent notification
  
  
This App was build with help of this Home Assistant Community Thread:  
https://community.home-assistant.io/t/update-notifications-core-hacs-supervisor-and-addons/182295
