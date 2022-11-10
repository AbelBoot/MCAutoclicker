using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

using static autoClickerMN.PersonalHelpers;
//using static autoClickerMN.Expedition;

namespace autoClickerMN
{

    public sealed class MCTestAgent
    {
        static bool isFirstLogin = true;

        private static string Response = string.Empty;
        private  static string uri = string.Empty;
        private static JObject contentsJson = new JObject();
        private static List<int> championsIds = new List<int>();
        private static List<int> monstersIds = new List<int>();
        private static HttpClient Client = new HttpClient();
        private static Timer timerLogin;
        private static Timer timerExp;

        static JObject expeditionObject = new JObject(
            new JProperty("championsIds", championsIds),
            new JProperty("monstersIds", monstersIds),
            new JProperty("accessToken", "")
            );

        static Task Main(string[] args)
        {
            //No need to dispose the timer as it will be automatically
            //disposed once we stop the program (?).
            TimerCallback timerCallback = new TimerCallback(Login);
            timerLogin = new Timer(timerCallback, null, 1000, 88200000);//24 heures et 30 inuutes//11100000); //185 minutes (mind the minute added at each expTimer, so we add 2 min)
            Console.ReadLine();
            return null;
        }
        static async void Login(object whatever)
        {
            #region First 2 Get requests

            if (!isFirstLogin)
            {
                //We need to create a new instance for each login not to mess things up (remove auth among others)
                //timerExp should be stopped here since we gonna start a new one once we have logged in again.
                Client = new HttpClient();
                timerExp.Change(Timeout.Infinite, Timeout.Infinite);
                timerExp.Dispose();
            }

            if (!Client.DefaultRequestHeaders.Contains("User-Agent"))
                Client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/104.0.0.0 Safari/537.36");

            var result = await Client.GetAsync("https://dashboard.monsterchampions.xyz/");

            var codeDict = GetAuthCodes(128);
            var reauthDict = new Dictionary<string, string>
            {
                ["client_id"] = "EpzLGXTElJ7NBO7rSJBIepWbY1da8EsY",
                ["redirect_uri"] = "https%3A%2F%2Fdashboard.monsterchampions.xyz%2Fchampions",
                ["audience"] = "api.monsterchampions.xyz",
                ["scope"] = "openid%20profile%20email%20admin",
                ["response_type"] = "code",
                ["response_mode"] = "web_message",
                ["state"] = "",
                ["nonce"] = "",
                ["code_challenge"] = codeDict["challenge"],
                ["code_challenge_method"] = "S256",
                ["auth0Client"] = "eyJuYW1lIjoiYXV0aDAtdnVlIiwidmVyc2lvbiI6IjEuMC4xIn0%3D",
            };
            var reauthDictString = GetPostParametersString(reauthDict);
            result = await Client.GetAsync($"https://dev-ehwvbbmf.us.auth0.com/authorize?{reauthDictString}");
            var contents = await result.Content.ReadAsStringAsync();
            #endregion

            #region Getting access token 
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(contents);
            var formNode = doc.DocumentNode.SelectSingleNode("//form[@method='POST']");
            if (formNode == null)
            {
                Console.WriteLine("No form.");
                return;
            }

            var paramDict = GetPostParameters(formNode);
            if (paramDict == null)
            {
                Console.WriteLine("No parameters in form.");
                return;
            }

            paramDict["username"] = "XXXX";
            paramDict["password"] = UrlEncoder("XXXX");
            paramDict["action"] = "default";
            StringContent parameters = new StringContent(GetPostParametersString(paramDict),
                                    Encoding.UTF8,
                                    "application/x-www-form-urlencoded");
            uri = "https://dev-ehwvbbmf.us.auth0.com/u/login?state=" + paramDict["state"];
            //First POST
            result = await Client.PostAsync(uri, parameters);
            Response = await result.Content.ReadAsStringAsync();

            var code = GetStringBetween(Response, "code\":\"", "\"");
            if (string.IsNullOrEmpty(code))
            {
                Console.WriteLine("No code to login.");
                return;
            }

            var paramJson = new JObject {
                new JProperty("client_id", "EpzLGXTElJ7NBO7rSJBIepWbY1da8EsY"),
                new JProperty("code_verifier", codeDict["verifier"]),
                new JProperty("code", code),
                new JProperty("grant_type", "authorization_code"),
                new JProperty("redirect_uri", "https://dashboard.monsterchampions.xyz/champions"),
            };

            parameters = new StringContent(paramJson.ToString(), Encoding.UTF8, "application/json");
            uri = "https://dev-ehwvbbmf.us.auth0.com/oauth/token";
            //Second POST
            result = await Client.PostAsync(uri, parameters);
            contents = await result.Content.ReadAsStringAsync();

            contentsJson = JObject.Parse(contents);
            var accessToken = (string)contentsJson.SelectToken("access_token");
            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("No access token.");
                return;
            }

            Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken); //This gives the access
            #endregion

