// Copyright (C) 2025  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BinStash.Contracts.Auth;
using BinStash.Core.Auth.Tokens;
using BinStash.Core.Auditing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Auth.Tenant;
using BinStash.Server.Configuration;
using BinStash.Server.Extensions;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using InfoResponse = BinStash.Contracts.Auth.InfoResponse;
using RegisterRequest = BinStash.Contracts.Auth.RegisterRequest;

namespace BinStash.Server.Endpoints;

public static class IdentityEndpoints
{
    private static readonly EmailAddressAttribute EmailAddressAttribute = new();
    
    public static RouteGroupBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        //var timeProvider = app.ServiceProvider.GetRequiredService<TimeProvider>();
        //var bearerTokenOptions = app.ServiceProvider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>();
        var emailSender = app.ServiceProvider.GetRequiredService<IEmailSender<BinStashUser>>();
        var linkGenerator = app.ServiceProvider.GetRequiredService<LinkGenerator>();
        var domainSettings = app.ServiceProvider.GetRequiredService<IOptions<DomainSettings>>().Value;
        
        // We'll figure out a unique endpoint name based on the final route pattern during endpoint generation.
        string? confirmEmailEndpointName = null;
        
        var requestLimits = app.ServiceProvider.GetRequiredService<IOptions<RequestLimitSettings>>().Value;

        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication")
            .WithDescription("Endpoints for authenticating users.")
            // Every endpoint here takes a small JSON document. Without this they inherit the
            // server-wide ceiling, which has to stay large for chunk upload — meaning an
            // unauthenticated caller could make the server read tens of megabytes per request.
            .RequireSmallRequestBody(requestLimits.MaxSmallRequestBodyBytes);
        
        group.MapPost("/register", async Task<Results<Ok, ValidationProblem>>
            ([FromBody] RegisterRequest registration, HttpContext context, [FromServices] IServiceProvider sp) =>
        {
            var authSettings = sp.GetRequiredService<IOptions<AuthSettings>>().Value;
            
            if (!authSettings.Settings.AllowRegistration)
                return CreateValidationProblem("RegistrationDisabled", "Registration is disabled.");
            
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            
            if (!userManager.SupportsUserEmail)
            {
                throw new NotSupportedException($"{nameof(MapIdentityEndpoints)} requires a user store with email support.");
            }

            var userStore = sp.GetRequiredService<IUserStore<BinStashUser>>();
            var emailStore = (IUserEmailStore<BinStashUser>)userStore;
            var email = registration.Email;

            if (string.IsNullOrEmpty(email) || !EmailAddressAttribute.IsValid(email))
            {
                return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidEmail(email)));
            }

            var user = new BinStashUser
            {
                Id = Guid.CreateVersion7(),
                FirstName = registration.FirstName,
                MiddleName = registration.MiddleName,
                LastName = registration.LastName
            };
            await userStore.SetUserNameAsync(user, email, CancellationToken.None);
            await emailStore.SetEmailAsync(user, email, CancellationToken.None);
            var result = await userManager.CreateAsync(user, registration.Password);

            if (!result.Succeeded)
            {
                return CreateValidationProblem(result);
            }

            await SendConfirmationEmailAsync(user, userManager, context, email);

            await AuditAuthAsync(sp, AuditActions.UserRegistered, AuditOutcome.Success, user.Id, email);

