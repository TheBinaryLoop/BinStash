using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace BinStash.Server.Auth;

public static class AuthDefaults
{
    public static readonly string AuthenticationScheme = $"{JwtBearerDefaults.AuthenticationScheme},ApiKey";
}