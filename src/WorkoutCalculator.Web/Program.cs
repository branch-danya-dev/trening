using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WorkoutCalculator.Web;
using WorkoutCalculator.Web.Services;

// Приложение работает только в браузере (WebAssembly)
[assembly: SupportedOSPlatform("browser")]

// Десятичная запятая во всех текстах (в том числе в подсказках из BodyModel и Core).
// Берём инвариантную культуру и меняем только числа: русских языковых данных в браузере может не быть.
var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
culture.NumberFormat = Fmt.Ru;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.CurrentCulture = culture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<AppData>();

// JS-модули для [JSImport]: путь — относительно папки _framework
await JSHost.ImportAsync(ViewerInterop.Module, "../js/viewer.js");
await JSHost.ImportAsync(BrowserStorage.Module, "../js/storage.js");
await JSHost.ImportAsync(PhotoStore.Module, "../js/photos.js");
await JSHost.ImportAsync(DataInterop.Module, "../js/data.js");
await JSHost.ImportAsync(BackupInterop.Module, "../js/backup.js");
await JSHost.ImportAsync(Capture.Module, "../js/capture.js");
await JSHost.ImportAsync(PhotoAnalyzer.Module, "../js/analysis.js");
await JSHost.ImportAsync(PhotoWarpInterop.Module, "../js/warp.js");

await builder.Build().RunAsync();
