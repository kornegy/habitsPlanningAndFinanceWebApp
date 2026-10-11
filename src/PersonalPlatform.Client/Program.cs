using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PersonalPlatform.Client;
using PersonalPlatform.Client.Auth;
using PersonalPlatform.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// In WebAssembly every registration is effectively a per-tab singleton. The HttpClient is built by hand (not via
// IHttpClientFactory) so its handler shares the one AuthSession instead of a separate handler scope.
builder.Services.AddSingleton<AuthSession>();
builder.Services.AddSingleton(sp => new HttpClient(new BearerHandler(sp.GetRequiredService<AuthSession>()) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(30),
});
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<Loc>();
builder.Services.AddSingleton<UserContext>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddSingleton<DraftStore>();
builder.Services.AddSingleton<AppEvents>();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<AppAuthenticationStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
builder.Services.AddCascadingAuthenticationState();

await builder.Build().RunAsync();
