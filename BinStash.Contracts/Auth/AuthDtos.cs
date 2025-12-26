// Copyright (C) 2025  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace BinStash.Contracts.Auth;

public sealed class RegisterRequest
{

    /// <summary>
    /// The user's first name.
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    /// The user's middle name.
    /// </summary>
    public string? MiddleName { get; set; }
    
    /// <summary>
    /// The user's last name.
    /// </summary>
    public required string LastName { get; set; }
    
    /// <summary>
    /// The user's email address which acts as a username.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// The user's password.
    /// </summary>
    public required string Password { get; init; }
}

public sealed class ConfirmEmailRequest
{
    /// <summary>
    /// The user's ID.
    /// </summary>
    public required string UserId { get; init; }
    
    /// <summary>
    /// The email confirmation code.
    /// </summary>
    public required string Code { get; init; }
    
    /// <summary>
    /// The changed email address, if applicable.
    /// </summary>
    public string? ChangedEmail { get; init; }
}

public sealed class InfoResponse
{
    /// <summary>
    /// The user's first name.
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    /// The user's middle name.
    /// </summary>
    public string? MiddleName { get; set; }
    
    /// <summary>
    /// The user's last name.
    /// </summary>
    public required string LastName { get; set; }
    
    /// <summary>
    /// The email address associated with the authenticated user.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Indicates whether or not the <see cref="Email"/> has been confirmed yet.
    /// </summary>
    public required bool IsEmailConfirmed { get; init; }
    
    /// <summary>
    /// Indicates whether or not the user has completed the onboarding process.
    /// </summary>
    public required bool OnboardingCompleted { get; set; }
}