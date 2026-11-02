using System.Net;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Amazon;
using Amazon.Runtime;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Restaurant.Web.Auth;

// Compatibility boundary for existing handlers. Identity is stored in PostgreSQL;
// the Cognito-shaped DTOs do not make network requests to AWS.
public sealed class PostgresIdentity : AmazonCognitoIdentityProviderClient
{
    private readonly NpgsqlDataSource source;
    private readonly SymmetricSecurityKey signingKey;
    private readonly PasswordHasher<string> hasher = new();
    private const string Issuer = "green-tasty-marathon";
    private const string Audience = "green-tasty-web";
    private readonly JwtSecurityTokenHandler tokens = new() { MapInboundClaims = false };
    public PostgresIdentity(NpgsqlDataSource source, string secret)
        : base(new AnonymousAWSCredentials(), RegionEndpoint.EUCentral1)
    {
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new ArgumentException("JWT_SIGNING_KEY must contain at least 32 bytes");
        this.source = source;
        signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public async Task<Dictionary<string,string>?> Validate(string token, string tokenUse = "id")
    {
        try
        {
            var principal = tokens.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            }, out _);
            var claims = principal.Claims.ToDictionary(c => c.Type, c => c.Value);
            if (claims.GetValueOrDefault("token_use") != tokenUse) return null;
            var user = await Find(claims["sub"]);
            if (user is null || claims.GetValueOrDefault("version") != user.Version.ToString()) return null;
            claims["cognito:groups"] = user.Roles;
            return claims;
        }
        catch (SecurityTokenException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private string Issue(Account user, string use)
    {
        var claims = new[] { new Claim("sub", user.Id), new Claim("email",user.Email),
            new Claim("cognito:username",user.Email), new Claim("cognito:groups",user.Roles),
            new Claim("token_use",use), new Claim("version",user.Version.ToString()), new Claim("jti",Guid.NewGuid().ToString()) };
        return tokens.WriteToken(new JwtSecurityToken(Issuer, Audience, claims, DateTime.UtcNow,
            DateTime.UtcNow.AddHours(1), new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)));
    }

