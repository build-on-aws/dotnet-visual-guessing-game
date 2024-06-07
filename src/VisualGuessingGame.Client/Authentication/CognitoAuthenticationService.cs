using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Web;
using Blazored.SessionStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace VisualGuessingGame.Client.Authentication;

public class CognitoAuthenticationService : AuthenticationStateProvider, IAccessTokenProvider
{
    private string? _rawAccessToken = null;
    private JwtSecurityToken? _accessToken = null;
    private string? _rawIdToken = null;
    private JwtSecurityToken? _idToken = null;
    private JwtSecurityToken? _refreshToken = null;
    private DateTimeOffset? _expiration = null;

    private readonly IConfiguration _configuration;
    private readonly ISessionStorageService _sessionStorageService;
    private readonly NavigationManager _navigation;
    private readonly HttpClient _httpClient;

    public CognitoAuthenticationService(IConfiguration configuration, ISessionStorageService sessionStorageService,
        NavigationManager navigation, HttpClient httpClient) : base()
    {
        _configuration = configuration;
        _sessionStorageService = sessionStorageService;
        _navigation = navigation;
        _httpClient = httpClient;
    }
    
    public override async Task<AuthenticationState> GetAuthenticationStateAsync() => new AuthenticationState(await GetUser());

    private async Task<ClaimsPrincipal> GetUser()
    {
        if (_idToken is null)
        {
            return new ClaimsPrincipal();
        }

        var identity = new ClaimsIdentity("Federation", "email", null);
        identity.AddClaims(_idToken.Claims);
        return new ClaimsPrincipal(identity);
    }

    public async Task ProcessLogIn()
    {
        var returnUrl = _navigation.HistoryEntryState is not null ? JsonSerializer.Deserialize<InteractiveRequestOptions>(_navigation.HistoryEntryState)?.ReturnUrl : null;
        await _sessionStorageService.SetItemAsync("returnUrl", returnUrl);
        _navigation.NavigateTo($"https://{_configuration["CognitoDomainName"]}.auth.{_configuration["CognitoRegion"]}.amazoncognito.com/login?response_type=code&client_id={_configuration["ClientId"]}&redirect_uri={_navigation.BaseUri.TrimEnd('/')}&scope=openid+profile");
    }

    public async Task ProcessLogInCallback()
    {
        var queryStringParams = HttpUtility.ParseQueryString(new Uri(_navigation.Uri).Query);
        var tokenRequest = new StringContent(
            $"grant_type=authorization_code&client_id={_configuration["ClientId"]}&code={queryStringParams["code"]}&redirect_uri={_navigation.BaseUri.TrimEnd('/')}",
            MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded"));
        var tokenResponse = await _httpClient.PostAsync($"https://{_configuration["CognitoDomainName"]}.auth.{_configuration["CognitoRegion"]}.amazoncognito.com/oauth2/token", tokenRequest);

        if (tokenResponse.StatusCode != HttpStatusCode.OK)
            throw new Exception($"{nameof(tokenResponse.StatusCode)} should be 200 OK");

        var cognitoTokenResponse =
            await JsonSerializer.DeserializeAsync<CognitoTokenResponse>(await tokenResponse.Content.ReadAsStreamAsync());

        if(cognitoTokenResponse is null)
            throw new Exception($"{nameof(cognitoTokenResponse)} is null");

        _rawAccessToken = cognitoTokenResponse.AccessToken;
        _accessToken = new JwtSecurityToken(cognitoTokenResponse.AccessToken);
        _rawIdToken = cognitoTokenResponse.IdToken;
        _idToken = new JwtSecurityToken(cognitoTokenResponse.IdToken);
        _refreshToken = new JwtSecurityToken(cognitoTokenResponse.RefreshToken);
        _expiration = DateTimeOffset.Now.AddSeconds(cognitoTokenResponse.ExpiresIn);

        await UpdateUserOnSuccess();

        var cachedUrl = await _sessionStorageService.GetItemAsync<string>("returnUrl");
        if (cachedUrl is not null)
        {
            await _sessionStorageService.RemoveItemAsync("returnUrl");
            _navigation.NavigateTo(cachedUrl);
        }
    }

    public void ProcessLogOut()
    {
        var returnUrl = _navigation.HistoryEntryState is not null ? JsonSerializer.Deserialize<InteractiveRequestOptions>(_navigation.HistoryEntryState)?.ReturnUrl : null;
        _navigation.NavigateTo($"https://{_configuration["CognitoDomainName"]}.auth.{_configuration["CognitoRegion"]}.amazoncognito.com/logout?client_id={_configuration["ClientId"]}&logout_uri={_navigation.BaseUri.TrimEnd('/')}");
    }

    private async Task UpdateUserOnSuccess()
    {
        var getUserTask = GetUser();
        await getUserTask;
        UpdateUser(getUserTask);
    }

    private void UpdateUser(Task<ClaimsPrincipal> task)
    {
        NotifyAuthenticationStateChanged(UpdateAuthenticationState(task));
        return;
        
        static async Task<AuthenticationState> UpdateAuthenticationState(Task<ClaimsPrincipal> futureUser) => new AuthenticationState(await futureUser);
    }

    public ValueTask<AccessTokenResult> RequestAccessToken()
    {
        if(_idToken is null) return ValueTask.FromResult(new AccessTokenResult(AccessTokenResultStatus.RequiresRedirect, new AccessToken(), null, null));
        
        var claim = _idToken.Claims.FirstOrDefault(x => x.Type == "scope");
        
        return ValueTask.FromResult(new AccessTokenResult(AccessTokenResultStatus.Success, new AccessToken() { Expires = _expiration!.Value, Value = _rawIdToken!, GrantedScopes = claim is null ? new List<string>() : claim.Value.Split()}, null, null));
    }
    
    public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
    {
        return RequestAccessToken();
    }
}