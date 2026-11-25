
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

public class TokenAuthorizeAttribute : AuthorizeAttribute
{
    public TokenAuthorizeAttribute()
    {
        AuthenticationSchemes =
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    }
}