            var tenantJoinService = sp.GetRequiredService<TenantJoinService>();
            await tenantJoinService.JoinOnRegisterAsync(user.Id, registration, context.RequestAborted);
            return TypedResults.Ok();
        })
            .RequireRateLimiting(RateLimitPolicies.EmailDispatch);

        group.MapPost("/login", async Task<Results<IResult, EmptyHttpResult, ProblemHttpResult>>
            ([FromBody] LoginRequest login, [FromQuery] bool? useCookies, [FromQuery] bool? useSessionCookies, [FromServices] IServiceProvider sp) =>
        {
            var signInManager = sp.GetRequiredService<SignInManager<BinStashUser>>();
            var tokenService = sp.GetRequiredService<ITokenService>();

            var useCookieScheme = (useCookies == true) || (useSessionCookies == true);
            var isPersistent = (useCookies == true) && (useSessionCookies != true);
            signInManager.AuthenticationScheme = useCookieScheme ? IdentityConstants.ApplicationScheme : IdentityConstants.BearerScheme;

            var user = await signInManager.UserManager.FindByEmailAsync(login.Email);
            if (user is null)
            {
                // Recorded with the attempted address and no subject id. A run of these against
                // addresses that do not exist is what account enumeration looks like, and it is
                // invisible unless the miss is written down.
                await AuditAuthAsync(sp, AuditActions.AuthLoginFailed, AuditOutcome.Failed, null, login.Email,
                    new Dictionary<string, object?> { ["reason"] = "unknown_account" });
                return TypedResults.Unauthorized();
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, login.Password, lockoutOnFailure: true);

            var secondFactorAttempted = false;
            if (result.RequiresTwoFactor)
            {
                if (!string.IsNullOrEmpty(login.TwoFactorCode))
                {
                    secondFactorAttempted = true;
                    result = await signInManager.TwoFactorAuthenticatorSignInAsync(login.TwoFactorCode, isPersistent, rememberClient: isPersistent);
                }
                else if (!string.IsNullOrEmpty(login.TwoFactorRecoveryCode))
                {
                    secondFactorAttempted = true;
                    result = await signInManager.TwoFactorRecoveryCodeSignInAsync(login.TwoFactorRecoveryCode);
                }
            }

            if (!result.Succeeded)
            {
                // A bare RequiresTwoFactor is not a failure — it is the server asking for the
                // second factor, and the client is expected to come back with one. Recording it
                // would bury the real failures in noise.
                if (result.IsLockedOut)
                {
                    await AuditAuthAsync(sp, AuditActions.AuthLoginLockedOut, AuditOutcome.Denied, user.Id, user.Email);
                }
                else if (secondFactorAttempted)
                {
                    await AuditAuthAsync(sp, AuditActions.AuthTwoFactorFailed, AuditOutcome.Failed, user.Id, user.Email);
                }
                else if (!result.RequiresTwoFactor)
                {
                    await AuditAuthAsync(sp, AuditActions.AuthLoginFailed, AuditOutcome.Failed, user.Id, user.Email,
                        new Dictionary<string, object?> { ["reason"] = result.IsNotAllowed ? "not_allowed" : "bad_credentials" });
                }

                return TypedResults.Problem(result.ToString(), statusCode: StatusCodes.Status401Unauthorized);
            }

            await AuditAuthAsync(sp, AuditActions.AuthLoginSucceeded, AuditOutcome.Success, user.Id, user.Email,
                new Dictionary<string, object?>
                {
                    ["scheme"] = useCookieScheme ? "cookie" : "bearer",
                    ["twoFactor"] = secondFactorAttempted
                });
            
            if (useCookieScheme)
            {
                await signInManager.SignInAsync(user, isPersistent);
                return TypedResults.Ok();
            }
            
            var (accessToken, refreshToken) =
                await tokenService.CreateTokensAsync(user as IdentityUser<Guid> ?? throw new NotSupportedException("The token service requires User to be IdentityUser<Guid>."));

            return TypedResults.Ok(new AccessTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 15 * 60 // 15 minutes
            });
        })
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/logout", async Task<Results<IResult, EmptyHttpResult, ProblemHttpResult>>
        ([FromQuery] bool? useCookies, [FromServices] IServiceProvider sp) =>
        {
            var signInManager = sp.GetRequiredService<SignInManager<BinStashUser>>();

            var useCookieScheme = useCookies == true;
            signInManager.AuthenticationScheme = useCookieScheme ? IdentityConstants.ApplicationScheme : IdentityConstants.BearerScheme;
            
            if (useCookieScheme)
            {
                var signedOut = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User;
                await signInManager.SignOutAsync();

                Guid.TryParse(signedOut?.FindFirstValue(ClaimTypes.NameIdentifier), out var signedOutId);
                await AuditAuthAsync(sp, AuditActions.AuthLogout, AuditOutcome.Success,
                    signedOutId == Guid.Empty ? null : signedOutId,
                    signedOut?.FindFirstValue(ClaimTypes.Email));

                return TypedResults.Ok();
            }
            
            return TypedResults.NotFound("Not implemented");
        });
        
        group.MapPost("/machine/token", async Task<Results<IResult, EmptyHttpResult, ProblemHttpResult>>
            ([FromBody] MachineTokenLoginRequest login, [FromServices] IServiceProvider sp) =>
        {
            var db = sp.GetRequiredService<BinStashDbContext>();
            var hasher = sp.GetRequiredService<IPasswordHasher<ApiKey>>();

            return TypedResults.NotFound("Not implemented");
        })
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/refresh", async Task<Results<Ok<AccessTokenResponse>, UnauthorizedHttpResult, ProblemHttpResult, ChallengeHttpResult>>
            ([FromBody] RefreshRequest refreshRequest, [FromServices] IServiceProvider sp) =>
        {
            var refreshTokenSplits = refreshRequest.RefreshToken.Split('.');
            if (refreshTokenSplits.Length != 2)
                return TypedResults.Problem("Invalid refresh token.", statusCode: StatusCodes.Status400BadRequest);
            
            var tokenGuid = new Guid(Convert.FromHexString(refreshTokenSplits[0]));
            
            var db = sp.GetRequiredService<BinStashDbContext>();
            var tokenService = sp.GetRequiredService<ITokenService>();
            var passwordHasher = sp.GetRequiredService<IPasswordHasher<BinStashUser>>();
            
            var existing = await db.UserRefreshTokens
                .Include(x => x.User)
                .SingleOrDefaultAsync(x => x.Id == tokenGuid);
            
            // Reject the /refresh attempt with a 401 if the token expired or the security stamp validation fails
            if (existing is null || !existing.IsActive)
            {
                // Presenting a token that is no longer good covers the replay case: a token that
                // has already been rotated away comes back here, and that is worth seeing.
                await AuditAuthAsync(sp, AuditActions.AuthTokenRefreshRejected, AuditOutcome.Failed,
                    existing?.User?.Id, existing?.User?.Email,
                    new Dictionary<string, object?> { ["reason"] = existing is null ? "unknown_token" : "inactive_token" });
                return TypedResults.Unauthorized();
            }

            var user = existing.User;

            // verify hashed token, just like password
            var verificationResult = passwordHasher.VerifyHashedPassword(
                user,
                existing.Token,
                refreshTokenSplits[1]
            );

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                await AuditAuthAsync(sp, AuditActions.AuthTokenRefreshRejected, AuditOutcome.Failed, user.Id, user.Email,
                    new Dictionary<string, object?> { ["reason"] = "secret_mismatch" });
                return TypedResults.Unauthorized();
            }

            // rotate token
            existing.RevokedAt = DateTime.UtcNow;

            var (newAccessToken, newRefreshToken) =
                await tokenService.CreateTokensAsync(user);

            // existing.RevokedAt already tracked
            await db.SaveChangesAsync();

            await AuditAuthAsync(sp, AuditActions.AuthTokenRefreshed, AuditOutcome.Success, user.Id, user.Email);

            return TypedResults.Ok(new AccessTokenResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresIn = 15 * 60 // 15 minutes
            });
        })
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapGet("/confirmEmail", async Task<Results<Ok,UnauthorizedHttpResult>>
            ([FromQuery] string userId, [FromQuery] string code, [FromQuery] string? changedEmail, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            if (await userManager.FindByIdAsync(userId) is not { } user)
            {
                // We could respond with a 404 instead of a 401 like Identity UI, but that feels like unnecessary information.
                return TypedResults.Unauthorized();
            }
            
            try
            {
                code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return TypedResults.Unauthorized();
            }

            IdentityResult result;

            if (string.IsNullOrEmpty(changedEmail))
            {
                result = await userManager.ConfirmEmailAsync(user, code);
            }
            else
            {
                // As with Identity UI, email and username are one and the same. So when we update the email,
                // we need to update the username.
                result = await userManager.ChangeEmailAsync(user, changedEmail, code);

                if (result.Succeeded)
                {
                    result = await userManager.SetUserNameAsync(user, changedEmail);
                }
            }

            if (!result.Succeeded)
            {
                return TypedResults.Unauthorized();
            }

            await AuditAuthAsync(sp, AuditActions.UserEmailConfirmed, AuditOutcome.Success, user.Id, user.Email,
                string.IsNullOrEmpty(changedEmail) ? null : new Dictionary<string, object?> { ["changedEmail"] = changedEmail });

            return TypedResults.Ok();
            //return TypedResults.Content(""<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/><title>Email Confirmed</title></head><body><h1>Email Confirmed</h1><p>Your email has been successfully confirmed. You can now close this window and return to the application.</p></body></html>", "text/html");
        })
        ?.Add(endpointBuilder =>
        {
            var finalPattern = ((RouteEndpointBuilder)endpointBuilder).RoutePattern.RawText;
            confirmEmailEndpointName = $"{nameof(MapIdentityEndpoints)}-{finalPattern}";
            endpointBuilder.Metadata.Add(new EndpointNameMetadata(confirmEmailEndpointName));
        });
        
        group.MapPost("/confirmEmail", async Task<Results<Ok, UnauthorizedHttpResult>>
            ([FromBody] ConfirmEmailRequest confirmEmailRequest, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            if (await userManager.FindByIdAsync(confirmEmailRequest.UserId) is not { } user)
            {
                // We could respond with a 404 instead of a 401 like Identity UI, but that feels like unnecessary information.
                return TypedResults.Unauthorized();
            }
            
            var code = confirmEmailRequest.Code;

            try
            {
                code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return TypedResults.Unauthorized();
            }

            IdentityResult result;

            if (string.IsNullOrEmpty(confirmEmailRequest.ChangedEmail))
            {
                result = await userManager.ConfirmEmailAsync(user, code);
            }
            else
            {
                // As with Identity UI, email and username are one and the same. So when we update the email,
                // we need to update the username.
                result = await userManager.ChangeEmailAsync(user, confirmEmailRequest.ChangedEmail, code);

                if (result.Succeeded)
                {
                    result = await userManager.SetUserNameAsync(user, confirmEmailRequest.ChangedEmail);
                }
            }

            if (!result.Succeeded)
            {
                return TypedResults.Unauthorized();
            }

            return TypedResults.Ok();
        });

        group.MapPost("/resendConfirmationEmail", async Task<Ok>
            ([FromBody] ResendConfirmationEmailRequest resendRequest, HttpContext context, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            if (await userManager.FindByEmailAsync(resendRequest.Email) is not { } user)
            {
                return TypedResults.Ok();
            }

            await SendConfirmationEmailAsync(user, userManager, context, resendRequest.Email);
            return TypedResults.Ok();
        })
            .RequireRateLimiting(RateLimitPolicies.EmailDispatch);

        group.MapPost("/forgotPassword", async Task<Results<Ok, ValidationProblem>>
            ([FromBody] ForgotPasswordRequest resetRequest, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            var user = await userManager.FindByEmailAsync(resetRequest.Email);

            if (user is not null && await userManager.IsEmailConfirmedAsync(user))
            {
                var code = await userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                await emailSender.SendPasswordResetCodeAsync(user, resetRequest.Email, HtmlEncoder.Default.Encode(code));

                // Only written when a mail actually went out. The endpoint deliberately answers
                // the same way for an unknown address, and an entry for every probe would turn
                // the audit log into the account-existence oracle the response refuses to be.
                await AuditAuthAsync(sp, AuditActions.UserPasswordResetRequested, AuditOutcome.Success, user.Id, user.Email);
            }

            // Don't reveal that the user does not exist or is not confirmed, so don't return a 200 if we had
            // returned a 400 for an invalid code given a valid user email.
            return TypedResults.Ok();
        })
            .RequireRateLimiting(RateLimitPolicies.EmailDispatch);

        group.MapPost("/resetPassword", async Task<Results<Ok, ValidationProblem>>
            ([FromBody] ResetPasswordRequest resetRequest, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();

            var user = await userManager.FindByEmailAsync(resetRequest.Email);

            if (user is null || !(await userManager.IsEmailConfirmedAsync(user)))
            {
                // Don't reveal that the user does not exist or is not confirmed, so don't return a 200 if we had
                // returned a 400 for an invalid code given a valid user email.
                return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken()));
            }

            IdentityResult result;
            try
            {
                var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(resetRequest.ResetCode));
                result = await userManager.ResetPasswordAsync(user, code, resetRequest.NewPassword);
            }
            catch (FormatException)
            {
                result = IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());
            }

            if (!result.Succeeded)
            {
                await AuditAuthAsync(sp, AuditActions.UserPasswordResetCompleted, AuditOutcome.Failed, user.Id, user.Email,
                    new Dictionary<string, object?> { ["reason"] = "invalid_token_or_password" });
                return CreateValidationProblem(result);
            }

            await AuditAuthAsync(sp, AuditActions.UserPasswordResetCompleted, AuditOutcome.Success, user.Id, user.Email);

            return TypedResults.Ok();
        })
            .RequireRateLimiting(RateLimitPolicies.Authentication);
        
        var accountGroup = group.MapGroup("/manage").RequireAuthorization();

        accountGroup.MapPost("/2fa", async Task<Results<Ok<TwoFactorResponse>, ValidationProblem, NotFound>>
            (ClaimsPrincipal claimsPrincipal, [FromBody] TwoFactorRequest tfaRequest, [FromServices] IServiceProvider sp) =>
        {
            var signInManager = sp.GetRequiredService<SignInManager<BinStashUser>>();
            var userManager = signInManager.UserManager;
            if (await userManager.GetUserAsync(claimsPrincipal) is not { } user)
            {
                return TypedResults.NotFound();
            }

            if (tfaRequest.Enable == true)
            {
                if (tfaRequest.ResetSharedKey)
                {
                    return CreateValidationProblem("CannotResetSharedKeyAndEnable",
                        "Resetting the 2fa shared key must disable 2fa until a 2fa token based on the new shared key is validated.");
                }

                if (string.IsNullOrEmpty(tfaRequest.TwoFactorCode))
                {
                    return CreateValidationProblem("RequiresTwoFactor",
                        "No 2fa token was provided by the request. A valid 2fa token is required to enable 2fa.");
                }

                if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, tfaRequest.TwoFactorCode))
                {
                    return CreateValidationProblem("InvalidTwoFactorCode",
                        "The 2fa token provided by the request was invalid. A valid 2fa token is required to enable 2fa.");
                }

                await userManager.SetTwoFactorEnabledAsync(user, true);
                await AuditAuthAsync(sp, AuditActions.UserTwoFactorEnabled, AuditOutcome.Success, user.Id, user.Email);
            }
            else if (tfaRequest.Enable == false || tfaRequest.ResetSharedKey)
            {
                await userManager.SetTwoFactorEnabledAsync(user, false);

                // Also reached by ResetSharedKey, which disables 2FA until the new key is proved.
                // Recorded either way: an account's second factor coming off is the event worth
                // seeing, regardless of which request turned it off.
                await AuditAuthAsync(sp, AuditActions.UserTwoFactorDisabled, AuditOutcome.Success, user.Id, user.Email,
                    new Dictionary<string, object?> { ["reason"] = tfaRequest.ResetSharedKey ? "shared_key_reset" : "disabled" });
            }

            if (tfaRequest.ResetSharedKey)
            {
                await userManager.ResetAuthenticatorKeyAsync(user);
            }

            string[]? recoveryCodes = null;
            if (tfaRequest.ResetRecoveryCodes || (tfaRequest.Enable == true && await userManager.CountRecoveryCodesAsync(user) == 0))
            {
                var recoveryCodesEnumerable = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
                recoveryCodes = recoveryCodesEnumerable?.ToArray();
            }

            if (tfaRequest.ForgetMachine)
            {
                await signInManager.ForgetTwoFactorClientAsync();
            }

            var key = await userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await userManager.ResetAuthenticatorKeyAsync(user);
                key = await userManager.GetAuthenticatorKeyAsync(user);

                if (string.IsNullOrEmpty(key))
                {
                    throw new NotSupportedException("The user manager must produce an authenticator key after reset.");
                }
            }

            return TypedResults.Ok(new TwoFactorResponse
            {
                SharedKey = key,
                RecoveryCodes = recoveryCodes,
                RecoveryCodesLeft = recoveryCodes?.Length ?? await userManager.CountRecoveryCodesAsync(user),
                IsTwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
                IsMachineRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user),
            });
        });

        accountGroup.MapGet("/info", async Task<Results<Ok<InfoResponse>, ValidationProblem, NotFound>>
            (ClaimsPrincipal claimsPrincipal, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            if (await userManager.GetUserAsync(claimsPrincipal) is not { } user)
            {
                return TypedResults.NotFound();
            }
            
            return TypedResults.Ok(await CreateInfoResponseAsync(user, userManager));
        });

        accountGroup.MapPost("/info", async Task<Results<Ok<InfoResponse>, ValidationProblem, NotFound>>
            (ClaimsPrincipal claimsPrincipal, [FromBody] InfoRequest infoRequest, HttpContext context, [FromServices] IServiceProvider sp) =>
        {
            var userManager = sp.GetRequiredService<UserManager<BinStashUser>>();
            if (await userManager.GetUserAsync(claimsPrincipal) is not { } user)
            {
                return TypedResults.NotFound();
            }

            if (!string.IsNullOrEmpty(infoRequest.NewEmail) && !EmailAddressAttribute.IsValid(infoRequest.NewEmail))
            {
                return CreateValidationProblem(IdentityResult.Failed(userManager.ErrorDescriber.InvalidEmail(infoRequest.NewEmail)));
            }

            if (!string.IsNullOrEmpty(infoRequest.NewPassword))
            {
                if (string.IsNullOrEmpty(infoRequest.OldPassword))
                {
                    return CreateValidationProblem("OldPasswordRequired",
                        "The old password is required to set a new password. If the old password is forgotten, use /resetPassword.");
                }

                var changePasswordResult = await userManager.ChangePasswordAsync(user, infoRequest.OldPassword, infoRequest.NewPassword);
                if (!changePasswordResult.Succeeded)
                {
                    return CreateValidationProblem(changePasswordResult);
                }
            }

            if (!string.IsNullOrEmpty(infoRequest.NewEmail))
            {
                var email = await userManager.GetEmailAsync(user);

                if (email != infoRequest.NewEmail)
                {
                    await SendConfirmationEmailAsync(user, userManager, context, infoRequest.NewEmail, isChange: true);
                }
            }

            return TypedResults.Ok(await CreateInfoResponseAsync(user, userManager));
        });

        accountGroup.MapPost("PasskeyCreateOptions", async Task<Results<ContentHttpResult, NotFound>> (HttpContext context, UserManager<BinStashUser> userManager, SignInManager<BinStashUser> signInManager) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            
            if (user is null)
                return TypedResults.NotFound();
            
            var userId = await userManager.GetUserIdAsync(user);
            var userName = await userManager.GetUserNameAsync(user) ?? "User";

            var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(new()
            {
                Id = userId,
                Name = userName,
                DisplayName = userName
            });
            
            return TypedResults.Content(optionsJson, contentType: "application/json");
        });

        async Task SendConfirmationEmailAsync(BinStashUser user, UserManager<BinStashUser> userManager, HttpContext context, string email, bool isChange = false)
        {
            if (confirmEmailEndpointName is null)
            {
                throw new NotSupportedException("No email confirmation endpoint was registered!");
            }

            var code = isChange
                ? await userManager.GenerateChangeEmailTokenAsync(user, email)
                : await userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var userId = await userManager.GetUserIdAsync(user);
            var routeValues = new RouteValueDictionary()
            {
                ["userId"] = userId,
                ["code"] = code,
            };

            if (isChange)
            {
                // This is validated by the /confirmEmail endpoint on change.
                routeValues.Add("changedEmail", email);
            }

            var confirmEmailUrl = BuildFrontendUrl(domainSettings, context,
                $"/verify-email?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}");
            /*var confirmEmailUrl = linkGenerator.GetUriByName(context, confirmEmailEndpointName, routeValues)
                ?? throw new NotSupportedException($"Could not find the endpoint named '{confirmEmailEndpointName}'.");*/

            await emailSender.SendConfirmationLinkAsync(user, email, HtmlEncoder.Default.Encode(confirmEmailUrl));
        }
        
        return group;
    }
    
    /// <summary>
    /// Records an authentication or account-lifecycle event.
    /// </summary>
    /// <remarks>
    /// These are written from unauthenticated requests, so the entry's actor is Anonymous and the
    /// account the request was <em>about</em> is the target. That is what makes a failed attempt
    /// attributable at all — there is no authenticated caller to name.
    ///
    /// <para>
    /// Deliberately instance-scoped. A tenant may be resolved from the subdomain by the time a
    /// login arrives, but the user has not been authenticated yet and may not belong to that
    /// tenant at all; filing the event under it would attribute a stranger's login attempt to a
    /// customer's audit trail. Authentication is the operator's security surface.
    /// </para>
    /// </remarks>
    private static async Task AuditAuthAsync(IServiceProvider sp, string action, AuditOutcome outcome, Guid? userId, string? display, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var audit = sp.GetRequiredService<IAuditLogWriter>();

        await audit.WriteAsync(new AuditEntryDraft
        {
            Action = action,
            InstanceScoped = true,
            Outcome = outcome,
            TargetType = nameof(BinStashUser),
            TargetId = userId?.ToString(),
            TargetName = display,
            Metadata = metadata
        });
    }

    private static ValidationProblem CreateValidationProblem(string errorCode, string errorDescription) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> {
            { errorCode, [errorDescription] }
        });

    private static ValidationProblem CreateValidationProblem(IdentityResult result)
    {
        // We expect a single error code and description in the normal case.
        // This could be golfed with GroupBy and ToDictionary, but perf! :P
        var errorDictionary = new Dictionary<string, string[]>(1);

        foreach (var error in result.Errors)
        {
            string[] newDescriptions;

            if (errorDictionary.TryGetValue(error.Code, out var descriptions))
            {
                newDescriptions = new string[descriptions.Length + 1];
                Array.Copy(descriptions, newDescriptions, descriptions.Length);
                newDescriptions[descriptions.Length] = error.Description;
            }
            else
            {
                newDescriptions = [error.Description];
            }

            errorDictionary[error.Code] = newDescriptions;
        }

        return TypedResults.ValidationProblem(errorDictionary);
    }

    /// <summary>
    /// Builds a frontend URL for email links using <see cref="DomainSettings.BaseUrl"/> when configured,
    /// falling back to the origin of the current HTTP request.
    /// </summary>
    internal static string BuildFrontendUrl(DomainSettings domainSettings, HttpContext context, string path)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(domainSettings.BaseUrl)
            ? domainSettings.BaseUrl.TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host}";
        return $"{baseUrl}{path}";
    }

    private static async Task<InfoResponse> CreateInfoResponseAsync(BinStashUser user, UserManager<BinStashUser> userManager)
    {
        return new()
        {
            FirstName = user.FirstName,
            MiddleName = user.MiddleName,
            LastName = user.LastName,
            Email = await userManager.GetEmailAsync(user) ?? throw new NotSupportedException("Users must have an email."),
            IsEmailConfirmed = await userManager.IsEmailConfirmedAsync(user),
            OnboardingCompleted = user.OnboardingCompleted,
            Roles = await userManager.GetRolesAsync(user)
        };
    }
}