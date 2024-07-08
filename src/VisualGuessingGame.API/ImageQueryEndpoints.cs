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
    public static class ImageQueryEndpoints
    {
        public static void RegisterImageQueryEndpoints(this WebApplication app)
        {
            app.MapPost("api/imagequery/{collectionName}", PostImageQueryRequest).RequireAuthorization();    
        }

        private static async Task<IResult> PostImageQueryRequest(IConfiguration configuration, HttpContext context, string collectionName)
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
                    "search_query"),
                ImageQuerySourceGenerator.Default.EmbedParameters);

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

            var results = JsonSerializer.Deserialize<EmbedResponse>(invokeResponse.Body, ImageQuerySourceGenerator.Default.EmbedResponse);

            var lambda_result = await lambda.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest()
            {
                FunctionName = configuration["LanceDBQueryFunction"],
                InvocationType = InvocationType.RequestResponse,
                Payload = JsonSerializer.Serialize<LanceDBQueryRequest>(
                    new LanceDBQueryRequest(
                        collectionName,
                        results?.embeddings.First() ?? []),
                    ImageQuerySourceGenerator.Default.LanceDBQueryRequest)
            });

            if(lambda_result is not null && lambda_result.StatusCode == 200)
            {
                var response = JsonSerializer.Deserialize<LanceDBQueryResponse>(lambda_result.Payload, ImageQuerySourceGenerator.Default.LanceDBQueryResponse);
                return Results.Ok<LanceDBQueryResponse>(response);
            }
            else
            {
                return Results.Problem("A problem occured while querying the vector database");
            }
        }
    }

    [JsonSerializable(typeof(EmbedParameters))]
    [JsonSerializable(typeof(EmbedResponse))]
    [JsonSerializable(typeof(LanceDBQueryRequest))]
    [JsonSerializable(typeof(LanceDBQueryResponse))]
    internal partial class ImageQuerySourceGenerator : JsonSerializerContext {}

    public record LanceDBQueryRequest(string collection, IEnumerable<float> vector);

    public record LanceDBQueryResponse(string req_id, string top1, string top2);

}
