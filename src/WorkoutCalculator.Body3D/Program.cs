using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WorkoutCalculator.Body3D;
using WorkoutCalculator.Body3D.Services;

// Приложение работает только в браузере (WebAssembly)
[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// JS-модули для [JSImport]: путь — относительно папки _framework
await JSHost.ImportAsync(ViewerInterop.Module, "../js/viewer.js");
await JSHost.ImportAsync(BrowserStorage.Module, "../js/storage.js");

await builder.Build().RunAsync();
