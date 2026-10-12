using Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// The MVC site is the only caller of the API (ADR-0002); auth forwarding arrives with issue #4.
builder.Services.AddHttpClient<ICompanionApiClient, CompanionApiClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("ApiBaseUrl is not configured.")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