            #region getting champion and monsters Ids
            //We need to get these only the first time.
            if (isFirstLogin)
            {
                //If needed the champions, monster id, other gold or other things (GET)
                uri = "https://api.monsterchampions.xyz/v1/player/me?include-off-chain=false";
                result = await Client.GetAsync(uri);
                contents = await result.Content.ReadAsStringAsync();

                contentsJson = JObject.Parse(contents);
                var championNodes = contentsJson.SelectToken("champions");
                var monsterNodes = contentsJson.SelectToken("monsters");
                if (championNodes == null || monsterNodes == null)
                {
                    Console.WriteLine("No champions and or monsters.");
                    return;
                }

                foreach (var championNode in championNodes)
                {
                    var championId = (string)championNode.SelectToken("nftId");
                    if (!string.IsNullOrEmpty(championId))
                    {
                        int championIdInt;
                        if (int.TryParse(championId, out championIdInt))
                        {
                            championsIds.Add(championIdInt);
                        }
                    }
                }
                foreach (var monsterNode in monsterNodes)
                {
                    var monsterId = (string)monsterNode.SelectToken("nftId");
                    if (!string.IsNullOrEmpty(monsterId))
                    {
                        int monsterIdInt;
                        if (int.TryParse(monsterId, out monsterIdInt))
                        {
                            monstersIds.Add(monsterIdInt);
                        }
                    }
                }
                #endregion

                expeditionObject["championsIds"] = JsonConvert.SerializeObject(championsIds);
                expeditionObject["monstersIds"] = JsonConvert.SerializeObject(monstersIds);
                expeditionObject["accessToken"] = accessToken;
            }

            isFirstLogin = false;

            //Converting JObject to object to pass it into the timer
            object expeditionObjectObject = expeditionObject.ToObject<object>();
            TimerCallback timerCallback = new TimerCallback(ExpeditionLoop);
            timerExp = new Timer(timerCallback, expeditionObjectObject, 1000, 3650000); // a bit more than a hour interval.
            Console.ReadLine();
        }
        static async void ExpeditionLoop(object expeditionObject)
        {
            #region Stop expedition
            //I dont know how it is if I have more than one expedition
            uri = "https://api.monsterchampions.xyz/v1/expeditions";
            var result = await Client.GetAsync(uri);
            var contents = await result.Content.ReadAsStringAsync();

            //Json is placed in an array
            //if no running expedition, we would have an empty array.
            if (contents == null || contents.Length == 0 || contents.Length <= 2)
            {
                Console.WriteLine("No running expeditions");
            }

            else if (System.Net.HttpStatusCode.BadGateway.Equals("Bad Gateway"))
            {
                isFirstLogin = true;
                Login(null);
                timerLogin.Change(Timeout.Infinite, Timeout.Infinite);
                timerLogin.Dispose();
            }

            else
            {
                contents = contents.Substring(1, contents.Length - 2);
                contentsJson = JObject.Parse(contents);
            }

            //if no expedition, it should be empty because that would be the json with the champions ids and stuff
            var expId = (string)contentsJson.SelectToken("uuid");
            var createdAt = (string)contentsJson.SelectToken("createdAt");
            if (!string.IsNullOrEmpty(expId) || !string.IsNullOrEmpty(createdAt))
            {
                var paramJson = new JObject {
                    new JProperty("reason", "redeemReward"), //cancel OR redeemReward
                };

                var parameters = new StringContent(paramJson.ToString(Formatting.None), Encoding.UTF8, "application/json");
                uri = "https://api.monsterchampions.xyz/v1/expeditions/" + expId;
                result = await Client.PatchAsync(uri, parameters);
                Thread.Sleep(5000);
            }
            #endregion

            //Would need to make a list if I have more than one champion
            #region getting assets timer JObject
            JObject expeditionJObject = (JObject)JToken.FromObject(expeditionObject);
            int championInt = 0;
            string[] monsterArray = new string[] { "" };
            int[] monsterArrInt = new int[] { };
            string championStr = (string)expeditionJObject["championsIds"];
            if (!string.IsNullOrEmpty(championStr))
            {
                championStr = championStr.Substring(1, championStr.Length - 2);
                //with only one champion, it s a bit useless to create an array above...
                Int32.TryParse(championStr, out championInt);
            }

            string monsterString = (string)expeditionJObject["monstersIds"];
            if (!string.IsNullOrEmpty(monsterString))
            {
                monsterArray = monsterString.Substring(1, monsterString.Length - 2).Split(',');
                monsterArrInt = monsterArray.Select(int.Parse).ToArray();
            }
            #endregion

            #region Start expedition
            var paramJsonStart = new JObject {
                new JProperty("championNftId", championInt),
                new JProperty("monsterNftIds", new JArray{ monsterArrInt })
            };

            var parametersStart = new StringContent(paramJsonStart.ToString(Formatting.None), Encoding.UTF8, "application/json");
            uri = "https://api.monsterchampions.xyz/v1/expeditions/";
            result = await Client.PostAsync(uri, parametersStart);
            #endregion
        }
    }
}


//This below works with regular object to start an expedition
//var champion = championsIds[0];
//int championListInt;
//Int32.TryParse(champion.ToString(), out championListInt);
//string[] monsterArray = new string[] { "" };
//string monsterString = (string)expeditionObject["monstersIds"];
//monsterArray = monsterString.Substring(1, monsterString.Length - 2).Split(',');
//int[] monsterArrInt = monsterArray.Select(int.Parse).ToArray();
//var paramJsonStart = new JObject {
//    new JProperty("championNftId", championListInt),
//    new JProperty("monsterNftIds", new JArray{ monsterArrInt })
//};
