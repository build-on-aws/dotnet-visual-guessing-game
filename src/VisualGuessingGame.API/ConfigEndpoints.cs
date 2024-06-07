namespace VisualGuessingGame.API
{
    public static class ConfigEndpoints
    {
        public static void RegisterConfigEndpoints(this WebApplication app)
        {

            var authority = app.Configuration["Authority"];
            var clientId = app.Configuration["ClientId"];
            var cognitoDomainName = app.Configuration["CognitoDomainName"];
            var cognitoRegion = app.Configuration["CognitoRegion"];

            app.MapGet("/config", () => new Config(authority ?? "", clientId ?? "", cognitoDomainName ?? "", cognitoRegion ?? ""));

        }
    }
}
