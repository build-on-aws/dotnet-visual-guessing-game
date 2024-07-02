// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Lambda;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Util;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace VisualGuessingGame.BackgroundTask;

public class Function
{
    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function()
    {

    }


    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
    /// to respond to SQS messages.
    /// </summary>
    /// <param name="evnt">The event for the Lambda function handler to process.</param>
    /// <param name="context">The ILambdaContext that provides methods for logging and describing the Lambda environment.</param>
    /// <returns></returns>
    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        foreach(var message in evnt.Records)
        {
            await ProcessMessageAsync(message, context);
        }
    }

    private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
    {
        context.Logger.LogInformation($"Processed message {message.Body}");


        JsonDocument json = JsonDocument.Parse(message.Body);

        string bucketName = json.RootElement.GetProperty("Records")[0].GetProperty("s3").GetProperty("bucket").GetProperty("name").GetString() ?? "";
        string key = json.RootElement.GetProperty("Records")[0].GetProperty("s3").GetProperty("object").GetProperty("key").GetString() ?? "";

        Console.WriteLine("key: {key}");

        var s3Client = new AmazonS3Client(RegionEndpoint.USEast1);
        var s3Object = await s3Client.GetObjectAsync(new GetObjectRequest()
        {
            BucketName = bucketName,
            Key = key
        });

        if (s3Object.HttpStatusCode != System.Net.HttpStatusCode.OK)
            return;

        var reader = new MemoryStream();
        await s3Object.ResponseStream.CopyToAsync(reader);
        string base64Image = Convert.ToBase64String(reader.ToArray());

        var bedrockRuntime = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);


        string claude3SonnetPayload = new JsonObject()
                    {
                        { "anthropic_version", "bedrock-2023-05-31" },
                        { "max_tokens", 2048 },
                        { "messages", new JsonArray()
                            {
                                new {
                                    role = "user",
                                    content = new JsonArray()
                                    {
                                        new
                                        {
                                            type = "image",
                                            source = new {
                                                type = "base64",
                                                media_type = "image/png",
                                                data = base64Image
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }.ToJsonString();

        var claude3SonnetInvokeResponse = await bedrockRuntime.InvokeModelAsync(new InvokeModelRequest()
        {
            ModelId = "anthropic.claude-3-sonnet-20240229-v1:0",
            Body = AWSSDKUtils.GenerateMemoryStreamFromString(claude3SonnetPayload),
            ContentType = "application/json",
            Accept = "application/json"
        });

        if (claude3SonnetInvokeResponse is not null && claude3SonnetInvokeResponse.HttpStatusCode == System.Net.HttpStatusCode.OK)
        {

            var claude3SonnetResults = JsonNode.ParseAsync(claude3SonnetInvokeResponse.Body).Result?["content"]?.AsArray();
            var imageDescription = new StringBuilder();
            if (claude3SonnetResults is not null)
            {
                foreach (var content in claude3SonnetResults)
                {
                    imageDescription.Append(content?["text"]);
                }
            }
            Console.WriteLine(imageDescription.ToString());


            string embedMultilingualPayload = new JsonObject()
                        {
                            {
                                "texts", new JsonArray()
                                {
                                    imageDescription.ToString()
                                }
                            },
                            {
                                "input_type", "search_document"
                            }
                        }.ToJsonString();

            var embedMultilingualInvokeResponse = await bedrockRuntime.InvokeModelAsync(new InvokeModelRequest()
            {
                ModelId = "cohere.embed-multilingual-v3",
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(embedMultilingualPayload),
                ContentType = "application/json",
                Accept = "application/json"
            });

            if (embedMultilingualInvokeResponse is not null && embedMultilingualInvokeResponse.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                var embedMultilingualResults = JsonSerializer.Deserialize<EmbedResponse>(embedMultilingualInvokeResponse.Body);

                var lambda = new AmazonLambdaClient();
                await lambda.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest()
                {
                    FunctionName = Environment.GetEnvironmentVariable("LanceDBIndexFunction"),
                    InvocationType = InvocationType.RequestResponse,
                    Payload = JsonSerializer.Serialize<LanceDBIndexRequest>(
                        new LanceDBIndexRequest(
                            key.Split('/')[0],
                            embedMultilingualResults?.embeddings.First() ?? [],
                            key.Split('/')[1],
                            imageDescription.ToString()))
                });
            }
        }

        await Task.CompletedTask;
    }


    public record EmbedResponse(IEnumerable<IEnumerable<float>> embeddings, string id, string response_type, IEnumerable<string> texts);

    public record LanceDBIndexRequest(string collection, IEnumerable<float> vector, string image_location, string image_description);



}