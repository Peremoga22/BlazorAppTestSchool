namespace BlazorAppTestSchool.Data;

public static class TeacherReportAccess
{
    public static bool CanView(IWebHostEnvironment environment, HttpContext? context) =>
        environment.IsDevelopment() && IsLocalRequest(context);

    private static bool IsLocalRequest(HttpContext? context)
    {
        if (context?.Connection.RemoteIpAddress is not { } address ||
            context.Request.Headers.ContainsKey("Forwarded") ||
            context.Request.Headers.ContainsKey("X-Forwarded-For"))
        {
            return false;
        }

        var host = context.Request.Host.Host.Trim('[', ']');
        var isLocalHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            (System.Net.IPAddress.TryParse(host, out var hostAddress) && System.Net.IPAddress.IsLoopback(hostAddress));
        return isLocalHost && System.Net.IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }
}