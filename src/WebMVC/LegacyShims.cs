namespace WebMVC
{
    /// <summary>Temporary backwards-compatibility shim for views (@inject WebMVC.DateTimeFormatter) until Milestone 5 (#131).</summary>
    public class DateTimeFormatter(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor)
        : WebMVC.Formatters.DateTimeFormatter(accessor);
}
