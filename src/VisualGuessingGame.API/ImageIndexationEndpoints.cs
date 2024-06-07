using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.CognitoIdentity;
using Amazon.Lambda;
using Amazon.Util;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualGuessingGame.API
{
    public static class ImageIndexationEndpoints
    {
        public static void RegisterImageIndexationEndpoints(this WebApplication app)
        {
            app.MapPost("api/imageindexation/{collectionName}/{imageKey}", PostImageIndexationRequest);    
        }

        private static async Task<IResult> PostImageIndexationRequest(IConfiguration configuration, HttpContext context, string collectionName, string imageKey)
        {
            CognitoAWSCredentials credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            var description = await (new StreamReader(context.Request.Body)).ReadToEndAsync();

            var bedrockRuntime = new AmazonBedrockRuntimeClient(credentials, RegionEndpoint.USEast1);

            var lambda = new AmazonLambdaClient(credentials);
            
            string payload = JsonSerializer.Serialize<EmbedParameters>(
                new EmbedParameters(
                    new List<string>() { description },
                    "search_document"),
                ImageIndexationSourceGenerator.Default.EmbedParameters);

            var invokeResponse = await bedrockRuntime.InvokeModelAsync(new InvokeModelRequest()
            {
                ModelId = "cohere.embed-multilingual-v3",
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload),
                ContentType = "application/json",
                Accept = "application/json"
            });

            if (invokeResponse.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return Results.Problem("A problem occured while converting description into embedding");
            }

            var results = JsonSerializer.Deserialize<EmbedResponse>(invokeResponse.Body, ImageIndexationSourceGenerator.Default.EmbedResponse);

            await lambda.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest()
            {
                FunctionName = configuration["LanceDBIndexFunction"],
                InvocationType = InvocationType.RequestResponse,
                Payload = JsonSerializer.Serialize<LanceDBIndexRequest>(
                    new LanceDBIndexRequest(
                        collectionName,
                        results?.embeddings.First() ?? [],
                        imageKey,
                        description),
                    ImageIndexationSourceGenerator.Default.LanceDBIndexRequest)
            });
 
            return Results.Ok<IEnumerable<float>>(results?.embeddings.First());
        }
    }

    [JsonSerializable(typeof(EmbedParameters))]
    [JsonSerializable(typeof(EmbedResponse))]
    [JsonSerializable(typeof(LanceDBIndexRequest))]
    internal partial class ImageIndexationSourceGenerator : JsonSerializerContext {}

    public record EmbedParameters(IEnumerable<string> texts, string input_type);

    public record EmbedResponse(IEnumerable<IEnumerable<float>> embeddings, string id, string response_type, IEnumerable<string> texts);

    public record LanceDBIndexRequest(string collection, IEnumerable<float> vector, string image_location, string image_description);

}