    private sealed record Account(string Id, string Email, string Hash, string First, string Last, string Roles, int Version);
    private async Task<Account?> Find(string name)
    {
        await using var command = source.CreateCommand("SELECT id::text,email,password_hash,first_name,last_name,roles,token_version FROM app_users WHERE email=lower($1) OR id::text=$1");
        command.Parameters.AddWithValue(name);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }
    private static Account Read(NpgsqlDataReader r) => new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetInt32(6));
    private static List<AttributeType> Attributes(Account u) => [new() { Name="sub",Value=u.Id },new() { Name="email",Value=u.Email },
        new() { Name="given_name",Value=u.First },new() { Name="family_name",Value=u.Last },new() { Name="cognito:groups",Value=u.Roles }];
    private async Task<Account> Require(string name) => await Find(name) ?? throw new UserNotFoundException("User not found");

    public override async Task<SignUpResponse> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default)
    {
        var attributes = request.UserAttributes.ToDictionary(a => a.Name,a => a.Value);
        var id = Guid.NewGuid();
        await using var command = source.CreateCommand("INSERT INTO app_users(id,email,password_hash,first_name,last_name) VALUES($1,lower($2),$3,$4,$5)");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(request.Username.Trim());
        command.Parameters.AddWithValue(hasher.HashPassword(request.Username,request.Password));
        command.Parameters.AddWithValue(attributes.GetValueOrDefault("given_name", ""));
        command.Parameters.AddWithValue(attributes.GetValueOrDefault("family_name", ""));
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { throw new UsernameExistsException("An account with this email already exists"); }
        return new SignUpResponse { UserSub=id.ToString(),UserConfirmed=true,HttpStatusCode=HttpStatusCode.OK };
    }

    public override async Task<InitiateAuthResponse> InitiateAuthAsync(InitiateAuthRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.AuthParameters["USERNAME"].Trim();
        // Lockout updates are atomic; password hashes use ASP.NET Identity's salted PBKDF2 format.
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id::text,email,password_hash,first_name,last_name,roles,token_version,locked_until FROM app_users WHERE email=lower($1) FOR UPDATE",connection,transaction);
        command.Parameters.AddWithValue(email);
        Account? user = null; bool locked = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            if (await reader.ReadAsync(cancellationToken)) { user=Read(reader);locked=!reader.IsDBNull(7)&&reader.GetDateTime(7)>DateTime.UtcNow; }
        if (user is null) { hasher.HashPassword(email,request.AuthParameters["PASSWORD"]); throw new NotAuthorizedException("Invalid email or password"); }
        var correct = !locked && hasher.VerifyHashedPassword(email,user.Hash,request.AuthParameters["PASSWORD"]) != PasswordVerificationResult.Failed;
        await using var update = new NpgsqlCommand(correct
            ? "UPDATE app_users SET failed_attempts=0,locked_until=NULL WHERE id=$1"
            : "UPDATE app_users SET failed_attempts=failed_attempts+1,locked_until=CASE WHEN failed_attempts>=4 THEN now()+interval '15 minutes' ELSE locked_until END WHERE id=$1",connection,transaction);
        update.Parameters.AddWithValue(Guid.Parse(user.Id));
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (!correct) throw new NotAuthorizedException("Invalid email or password");
        return new InitiateAuthResponse { HttpStatusCode=HttpStatusCode.OK,AuthenticationResult=new AuthenticationResultType
            { IdToken=Issue(user,"id"),AccessToken=Issue(user,"access"),TokenType="Bearer",ExpiresIn=3600 } };
    }

    public override async Task<AdminGetUserResponse> AdminGetUserAsync(AdminGetUserRequest request,CancellationToken cancellationToken=default)
    { var user=await Require(request.Username);return new() { Username=user.Email,UserAttributes=Attributes(user),Enabled=true,HttpStatusCode=HttpStatusCode.OK }; }
    public override async Task<ListUsersResponse> ListUsersAsync(ListUsersRequest request,CancellationToken cancellationToken=default)
    {
        var users=new List<UserType>();
        await using var command=source.CreateCommand("SELECT id::text,email,password_hash,first_name,last_name,roles,token_version FROM app_users ORDER BY email");
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) { var u=Read(reader);users.Add(new() { Username=u.Email,Attributes=Attributes(u),Enabled=true }); }
        if(!string.IsNullOrWhiteSpace(request.Filter))
        { var id=request.Filter.Split('"').ElementAtOrDefault(1);users=users.Where(u=>u.Attributes.Any(a=>a.Name=="sub"&&a.Value==id)).ToList(); }
        return new() { Users=users,HttpStatusCode=HttpStatusCode.OK };
    }
    public override Task<AdminConfirmSignUpResponse> AdminConfirmSignUpAsync(AdminConfirmSignUpRequest request,CancellationToken cancellationToken=default) => Task.FromResult(new AdminConfirmSignUpResponse { HttpStatusCode=HttpStatusCode.OK });
    public override Task<ListGroupsResponse> ListGroupsAsync(ListGroupsRequest request,CancellationToken cancellationToken=default) => Task.FromResult(new ListGroupsResponse { Groups=[new GroupType { GroupName="Customer" },new GroupType { GroupName="Waiter" },new GroupType { GroupName="Admin" }],HttpStatusCode=HttpStatusCode.OK });
    public override Task<CreateGroupResponse> CreateGroupAsync(CreateGroupRequest request,CancellationToken cancellationToken=default) => Task.FromResult(new CreateGroupResponse { HttpStatusCode=HttpStatusCode.OK });
    public override async Task<AdminListGroupsForUserResponse> AdminListGroupsForUserAsync(AdminListGroupsForUserRequest request,CancellationToken cancellationToken=default) =>
        new() { Groups=(await Require(request.Username)).Roles.Split(',').Select(r=>new GroupType { GroupName=r }).ToList(),HttpStatusCode=HttpStatusCode.OK };
    private async Task SetRoles(string name,string group,bool add)
    {
        var user=await Require(name);var roles=user.Roles.Split(',').ToHashSet();if(add)roles.Add(group);else roles.Remove(group);
        await using var command=source.CreateCommand("UPDATE app_users SET roles=$1 WHERE id=$2");command.Parameters.AddWithValue(string.Join(',',roles));command.Parameters.AddWithValue(Guid.Parse(user.Id));await command.ExecuteNonQueryAsync();
    }
    public override async Task<AdminAddUserToGroupResponse> AdminAddUserToGroupAsync(AdminAddUserToGroupRequest request,CancellationToken cancellationToken=default)
    { await SetRoles(request.Username,request.GroupName,true);return new() { HttpStatusCode=HttpStatusCode.OK }; }
    public override async Task<AdminRemoveUserFromGroupResponse> AdminRemoveUserFromGroupAsync(AdminRemoveUserFromGroupRequest request,CancellationToken cancellationToken=default)
    { await SetRoles(request.Username,request.GroupName,false);return new() { HttpStatusCode=HttpStatusCode.OK }; }
    public override async Task<AdminUpdateUserAttributesResponse> AdminUpdateUserAttributesAsync(AdminUpdateUserAttributesRequest request,CancellationToken cancellationToken=default)
    {
        var user=await Require(request.Username);var values=request.UserAttributes.ToDictionary(a=>a.Name,a=>a.Value);
        await using var command=source.CreateCommand("UPDATE app_users SET first_name=$1,last_name=$2 WHERE id=$3");
        command.Parameters.AddWithValue(values.GetValueOrDefault("given_name",user.First));command.Parameters.AddWithValue(values.GetValueOrDefault("family_name",user.Last));command.Parameters.AddWithValue(Guid.Parse(user.Id));await command.ExecuteNonQueryAsync(cancellationToken);
        return new() { HttpStatusCode=HttpStatusCode.OK };
    }
    public override async Task<ChangePasswordResponse> ChangePasswordAsync(ChangePasswordRequest request,CancellationToken cancellationToken=default)
    {
        var claims=await Validate(request.AccessToken,"access") ?? throw new NotAuthorizedException("Invalid access token");
        var user=await Require(claims["sub"]);
        if(hasher.VerifyHashedPassword(user.Email,user.Hash,request.PreviousPassword)==PasswordVerificationResult.Failed) throw new NotAuthorizedException("Invalid password");
        APIHandler.Helpers.PersonalDataValidator.ValidatePassword(request.ProposedPassword);
        await using var command=source.CreateCommand("UPDATE app_users SET password_hash=$1,token_version=token_version+1 WHERE id=$2");
        command.Parameters.AddWithValue(hasher.HashPassword(user.Email,request.ProposedPassword));command.Parameters.AddWithValue(Guid.Parse(user.Id));await command.ExecuteNonQueryAsync(cancellationToken);
        return new() { HttpStatusCode=HttpStatusCode.OK };
    }
}
