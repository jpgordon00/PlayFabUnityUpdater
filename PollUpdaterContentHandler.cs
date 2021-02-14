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
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net;
using Newtonsoft.Json;
using System.Text;
using PlayFab.Plugins.CloudScript;
using PlayFab.Samples;
using System.Text;
using SimpleJSON;

namespace PlayFab.AzureFunctions
{
    public static class PollUpdaterContentHandler
    {

        [FunctionName("PollUpdaterContent")]
        public static async Task<dynamic> PollUpdaterContent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            var context = await FunctionContext<dynamic>.Create(req);
            if (context == null) return new { error = true };
            var serverApi = new PlayFabServerInstanceAPI(context.ApiSettings, context.AuthenticationContext);

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
            var userReadOnlyDataResponse = await serverApi.GetUserReadOnlyDataAsync(new GetUserDataRequest { PlayFabId = context.CallerEntityProfile.Lineage.MasterPlayerAccountId, Keys = new List<string>() { "Version" } });
            foreach (JSONNode version in versions)
            {
                if (version["name"] == newestVersionStr) newestVersion = version;
                if (userReadOnlyDataResponse.Result != null) if (userReadOnlyDataResponse.Result.Data != null) if (userReadOnlyDataResponse.Result.Data.ContainsKey("Version")) if (userReadOnlyDataResponse.Result.Data["Version"].Value == version["name"]) currentVersion = version;
            }
            if (newestVersion == null) return new { error = true, msg = "CurrentVersion '" + newestVersionStr + "' does not exist in Versions" };
            if (currentVersion == null) return new { error = true, msg = "Player version not set yet" };

            /* Gather filename, contentKey and name for each item in content*/
            List<ReturnContent> content = new List<ReturnContent>();
            for (int i = 0; i < currentVersion["content"].Count; i++)
            {
                content.Add(new ReturnContent
                {
                    filename = currentVersion["content"][i]["filename"],
                    contentKey = currentVersion["name"] + "/" + currentVersion["content"][i]["contentKey"],
                    name = currentVersion["content"][i]["name"]
                });
            }
            string cv = currentVersion["name"];
            return new { currentVersion = cv, content = content };
        }
    }

    public class ReturnContent
    {
        public string filename, contentKey, name;
    }
}