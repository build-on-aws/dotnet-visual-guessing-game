using Blazored.SessionStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VisualGuessingGame.Client;
using VisualGuessingGame.Client.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

    

var httpClient = builder.Services.BuildServiceProvider().GetRequiredService<HttpClient>();

if (!builder.HostEnvironment.IsDevelopment())
{
    builder.Configuration.AddJsonStream(await httpClient.GetStreamAsync("config"));
    builder.Services.AddScoped<BaseAddressAuthorizationMessageHandler>();
    builder.Services.AddHttpClient("WebApi", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
        .AddHttpMessageHandler(sp => sp.GetRequiredService<BaseAddressAuthorizationMessageHandler>());
}
else
{
    var temp = await httpClient.GetStreamAsync("http://localhost:5159/config");
    var tempstring = new StreamReader(temp).ReadToEnd();
    builder.Configuration.AddJsonStream(await httpClient.GetStreamAsync("http://localhost:5159/config"));
    builder.Services.AddScoped<AuthorizationMessageHandler>();
    builder.Services.AddHttpClient("WebApi", client => client.BaseAddress = new Uri("http://localhost:5159"))
        .AddHttpMessageHandler(sp => sp.GetRequiredService<AuthorizationMessageHandler>().ConfigureHandler(
            authorizedUrls: ["http://localhost:5159"],
            scopes: ["openid", "profile"]));
}

builder.Services.AddBlazoredSessionStorageAsSingleton();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<CognitoAuthenticationService>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => (AuthenticationStateProvider)sp.GetRequiredService<CognitoAuthenticationService>());
builder.Services.AddSingleton<IAccessTokenProvider>(sp => (IAccessTokenProvider)sp.GetRequiredService<CognitoAuthenticationService>());

await builder.Build().RunAsync();