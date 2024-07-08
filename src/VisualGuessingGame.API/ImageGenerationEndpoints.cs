using Amazon.CognitoIdentity;
using Amazon.BedrockRuntime;
using Rockhead.Extensions;
using Rockhead.Extensions.Amazon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Amazon.BedrockRuntime.Model;
using Amazon.Util;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualGuessingGame.API
{
    public static class ImageGenerationEndpoints
    {
        public static void RegisterImageGenerationEndpoints(this WebApplication app)
        {
            app.MapPost("api/imagegen", PostImageGenerationRequest).RequireAuthorization();
        }

        private static async Task<IResult> PostImageGenerationRequest(IConfiguration configuration, HttpContext context, [FromBody] ImageGenParam param)
        {

            var credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            var client = new AmazonBedrockRuntimeClient(credentials, Amazon.RegionEndpoint.USEast1);
            
            var response = await InvokeTitanImageGeneratorG1ForTextToImageAsync(client, new TitanImageTextToImageParams() { Text = param.Prompt }, new TitanImageGenerationConfig() { NumberOfImages = param.Number });
            
            
            if(response is null || (response.Error is not null && response.Error.Length > 0))
                return Results.Problem("An error occured");
            return Results.Ok<IEnumerable<string>>(response.Images);
        }

        public static async Task<TitanImageGeneratorG1Response?> InvokeTitanImageGeneratorG1ForTextToImageAsync( AmazonBedrockRuntimeClient client, TitanImageTextToImageParams textToImageParams, TitanImageGenerationConfig? imageGenerationConfig = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(textToImageParams);

            var payload = new JsonObject()
            {
                ["taskType"] = "TEXT_IMAGE",
                ["textToImageParams"] = JsonSerializer.SerializeToNode(textToImageParams, ImageGenSourceGenerator.Default.TitanImageTextToImageParams)
            };

            if (imageGenerationConfig is not null)
            {
                payload.Add("imageGenerationConfig", JsonSerializer.SerializeToNode(imageGenerationConfig, ImageGenSourceGenerator.Default.TitanImageGenerationConfig));
            }

            InvokeModelResponse response = await client.InvokeModelAsync(new InvokeModelRequest()
            {
                ModelId = new Model.TitanImageGeneratorV1().ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload.ToJsonString())
            },
                cancellationToken).ConfigureAwait(false);

            return await JsonSerializer.DeserializeAsync<TitanImageGeneratorG1Response>(response.Body, ImageGenSourceGenerator.Default.TitanImageGeneratorG1Response, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    [JsonSerializable(typeof(TitanImageTextToImageParams))]
    [JsonSerializable(typeof(TitanImageGenerationConfig))]
    [JsonSerializable(typeof(TitanImageGeneratorG1Response))]
    internal partial class ImageGenSourceGenerator : JsonSerializerContext { }

    public record ImageGenParam(string Prompt, int Number);
}
