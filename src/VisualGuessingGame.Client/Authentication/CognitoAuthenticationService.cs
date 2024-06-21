using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;
using Blazored.SessionStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace VisualGuessingGame.Client.Authentication;

public class CognitoAuthenticationService : AuthenticationStateProvider, IAccessTokenProvider
{
    private string? _rawAccessToken = null;
    private JwtSecurityToken? _accessToken = null;
    private string? _rawIdToken = null;
    private JwtSecurityToken? _idToken = null;
    private string? _rawRefreshToken = null;
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

    #region AuthenticationStateProvider implementation
    public override async Task<AuthenticationState> GetAuthenticationStateAsync() => new AuthenticationState(await GetUser());
    #endregion

    #region IAccessTokenProvider implementation
    public async ValueTask<AccessTokenResult> RequestAccessToken()
    {
        await GetTokens(true);
        if (_idToken is null)
        {
            return new AccessTokenResult(AccessTokenResultStatus.RequiresRedirect, new AccessToken(), null, null);
        }
        else
        {
            var claim = _idToken.Claims.FirstOrDefault(x => x.Type == "scope");
            return new AccessTokenResult(AccessTokenResultStatus.Success, new AccessToken() { Expires = _expiration!.Value, Value = _rawIdToken!, GrantedScopes = claim is null ? new List<string>() : claim.Value.Split() }, null, null);
        }
    }

    public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
    {
        return RequestAccessToken();
    }
    #endregion

    #region hostedUI callback processing
    public async Task ProcessLogIn()
    {
        var returnUrl = _navigation.HistoryEntryState is not null ? JsonSerializer.Deserialize<InteractiveRequestOptions>(_navigation.HistoryEntryState)?.ReturnUrl : null;
        await _sessionStorageService.SetItemAsync("returnUrl", returnUrl);
        _navigation.NavigateTo($"https://{_configuration["CognitoDomainName"]}.auth.{_configuration["CognitoRegion"]}.amazoncognito.com/login?response_type=code&client_id={_configuration["ClientId"]}&redirect_uri={_navigation.BaseUri.TrimEnd('/')}&scope=openid+profile");
    }

    public async Task ProcessLogInCallback()
    {
        await GetTokens();

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
    #endregion

    #region ClaimsPrincipal management
    private async Task<ClaimsPrincipal> GetUser()
    {
        if (_idToken is null)
        {
            await RetrieveTokensFromStorage();
            if (_idToken is null)
            {
                return new ClaimsPrincipal();
            }
        }

        var identity = new ClaimsIdentity("Federation", "email", null);
        identity.AddClaims(_idToken.Claims);
        return new ClaimsPrincipal(identity);
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
    #endregion

    #region token management
    private async Task RetrieveTokensFromStorage()
    {
        try
        {
            _rawIdToken = await _sessionStorageService.GetItemAsStringAsync("id_token");
            _rawAccessToken = await _sessionStorageService.GetItemAsStringAsync("access_token");
            _rawRefreshToken = await _sessionStorageService.GetItemAsStringAsync("refresh_token");
            _expiration = await _sessionStorageService.GetItemAsync<DateTimeOffset?>("expiration");
            ParseTokens();
        }
        catch
        {
            _rawIdToken = null;
            _rawAccessToken = null;
            _rawRefreshToken = null;
        }
    }

    private void ParseTokens()
    {
        _idToken = _rawIdToken is not null ? new JwtSecurityToken(_rawIdToken) : null;
        _accessToken = _rawAccessToken is not null ? new JwtSecurityToken(_rawAccessToken) : null;
    }

    private async Task StoreTokens()
    {
        await _sessionStorageService.SetItemAsStringAsync("id_token", _rawIdToken);
        await _sessionStorageService.SetItemAsStringAsync("access_token", _rawAccessToken);
        await _sessionStorageService.SetItemAsStringAsync("refresh_token", _rawRefreshToken);
        await _sessionStorageService.SetItemAsync<DateTimeOffset?>("expiration", _expiration);
    }

    private HttpRequestMessage BuildTokenRequest(bool refresh = false)
    {
        var queryStringParams = HttpUtility.ParseQueryString(new Uri(_navigation.Uri).Query);
        
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"https://{_configuration["CognitoDomainName"]}.auth.{_configuration["CognitoRegion"]}.amazoncognito.com/oauth2/token");
        request.Content = new StringContent($"grant_type={(refresh ? "refresh_token" : "authorization_code")}&client_id={_configuration["ClientId"]}&{(refresh ? $"refresh_token={_rawRefreshToken}" : $"code={queryStringParams["code"]}&redirect_uri={_navigation.BaseUri.TrimEnd('/')}")}", MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded"));
       
        return request;
    }

    private async Task GetTokens(bool refresh = false)
    {
        if (!refresh || (refresh && _rawRefreshToken != null && _expiration != null && ((_expiration - DateTimeOffset.Now)?.TotalSeconds < 600)))
        {
            var tokenResponse = await _httpClient.SendAsync(BuildTokenRequest(refresh));

            if (tokenResponse.StatusCode == HttpStatusCode.OK)
            {

                var cognitoTokenResponse =
                    await JsonSerializer.DeserializeAsync<CognitoTokenResponse>(await tokenResponse.Content.ReadAsStreamAsync());

                if (cognitoTokenResponse is null)
                    throw new Exception($"{nameof(cognitoTokenResponse)} is null");

                _rawAccessToken = cognitoTokenResponse.AccessToken;
                _rawIdToken = cognitoTokenResponse.IdToken;
                if (cognitoTokenResponse.RefreshToken is not null)
                {
                    _rawRefreshToken = cognitoTokenResponse.RefreshToken;
                }
                _expiration = DateTimeOffset.Now.AddSeconds(cognitoTokenResponse.ExpiresIn);

                await StoreTokens();
                ParseTokens();
            }
            else
            {
                await ClearTokens();
            }
        }
    }

    private async Task ClearTokens()
    {
        _rawAccessToken = null;
        _rawIdToken = null;
        _rawRefreshToken = null;
        _idToken = null;
        _accessToken = null;
        _expiration = null;
        await _sessionStorageService.RemoveItemAsync("id_token");
        await _sessionStorageService.RemoveItemAsync("access_token");
        await _sessionStorageService.RemoveItemAsync("refresh_token");
        await _sessionStorageService.RemoveItemAsync("expiration");
    }
    #endregion
}