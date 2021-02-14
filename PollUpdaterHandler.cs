using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using PlayFab.ServerModels;
using PlayFab.Json;
using System.Collections.Generic;
using PlayFab.DataModels;
using System.Net.Http;
using System.Net.Http.Headers;
using PlayFab.Plugins.CloudScript;
using PlayFab.Samples;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net;
using Newtonsoft.Json;
using SimpleJSON;

namespace PlayFab.AzureFunctions
{
    public static class PollUpdaterHandler
    {

        [FunctionName("PollUpdater")]
        public static async Task<dynamic> PollUpdater(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            string body = await req.Content.ReadAsStringAsync();
            PlayerPlayStreamFunctionExecutionContext<object[]> context = JsonConvert.DeserializeObject<PlayerPlayStreamFunctionExecutionContext<object[]>>(body);
            if (context.PlayStreamEventEnvelope.EventName != "player_logged_in") return new { error = true, msg = "Event must be player_logged_in"};
            string CurrentPlayerId = context.PlayerProfile.PlayerId;
            var apiSettings = new PlayFabApiSettings 
            { 
  	            TitleId = context.TitleAuthenticationContext.Id,
  	            DeveloperSecretKey = Environment.GetEnvironmentVariable("PLAYFAB_DEV_SECRET_KEY",EnvironmentVariableTarget.Process),
            }; 
            var serverApi = new PlayFabServerInstanceAPI(apiSettings);

            /* Gather JSON versions */
            var titleDataResponse = await serverApi.GetTitleDataAsync(new GetTitleDataRequest { Keys = new List<string>() { "CurrentVersion" } });
            var titleInternalDataResponse = await serverApi.GetTitleInternalDataAsync(new GetTitleDataRequest { Keys = new List<string>() { "Versions" } });
            string newestVersionStr = titleDataResponse.Result.Data["CurrentVersion"];
            if (newestVersionStr == null) return new { error = true, msg = "Incorrect 'CurrentVersion' in TitleData"};
            JSONNode versions = null; // json of all versions existing in internal title data 'Versions'
            JSONNode newestVersion = null; // newest version subchild of 'Version'
            JSONNode currentVersion = null; // null if no exisitng version
            versions = JSON.Parse(titleInternalDataResponse.Result.Data["Versions"]);
            if (versions == null) return new { error = true, msg = "Incorrect 'Versions' in InternalTitleData" };
            var userReadOnlyDataResponse = await serverApi.GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayFabId = CurrentPlayerId, Keys = new List<string>() { "Version" } });
            foreach (JSONNode version in versions)
            {
                if (version["name"] == newestVersionStr) newestVersion = version;
                if (userReadOnlyDataResponse.Result != null) if (userReadOnlyDataResponse.Result.Data != null) if (userReadOnlyDataResponse.Result.Data.ContainsKey("Version")) if (userReadOnlyDataResponse.Result.Data["Version"].Value == version["name"]) currentVersion = version;
            }
            if (newestVersion == null) return new { error = true, msg = "CurrentVersion '" + newestVersionStr + "' does not exist in Versions" };
            bool doesExpectUpdate = false;
            if (currentVersion == null)
            {
                /* Case new login */
                doesExpectUpdate = true;
                await serverApi.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest
                {
                    PlayFabId = CurrentPlayerId,
                    Data = new Dictionary<string, string>() { ["Version"] = newestVersion["name"] }
                });
                await serverApi.UpdatePlayerStatisticsAsync(new UpdatePlayerStatisticsRequest
                {
                    PlayFabId = CurrentPlayerId, 
                    Statistics = new List<StatisticUpdate>
                    {
                        new StatisticUpdate
                        {
                            StatisticName = "CurrentVersion",
                            Value = newestVersion["id"].AsInt,
                            Version = null
                        },
                        new StatisticUpdate
                        {
                            StatisticName = "NumVersionUpdates",
                            Value = 0,
                            Version = null
                        }
                    }
                });
                await serverApi.WritePlayerEventAsync(new WriteServerPlayerEventRequest
                {
                    PlayFabId =  CurrentPlayerId,
                    EventName = "version_updated",
                    Body = new Dictionary<string, object>()
                    {
                        ["NewVersion"] = new
                        {
                            name = newestVersion["name"].ToString(),
                            id = newestVersion["id"].AsInt,
                            buildVersion = newestVersion["buildVersion"].ToString(),
                        }
                    }
                });
            }
            else if (newestVersion["id"].AsInt > currentVersion["id"].AsInt)
            {
                /* Case new update found */
                doesExpectUpdate = true;
                var playerStatisticsResponse = await serverApi.GetPlayerStatisticsAsync(new GetPlayerStatisticsRequest{PlayFabId = CurrentPlayerId, StatisticNames = new List<string>(){"NumVersionUpdates"}});
                int numVersionChanges = playerStatisticsResponse.Result.Statistics[0].Value;
                await serverApi.UpdateUserReadOnlyDataAsync(new UpdateUserDataRequest
                {
                    PlayFabId = CurrentPlayerId,
                    Data = new Dictionary<string, string>() { ["Version"] = newestVersion["name"] }
                });
                await serverApi.UpdatePlayerStatisticsAsync(new UpdatePlayerStatisticsRequest
                {
                    PlayFabId = CurrentPlayerId,
                    Statistics = new List<StatisticUpdate>
                    {
                        new StatisticUpdate
                        {
                            StatisticName = "CurrentVersion",
                            Value = newestVersion["id"].AsInt,
                            Version = 0
                        },
                        new StatisticUpdate
                        {
                            StatisticName = "NumVersionUpdates",
                            Value = numVersionChanges + 1,
                            Version = 0
                        }
                    }
                });
                await serverApi.WritePlayerEventAsync(new WriteServerPlayerEventRequest
                {
                    PlayFabId = CurrentPlayerId,
                    EventName = "version_updated",
                    Body = new Dictionary<string, object>()
                    {
                        ["LastVersion"] = new
                        {
                            name = currentVersion["name"].ToString(),
                            id = currentVersion["id"].AsInt,
                            buildVersion = currentVersion["buildVersion"].ToString(),
                        },
                        ["NewVersion"] = new
                        {
                            name = newestVersion["name"].ToString(),
                            id = newestVersion["id"].AsInt,
                            buildVersion = newestVersion["buildVersion"].ToString(),
                        }
                    }
                });
            }
            else
            {
                /* Case no new update found*/
            }

            return new { expectUpdate = doesExpectUpdate };
        }
    }
}