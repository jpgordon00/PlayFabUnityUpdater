# PlayFabUnityUpdater
A server-authoritative auto-updater for Unity and PlayFab.

## What frameworks/libraries are involved?
- .NET Core and C#
- [PlayFab](https://playfab.com/) as the backend-as-a-service.
> PlayFab provides file hosting through their environment and Microsoft's CDN. It also allows use to host code that has SDK access to their environment.
- [Azure Functions V3](https://docs.microsoft.com/en-us/azure/azure-functions/functions-versions) for writing C# [CloudScript functions](https://docs.microsoft.com/en-us/gaming/playfab/features/automation/cloudscript-af/).
- [SimpleJSON](https://github.com/HenrikPoulsen/SimpleJSON) for JSON parsing.

## What does it do?
- Keeps a list of versions and various attributes associated with the version, on a per-player basis.
> Versions are stored as JSON in [internal title data](https://docs.microsoft.com/en-us/gaming/playfab/features/data/titledata/quickstart)Players are assigned the newest version on login, stored as [internal player data](https://docs.microsoft.com/en-us/rest/api/playfab/server/player-data-management/getuserinternaldata?view=playfab-rest). Versions are updated upon login if a newer version is found. Every update is written as a [PlayStream event](https://docs.microsoft.com/en-us/rest/api/playfab/events/playstream-events/writeevents?view=playfab-rest). 
- Serves players additional info for each file served from the PlayFab CDN.
> Files are requested through a Cloudscript Function that includes the files URI, a unique name, and a name for the resulting file when downloaded. A use case for this would be to identify each file through the 'Name' attribute. Another use case for this would be to modify the function to include additional metadata.

## How do I use this?
- Add the field 'Versions' as JSON in internal title data.
    - The object name is 'Versions' and is a JSON object containing ananonomyous array. 
    - The newsest version at any time is the version with the largest "id" attribute. These should be unique.
    - Each version has the following properties:
        - name, a unique string identifier for the version.
        - id, a unique integer identifier where the largest version is the version assigned to players.
        - buildVersion, a string where the a matching build version for [PlayFab Matchmaking 2.0](https://docs.microsoft.com/en-us/gaming/playfab/features/multiplayer/matchmaking/) can be used.
        - content, a array of objects containing name, filename and contentKey for each file. The attribute 'contentKey' should match the content key for the given file in the CDN, which should be a path.
- Pushing a new update requires:
    - Adding a version to Versions whose attribute "id" is larger than all previous versions.
    - Change CurrentVersion in title data to a string matching the attribute "title" in the version with the largest attribute "id". 
