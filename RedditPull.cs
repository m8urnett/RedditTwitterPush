using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using RestSharp;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedditTwitterSyndicator
{
    public static class RedditPull
    {
        [FunctionName("RedditPull")]
        public static async Task Run([TimerTrigger("0/15 * * * * *", RunOnStartup = false)]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"RedditPull timer trigger function executed at: {DateTime.Now}");
            string accessToken = GetAccessToken(log);
            List<RedditPost> redditPosts = GetTopPosts(log, accessToken);
            await WriteToTable(log,redditPosts);
        }

        private static async Task WriteToTable(ILogger log, List<RedditPost> posts)
        {
            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
            var tableClient = storageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference("PostQueue");

            var postEntities = posts.Select(post => new PostQueueEntity(post.Title, post.Url));
            var batchOperations = new TableBatchOperation();
            postEntities.Select(entity => TableOperation.Insert(entity)).ToList()
                .ForEach(insertOperation => batchOperations.Add(insertOperation));
            
            await table.ExecuteBatchAsync(batchOperations);
        }

        struct RedditPost
        {
            public string Title { get; set; }
            public string Url { get; set; }
        }

        struct SubRedditPostQueryResponse
        {
            public struct QueryResponseData
            {
                public struct QueryResponseChildren
                {
                    public struct QueryResponsePostData
                    {
                        public string Title { get; set; }
                        public string Url { get; set; }
                    }
                    public QueryResponsePostData Data { get; set; }
                }
                public List<QueryResponseChildren> Children { get; set; }
            }
            public QueryResponseData Data { get; set; }
        }

        private static List<RedditPost> GetTopPosts(ILogger log, string accessToken)
        {
            RestClient client = new RestClient("https://oauth.reddit.com");
            RestRequest request = new RestRequest("/r/WindowsSecurity/new", Method.GET);
            request.AddHeader("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT"));
            request.AddHeader("x-li-format", "json");
            request.AddHeader("Authorization", $"bearer {accessToken}");
            request.AddQueryParameter("t", "day");

            IRestResponse response = client.Execute(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                log.LogCritical("Failed to read reddit posts");
            }

            var responseContent = JsonConvert.DeserializeObject<SubRedditPostQueryResponse>(response.Content.ToString());
            return responseContent.Data.Children.Select(post => new RedditPost() { Title = post.Data.Title, Url = post.Data.Url }).ToList();
        }

        private static string GetAccessToken(ILogger log)
        {
            string redditAppId = Environment.GetEnvironmentVariable("REDDIT_APP_ID");
            string redditAppSecret = Environment.GetEnvironmentVariable("REDDIT_APP_SECRET");
            string redditUserName = Environment.GetEnvironmentVariable("REDDIT_USERNAME");
            string redditPassword = Environment.GetEnvironmentVariable("REDDIT_PASSWORD");

            RestClient client = new RestClient("https://www.reddit.com/api/v1");
            RestRequest request = new RestRequest("/access_token", Method.POST);
            request.AddHeader("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT"));
            request.AddHeader("x-li-format", "json");

            request.AddParameter("grant_type", "password");
            request.AddParameter("username", redditUserName);
            request.AddParameter("password", redditPassword);

            client.Authenticator = new RestSharp.Authenticators.HttpBasicAuthenticator(redditAppId, redditAppSecret);

            IRestResponse response = client.Execute(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                log.LogCritical($"Failed to get reddit access token {response.Content}");
            }

            dynamic responseContent = JsonConvert.DeserializeObject(response.Content.ToString());
            return responseContent["access_token"].Value;
        }
    }
}
