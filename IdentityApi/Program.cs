using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using IdentityApi.Infrastructure;
using IdentityApi.Models;
using IdentityApi.Helpers;
using IdentityApi.Extensions;
using IdentityApi.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
var endpointHelper = new RouteHelper();

var securityKey = builder.Environment.IsDevelopment()
    ? builder.Configuration["SecurityKey"]
    : Environment.GetEnvironmentVariable("SECURITY_KEY");
if (securityKey == null)
{
    throw new NullReferenceException("Security key is null.");
}
var signingKey = endpointHelper.CreateSigningKey(securityKey);

var connectionString = builder.Environment.IsDevelopment()
    ? builder.Configuration["ConnectionString"]
    : Environment.GetEnvironmentVariable("CONNECTION_STRING");

if (connectionString == null)
{
    throw new NullReferenceException("Connection string to DB is null.");
}

// Add services to the container.
builder.Services
    .AddScoped<IUserRepository, UserRepository>()
    .AddEndpointsApiExplorer()
    .AddSwaggerGen()
    .AddDbContext<ApiDbContext>(options => options.UseSqlServer(connectionString))
    .AddIdentity<IdentityUser, IdentityRole>()
    .AddDefaultTokenProviders()
    .AddEntityFrameworkStores<ApiDbContext>();

// Identity options config and JWT.
builder.Services.Configure<IdentityOptions>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddAuthentication(options =>
{
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
    {
        ValidAudience = builder.Configuration["Audience"],
        ValidIssuer = builder.Configuration["Issuer"],
        IssuerSigningKey = signingKey,
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseHealthChecks("/health");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger().UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Register route.
app.MapPost("/register", async ([FromBody] RegisterModel model, IUserRepository userRepository) =>
{
    var validationResult = model.Validate();
    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors;
        return Results.BadRequest(endpointHelper.CreateErrorMessage(errors));
    }
    var (createResult, user) = await userRepository.CreateUserAsync(model.UserName, model.Email, model.Password);
    if (!createResult.Succeeded)
    {
        return Results.BadRequest();
    }
    var addToRoleResult = await userRepository.AddUserToRolesAsync(user, "user");
    if (!addToRoleResult.Succeeded)
    {
        return Results.Problem();
    }
    return Results.Ok("User registered.");
})
.WithName("Register").AddDefaultStatusCodes();

// Login route.
app.MapPost("/login", async ([FromBody] LoginModel model, IUserRepository userRepository, SignInManager<IdentityUser> signInManager) => {
    var signInResult = await signInManager.PasswordSignInAsync(model.UserName, model.Password, true, false);
    if (!signInResult.Succeeded)
    {
        return Results.BadRequest();
    }
    var (user, roles) = await userRepository.GetUserByUserName(model.UserName);
    var claims = endpointHelper.CreateClaimsForDefaultUser(user.Id, user.Email, user.UserName, roles);
    return Results.Ok(new
    {
        User = user.Id,
        Token = endpointHelper.CreateJwtToken(builder.Configuration["Issuer"], builder.Configuration["Audience"], claims, signingKey)
    });
})
.WithName("Login").AddDefaultStatusCodes();

// Edit account route.
app.MapPut("/edit/{id}", [Authorize] (IUserRepository userRepository, string id) => {

})
.WithName("Edit account").RequireAuthorization().AddDefaultStatusCodes();

// Delete account route.
app.MapDelete("/delete/{id}", [Authorize] (IUserRepository userRepository, string id) => {
    
})
.WithName("Delete account").AddDefaultStatusCodes();

// Reset password account route.
app.MapPut("/resetpassword/{id}", [Authorize] async (string id, IUserRepository userRepository, [FromBody] PasswordResetModel model) => {
    var resetResult = await userRepository.ResetPassword(id, model.NewPassword);
    if (!resetResult.Succeeded)
    {
        return Results.BadRequest();
    }
    return Results.Ok("Password was reset.");
})
.WithName("Reset password").AddDefaultStatusCodes();

app.Run();