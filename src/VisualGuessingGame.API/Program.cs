using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Serialization.SystemTextJson;
using VisualGuessingGame.API;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.Text.Json.Serialization;
using static VisualGuessingGame.API.ImageStorageEndpoints;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi, new SourceGeneratorLambdaJsonSerializer<AppJsonSerializerContext>());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Authority"];
    options.TokenValidationParameters = new TokenValidationParameters()
    {
        ValidateIssuerSigningKey = true,
        ValidateAudience = false
    };
});

builder.Services.AddAuthorization();
builder.Services.AddCors();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseCors(policy => policy.WithOrigins("https://localhost:7215")
        .AllowAnyMethod()
        .WithHeaders(HeaderNames.ContentType, HeaderNames.Authorization)
        .AllowCredentials());
}

//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.RegisterConfigEndpoints();
app.RegisterImageGenerationEndpoints();
app.RegisterImageStorageEndpoints();
app.RegisterImageDescriptionEndpoints();
app.RegisterImageIndexationEndpoints();
app.RegisterImageQueryEndpoints();
app.Run();

record Config(string Authority, string ClientId, string CognitoDomainName, string CognitoRegion);


[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(APIGatewayProxyRequest))]
[JsonSerializable(typeof(APIGatewayProxyResponse))]
[JsonSerializable(typeof(ImageGenParam))]
[JsonSerializable(typeof(ImageStorageParam))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(IEnumerable<ImagePreSignedUrl>))]
[JsonSerializable(typeof(IEnumerable<float>))]
[JsonSerializable(typeof(LanceDBQueryResponse))]
[JsonSerializable(typeof(ImagePreSignedUrl))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}