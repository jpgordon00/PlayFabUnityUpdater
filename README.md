# PlayFabUnityUpdater
A server-authoritative auto-updater for Unity and PlayFab.

# What frameworks/libraries are involved?
- .NET Core and C#
- [PlayFab](https://playfab.com/) as the backend-as-a-service.
> PlayFab provides file hosting through their environment and with Microsoft Azure's CDN. It also allows the hosting of private code that has access to their environment via an SDK.

> This particular project requires the [PlayFab Unity SDK](https://docs.microsoft.com/en-us/gaming/playfab/sdks/unity3d/). 
- [Azure Functions V3](https://docs.microsoft.com/en-us/azure/azure-functions/functions-versions) for writing C# [CloudScript functions](https://docs.microsoft.com/en-us/gaming/playfab/features/automation/cloudscript-af/).
- [SimpleJSON](https://github.com/HenrikPoulsen/SimpleJSON) for JSON parsing on client and server.
- > While both PlayFab and Unity provides their own JSON serialization and deserialization, I found it useful to use a single resource in my client and server code. This partciular library is a single .cs file that supports JSON deserialization only. In the Azure Functions I used PlayFab serialization to serialiize the returning of JSON objects.
- [GroupDownloader](https://github.com/jpgordon00/UnityGroupDownloader) for 'one-by-one' downloading of multiple files using [UnityWebRequest](https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequest.html).

# What does it do?
- Allows the developer to push content updates at will, where clients are updated in a server authoratative manner.
> Updates are queried by the response to logged in event, meaning this process is server authoratitve . Each update is identified by an integer named ID, where a player is updated only if an update exists with a larger ID integer than the players current version. For example, if all updates are kept in increasing order then the player is guarenteed to have the newest version. Another example would be to seperate updates by more than one numeric value, for example two at a time. By doing this, you could release intermediate updates that new players are updated to but existing players are not.
- Keeps a list of versions and various attributes associated with the version, on a per-player basis.
> Versions are stored as JSON in [internal title data](https://docs.microsoft.com/en-us/gaming/playfab/features/data/titledata/quickstart). Players are assigned the newest version on login, stored as [internal player data](https://docs.microsoft.com/en-us/rest/api/playfab/server/player-data-management/getuserinternaldata?view=playfab-rest). Versions are updated upon login if a newer version is found. Every update is written as a [PlayStream event](https://docs.microsoft.com/en-us/rest/api/playfab/events/playstream-events/writeevents?view=playfab-rest). [PlayFab statistics](https://docs.microsoft.com/en-us/gaming/playfab/features/data/playerdata/using-player-statistics) are written to track current version and number of version updates for each player.
- Serves a set of files specified in the version that player is using, where each file is served from the PlayFab CDN.
> Players with different versions can exist at the same time, since the function serves content from that players current version. 
- All versions have completely seperate files stored in the PlayFab CDN, so multiple versions and players using those versions can exist simulatenously.
> Files are requested through a Cloudscript Function that includes the files URI, a unique name, and the resulting file name. A use case for this would be to identify each file through the 'Name' attribute. Another use case for this would be to modify the function to include additional metadata for each version or each file.
- Client requests URI for and only downloads missing files.
- Incase of error during a download, the updater removes partially downloaded files.
> UpdateHandler.cs provides access to elapsed time, progress and how many times the update process is re-invoked.

> UpdateHandler.cs provides cleanup in the case of a new update downloaded. All base folders not matching the used version is always deleted, upon update invokation.

# What I learned.
- Best practices for deploying and developing Azure Functions apps using [Visual Studio](https://visualstudio.microsoft.com/) and [Visual Studio Code](https://code.visualstudio.com/).
- Best practices for data storage and player management using PlayFab.
- This project's design decision to be as modular as possible saved me a significant amount of development time. While this project was created for a multiplayer game, it was very easy to extract and upload as a standalone component.
- JSON parsing and async programming in C#.

# How do I use this?
## Setup:
- Add the field 'Versions' as JSON in internal title data.
    - The object name is 'Versions' and is a JSON object containing ananonomyous array. 
    - The newsest version at any time is the version whose name matches the string defined in 'CurrentVersion' in title data. An update is issued ONLY if the current version has a larger "id" attribute than the players current version, or if the player has no version assigned yet.
    - Each version has the following properties:
        - name, a unique string identifier for the version.
        - id, a unique integer identifier where the largest version is the version assigned to players.
        - buildVersion, a string where the a matching build version for [PlayFab Matchmaking 2.0](https://docs.microsoft.com/en-us/gaming/playfab/features/multiplayer/matchmaking/) can be used.
        - content, a array of objects containing name, filename and contentKey for each file. The attribute 'contentKey' should match the content key for the given file in the CDN, which should be a path.
- In title data, Set the current update version by setting the attribute 'CurrentVersion' to be a string matching a version title.
- Add all the required files listed in 'content', the PlayFab CDN in the developer portal, for whatever versions you want to support into the PlayFab CDN.
> The content key points to a folder with with the current versions 'name' attribute as its folder name. For example, if the content key is "coffee.png" and the version is "DEV", then the folder should be "DEV/coffee.png".
- Deploy two Azure Functions and register them on PlayFab Cloudscript Functions.
> [Deploy](https://docs.microsoft.com/en-us/azure/devops/pipelines/targets/azure-functions?view=azure-devops&tabs=dotnet-core%2Cyaml) PollUpdater and PollUpdaterContent to the cloud. Ensure you have SimpleJSON.cs somewhere in your source.
> Register PollUpdater and PollUpdaterContent in [PlayFab Functions](https://docs.microsoft.com/en-us/gaming/playfab/features/automation/cloudscript-af/quickstart)
> In [Automation](https://docs.microsoft.com/en-us/gaming/playfab/features/automation/), register PollUpdater for a player_logged_in event.
- Add UpdateHandler.cs to your Unity project and ensure PlayFab is authenticated before invoking UpdateHandler.Instance.UpdateProcedure().
- Add GroupDownloader.cs to your Unity project.

## Pushing a new update:
- Optionally add a new version to Versions whose attribute "id" is larger than all previous versions. Only players whose version ID's are explicitly lower than the 'CurrentVersion' are updated.
- Optionally Change CurrentVersion in title data to a string matching the attribute "title" in the version with the largest attribute "id". All new players are updated to the 'CurrentVersion' regardless of its ID attribute.
> The 'CurrentVersion' string must match an existing version 'name' property or the update will fail.
- Optionally change the content in the new version with matching files in the PlayFab CDN.

## A visual view of all the components involved in configuring new updates:
![Versions in Internal Title Data](https://i.gyazo.com/d9f8fe798877b3f6e2d21a166d1bab4a.png)
![Versions JSON](https://i.gyazo.com/7942f1fe2e9bf4c3b664f18dfdc34b14.png)
![CurrentVersion in title data](https://i.gyazo.com/bac0068a2f19ec4e06296136d0681803.png)
![Files for versions in File Management](https://i.gyazo.com/32642f0fe8e07a7c0675046e4bdf3db1.png)

# Future improvements.
- Keep seperate version lists for each code version.
> If we kept some unique object for every "code version", then we could ensure that downloaded update data is tailored for the current client app. Because the app store and google store both allow in-store updates, developers likely will want seperate assets/version lists for each different update. You may also want to force users to download an update on the store to have access to the newest version (which would be newest version list and version within that list). Alternatively, you couldc create custom upgrade paths within this upgrade tree to describe the upgrade path for any nuber of upgrade situations.
- Callbacks for update starting and update failing.
> I encourage developers to edit UpdateHandler.cs and invoke whatever functions they want themselfs. However, if requested, I will add callbacks in the form of events to UpdateHandler.
- UpdateHandler.cs by default can recursively re-invoke the PollUpdaterContent function. While this was done because newly created accounts don't always update PlayFab data quickly enough for PollUpdaterContent to be succesful, I would like to expand on this system. It is not flexible in handling a multitude of errors.
- The updater does not support large files. Since files are never left partially downloaded, exteremely large files would be lost if a download interuption existed at any point during the download.
> A solution would be to break down large files into smaller chunks. This would mean download progress would not be lost in case of an interuption.
- Files can be loaded into memory straight from a FileManifest object.
> This is partially implemented in the ProcessManifest function.

## Remarks.
- Developers should use this auto-updater to download Unity AssetBundles.
- This updater fails gracefully.
> The Cloudscript Functions return errors in the case of inproper setup (for example not finding a correct version).

> The client invokes error callbacks and sets (optionally) low timeout intervals for download updates.
- This updater is efficient in that it only downloads missing files and can support a massive amounts of smaller files. 
- The code heavily is commented.
