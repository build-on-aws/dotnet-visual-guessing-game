using Amazon.CognitoIdentity;
using Amazon.BedrockRuntime;
using Rockhead.Extensions;
using Rockhead.Extensions.Amazon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Amazon.BedrockRuntime.Model;
using Amazon.S3;
using Amazon.Util;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Amazon;
using System.Text.Json.Serialization.Metadata;
using System.Text;

namespace VisualGuessingGame.API
{
    public static class ImageDescriptionEndpoints
    {
        public static void RegisterImageDescriptionEndpoints(this WebApplication app)
        {
            app.MapGet("api/imagedescription/{collectionName}/{imageKey}", GetImageDescriptionRequest).RequireAuthorization();    
        }

        private static async Task<IResult> GetImageDescriptionRequest(IConfiguration configuration, HttpContext context, string collectionName, string imageKey)
        {
            var credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            var client = new AmazonS3Client(credentials);

            var s3Object = await client.GetObjectAsync(new GetObjectRequest()
            {
                BucketName = configuration["ImageStorageBucketName"],
                Key = $"{collectionName}/{imageKey}"
            });

            if (s3Object.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return Results.Problem("A problem occured while processing the image");
            }

            var reader = new MemoryStream();
            await s3Object.ResponseStream.CopyToAsync(reader);
            string base64Image = Convert.ToBase64String(reader.ToArray());

            var bedrockRuntime = new AmazonBedrockRuntimeClient(credentials, RegionEndpoint.USEast1);

            string payload = JsonSerializer.Serialize<ClaudeV3Parameters>(
                new ClaudeV3Parameters(
                    "bedrock-2023-05-31",
                    2048,
                    [
                        new ClaudeV3Message("user",
                        [
                            new("image",  new ClaudeV3MessageContentSource("base64", "image/png", base64Image))
                        ])
                    ]),
                ImageDescriptionSourceGenerator.Default.ClaudeV3Parameters);

            var invokeResponse = await bedrockRuntime.InvokeModelAsync(new InvokeModelRequest()
            {
                ModelId = "anthropic.claude-3-sonnet-20240229-v1:0",
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload),
                ContentType = "application/json",
                Accept = "application/json"
            });

            if (invokeResponse.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return Results.Problem("A problem occured while processing the image");
            }

            var results = (await JsonNode.ParseAsync(invokeResponse.Body))?["content"]?.AsArray();
            var description = new StringBuilder();
            if (results is not null)
            {
                foreach (var content in results)
                {
                    if (content?["type"]?.GetValue<string>() == "text")
                    {
                        description.AppendLine(content?["text"]?.GetValue<string>());
                    }
                }
            }
            return Results.Ok<string>(description.ToString());
        }
    }

    [JsonSerializable(typeof(ClaudeV3Parameters))]
    internal partial class ImageDescriptionSourceGenerator : JsonSerializerContext {}

    public record ClaudeV3Parameters(string anthropic_version, int max_tokens, IEnumerable<ClaudeV3Message> messages);

    public record ClaudeV3Message(string role, IEnumerable<ClaudeV3MessageContent> content);

    public record ClaudeV3MessageContent(string type, ClaudeV3MessageContentSource source);

    public record ClaudeV3MessageContentSource(string type, string media_type, string data);

}
