using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PersonalPlatform.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
